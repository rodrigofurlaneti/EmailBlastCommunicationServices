using EmailBlastCommunicationServices.Api.Mappers.EventGrid;
using EmailBlastCommunicationServices.Application.Interfaces.Messaging;
using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Domain.ValueObjects;
using EmailBlastCommunicationServices.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using Azure.Communication.Email;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace EmailBlastCommunicationServices.UnitTests;

public class EndpointTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        public IEmailStore Store { get; } = Substitute.For<IEmailStore>();
        public IEmailOperationStatusReader OperationReader { get; } = Substitute.For<IEmailOperationStatusReader>();
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Production")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["EventGrid:WebhookKey"] = "test-key" }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IEmailStore>();
                services.AddSingleton(Store);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton(Substitute.For<IEmailSender>());
                services.RemoveAll<IEmailOperationStatusReader>();
                services.AddSingleton(OperationReader);
            });
    }

    [Fact]
    public async Task SwaggerDocumentsSendRequestAndAcceptedResponse()
    {
        using var factory = new Factory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty("/api/emails").GetProperty("post");
        operation.GetProperty("responses").TryGetProperty("202", out _).Should().BeTrue();
        var example = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("EmailRequest").GetProperty("example");
        example.GetProperty("systemId").GetInt32().Should().Be(1);
        (await client.GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task OperationStatusUsesAzureIdWithoutSystemIdOrDatabaseAccess()
    {
        using var factory = new Factory();
        const string operationId = "b2a9d5ec-d55d-4d98-8b18-b964ec39c9c1";
        factory.OperationReader.GetAsync(operationId, Arg.Any<CancellationToken>()).Returns(new ProviderOperationStatus("Succeeded", true));
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/emails/operations/{operationId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<EmailOperationStatus>()).Should().Be(new EmailOperationStatus(operationId, "Succeeded", true));
        factory.Store.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("bad-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidOperationIdDoesNotCallAzure(string id)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        (await client.GetAsync($"/api/emails/operations/{id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        factory.OperationReader.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task MissingAzureOperationReturns404()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        (await client.GetAsync($"/api/emails/operations/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StatusQueryDoesNotRequireAzureSender()
    {
        using var factory = new Factory();
        factory.Store.SystemExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.RemoveAll<IEmailSender>()));
        using var client = host.CreateClient();
        var response = await client.GetAsync("/api/emails/1?systemId=1");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await factory.Store.Received().GetAsync(1, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownSystemIsRejectedBeforeQueueing()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/emails", new EmailRequest(99, "a@example.com", "Subject", "Body", null));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.Store.DidNotReceive().QueueAsync(Arg.Any<EmailRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WebhookRespondsToSubscriptionValidation()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Webhook-Key", "test-key");
        var response = await client.PostAsJsonAsync("/api/webhooks/event-grid", new[] {
            new { eventType = GridParser.ValidationEvent, data = new { validationCode = "challenge" } } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"validationResponse\":\"challenge\"");
    }

    [Fact]
    public async Task WebhookRequiresSecret()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/webhooks/event-grid", Array.Empty<object>());
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task DeliveryReportIsMappedAndEarlyReportsAreRetried(bool found, HttpStatusCode expected)
    {
        using var factory = new Factory();
        factory.Store.ApplyReportAsync(Arg.Any<DeliveryReport>(), Arg.Any<CancellationToken>()).Returns(found);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Webhook-Key", "test-key");
        var response = await client.PostAsJsonAsync("/api/webhooks/event-grid", new[] {
            new { eventType = GridParser.DeliveryEvent, data = new { messageId = "op", recipient = "a@example.com", status = "Delivered" } } });
        response.StatusCode.Should().Be(expected);
        await factory.Store.Received().ApplyReportAsync(new DeliveryReport("op", "a@example.com", "Delivered", null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MalformedBatchDoesNotPartiallyApplyReports()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Webhook-Key", "test-key");
        var response = await client.PostAsJsonAsync("/api/webhooks/event-grid", new[] {
            new { eventType = GridParser.DeliveryEvent, data = new { messageId = "op", recipient = "a@example.com", status = "Delivered" } },
            new { eventType = GridParser.DeliveryEvent, data = new { messageId = "op", recipient = "a@example.com", status = "INVALID" } } });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await factory.Store.DidNotReceive().ApplyReportAsync(Arg.Any<DeliveryReport>(), Arg.Any<CancellationToken>());
    }
}

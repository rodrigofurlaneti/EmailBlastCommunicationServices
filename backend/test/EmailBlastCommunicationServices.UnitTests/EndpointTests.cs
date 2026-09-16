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
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Production")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["EventGrid:WebhookKey"] = "test-key" }))
            .ConfigureServices(services =>
            {
                services.RemoveAll<IEmailStore>();
                services.AddSingleton(Store);
                services.RemoveAll<EmailClient>();
                services.AddSingleton(Substitute.For<EmailClient>());
            });
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

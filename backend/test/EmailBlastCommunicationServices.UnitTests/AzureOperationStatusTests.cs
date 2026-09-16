using System.Net;
using System.Text;
using Azure;
using Azure.Core.Pipeline;
using Azure.Communication.Email;
using EmailBlastCommunicationServices.Infrastructure.Integrations.AzureCommunicationServices;
using FluentAssertions;
using Xunit;

namespace EmailBlastCommunicationServices.UnitTests;

public class AzureOperationStatusTests
{
    private sealed class FakeHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            request.Method.Should().Be(HttpMethod.Get);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static EmailClient CreateClient(HttpClient http)
    {
        var options = new EmailClientOptions { Transport = new HttpClientTransport(http) };
        options.Retry.MaxRetries = 0;
        return new EmailClient(new Uri("https://example.communication.azure.com"),
            new AzureKeyCredential(Convert.ToBase64String(new byte[32])), options);
    }

    [Theory]
    [InlineData("NotStarted", false)]
    [InlineData("Running", false)]
    [InlineData("Succeeded", true)]
    [InlineData("Failed", true)]
    [InlineData("Canceled", true)]
    public async Task ReadsEachStateWithoutPollingOrSending(string status, bool completed)
    {
        var id = Guid.NewGuid().ToString();
        using var handler = new FakeHandler(HttpStatusCode.OK, $$"""{"id":"{{id}}","status":"{{status}}"}""");
        using var http = new HttpClient(handler);
        var result = await new AzureEmailOperationStatusReader(CreateClient(http)).GetAsync(id, default);
        result!.Status.Should().Be(status);
        result.HasCompleted.Should().Be(completed);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task MissingOperationReturnsNull()
    {
        using var handler = new FakeHandler(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"NotFound\"}}");
        using var http = new HttpClient(handler);
        (await new AzureEmailOperationStatusReader(CreateClient(http)).GetAsync(Guid.NewGuid().ToString(), default)).Should().BeNull();
    }

    [Fact]
    public async Task ProviderAuthenticationFailureIsNotMistakenForFailedEmail()
    {
        using var handler = new FakeHandler(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"Unauthorized\"}}");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<RequestFailedException>(() => new AzureEmailOperationStatusReader(CreateClient(http)).GetAsync(Guid.NewGuid().ToString(), default));
    }
}

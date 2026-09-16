using Azure;
using Azure.Communication.Email;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace EmailBlastCommunicationServices.UnitTests;

public class EmailTests
{
    private static readonly EmailRequest Valid = new(1, "person@example.com", "Assunto", "Texto", null);
    private readonly IEmailStore store = Substitute.For<IEmailStore>();
    private readonly EmailClient client = Substitute.For<EmailClient>();
    private EmailService Service => new(store, client, new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Email:SenderAddress"] = "sender@example.com" }).Build());

    [Fact]
    public void ValidatesRequiredFieldsAndDatabaseLimits()
    {
        Valid.Validate().Should().BeEmpty();
        (Valid with { SystemId = 0, Recipient = "bad", Subject = "", BodyText = null }).Validate().Should().HaveCount(4);
        (Valid with { Subject = new string('a', 256) }).Validate().Should().ContainKey("Subject");
        (Valid with { BodyText = new string('á', 32768) }).Validate().Should().ContainKey("BodyHtml");
        (Valid with { BodyText = null, BodyHtml = "<p>html</p>" }).Validate().Should().BeEmpty();
    }

    [Fact]
    public async Task PersistsQueuedBeforeAzureAndSentAfterAcceptance()
    {
        var order = new List<string>();
        store.QueueAsync(Valid, Arg.Any<CancellationToken>()).Returns(_ => { order.Add("Queued"); return 42; });
        var operation = Substitute.For<EmailSendOperation>();
        operation.Id.Returns("operation-42");
        client.SendAsync(WaitUntil.Started, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("Azure"); return operation; });
        store.SetSendResultAsync(42, "Sent", "operation-42", null, Arg.Any<CancellationToken>())
            .Returns(_ => { order.Add("Sent"); return Task.CompletedTask; });
        var result = await Service.SendAsync(Valid, default);
        order.Should().Equal("Queued", "Azure", "Sent");
        result.Should().Be(new EmailAccepted(42, "operation-42", "Sent"));
    }

    [Fact]
    public async Task AzureFailurePersistsFailedWithoutLeakingExceptionDetails()
    {
        store.QueueAsync(Valid, Arg.Any<CancellationToken>()).Returns(42);
        client.SendAsync(WaitUntil.Started, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EmailSendOperation>(new RequestFailedException("sensitive")));
        await Assert.ThrowsAsync<RequestFailedException>(() => Service.SendAsync(Valid, default));
        await store.Received(1).SetSendResultAsync(42, Arg.Is("Failed"), Arg.Is<string?>(s => s == null),
            Arg.Is<string>(s => !s.Contains("sensitive")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationStillPersistsFailureWithIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        store.QueueAsync(Valid, Arg.Any<CancellationToken>()).Returns(42);
        client.SendAsync(WaitUntil.Started, Arg.Any<EmailMessage>(), cancellation.Token)
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromException<EmailSendOperation>(new OperationCanceledException(cancellation.Token));
            });
        await Assert.ThrowsAsync<OperationCanceledException>(() => Service.SendAsync(Valid, cancellation.Token));
        await store.Received(1).SetSendResultAsync(42, Arg.Is("Failed"), Arg.Is<string?>(s => s == null),
            Arg.Is<string>(s => s.Contains("indeterminada")), Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task QueueFailureNeverSendsEmail()
    {
        store.QueueAsync(Valid, Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new InvalidOperationException()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.SendAsync(Valid, default));
        await client.DidNotReceive().SendAsync(Arg.Any<WaitUntil>(), Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistenceFailureAfterAcceptanceDoesNotMarkEmailFailed()
    {
        store.QueueAsync(Valid, Arg.Any<CancellationToken>()).Returns(42);
        var operation = Substitute.For<EmailSendOperation>();
        operation.Id.Returns("operation-42");
        client.SendAsync(WaitUntil.Started, Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(operation);
        store.SetSendResultAsync(42, "Sent", "operation-42", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.SendAsync(Valid, default));
        await store.DidNotReceive().SetSendResultAsync(42, "Failed", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

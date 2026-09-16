using EmailBlastCommunicationServices.Application.Interfaces.Messaging;
using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
using EmailBlastCommunicationServices.Application.Commands.ProcessDeliveryReports;
using EmailBlastCommunicationServices.Application.Commands.SendEmail;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Domain.Rules;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace EmailBlastCommunicationServices.UnitTests;

public class ApplicationTests
{
    [Fact]
    public async Task InvalidRequestIsRejectedWithoutPersistenceOrSending()
    {
        var store = Substitute.For<IEmailStore>();
        var sender = Substitute.For<IEmailSender>();
        var service = new SendEmailHandler(store, sender);
        var result = await service.SendAsync(new(0, "bad", "", null, null), default);
        result.Errors.Should().HaveCount(4);
        store.ReceivedCalls().Should().BeEmpty();
        sender.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownSystemIsRejectedByApplication()
    {
        var store = Substitute.For<IEmailStore>();
        var sender = Substitute.For<IEmailSender>();
        var result = await new SendEmailHandler(store, sender).SendAsync(new(99, "a@example.com", "Subject", "Body", null), default);
        result.UnknownSystem.Should().BeTrue();
        await store.DidNotReceive().QueueAsync(Arg.Any<EmailRequest>(), Arg.Any<CancellationToken>());
        sender.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sent", "Delivered", true)]
    [InlineData("OutForDelivery", "Bounced", true)]
    [InlineData("Delivered", "OutForDelivery", false)]
    [InlineData("Delivered", "Delivered", false)]
    [InlineData("Failed", "Delivered", false)]
    [InlineData("Expanded", "OutForDelivery", false)]
    [InlineData("Expanded", "Delivered", true)]
    [InlineData("Sent", "Unknown", false)]
    public void DomainControlsDeliveryTransitions(string current, string next, bool expected) =>
        DeliveryStatus.CanApplyReport(current, next).Should().Be(expected);

    [Fact]
    public async Task InvalidReportBatchIsRejectedBeforeAnyUpdate()
    {
        var store = Substitute.For<IEmailStore>();
        var service = new ProcessDeliveryReportsHandler(store);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyAsync([
            new("op", "a@example.com", "Delivered", null),
            new("op", "a@example.com", "invalid", null)], default));
        store.ReceivedCalls().Should().BeEmpty();
    }
}

using EmailBlastCommunicationServices.Application.Contracts.Emails;
namespace EmailBlastCommunicationServices.Application.Interfaces.Messaging;

public interface IEmailSender
{
    Task<string> SendAsync(EmailRequest request, CancellationToken ct);
}

using EmailBlastCommunicationServices.Application.Interfaces.Messaging;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using Azure;
using Azure.Communication.Email;

namespace EmailBlastCommunicationServices.Infrastructure.Integrations.AzureCommunicationServices;

public sealed class AzureEmailSender(EmailClient client, string senderAddress) : IEmailSender
{
    public async Task<string> SendAsync(EmailRequest request, CancellationToken ct)
    {
        var content = new EmailContent(request.Subject) { PlainText = request.BodyText, Html = request.BodyHtml };
        var message = new EmailMessage(senderAddress, request.Recipient, content);
        var operation = await client.SendAsync(WaitUntil.Started, message, ct);
        return operation.Id;
    }
}

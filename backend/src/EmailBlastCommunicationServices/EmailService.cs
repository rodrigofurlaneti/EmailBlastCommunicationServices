using Azure;
using Azure.Communication.Email;

namespace EmailBlastCommunicationServices;

public sealed class EmailService(IEmailStore store, EmailClient client, IConfiguration configuration)
{
    public async Task<EmailAccepted> SendAsync(EmailRequest request, CancellationToken ct)
    {
        var content = new EmailContent(request.Subject) { PlainText = request.BodyText, Html = request.BodyHtml };
        var message = new EmailMessage(configuration["Email:SenderAddress"], request.Recipient, content);
        var id = await store.QueueAsync(request, ct);
        EmailSendOperation operation;
        try
        {
            operation = await client.SendAsync(WaitUntil.Started, message, ct);
        }
        catch (Exception ex)
        {
            // Persist even when the caller disconnects; never store SDK messages containing request data.
            using var persist = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var reason = ex is OperationCanceledException ? "Envio cancelado; aceitação pelo provedor pode ser indeterminada."
                : "Falha ao solicitar envio ao provedor.";
            await store.SetSendResultAsync(id, "Failed", null, reason, persist.Token);
            throw;
        }
        // A database failure here must not mislabel an accepted email as Failed.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await store.SetSendResultAsync(id, "Sent", operation.Id, null, completion.Token);
        return new(id, operation.Id, "Sent");
    }
}

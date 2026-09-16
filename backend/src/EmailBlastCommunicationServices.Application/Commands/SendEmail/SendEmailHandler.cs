using EmailBlastCommunicationServices.Application.Interfaces.Messaging;
using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Domain.Rules;
namespace EmailBlastCommunicationServices.Application.Commands.SendEmail;

public sealed class SendEmailHandler(IEmailStore store, IEmailSender sender)
{
    public async Task<SendEmailResult> SendAsync(EmailRequest request, CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0) return new(null, errors);
        if (!await store.SystemExistsAsync(request.SystemId, ct))
            return new(null, new() { [nameof(request.SystemId)] = ["SystemId não cadastrado."] }, true);

        var id = await store.QueueAsync(request, ct);
        string operationId;
        try
        {
            operationId = await sender.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            using var persist = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var reason = ex is OperationCanceledException ? "Envio cancelado; aceitação pelo provedor pode ser indeterminada."
                : "Falha ao solicitar envio ao provedor.";
            await store.SetSendResultAsync(id, DeliveryStatus.Failed, null, reason, persist.Token);
            throw;
        }
        // Database failures after acceptance must not mark an accepted email as Failed.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await store.SetSendResultAsync(id, DeliveryStatus.Sent, operationId, null, completion.Token);
        return new(new(id, operationId, DeliveryStatus.Sent), null);
    }

}

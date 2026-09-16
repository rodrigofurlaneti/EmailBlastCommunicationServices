using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Application.Interfaces.Messaging;

namespace EmailBlastCommunicationServices.Infrastructure.Integrations.AzureCommunicationServices;

public sealed class AzureEmailOperationStatusReader(EmailClient client) : IEmailOperationStatusReader
{
    public async Task<ProviderOperationStatus?> GetAsync(string operationId, CancellationToken ct)
    {
        var operation = new EmailSendOperation(operationId, client);
        Response response;
        try
        {
            // A single refresh, not a loop waiting for completion.
            response = await operation.UpdateStatusAsync(ct);
        }
        catch (RequestFailedException) when (operation.HasCompleted && operation.GetRawResponse().Status == 200)
        {
            // SDK throws for Failed/Canceled even though the status lookup succeeded.
            response = operation.GetRawResponse();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        // Value is only available on success; the response also contains pending/failure states.
        var stream = response.ContentStream ?? throw new InvalidOperationException("Azure retornou uma resposta sem conteúdo.");
        // The SDK may dispose its buffered MemoryStream after deserializing the result.
        // ToArray still reads that buffer; no second Azure request is needed.
        using var document = stream is MemoryStream buffer
            ? JsonDocument.Parse(buffer.ToArray())
            : await ParseResponseAsync(stream, ct);
        var status = document.RootElement.GetProperty("status").GetString();
        if (status is not ("NotStarted" or "Running" or "Succeeded" or "Failed" or "Canceled"))
            throw new InvalidOperationException("Azure retornou um status de operação desconhecido.");
        return new(status, status is "Succeeded" or "Failed" or "Canceled");
    }

    private static async Task<JsonDocument> ParseResponseAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek) stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}

using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Application.Interfaces.Messaging;

namespace EmailBlastCommunicationServices.Application.Queries.GetEmailOperationStatus;

public sealed class GetEmailOperationStatusHandler(IEmailOperationStatusReader reader)
{
    public async Task<GetEmailOperationStatusResult> GetAsync(string operationId, CancellationToken ct)
    {
        if (!Guid.TryParse(operationId, out var parsedId) || parsedId == Guid.Empty)
            return new(null, OperationLookupError.InvalidRequest);
        var normalizedId = parsedId.ToString("D");
        var operation = await reader.GetAsync(normalizedId, ct);
        if (operation is null) return new(null, OperationLookupError.OperationNotFound);
        return new(new(normalizedId, operation.Status, operation.HasCompleted));
    }
}

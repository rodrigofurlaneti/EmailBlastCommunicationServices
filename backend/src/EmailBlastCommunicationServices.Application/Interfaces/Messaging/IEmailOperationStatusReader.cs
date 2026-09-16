using EmailBlastCommunicationServices.Application.Contracts.Emails;

namespace EmailBlastCommunicationServices.Application.Interfaces.Messaging;

public interface IEmailOperationStatusReader
{
    Task<ProviderOperationStatus?> GetAsync(string operationId, CancellationToken ct);
}

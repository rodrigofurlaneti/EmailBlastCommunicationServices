using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Domain.ValueObjects;
namespace EmailBlastCommunicationServices.Application.Interfaces.Persistence;

public interface IEmailStore
{
    Task<bool> SystemExistsAsync(int systemId, CancellationToken ct);
    Task<int> QueueAsync(EmailRequest request, CancellationToken ct);
    Task SetSendResultAsync(int id, string status, string? operationId, string? error, CancellationToken ct);
    Task<EmailStatus?> GetAsync(int id, int systemId, CancellationToken ct);
    Task<bool> ApplyReportAsync(DeliveryReport report, CancellationToken ct);
}

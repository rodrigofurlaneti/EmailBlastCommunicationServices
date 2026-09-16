using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
using EmailBlastCommunicationServices.Domain.Rules;
using EmailBlastCommunicationServices.Domain.ValueObjects;
namespace EmailBlastCommunicationServices.Application.Commands.ProcessDeliveryReports;

public sealed class ProcessDeliveryReportsHandler(IEmailStore store)
{
    public async Task<bool> ApplyAsync(IReadOnlyList<DeliveryReport> reports, CancellationToken ct)
    {
        if (reports.Any(report => !DeliveryStatus.IsReportStatus(report.Status)))
            throw new ArgumentException("Status de entrega inválido.", nameof(reports));
        foreach (var report in reports)
            if (!await store.ApplyReportAsync(report, ct)) return false;
        return true;
    }
}

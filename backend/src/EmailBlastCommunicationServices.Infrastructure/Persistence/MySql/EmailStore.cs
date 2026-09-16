using Microsoft.EntityFrameworkCore;
using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Domain.Rules;
using EmailBlastCommunicationServices.Domain.ValueObjects;
using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;
using EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.MySql;

public sealed class EmailStore(EmailBlastDbContext context) : IEmailStore
{
    public Task<bool> SystemExistsAsync(int systemId, CancellationToken ct) =>
        context.Systems.AnyAsync(system => system.Id == systemId, ct);

    public Task<int> QueueAsync(EmailRequest request, CancellationToken ct) =>
        new EmailLogRepository(context).CreateAsync(new EmailLog
        {
            SystemId = request.SystemId, Recipient = request.Recipient!, Subject = request.Subject!,
            BodyText = request.BodyText, BodyHtml = request.BodyHtml
        }, ct);

    public async Task SetSendResultAsync(int id, string status, string? operationId, string? error, CancellationToken ct)
    {
        if (status is not (DeliveryStatus.Sent or DeliveryStatus.Failed))
            throw new ArgumentException("Resultado de envio inválido.", nameof(status));
        var queuedId = await context.DeliveryReportTypes.Where(type => type.StatusName == DeliveryStatus.Queued).Select(type => type.Id).SingleAsync(ct);
        var nextId = await context.DeliveryReportTypes.Where(type => type.StatusName == status).Select(type => type.Id).SingleAsync(ct);
        var affected = await context.EmailLogs.Where(log => log.Id == id && log.DeliveryReportTypeId == queuedId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(log => log.DeliveryReportTypeId, nextId)
                .SetProperty(log => log.OperationId, operationId)
                .SetProperty(log => log.ErrorMessage, error), ct);
        if (affected != 1) throw new InvalidOperationException("Não foi possível persistir o resultado do envio.");
    }

    public Task<EmailStatus?> GetAsync(int id, int systemId, CancellationToken ct) =>
        (from log in context.EmailLogs.AsNoTracking()
         join type in context.DeliveryReportTypes on log.DeliveryReportTypeId equals type.Id
         where log.Id == id && log.SystemId == systemId
         select new EmailStatus(log.Id, log.SystemId, log.DeliveryReportTypeId, type.StatusName,
             log.OperationId, log.CreatedAt, log.UpdatedAt)).SingleOrDefaultAsync(ct);

    public async Task<bool> ApplyReportAsync(DeliveryReport report, CancellationToken ct)
    {
        if (!DeliveryStatus.IsReportStatus(report.Status)) throw new ArgumentException("Status inválido.");
        var logs = context.EmailLogs.Where(log => log.OperationId == report.MessageId && log.Recipient == report.Recipient);
        if (!await logs.AnyAsync(ct)) return false;
        var types = await context.DeliveryReportTypes.AsNoTracking().ToListAsync(ct);
        var nextId = types.Single(type => type.StatusName == report.Status).Id;
        var allowed = types.Where(type => DeliveryStatus.CanApplyReport(type.StatusName, report.Status)).Select(type => type.Id).ToArray();
        // The predicate is part of the atomic database update, preventing terminal-state regressions.
        await logs.Where(log => allowed.Contains(log.DeliveryReportTypeId)).ExecuteUpdateAsync(setters => setters
            .SetProperty(log => log.DeliveryReportTypeId, nextId)
            .SetProperty(log => log.ErrorMessage, report.ErrorMessage), ct);
        return true;
    }
}

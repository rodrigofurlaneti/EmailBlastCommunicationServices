using System.Text;
using Microsoft.EntityFrameworkCore;
using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Domain.Interfaces;
using EmailBlastCommunicationServices.Domain.Queries;
using EmailBlastCommunicationServices.Domain.Rules;
using EmailBlastCommunicationServices.Domain.ValueObjects;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

public sealed class EmailLogRepository(EmailBlastDbContext context) : IEmailLogRepository
{
    public async Task<int> CreateAsync(EmailLog entity, CancellationToken ct)
    {
        Validate(entity);
        if (entity.Id != 0 || entity.OperationId is not null || entity.ErrorMessage is not null)
            throw new ArgumentException("Um novo log deve representar uma intenção ainda não processada.");
        entity.DeliveryReportTypeId = await context.DeliveryReportTypes.Where(type => type.StatusName == DeliveryStatus.Queued)
            .Select(type => type.Id).SingleAsync(ct);
        entity.CreatedAt = null;
        entity.UpdatedAt = null;
        context.EmailLogs.Add(entity);
        try { await context.SaveChangesAsync(ct); return entity.Id; }
        finally { context.Entry(entity).State = EntityState.Detached; }
    }

    public Task<EmailLog?> GetByIdAsync(EmailLogKey key, CancellationToken ct)
    {
        ValidateKey(key);
        return context.EmailLogs.AsNoTracking().SingleOrDefaultAsync(log => log.Id == key.Id && log.SystemId == key.SystemId, ct);
    }

    public async Task<IReadOnlyList<EmailLog>> ListAsync(EmailLogQuery query, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.SystemId);
        RepositoryValidation.Page(query.Offset, query.Limit);
        return await context.EmailLogs.AsNoTracking().Where(log => log.SystemId == query.SystemId)
            .OrderBy(log => log.Id).Skip(query.Offset).Take(query.Limit).ToListAsync(ct);
    }

    public async Task<bool> UpdateAsync(EmailLog entity, CancellationToken ct)
    {
        Validate(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entity.Id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entity.DeliveryReportTypeId);
        var current = await context.EmailLogs.AsNoTracking()
            .SingleOrDefaultAsync(log => log.Id == entity.Id && log.SystemId == entity.SystemId, ct);
        if (current is null) return false;
        var statuses = await context.DeliveryReportTypes.AsNoTracking().Where(type =>
            type.Id == current.DeliveryReportTypeId || type.Id == entity.DeliveryReportTypeId)
            .ToDictionaryAsync(type => type.Id, type => type.StatusName, ct);
        if (!statuses.TryGetValue(entity.DeliveryReportTypeId, out var next))
            throw new ArgumentException("DeliveryReportTypeId não cadastrado.");
        var previous = statuses[current.DeliveryReportTypeId];
        if (previous != next && !(previous == DeliveryStatus.Queued && next is DeliveryStatus.Sent or DeliveryStatus.Failed)
            && !DeliveryStatus.CanApplyReport(previous, next))
            throw new InvalidOperationException("Transição de status não permitida.");
        if (previous != DeliveryStatus.Queued && (current.Recipient != entity.Recipient || current.Subject != entity.Subject ||
            current.BodyText != entity.BodyText || current.BodyHtml != entity.BodyHtml || current.OperationId != entity.OperationId))
            throw new InvalidOperationException("Conteúdo e OperationId não podem mudar após o processamento do envio.");
        if (next == DeliveryStatus.Queued && entity.OperationId is not null ||
            next == DeliveryStatus.Sent && string.IsNullOrWhiteSpace(entity.OperationId))
            throw new ArgumentException("OperationId incompatível com o status solicitado.");
        context.EmailLogs.Attach(current);
        try
        {
            current.DeliveryReportTypeId = entity.DeliveryReportTypeId;
            current.OperationId = entity.OperationId;
            current.Recipient = entity.Recipient;
            current.Subject = entity.Subject;
            current.BodyText = entity.BodyText;
            current.BodyHtml = entity.BodyHtml;
            current.ErrorMessage = entity.ErrorMessage;
            // Status and OperationId are concurrency tokens in the entity configuration.
            await context.SaveChangesAsync(ct);
            return true;
        }
        finally { context.Entry(current).State = EntityState.Detached; }
    }

    public async Task<bool> DeleteAsync(EmailLogKey key, CancellationToken ct)
    {
        ValidateKey(key);
        return await context.EmailLogs.Where(log => log.Id == key.Id && log.SystemId == key.SystemId).ExecuteDeleteAsync(ct) > 0;
    }
    private static void ValidateKey(EmailLogKey key)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(key.Id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(key.SystemId);
    }

    private static void Validate(EmailLog entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (EmailRules.Validate(entity.SystemId, entity.Recipient, entity.Subject, entity.BodyText, entity.BodyHtml).Count > 0)
            throw new ArgumentException("Dados do email inválidos.");
        if (entity.OperationId?.Length > 100 || Encoding.UTF8.GetByteCount(entity.ErrorMessage ?? "") > 65535)
            throw new ArgumentException("OperationId ou ErrorMessage excede o tamanho permitido.");
    }
}

using Dapper;
using MySqlConnector;

namespace EmailBlastCommunicationServices;

public interface IEmailStore
{
    Task<bool> SystemExistsAsync(int systemId, CancellationToken ct);
    Task<int> QueueAsync(EmailRequest request, CancellationToken ct);
    Task SetSendResultAsync(int id, string status, string? operationId, string? error, CancellationToken ct);
    Task<EmailStatus?> GetAsync(int id, int systemId, CancellationToken ct);
    Task<bool> ApplyReportAsync(DeliveryReport report, CancellationToken ct);
}

public sealed class EmailStore(MySqlDataSource source) : IEmailStore
{
    public async Task<bool> SystemExistsAsync(int systemId, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM Systems WHERE Id = @systemId)", new { systemId }, cancellationToken: ct));
    }

    public async Task<int> QueueAsync(EmailRequest request, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO EmailLogs (SystemId, DeliveryReportTypeId, Recipient, Subject, BodyText, BodyHtml)
            VALUES (@SystemId, (SELECT Id FROM DeliveryReportTypes WHERE StatusName = 'Queued'),
                    @Recipient, @Subject, @BodyText, @BodyHtml);
            SELECT LAST_INSERT_ID();
            """, request, cancellationToken: ct));
    }

    public async Task SetSendResultAsync(int id, string status, string? operationId, string? error, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct);
        var affected = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE EmailLogs
            SET DeliveryReportTypeId = (SELECT Id FROM DeliveryReportTypes WHERE StatusName = @status),
                OperationId = @operationId, ErrorMessage = @error
            WHERE Id = @id AND DeliveryReportTypeId = (SELECT Id FROM DeliveryReportTypes WHERE StatusName = 'Queued')
            """, new { id, status, operationId, error }, cancellationToken: ct));
        if (affected != 1) throw new InvalidOperationException("Não foi possível persistir o resultado do envio.");
    }

    public async Task<EmailStatus?> GetAsync(int id, int systemId, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct);
        return await db.QuerySingleOrDefaultAsync<EmailStatus>(new CommandDefinition("""
            SELECT e.Id, e.SystemId, e.DeliveryReportTypeId, t.StatusName AS Status,
                   e.OperationId, e.CreatedAt, e.UpdatedAt
            FROM EmailLogs e JOIN DeliveryReportTypes t ON t.Id = e.DeliveryReportTypeId
            WHERE e.Id = @id AND e.SystemId = @systemId
            """, new { id, systemId }, cancellationToken: ct));
    }

    public async Task<bool> ApplyReportAsync(DeliveryReport report, CancellationToken ct)
    {
        await using var db = await source.OpenConnectionAsync(ct);
        // Early reports are retried by Event Grid until OperationId has been persisted.
        var exists = await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM EmailLogs WHERE OperationId = @MessageId AND Recipient = @Recipient)
            """, report, cancellationToken: ct));
        if (!exists) return false;
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE EmailLogs e JOIN DeliveryReportTypes existingStatus ON existingStatus.Id = e.DeliveryReportTypeId
            SET e.DeliveryReportTypeId = (SELECT Id FROM DeliveryReportTypes WHERE StatusName = @Status),
                e.ErrorMessage = @ErrorMessage
            WHERE e.OperationId = @MessageId AND e.Recipient = @Recipient
              AND existingStatus.StatusName IN ('Queued', 'Sent', 'OutForDelivery', 'Expanded')
              AND NOT (existingStatus.StatusName = 'Expanded' AND @Status = 'OutForDelivery')
            """, report, cancellationToken: ct));
        return true;
    }
}

namespace EmailBlastCommunicationServices.Domain.Entities;

/// <summary>Representa uma linha da tabela EmailLogs, incluindo as chaves estrangeiras.</summary>
public sealed class EmailLog
{
    public int Id { get; set; }
    public int SystemId { get; set; }
    public int DeliveryReportTypeId { get; set; }
    public string? OperationId { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? BodyText { get; set; }
    public string? BodyHtml { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

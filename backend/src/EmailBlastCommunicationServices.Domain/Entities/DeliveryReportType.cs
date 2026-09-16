namespace EmailBlastCommunicationServices.Domain.Entities;

/// <summary>Representa uma linha da tabela DeliveryReportTypes.</summary>
public sealed class DeliveryReportType
{
    public int Id { get; set; }
    public string StatusName { get; set; } = string.Empty;
}

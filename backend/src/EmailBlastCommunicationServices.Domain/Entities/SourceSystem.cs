namespace EmailBlastCommunicationServices.Domain.Entities;

/// <summary>Representa uma linha da tabela Systems.</summary>
public sealed class SourceSystem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; }
}

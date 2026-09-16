namespace EmailBlastCommunicationServices.Application.Contracts.Emails;
public sealed record EmailStatus(int Id, int SystemId, int DeliveryReportTypeId, string Status,
    string? OperationId, DateTime? CreatedAt, DateTime? UpdatedAt);

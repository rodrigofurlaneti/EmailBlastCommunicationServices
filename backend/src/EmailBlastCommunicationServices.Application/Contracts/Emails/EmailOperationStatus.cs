namespace EmailBlastCommunicationServices.Application.Contracts.Emails;

public sealed record EmailOperationStatus(string OperationId, string OperationStatus, bool HasCompleted);

public sealed record ProviderOperationStatus(string Status, bool HasCompleted);

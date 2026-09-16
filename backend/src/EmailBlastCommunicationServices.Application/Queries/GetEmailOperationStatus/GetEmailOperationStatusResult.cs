using EmailBlastCommunicationServices.Application.Contracts.Emails;

namespace EmailBlastCommunicationServices.Application.Queries.GetEmailOperationStatus;

public enum OperationLookupError { None, InvalidRequest, OperationNotFound }
public sealed record GetEmailOperationStatusResult(EmailOperationStatus? Value, OperationLookupError Error = OperationLookupError.None);

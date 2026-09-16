namespace EmailBlastCommunicationServices.Domain.Queries;

public sealed record EmailLogQuery(int SystemId, int Offset = 0, int Limit = 100);

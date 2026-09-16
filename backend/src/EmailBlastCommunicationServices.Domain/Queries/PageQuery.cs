namespace EmailBlastCommunicationServices.Domain.Queries;

public sealed record PageQuery(int Offset = 0, int Limit = 100);

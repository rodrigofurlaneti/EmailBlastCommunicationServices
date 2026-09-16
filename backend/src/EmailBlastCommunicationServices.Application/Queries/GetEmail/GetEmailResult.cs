using EmailBlastCommunicationServices.Application.Contracts.Emails;
namespace EmailBlastCommunicationServices.Application.Queries.GetEmail;
public sealed record GetEmailResult(EmailStatus? Email, bool InvalidRequest);

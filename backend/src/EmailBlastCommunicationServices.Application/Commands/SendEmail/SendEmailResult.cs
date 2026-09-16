using EmailBlastCommunicationServices.Application.Contracts.Emails;
namespace EmailBlastCommunicationServices.Application.Commands.SendEmail;
public sealed record SendEmailResult(EmailAccepted? Email, Dictionary<string, string[]>? Errors, bool UnknownSystem = false);

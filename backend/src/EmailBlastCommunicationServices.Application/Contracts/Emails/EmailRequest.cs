using EmailBlastCommunicationServices.Domain.Rules;
namespace EmailBlastCommunicationServices.Application.Contracts.Emails;

public sealed record EmailRequest(int SystemId, string? Recipient, string? Subject, string? BodyText, string? BodyHtml)
{
    public Dictionary<string, string[]> Validate() => EmailRules.Validate(SystemId, Recipient, Subject, BodyText, BodyHtml);
}

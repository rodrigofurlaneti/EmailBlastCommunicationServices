using System.Net.Mail;
using System.Text;
namespace EmailBlastCommunicationServices.Domain.Rules;

public static class EmailRules
{
    public static Dictionary<string, string[]> Validate(int SystemId, string? Recipient, string? Subject, string? BodyText, string? BodyHtml)
    {
        var errors = new Dictionary<string, string[]>();
        if (SystemId <= 0) errors[nameof(SystemId)] = ["Informe um SystemId válido."];
        if (string.IsNullOrWhiteSpace(Recipient) || Recipient.Length > 255 ||
            !MailAddress.TryCreate(Recipient, out var address) || address.Address != Recipient)
            errors[nameof(Recipient)] = ["Informe um único email válido com até 255 caracteres."];
        if (string.IsNullOrWhiteSpace(Subject) || Subject.Length > 255)
            errors[nameof(Subject)] = ["Informe um assunto com até 255 caracteres."];
        if (string.IsNullOrWhiteSpace(BodyText) && string.IsNullOrWhiteSpace(BodyHtml))
            errors[nameof(BodyText)] = ["Informe BodyText ou BodyHtml."];
        if (Encoding.UTF8.GetByteCount(BodyText ?? "") > 65535 || Encoding.UTF8.GetByteCount(BodyHtml ?? "") > 65535)
            errors[nameof(BodyHtml)] = ["Cada corpo deve ocupar no máximo 65535 bytes UTF-8 (MySQL TEXT)."];
        return errors;
    }
}

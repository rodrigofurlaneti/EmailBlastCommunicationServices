using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace EmailBlastCommunicationServices;

public sealed record EmailRequest(int SystemId, string? Recipient, string? Subject, string? BodyText, string? BodyHtml)
{
    public Dictionary<string, string[]> Validate()
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

public sealed record EmailAccepted(int Id, string OperationId, string Status);
public sealed record EmailStatus(int Id, int SystemId, int DeliveryReportTypeId, string Status,
    string? OperationId, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record GridEvent(string? EventType, JsonElement Data);
public sealed record DeliveryReport(string MessageId, string Recipient, string Status, string? ErrorMessage);

public static class GridParser
{
    public const string ValidationEvent = "Microsoft.EventGrid.SubscriptionValidationEvent";
    public const string DeliveryEvent = "Microsoft.Communication.EmailDeliveryReportReceived";
    public static string? Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static DeliveryReport? Parse(JsonElement data)
    {
        var id = Text(data, "messageId");
        var recipient = Text(data, "recipient");
        var status = Text(data, "status");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || string.IsNullOrWhiteSpace(recipient) ||
            recipient.Length > 255 || status is not ("OutForDelivery" or "Delivered" or "Bounced" or
            "Failed" or "Dropped" or "Suppressed" or "Quarantined" or "FilteredSpam" or "Expanded")) return null;
        var error = data.TryGetProperty("deliveryStatusDetails", out var details) ? Text(details, "statusMessage") : null;
        return new(id, recipient, status, status == "Delivered" ? null : error);
    }
}

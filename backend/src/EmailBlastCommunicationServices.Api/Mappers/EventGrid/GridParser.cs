using EmailBlastCommunicationServices.Domain.Rules;
using EmailBlastCommunicationServices.Domain.ValueObjects;
using System.Text.Json;
namespace EmailBlastCommunicationServices.Api.Mappers.EventGrid;



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
            recipient.Length > 255 || !DeliveryStatus.IsReportStatus(status)) return null;
        var error = data.TryGetProperty("deliveryStatusDetails", out var details) ? Text(details, "statusMessage") : null;
        return new(id, recipient, status, status == "Delivered" ? null : error);
    }
}

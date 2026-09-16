namespace EmailBlastCommunicationServices.Domain.ValueObjects;
public sealed record DeliveryReport(string MessageId, string Recipient, string Status, string? ErrorMessage);

using System.Text.Json;
namespace EmailBlastCommunicationServices.Api.Contracts.EventGrid;
public sealed record GridEvent(string? EventType, JsonElement Data);

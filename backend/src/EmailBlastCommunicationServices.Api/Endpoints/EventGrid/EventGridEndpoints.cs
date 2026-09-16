using EmailBlastCommunicationServices.Api.Contracts.EventGrid;
using EmailBlastCommunicationServices.Api.Mappers.EventGrid;
using EmailBlastCommunicationServices.Application.Commands.ProcessDeliveryReports;
using EmailBlastCommunicationServices.Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;

namespace EmailBlastCommunicationServices.Api.Endpoints.EventGrid;

public static class EventGridEndpoints
{
    public static void MapEventGridEndpoints(this WebApplication app)
    {
        app.MapPost("/api/webhooks/event-grid", async (GridEvent?[] events, HttpRequest request,
            ProcessDeliveryReportsHandler service, CancellationToken ct) =>
        {
            var key = app.Configuration["EventGrid:WebhookKey"];
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configure EventGrid:WebhookKey.");
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Headers["X-Webhook-Key"].ToString()))))
                return Results.Unauthorized();
            if (events.Length == 0 || events.Any(e => e is null || string.IsNullOrWhiteSpace(e.EventType)))
                return Results.BadRequest(new { error = "Informe um lote de eventos válido." });
            if (events.Length == 1 && events[0]!.EventType == GridParser.ValidationEvent)
            {
                var code = GridParser.Text(events[0]!.Data, "validationCode");
                return string.IsNullOrWhiteSpace(code) ? Results.BadRequest() : Results.Ok(new { validationResponse = code });
            }
            var reports = new List<DeliveryReport>();
            foreach (var item in events)
            {
                if (item!.EventType == GridParser.ValidationEvent) return Results.BadRequest();
                if (item.EventType != GridParser.DeliveryEvent) continue;
                var report = GridParser.Parse(item.Data);
                if (report is null) return Results.BadRequest(new { error = "Relatório de entrega inválido." });
                reports.Add(report);
            }
            return await service.ApplyAsync(reports, ct) ? Results.Ok() : Results.StatusCode(503);
        });
    }
}

using System.Security.Cryptography;
using System.Text;
using Azure.Communication.Email;
using EmailBlastCommunicationServices;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true).AddEnvironmentVariables();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(_ => new MySqlDataSourceBuilder(
    Required(builder.Configuration, "ConnectionStrings:MySql")).Build());
builder.Services.AddSingleton(_ => new EmailClient(Required(builder.Configuration, "COMMUNICATION_SERVICES_CONNECTION_STRING")));
builder.Services.AddScoped<IEmailStore, EmailStore>();
builder.Services.AddScoped<EmailService>();
var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/emails", async (EmailRequest request, IEmailStore store, EmailService service, CancellationToken ct) =>
{
    var errors = request.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    if (!await store.SystemExistsAsync(request.SystemId, ct))
        return Results.BadRequest(new { error = "SystemId não cadastrado." });
    Required(app.Configuration, "Email:SenderAddress");
    var result = await service.SendAsync(request, ct);
    return Results.Accepted($"/api/emails/{result.Id}?systemId={request.SystemId}", result);
});

app.MapGet("/api/emails/{id:int}", async (int id, int systemId, IEmailStore store, CancellationToken ct) =>
{
    if (id <= 0 || systemId <= 0 || !await store.SystemExistsAsync(systemId, ct))
        return Results.BadRequest(new { error = "Id ou SystemId inválido." });
    var status = await store.GetAsync(id, systemId, ct);
    return status is null ? Results.NotFound() : Results.Ok(status);
});

app.MapPost("/api/webhooks/event-grid", async (GridEvent?[] events, HttpRequest request, IEmailStore store, CancellationToken ct) =>
{
    var key = Required(app.Configuration, "EventGrid:WebhookKey");
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
    foreach (var report in reports)
        if (!await store.ApplyReportAsync(report, ct)) return Results.StatusCode(503);
    return Results.Ok();
});
app.Run();

static string Required(IConfiguration configuration, string key) =>
    !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! :
    throw new InvalidOperationException($"Configure {key} antes de utilizar este recurso.");

public partial class Program;

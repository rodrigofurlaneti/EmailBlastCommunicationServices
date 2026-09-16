using EmailBlastCommunicationServices.Application.Commands.SendEmail;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Application.Queries.GetEmail;
namespace EmailBlastCommunicationServices.Api.Endpoints.Emails;

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        app.MapPost("/api/emails", async (EmailRequest request, SendEmailHandler service, CancellationToken ct) =>
        {
            var result = await service.SendAsync(request, ct);
            if (result.UnknownSystem) return Results.BadRequest(new { error = "SystemId não cadastrado." });
            if (result.Errors is not null) return Results.ValidationProblem(result.Errors);
            return Results.Accepted($"/api/emails/{result.Email!.Id}?systemId={request.SystemId}", result.Email);
        });

        app.MapGet("/api/emails/{id:int}", async (int id, int systemId, GetEmailHandler service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(id, systemId, ct);
            if (result.InvalidRequest) return Results.BadRequest(new { error = "Id ou SystemId inválido." });
            return result.Email is null ? Results.NotFound() : Results.Ok(result.Email);
        });
    }
}

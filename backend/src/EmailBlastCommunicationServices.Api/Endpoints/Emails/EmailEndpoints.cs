using EmailBlastCommunicationServices.Application.Commands.SendEmail;
using EmailBlastCommunicationServices.Application.Contracts.Emails;
using EmailBlastCommunicationServices.Application.Queries.GetEmail;
using EmailBlastCommunicationServices.Application.Queries.GetEmailOperationStatus;
namespace EmailBlastCommunicationServices.Api.Endpoints.Emails;

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        app.MapGet("/api/emails/operations/{operationId}", async (string operationId,
            GetEmailOperationStatusHandler handler, CancellationToken ct) =>
        {
            var result = await handler.GetAsync(operationId, ct);
            return result.Error switch
            {
                OperationLookupError.InvalidRequest => Results.BadRequest(new { error = "OperationId deve ser um GUID válido." }),
                OperationLookupError.OperationNotFound => Results.NotFound(new { error = "A operação não está disponível no Azure." }),
                _ => Results.Ok(result.Value)
            };
        })
        .WithTags("Emails")
        .WithSummary("Consultar a operação de envio diretamente no Azure")
        .WithDescription("Informe o OperationId retornado no envio. Não exige SystemId. Succeeded não confirma entrega. Esta consulta não modifica o banco.")
        .Produces<EmailOperationStatus>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError);
        app.MapPost("/api/emails", async (EmailRequest request, SendEmailHandler service, CancellationToken ct) =>
        {
            var result = await service.SendAsync(request, ct);
            if (result.UnknownSystem) return Results.BadRequest(new { error = "SystemId não cadastrado." });
            if (result.Errors is not null) return Results.ValidationProblem(result.Errors);
            return Results.Accepted($"/api/emails/{result.Email!.Id}?systemId={request.SystemId}", result.Email);
        })
        .WithTags("Emails")
        .WithSummary("Enviar um email")
        .WithDescription("Informe um SystemId cadastrado e um destinatário real. Retorna 202 após aceitação pelo Azure; o webhook não é necessário para testar o envio.")
        .Produces<EmailAccepted>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        app.MapGet("/api/emails/{id:int}", async (int id, int systemId, GetEmailHandler service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(id, systemId, ct);
            if (result.InvalidRequest) return Results.BadRequest(new { error = "Id ou SystemId inválido." });
            return result.Email is null ? Results.NotFound() : Results.Ok(result.Email);
        })
        .WithTags("Emails")
        .WithSummary("Consultar o status de um email")
        .Produces<EmailStatus>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}

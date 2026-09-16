using EmailBlastCommunicationServices.Application.Contracts.Emails;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EmailBlastCommunicationServices.Api.Configuration;

public sealed class EmailRequestExampleFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(EmailRequest)) return;
        schema.Example = new OpenApiObject
        {
            ["systemId"] = new OpenApiInteger(1),
            ["recipient"] = new OpenApiString("destinatario@example.com"),
            ["subject"] = new OpenApiString("Teste de envio"),
            ["bodyText"] = new OpenApiString("Olá! Este é um email de teste."),
            ["bodyHtml"] = new OpenApiString("<p>Olá! Este é um email de teste.</p>")
        };
    }
}

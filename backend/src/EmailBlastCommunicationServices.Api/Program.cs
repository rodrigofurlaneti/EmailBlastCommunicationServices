using EmailBlastCommunicationServices.Api.Endpoints.Emails;
using EmailBlastCommunicationServices.Api.Endpoints.EventGrid;
using EmailBlastCommunicationServices.Application.Commands.ProcessDeliveryReports;
using EmailBlastCommunicationServices.Application.Commands.SendEmail;
using EmailBlastCommunicationServices.Application.Queries.GetEmail;
using EmailBlastCommunicationServices.Infrastructure;
using EmailBlastCommunicationServices.Api.Configuration;
using Microsoft.OpenApi.Models;
using EmailBlastCommunicationServices.Application.Queries.GetEmailOperationStatus;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "EmailBlast Communication Services", Version = "v1" });
    options.SchemaFilter<EmailRequestExampleFilter>();
});
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<SendEmailHandler>();
builder.Services.AddScoped<GetEmailHandler>();
builder.Services.AddScoped<GetEmailOperationStatusHandler>();
builder.Services.AddScoped<ProcessDeliveryReportsHandler>();
var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("v1/swagger.json", "EmailBlast v1"));
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapEmailEndpoints();
app.MapEventGridEndpoints();
app.Run();

public partial class Program;

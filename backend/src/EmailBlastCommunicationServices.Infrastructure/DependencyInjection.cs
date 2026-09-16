using EmailBlastCommunicationServices.Application.Interfaces.Messaging;
using EmailBlastCommunicationServices.Application.Interfaces.Persistence;
using EmailBlastCommunicationServices.Infrastructure.Integrations.AzureCommunicationServices;
using EmailBlastCommunicationServices.Infrastructure.Persistence.MySql;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;
using EmailBlastCommunicationServices.Domain.Interfaces;
using EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

namespace EmailBlastCommunicationServices.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EmailBlastDbContext>(options => options.UseMySql(
            Required(configuration, "ConnectionStrings:MySql"),
            new MySqlServerVersion(Version.Parse(configuration["Database:ServerVersion"] ?? "8.0.0"))));
        services.AddSingleton(_ => new EmailClient(Required(configuration, "COMMUNICATION_SERVICES_CONNECTION_STRING")));
        services.AddScoped<IEmailStore, EmailStore>();
        services.AddScoped<ISourceSystemRepository, SourceSystemRepository>();
        services.AddScoped<IDeliveryReportTypeRepository, DeliveryReportTypeRepository>();
        services.AddScoped<IEmailLogRepository, EmailLogRepository>();
        services.AddScoped<IEmailSender>(provider => new AzureEmailSender(
            provider.GetRequiredService<EmailClient>(), Required(configuration, "Email:SenderAddress")));
        return services;
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key]) ? configuration[key]! :
        throw new InvalidOperationException($"Configure {key} antes de utilizar este recurso.");
}

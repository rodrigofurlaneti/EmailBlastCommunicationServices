using EmailBlastCommunicationServices.Application.Commands.SendEmail;
using EmailBlastCommunicationServices.Domain.Rules;
using EmailBlastCommunicationServices.Infrastructure.Persistence.MySql;
using System.Reflection;
using Xunit;

namespace EmailBlastCommunicationServices.ArchTests;

public class DependencyTests
{
    [Fact]
    public void DomainDependsOnlyOnBaseLibraries() => Assert.All(
        typeof(EmailRules).Assembly.GetReferencedAssemblies(), reference => Assert.True(IsBaseLibrary(reference)));

    [Fact]
    public void ApplicationDependsOnlyOnDomainAndBaseLibraries() => Assert.All(
        typeof(SendEmailHandler).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(IsBaseLibrary(reference) || reference.Name == typeof(EmailRules).Assembly.GetName().Name));

    [Fact]
    public void InfrastructureDoesNotDependOnApi() => Assert.DoesNotContain(
        typeof(EmailStore).Assembly.GetReferencedAssemblies(), reference => reference.Name!.EndsWith(".Api"));

    [Fact]
    public void ApiUsesInfrastructureOnlyForComposition()
    {
        var references = typeof(Program).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name!.StartsWith("Azure.") ||
            reference.Name == "Dapper" || reference.Name == "MySqlConnector");
    }

    private static bool IsBaseLibrary(AssemblyName reference) =>
        reference.Name is "System" or "netstandard" || reference.Name!.StartsWith("System.");
}

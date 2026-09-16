using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EmailBlastCommunicationServices.UnitTests;

public class PersistenceModelTests
{
    private static EmailBlastDbContext CreateContext() => new(new DbContextOptionsBuilder<EmailBlastDbContext>()
        .UseMySql("Server=localhost;Database=model_test", new MySqlServerVersion(new Version(8, 0, 0))).Options);

    [Fact]
    public void MapsExistingSchemaAndRestrictsForeignKeyDeletion()
    {
        using var context = CreateContext();
        context.Model.FindEntityType(typeof(SourceSystem))!.GetTableName().Should().Be("Systems");
        context.Model.FindEntityType(typeof(DeliveryReportType))!.GetTableName().Should().Be("DeliveryReportTypes");
        var log = context.Model.FindEntityType(typeof(EmailLog))!;
        log.GetTableName().Should().Be("EmailLogs");
        log.GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
            "Id", "SystemId", "DeliveryReportTypeId", "OperationId", "Recipient", "Subject",
            "BodyText", "BodyHtml", "ErrorMessage", "CreatedAt", "UpdatedAt");
        log.GetForeignKeys().Should().HaveCount(2).And.OnlyContain(key => key.DeleteBehavior == DeleteBehavior.Restrict);
        log.FindProperty(nameof(EmailLog.Recipient))!.GetMaxLength().Should().Be(255);
        log.FindProperty(nameof(EmailLog.BodyHtml))!.GetColumnType().Should().Be("text");
        log.FindProperty(nameof(EmailLog.CreatedAt))!.IsNullable.Should().BeTrue();
        log.FindProperty(nameof(EmailLog.UpdatedAt))!.GetDefaultValueSql().Should().Be("CURRENT_TIMESTAMP");
    }

    [Fact]
    public void UsesStatusAndOperationIdAsConcurrencyTokens()
    {
        using var context = CreateContext();
        var log = context.Model.FindEntityType(typeof(EmailLog))!;
        log.FindProperty(nameof(EmailLog.DeliveryReportTypeId))!.IsConcurrencyToken.Should().BeTrue();
        log.FindProperty(nameof(EmailLog.OperationId))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void ProviderTranslatesScopedStatusQueryWithoutConnecting()
    {
        using var context = CreateContext();
        var query = from log in context.EmailLogs
                    join type in context.DeliveryReportTypes on log.DeliveryReportTypeId equals type.Id
                    where log.SystemId == 7 && log.Id == 42
                    select new { log.Id, type.StatusName };
        var sql = query.ToQueryString();
        sql.Should().Contain("EmailLogs").And.Contain("DeliveryReportTypes").And.Contain("SystemId");
    }
}

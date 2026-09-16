using EmailBlastCommunicationServices.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;
using EmailBlastCommunicationServices.Infrastructure.Persistence.Context;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using EmailBlastCommunicationServices.Domain.Entities;
using EmailBlastCommunicationServices.Infrastructure.Persistence.MySql.Repositories;

namespace EmailBlastCommunicationServices.IntegrationTests;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION_STRING")))
            Skip = "Configure TEST_MYSQL_CONNECTION_STRING para um servidor MySQL 8 isolado de testes.";
    }
}

public class DatabaseTests
{
    [MySqlFact]
    public async Task PersistsLifecycleAndProtectsTerminalStatusAndSystemScope()
    {
        var database = "emailblast_test_" + Guid.NewGuid().ToString("N");
        var settings = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TEST_MYSQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Configure a conexão de testes."))
            { Database = "" };
        await using var admin = new MySqlConnection(settings.ConnectionString);
        await admin.OpenAsync();
        try
        {
            foreach (var script in new[] { "createdatabase.sql", "002_delivery_report_types.sql" })
            {
                var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "sql", script));
                await using var command = new MySqlCommand(sql.Replace("emailblastdb", database, StringComparison.Ordinal), admin);
                await command.ExecuteNonQueryAsync();
            }
            settings.Database = database;
            await using var context = new EmailBlastDbContext(new DbContextOptionsBuilder<EmailBlastDbContext>()
                .UseMySql(settings.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))).Options);
            var store = new EmailStore(context);
            (await store.SystemExistsAsync(1, default)).Should().BeTrue();
            (await store.SystemExistsAsync(999, default)).Should().BeFalse();
            var id = await store.QueueAsync(new(1, "a@example.com", "O'Reilly", "Body", null), default);
            id.Should().BeGreaterThan(0);
            (await store.GetAsync(id, 1, default))!.Status.Should().Be("Queued");
            await store.SetSendResultAsync(id, "Sent", "operation", null, default);
            var sent = await store.GetAsync(id, 1, default);
            sent!.Status.Should().Be("Sent");
            sent.OperationId.Should().Be("operation");
            (await store.GetAsync(id, 999, default)).Should().BeNull();
            (await store.ApplyReportAsync(new("operation", "other@example.com", "Delivered", null), default)).Should().BeFalse();
            await store.ApplyReportAsync(new("operation", "a@example.com", "Delivered", null), default);
            await store.ApplyReportAsync(new("operation", "a@example.com", "Delivered", null), default);
            await store.ApplyReportAsync(new("operation", "a@example.com", "OutForDelivery", null), default);
            (await store.GetAsync(id, 1, default))!.Status.Should().Be("Delivered");
            var failedId = await store.QueueAsync(new(1, "a@example.com", "Failure", null, "<p>Body</p>"), default);
            await store.SetSendResultAsync(failedId, "Failed", null, "Provider failure", default);
            (await store.GetAsync(failedId, 1, default))!.Status.Should().Be("Failed");

            var systems = new SourceSystemRepository(context);
            var types = new DeliveryReportTypeRepository(context);
            var logs = new EmailLogRepository(context);
            var system = new SourceSystem { Name = "Repository test", Description = "Test" };
            system.Id = await systems.CreateAsync(system, default);
            (await systems.GetByIdAsync(system.Id, default))!.Name.Should().Be(system.Name);
            system.Description = "Updated";
            (await systems.UpdateAsync(system, default)).Should().BeTrue();
            (await systems.GetByIdAsync(system.Id, default))!.Description.Should().Be("Updated");
            (await systems.ListAsync(new(0, 100), default)).Should().Contain(s => s.Id == system.Id);

            var type = new DeliveryReportType { StatusName = "CustomTestStatus" };
            type.Id = await types.CreateAsync(type, default);
            type.StatusName = "RenamedTestStatus";
            (await types.UpdateAsync(type, default)).Should().BeTrue();
            (await types.GetByIdAsync(type.Id, default))!.StatusName.Should().Be(type.StatusName);
            (await types.ListAsync(new(0, 100), default)).Should().Contain(t => t.Id == type.Id);
            (await types.DeleteAsync(type.Id, default)).Should().BeTrue();
            (await types.GetByIdAsync(type.Id, default)).Should().BeNull();

            var log = new EmailLog { SystemId = system.Id, Recipient = "a@example.com", Subject = "CRUD", BodyText = "Text" };
            log.Id = await logs.CreateAsync(log, default);
            log = (await logs.GetByIdAsync(new(log.Id, system.Id), default))!;
            log.CreatedAt.Should().NotBeNull();
            log.BodyText.Should().Be("Text");
            (await logs.ListAsync(new(system.Id), default)).Should().ContainSingle();
            (await logs.GetByIdAsync(new(log.Id, 1), default)).Should().BeNull();
            (await logs.DeleteAsync(new(log.Id, 1), default)).Should().BeFalse();
            var wrongScope = new EmailLog { Id = log.Id, SystemId = 1, DeliveryReportTypeId = log.DeliveryReportTypeId,
                Recipient = log.Recipient, Subject = log.Subject, BodyText = log.BodyText };
            (await logs.UpdateAsync(wrongScope, default)).Should().BeFalse();
            log.Subject = "Updated subject";
            (await logs.UpdateAsync(log, default)).Should().BeTrue();
            (await logs.GetByIdAsync(new(log.Id, system.Id), default))!.Subject.Should().Be("Updated subject");
            await Assert.ThrowsAsync<MySqlException>(() => systems.DeleteAsync(system.Id, default));
            await Assert.ThrowsAsync<MySqlException>(() => types.DeleteAsync(log.DeliveryReportTypeId, default));
            var queuedId = log.DeliveryReportTypeId;
            log.DeliveryReportTypeId = (await types.ListAsync(new(), default)).Single(t => t.StatusName == "Sent").Id;
            log.OperationId = "crud-operation";
            (await logs.UpdateAsync(log, default)).Should().BeTrue();
            log.DeliveryReportTypeId = queuedId;
            await Assert.ThrowsAsync<InvalidOperationException>(() => logs.UpdateAsync(log, default));
            (await logs.DeleteAsync(new(log.Id, system.Id), default)).Should().BeTrue();
            (await logs.GetByIdAsync(new(log.Id, system.Id), default)).Should().BeNull();
            (await systems.DeleteAsync(system.Id, default)).Should().BeTrue();
            (await systems.GetByIdAsync(system.Id, default)).Should().BeNull();
        }
        finally
        {
            // Only the unique database created by this test can be removed.
            await using var command = new MySqlCommand($"DROP DATABASE IF EXISTS `{database}`", admin);
            await command.ExecuteNonQueryAsync();
        }
    }
}

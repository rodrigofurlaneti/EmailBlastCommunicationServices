using Dapper;
using FluentAssertions;
using MySqlConnector;
using Xunit;

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
                await admin.ExecuteAsync(sql.Replace("emailblastdb", database, StringComparison.Ordinal));
            }
            settings.Database = database;
            await using var source = new MySqlDataSourceBuilder(settings.ConnectionString).Build();
            var store = new EmailStore(source);
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
        }
        finally
        {
            // Only the unique database created by this test can be removed.
            await admin.ExecuteAsync($"DROP DATABASE IF EXISTS `{database}`");
        }
    }
}

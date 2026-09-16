using EmailBlastCommunicationServices.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.Context;

public sealed class EmailBlastDbContext(DbContextOptions<EmailBlastDbContext> options) : DbContext(options)
{
    public DbSet<SourceSystem> Systems => Set<SourceSystem>();
    public DbSet<DeliveryReportType> DeliveryReportTypes => Set<DeliveryReportType>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EmailBlastDbContext).Assembly);
    }
}

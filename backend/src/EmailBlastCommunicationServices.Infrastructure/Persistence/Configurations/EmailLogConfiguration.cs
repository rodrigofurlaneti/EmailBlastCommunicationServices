using EmailBlastCommunicationServices.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.Configurations;

public sealed class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("EmailLogs");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
        builder.Property(entity => entity.DeliveryReportTypeId).IsConcurrencyToken();
        builder.Property(entity => entity.OperationId).HasMaxLength(100).IsConcurrencyToken();
        builder.Property(entity => entity.Recipient).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.Subject).HasMaxLength(255).IsRequired();
        builder.Property(entity => entity.BodyText).HasColumnType("text");
        builder.Property(entity => entity.BodyHtml).HasColumnType("text");
        builder.Property(entity => entity.ErrorMessage).HasColumnType("text");
        builder.Property(entity => entity.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(entity => entity.UpdatedAt).HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(entity => entity.Recipient).HasDatabaseName("idx_recipient");
        builder.HasIndex(entity => entity.OperationId).HasDatabaseName("idx_operation_id");
        builder.HasOne<SourceSystem>().WithMany().HasForeignKey(entity => entity.SystemId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_system");
        builder.HasOne<DeliveryReportType>().WithMany().HasForeignKey(entity => entity.DeliveryReportTypeId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_status");
    }
}

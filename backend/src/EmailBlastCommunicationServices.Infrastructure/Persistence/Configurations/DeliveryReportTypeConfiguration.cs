using EmailBlastCommunicationServices.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.Configurations;

public sealed class DeliveryReportTypeConfiguration : IEntityTypeConfiguration<DeliveryReportType>
{
    public void Configure(EntityTypeBuilder<DeliveryReportType> builder)
    {
        builder.ToTable("DeliveryReportTypes");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
        builder.Property(entity => entity.StatusName).HasMaxLength(50).IsRequired();
        builder.HasIndex(entity => entity.StatusName).IsUnique().HasDatabaseName("StatusName");
    }
}

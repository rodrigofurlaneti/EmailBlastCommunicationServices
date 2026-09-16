using EmailBlastCommunicationServices.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmailBlastCommunicationServices.Infrastructure.Persistence.Configurations;

public sealed class SourceSystemConfiguration : IEntityTypeConfiguration<SourceSystem>
{
    public void Configure(EntityTypeBuilder<SourceSystem> builder)
    {
        builder.ToTable("Systems");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedOnAdd();
        builder.Property(entity => entity.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(entity => entity.Name).IsUnique().HasDatabaseName("Name");
        builder.Property(entity => entity.Description).HasMaxLength(255);
        builder.Property(entity => entity.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}

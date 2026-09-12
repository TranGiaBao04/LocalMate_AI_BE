using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("TAGS", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_TAGS_Type",
                "\"Type\" IN ('Interest', 'TravelStyle')"));

        builder.HasKey(tag => tag.Id);

        builder.Property(tag => tag.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(tag => tag.Name)
            .HasColumnType("character varying(100)")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(tag => tag.Type)
            .HasConversion<string>()
            .HasColumnType("character varying(20)")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(tag => tag.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(tag => tag.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(tag => tag.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.HasIndex(tag => new { tag.Type, tag.Name })
            .IsUnique()
            .HasDatabaseName("UX_TAGS_Type_Name");
    }
}

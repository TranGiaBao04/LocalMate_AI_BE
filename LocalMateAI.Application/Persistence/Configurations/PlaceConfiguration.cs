using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Application.Persistence.Configurations;

public sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Address)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.Location)
            .HasColumnType("geometry (point, 4326)")
            .IsRequired();

        builder.Property(x => x.EstimatedCostMin)
            .HasColumnType("numeric(12,0)");

        builder.Property(x => x.EstimatedCostMax)
            .HasColumnType("numeric(12,0)");

        builder.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Spatial index (GiST) là task riêng BE-30, hạn 2026-09-28 — chưa thêm ở đây.
    }
}

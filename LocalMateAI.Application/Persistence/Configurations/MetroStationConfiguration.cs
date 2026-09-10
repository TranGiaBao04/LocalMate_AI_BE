using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Application.Persistence.Configurations;

public sealed class MetroStationConfiguration : IEntityTypeConfiguration<MetroStation>
{
    public void Configure(EntityTypeBuilder<MetroStation> builder)
    {
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Location)
            .HasColumnType("geometry (point, 4326)")
            .IsRequired();

        // Spatial index (GiST) là task riêng BE-30, hạn 2026-09-28 — chưa thêm ở đây.
    }
}

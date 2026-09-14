using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

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

        builder.HasIndex(x => x.Location)
            .HasMethod("gist");
    }
}

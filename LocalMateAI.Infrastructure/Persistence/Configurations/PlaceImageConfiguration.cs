using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlaceImageConfiguration : IEntityTypeConfiguration<PlaceImage>
{
    public void Configure(EntityTypeBuilder<PlaceImage> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Url)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.Caption)
            .HasMaxLength(500);

        builder.Property(x => x.DisplayOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne(x => x.Place)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.PlaceId, x.DisplayOrder });
        builder.HasIndex(x => new { x.PlaceId, x.IsPrimary });
    }
}

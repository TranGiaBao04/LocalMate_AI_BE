using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class CuratedItineraryConfiguration : IEntityTypeConfiguration<CuratedItinerary>
{
    public void Configure(EntityTypeBuilder<CuratedItinerary> builder)
    {
        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.CoverImageUrl)
            .HasMaxLength(500);

        builder.Property(x => x.EstimatedCostMin)
            .HasColumnType("numeric(12,0)");

        builder.Property(x => x.EstimatedCostMax)
            .HasColumnType("numeric(12,0)");

        builder.HasMany(x => x.Items)
            .WithOne(x => x.CuratedItinerary)
            .HasForeignKey(x => x.CuratedItineraryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

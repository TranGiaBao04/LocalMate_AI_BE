using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class CuratedItineraryItemConfiguration : IEntityTypeConfiguration<CuratedItineraryItem>
{
    public void Configure(EntityTypeBuilder<CuratedItineraryItem> builder)
    {
        builder.HasOne(x => x.Place)
            .WithMany()
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CuratedItineraryId, x.OrderIndex })
            .IsUnique();
    }
}

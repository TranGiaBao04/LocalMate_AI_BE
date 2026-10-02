using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class ItineraryItemConfiguration : IEntityTypeConfiguration<ItineraryItem>
{
    public void Configure(EntityTypeBuilder<ItineraryItem> builder)
    {
        builder.Property(item => item.IsVisited)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(item => item.VisitedAt)
            .IsRequired(false);

        builder.Property(item => item.EstimatedBudget)
            .HasColumnType("numeric(12,0)");

        builder.HasOne(item => item.Place)
            .WithMany()
            .HasForeignKey(item => item.PlaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.TripId, item.OrderIndex })
            .IsUnique();
    }
}

using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class TripTagConfiguration : IEntityTypeConfiguration<TripTag>
{
    public void Configure(EntityTypeBuilder<TripTag> builder)
    {
        builder.ToTable("TripTags");

        builder.HasKey(tripTag => new
            {
                tripTag.TripId,
                tripTag.TagId
            })
            .HasName("PK_TripTags");

        builder.Property(tripTag => tripTag.TripId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(tripTag => tripTag.TagId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<Trip>()
            .WithMany(trip => trip.Tags)
            .HasForeignKey(tripTag => tripTag.TripId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_TripTags_Trips_TripId");

        builder.HasOne(tripTag => tripTag.Tag)
            .WithMany()
            .HasForeignKey(tripTag => tripTag.TagId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TripTags_Tags_TagId");

        builder.HasIndex(tripTag => tripTag.TagId)
            .HasDatabaseName("IX_TripTags_TagId");
    }
}

using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.Property(trip => trip.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(trip => trip.TravelMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(TravelMode.Auto);

        builder.Property(trip => trip.PlannedStartAt)
            .HasColumnType("timestamp without time zone");

        builder.Property(trip => trip.Note).HasMaxLength(300);

        builder.Property(trip => trip.BudgetMin)
            .HasColumnType("numeric(12,0)");

        builder.Property(trip => trip.BudgetMax)
            .HasColumnType("numeric(12,0)");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(trip => trip.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Trips_Users_UserId");

        builder.HasOne<MetroStation>()
            .WithMany()
            .HasForeignKey(trip => trip.StartStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MetroStation>()
            .WithMany()
            .HasForeignKey(trip => trip.DestinationStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(trip => trip.Items)
            .WithOne(item => item.Trip)
            .HasForeignKey(item => item.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(trip => trip.CreatedAt)
            .HasDatabaseName("IX_Trips_CreatedAt");
    }
}

using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("Feedbacks");

        builder.HasKey(feedback => feedback.Id);

        builder.Property(feedback => feedback.TripId)
            .IsRequired();

        builder.Property(feedback => feedback.UserId)
            .IsRequired();

        builder.Property(feedback => feedback.QuickTag)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(feedback => feedback.Comment)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.HasIndex(feedback => new { feedback.UserId, feedback.TripId })
            .IsUnique()
            .HasDatabaseName("UX_Feedbacks_UserId_TripId");

        builder.HasIndex(feedback => feedback.TripId)
            .HasDatabaseName("IX_Feedbacks_TripId");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(feedback => feedback.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Feedbacks_Users_UserId");

        builder.HasOne<Trip>()
            .WithMany()
            .HasForeignKey(feedback => feedback.TripId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Feedbacks_Trips_TripId");
    }
}

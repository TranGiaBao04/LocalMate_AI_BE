using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlaceReviewConfiguration : IEntityTypeConfiguration<PlaceReview>
{
    public void Configure(EntityTypeBuilder<PlaceReview> builder)
    {
        var allowedQuickTags = string.Join(", ", ReviewQuickTags.All.Select(tag => $"'{tag}'"));

        builder.ToTable("PlaceReviews", table =>
        {
            table.HasCheckConstraint(
                "CK_PlaceReviews_Rating",
                "\"Rating\" >= 1 AND \"Rating\" <= 5");
            table.HasCheckConstraint(
                "CK_PlaceReviews_QuickTags_MaxThree",
                "cardinality(\"QuickTags\") <= 3");
            table.HasCheckConstraint(
                "CK_PlaceReviews_QuickTags_AllowedValues",
                $"\"QuickTags\" <@ ARRAY[{allowedQuickTags}]::text[]");
        });

        builder.HasKey(review => review.Id);

        builder.Property(review => review.UserId)
            .IsRequired();

        builder.Property(review => review.PlaceId)
            .IsRequired();

        builder.Property(review => review.ItineraryItemId)
            .IsRequired();

        builder.Property(review => review.Rating)
            .IsRequired();

        builder.Property(review => review.QuickTags)
            .HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]")
            .IsRequired();

        builder.Property(review => review.Comment)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.HasIndex(review => new { review.UserId, review.ItineraryItemId })
            .IsUnique()
            .HasDatabaseName("UX_PlaceReviews_UserId_ItineraryItemId");

        builder.HasIndex(review => review.PlaceId)
            .HasDatabaseName("IX_PlaceReviews_PlaceId");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(review => review.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PlaceReviews_Users_UserId");

        builder.HasOne<Place>()
            .WithMany()
            .HasForeignKey(review => review.PlaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PlaceReviews_Places_PlaceId");

        builder.HasOne<ItineraryItem>()
            .WithMany()
            .HasForeignKey(review => review.ItineraryItemId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PlaceReviews_ItineraryItems_ItineraryItemId");
    }
}

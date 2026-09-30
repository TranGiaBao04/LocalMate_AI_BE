using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanVersionConfiguration : IEntityTypeConfiguration<SubscriptionPlanVersion>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanVersion> b)
    {
        b.ToTable("SubscriptionPlanVersions", t =>
        {
            t.HasCheckConstraint("CK_Versions_Number", "\"VersionNumber\" > 0");
            t.HasCheckConstraint("CK_Versions_Price", "\"Price\" >= 0");
            t.HasCheckConstraint("CK_Versions_Duration", "\"DurationDays\" IS NULL OR \"DurationDays\" > 0");
            t.HasCheckConstraint("CK_Versions_Generate", "\"GenerateLimit\" IS NULL OR \"GenerateLimit\" >= 0");
            t.HasCheckConstraint("CK_Versions_Saved", "\"SavedTripLimit\" IS NULL OR \"SavedTripLimit\" >= 0");
            t.HasCheckConstraint("CK_Versions_Origin", "\"Origin\" IN ('Published','LegacyBaseline','LegacyReconstructed') AND (\"Origin\" <> 'Published' OR \"PublishedAt\" IS NOT NULL)");
        });
        b.HasKey(v => v.Id);
        // Database-only publication stamp permits features only in the version's creating transaction.
        b.Property<long>("FeaturePublicationTransactionId")
            .HasDefaultValueSql("pg_current_xact_id()::text::bigint");
        b.HasAlternateKey(v => new { v.PlanId, v.Id });
        b.Property(v => v.Price).HasColumnType("numeric(12,0)");
        b.Property(v => v.Origin).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(v => new { v.PlanId, v.VersionNumber }).IsUnique();
        b.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(v => v.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

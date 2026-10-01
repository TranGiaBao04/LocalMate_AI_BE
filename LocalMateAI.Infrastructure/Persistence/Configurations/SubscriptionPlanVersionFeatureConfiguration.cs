using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanVersionFeatureConfiguration : IEntityTypeConfiguration<SubscriptionPlanVersionFeature>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanVersionFeature> b)
    {
        b.ToTable("SubscriptionPlanVersionFeatures", t =>
            t.HasCheckConstraint("CK_VersionFeatures_SortOrder", "\"SortOrder\" >= 0"));
        b.HasKey(f => new { f.PlanVersionId, f.FeatureId });
        b.HasOne<SubscriptionPlanVersion>().WithMany().HasForeignKey(f => f.PlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PlanFeature>().WithMany().HasForeignKey(f => f.FeatureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

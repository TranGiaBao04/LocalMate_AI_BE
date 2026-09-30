using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> b)
    {
        b.ToTable("SubscriptionPlans", t =>
        {
            t.HasCheckConstraint("CK_Plans_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{0,63}$'");
            t.HasCheckConstraint("CK_Plans_Priority", "\"EntitlementPriority\" >= 0");
            t.HasCheckConstraint("CK_Plans_Free", "\"Code\" <> 'FREE' OR (\"IsActive\" AND \"IsSystem\" AND \"EntitlementPriority\" = 0)");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Code).HasMaxLength(64).IsRequired();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(p => p.Code).IsUnique();
        b.HasIndex(p => p.EntitlementPriority).IsUnique();
        b.HasIndex(p => new { p.IsActive, p.EntitlementPriority });
        b.HasOne<SubscriptionPlanVersion>().WithMany()
            .HasForeignKey(p => new { p.Id, p.CurrentVersionId })
            .HasPrincipalKey(v => new { v.PlanId, v.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

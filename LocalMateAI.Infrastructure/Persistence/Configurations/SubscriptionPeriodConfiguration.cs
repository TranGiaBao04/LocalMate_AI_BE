using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionPeriodConfiguration : IEntityTypeConfiguration<SubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<SubscriptionPeriod> b)
    {
        b.ToTable("SubscriptionPeriods", t =>
        {
            t.HasCheckConstraint("CK_Periods_Range", "\"StartsAt\" < \"EndsAt\"");
            t.HasCheckConstraint("CK_Periods_Source", "(\"SourcePaymentOrderId\" IS NOT NULL) <> (\"LegacyUserSubscriptionId\" IS NOT NULL)");
        });
        b.HasKey(p => p.Id);
        b.HasAlternateKey(p => new { p.Id, p.UserId });
        b.HasIndex(p => new { p.UserId, p.StartsAt, p.EndsAt });
        b.HasIndex(p => new { p.UserId, p.PlanId, p.EndsAt });
        b.HasIndex(p => p.SourcePaymentOrderId).IsUnique().HasFilter("\"SourcePaymentOrderId\" IS NOT NULL");
        b.HasIndex(p => p.LegacyUserSubscriptionId).IsUnique().HasFilter("\"LegacyUserSubscriptionId\" IS NOT NULL");
        b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(p => p.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubscriptionPlanVersion>().WithMany()
            .HasForeignKey(p => new { p.PlanId, p.PlanVersionId })
            .HasPrincipalKey(v => new { v.PlanId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentOrder>().WithMany()
            .HasForeignKey(p => new { p.SourcePaymentOrderId, p.UserId })
            .HasPrincipalKey(o => new { o.Id, o.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserSubscription>().WithMany().HasForeignKey(p => p.LegacyUserSubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

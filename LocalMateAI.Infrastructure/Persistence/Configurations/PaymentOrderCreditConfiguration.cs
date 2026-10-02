using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PaymentOrderCreditConfiguration : IEntityTypeConfiguration<PaymentOrderCredit>
{
    public void Configure(EntityTypeBuilder<PaymentOrderCredit> b)
    {
        b.ToTable("PaymentOrderCredits", t =>
        {
            t.HasCheckConstraint("CK_OrderCredits_Days", "\"RemainingDays\" >= 0");
            t.HasCheckConstraint("CK_OrderCredits_Amount", "\"CalculatedCreditAmount\" >= 0");
            t.HasCheckConstraint("CK_OrderCredits_ReleaseProof", """
                ("ReleasedAt" IS NULL AND "ReleaseProviderCheckedAt" IS NULL AND "ReleaseProviderStatus" IS NULL
                 AND "ReleaseProviderRequestedAmount" IS NULL AND "ReleaseProviderAmountPaid" IS NULL
                 AND "ReleaseProviderAmountRemaining" IS NULL AND "ReleaseReasonCode" IS NULL) OR
                ("ReleasedAt" IS NOT NULL AND "ReleaseProviderCheckedAt" IS NOT NULL AND "ReleaseProviderStatus" IS NOT NULL
                 AND "ReleaseProviderRequestedAmount" IS NOT NULL AND "ReleaseProviderAmountPaid" IS NOT NULL
                 AND "ReleaseProviderAmountRemaining" IS NOT NULL AND "ReleaseReasonCode" IS NOT NULL
                 AND "ReleaseProviderStatus" = 'Cancelled' AND "ReleaseProviderRequestedAmount" > 0
                 AND "ReleaseProviderAmountPaid" = 0 AND "ReleaseProviderAmountRemaining" = "ReleaseProviderRequestedAmount"
                 AND "ReleaseReasonCode" = 'provider_cancelled_no_funds'
                 AND "ReleaseProviderCheckedAt" <= "ReleasedAt"
                 AND "ReleasedAt" - "ReleaseProviderCheckedAt" <= interval '1 minute')
                """);
        });
        b.HasKey(c => new { c.OrderId, c.PeriodId });
        b.Property(c => c.CalculatedCreditAmount).HasColumnType("numeric(12,0)");
        b.Property(c => c.ReleaseProviderRequestedAmount).HasColumnType("numeric(12,0)");
        b.Property(c => c.ReleaseProviderAmountPaid).HasColumnType("numeric(12,0)");
        b.Property(c => c.ReleaseProviderAmountRemaining).HasColumnType("numeric(12,0)");
        b.Property(c => c.ReleaseProviderStatus).HasMaxLength(30);
        b.Property(c => c.ReleaseReasonCode).HasMaxLength(64);
        b.HasIndex(c => c.PeriodId).IsUnique().HasFilter("\"ReleasedAt\" IS NULL")
            .HasDatabaseName("UX_OrderCredits_UnreleasedPeriod");
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(c => new { c.OrderId, c.UserId })
            .HasPrincipalKey(o => new { o.Id, o.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(c => new { c.PeriodId, c.UserId })
            .HasPrincipalKey(p => new { p.Id, p.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}

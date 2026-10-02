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
        });
        b.HasKey(c => new { c.OrderId, c.PeriodId });
        b.Property(c => c.CalculatedCreditAmount).HasColumnType("numeric(12,0)");
        b.HasIndex(c => c.PeriodId).IsUnique().HasFilter("\"ReleasedAt\" IS NULL")
            .HasDatabaseName("UX_OrderCredits_UnreleasedPeriod");
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(c => new { c.OrderId, c.UserId })
            .HasPrincipalKey(o => new { o.Id, o.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(c => new { c.PeriodId, c.UserId })
            .HasPrincipalKey(p => new { p.Id, p.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}

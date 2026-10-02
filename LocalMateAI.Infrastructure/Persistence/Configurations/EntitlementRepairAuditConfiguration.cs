using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class EntitlementRepairAuditConfiguration : IEntityTypeConfiguration<EntitlementRepairAudit>
{
    public void Configure(EntityTypeBuilder<EntitlementRepairAudit> b)
    {
        b.ToTable("EntitlementRepairAudits", t =>
        {
            t.HasCheckConstraint("CK_RepairAudit_Outcome", "\"Outcome\" IN ('Repaired','AlreadyGranted','NotEligible','Conflict')");
            t.HasCheckConstraint("CK_RepairAudit_Reason", "length(btrim(\"Reason\")) BETWEEN 1 AND 500 AND \"Reason\" = btrim(\"Reason\") AND \"Reason\" !~ '[[:cntrl:]]'");
            t.HasCheckConstraint("CK_RepairAudit_Period", "\"Outcome\" NOT IN ('Repaired','AlreadyGranted') OR \"SubscriptionPeriodId\" IS NOT NULL");
            t.HasCheckConstraint("CK_RepairAudit_Code", "length(\"DecisionCode\") BETWEEN 1 AND 64");
        });
        b.HasKey(a => a.Id);
        b.Property(a => a.Reason).HasMaxLength(500).IsRequired();
        b.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(a => a.DecisionCode).HasMaxLength(64).IsRequired();
        b.Property(a => a.ReconstructionMode).HasMaxLength(64);
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(a => a.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(a => a.SubscriptionPeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(a => new { a.PaymentOrderId, a.OccurredAt, a.Id });
        b.HasIndex(a => a.PaymentOrderId).IsUnique().HasFilter("\"Outcome\" = 'Repaired'");
    }
}

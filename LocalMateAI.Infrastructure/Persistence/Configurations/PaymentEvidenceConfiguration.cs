using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PaymentWebhookReceiptConfiguration : IEntityTypeConfiguration<PaymentWebhookReceipt>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookReceipt> b)
    {
        b.ToTable("PaymentWebhookReceipts", t =>
        {
            t.HasCheckConstraint("CK_WebhookReceipt_Hash", "\"RawPayloadSha256\" ~ '^[0-9a-f]{64}$'");
            t.HasCheckConstraint("CK_WebhookReceipt_Retention", "\"RawPayloadRetainUntil\" > \"ReceivedAt\"");
            t.HasCheckConstraint("CK_WebhookReceipt_RawState", "(\"RawPayload\" IS NOT NULL AND \"RawPayloadPurgedAt\" IS NULL) OR (\"RawPayload\" IS NULL AND \"RawPayloadPurgedAt\" IS NOT NULL AND \"RawPayloadPurgedAt\" >= \"RawPayloadRetainUntil\")");
            t.HasCheckConstraint("CK_WebhookReceipt_RawSize", "octet_length(\"RawPayload\") <= 65536");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.Amount).HasColumnType("numeric(12,0)");
        b.Property(r => r.RawPayload).HasColumnType("text");
        b.Property(r => r.RawPayloadSha256).HasMaxLength(64).IsRequired();
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(r => r.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => new { r.ProviderOrderCode, r.ReceivedAt });
        b.HasIndex(r => r.PaymentOrderId);
        b.HasIndex(r => r.RawPayloadRetainUntil).HasFilter("\"RawPayload\" IS NOT NULL");
    }
}

public sealed class PaymentOrderStatusHistoryConfiguration : IEntityTypeConfiguration<PaymentOrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<PaymentOrderStatusHistory> b)
    {
        b.ToTable("PaymentOrderStatusHistories", t => t.HasCheckConstraint("CK_PaymentHistory_RealTransition",
            "\"FromStatus\" IS NULL OR \"FromStatus\" <> \"ToStatus\""));
        b.HasKey(h => h.Id);
        b.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(h => h.Source).HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(h => h.ReasonCode).HasMaxLength(100);
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(h => h.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(h => h.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentWebhookReceipt>().WithMany().HasForeignKey(h => h.WebhookReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(h => new { h.PaymentOrderId, h.OccurredAt });
    }
}

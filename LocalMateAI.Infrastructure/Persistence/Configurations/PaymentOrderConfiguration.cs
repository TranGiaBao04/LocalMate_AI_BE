using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        builder.ToTable("PaymentOrders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.PlanCode).HasConversion<string>().HasMaxLength(20).IsRequired(false);
        builder.Property(order => order.PlanVersionBinding).HasConversion<string>().HasMaxLength(30);
        builder.ToTable("PaymentOrders", t => t.HasCheckConstraint("CK_PaymentOrders_NativeBinding",
            "\"PlanVersionBinding\" <> 'Native' OR (\"PlanId\" IS NOT NULL AND \"PlanVersionId\" IS NOT NULL)"));
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(o => o.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SubscriptionPlanVersion>().WithMany()
            .HasForeignKey(o => new { o.PlanId, o.PlanVersionId })
            .HasPrincipalKey(v => new { v.PlanId, v.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(o => new { o.Id, o.UserId });
        builder.HasIndex(o => new { o.UserId, o.PlanId, o.Type, o.Status });
        builder.Property(order => order.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.Amount).HasColumnType("numeric(12,0)");
        builder.Property(order => order.ProviderOrderCode)
            .HasDefaultValueSql("nextval('\"PaymentOrderCodeSequence\"')");
        builder.Property(order => order.CheckoutUrl).IsRequired(false);
        builder.Property(order => order.QrCode).IsRequired(false);
        builder.HasIndex(order => order.ProviderOrderCode)
            .IsUnique()
            .HasDatabaseName("UX_PaymentOrders_ProviderOrderCode");
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(order => order.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

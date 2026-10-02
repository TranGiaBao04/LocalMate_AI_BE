using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SingleItineraryEntitlementConfiguration : IEntityTypeConfiguration<SingleItineraryEntitlement>
{
    public void Configure(EntityTypeBuilder<SingleItineraryEntitlement> b)
    {
        b.ToTable("SingleItineraryEntitlements", t =>
        {
            t.HasCheckConstraint("CK_SingleEntitlement_Consumption",
                "(\"ConsumedAt\" IS NULL AND \"ConsumedTripId\" IS NULL) OR " +
                "(\"ConsumedAt\" IS NOT NULL AND \"ConsumedTripId\" IS NOT NULL AND \"ConsumedAt\" >= \"GrantedAt\")");
        });
        b.HasKey(e => e.Id);
        b.HasIndex(e => e.SourcePaymentOrderId).IsUnique();
        b.HasIndex(e => e.ConsumedTripId).IsUnique().HasFilter("\"ConsumedTripId\" IS NOT NULL");
        b.HasIndex(e => new { e.UserId, e.ConsumedAt, e.GrantedAt, e.Id });
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(e => new { e.SourcePaymentOrderId, e.UserId })
            .HasPrincipalKey(o => new { o.Id, o.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SingleItineraryProductVersion>().WithMany().HasForeignKey(e => e.SingleItineraryProductVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Trip>().WithMany().HasForeignKey(e => e.ConsumedTripId).OnDelete(DeleteBehavior.Restrict);
    }
}

using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlanGrantConfiguration : IEntityTypeConfiguration<PlanGrant>
{
    public void Configure(EntityTypeBuilder<PlanGrant> builder)
    {
        builder.ToTable("PlanGrants", table =>
        {
            table.HasCheckConstraint("CK_PlanGrants_Range", "\"StartsAt\" < \"EndsAt\"");
            table.HasCheckConstraint("CK_PlanGrants_Days", "\"Days\" > 0");
        });

        builder.HasKey(grant => grant.Id);

        builder.Property(grant => grant.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(grant => grant.Reason)
            .HasMaxLength(500);

        builder.Property(grant => grant.DeduplicationKey)
            .HasMaxLength(200);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(grant => grant.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(grant => grant.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // Khoá ghép như SubscriptionPeriods: phiên bản phải thuộc đúng gói.
        builder.HasOne<SubscriptionPlanVersion>()
            .WithMany()
            .HasForeignKey(grant => new { grant.PlanId, grant.PlanVersionId })
            .HasPrincipalKey(version => new { version.PlanId, version.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(grant => grant.GrantedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(grant => new { grant.UserId, grant.EndsAt })
            .HasDatabaseName("IX_PlanGrants_UserId_EndsAt");

        // Nguồn tự động ghi bằng ON CONFLICT trên index này; nhiều dòng null không đụng nhau.
        builder.HasIndex(grant => grant.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("UX_PlanGrants_DeduplicationKey");
    }
}

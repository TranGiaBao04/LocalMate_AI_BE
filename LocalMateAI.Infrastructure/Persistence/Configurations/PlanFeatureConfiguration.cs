using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> b)
    {
        b.ToTable("PlanFeatures", t =>
        {
            t.HasCheckConstraint("CK_PlanFeatures_Code", "\"Code\" ~ '^[A-Z][A-Z0-9_]{0,63}$'");
            t.HasCheckConstraint("CK_PlanFeatures_Name", "length(btrim(\"Name\")) > 0");
        });
        b.HasKey(f => f.Id);
        b.Property(f => f.Code).HasMaxLength(64).IsRequired();
        b.Property(f => f.Name).HasMaxLength(200).IsRequired();
        b.Property(f => f.Description).HasMaxLength(1000);
        b.HasIndex(f => f.Code).IsUnique();
    }
}

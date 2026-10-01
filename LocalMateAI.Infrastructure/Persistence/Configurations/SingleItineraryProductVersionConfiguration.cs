using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SingleItineraryProductVersionConfiguration : IEntityTypeConfiguration<SingleItineraryProductVersion>
{
    public void Configure(EntityTypeBuilder<SingleItineraryProductVersion> b)
    {
        b.ToTable("SingleItineraryProductVersions", t =>
        {
            t.HasCheckConstraint("CK_SingleProduct_Version", "\"VersionNumber\" > 0");
            t.HasCheckConstraint("CK_SingleProduct_Price", "\"Price\" > 0");
        });
        b.HasKey(v => v.Id);
        b.HasIndex(v => v.VersionNumber).IsUnique();
        b.Property(v => v.Price).HasColumnType("numeric(12,0)");
        b.HasData(SingleItineraryBaseline.Version());
    }
}

using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlaceTagConfiguration : IEntityTypeConfiguration<PlaceTag>
{
    public void Configure(EntityTypeBuilder<PlaceTag> builder)
    {
        builder.ToTable("PlaceTags");

        builder.HasKey(placeTag => new
            {
                placeTag.PlaceId,
                placeTag.TagId
            })
            .HasName("PK_PlaceTags");

        builder.Property(placeTag => placeTag.PlaceId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(placeTag => placeTag.TagId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne(placeTag => placeTag.Place)
            .WithMany(place => place.Tags)
            .HasForeignKey(placeTag => placeTag.PlaceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PlaceTags_Places_PlaceId");

        builder.HasOne(placeTag => placeTag.Tag)
            .WithMany()
            .HasForeignKey(placeTag => placeTag.TagId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PlaceTags_Tags_TagId");

        builder.HasIndex(placeTag => placeTag.TagId)
            .HasDatabaseName("IX_PlaceTags_TagId");
    }
}
using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlaceEmbeddingConfiguration : IEntityTypeConfiguration<PlaceEmbedding>
{
    public void Configure(EntityTypeBuilder<PlaceEmbedding> builder)
    {
        builder.ToTable("PlaceEmbeddings");

        builder.HasKey(embedding => embedding.PlaceId);

        builder.Property(embedding => embedding.Model)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(embedding => embedding.ContentHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(embedding => embedding.Vector)
            .HasColumnType("real[]")
            .IsRequired();

        builder.HasOne<Place>()
            .WithMany()
            .HasForeignKey(embedding => embedding.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

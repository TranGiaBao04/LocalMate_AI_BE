using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class UsageEventConfiguration : IEntityTypeConfiguration<UsageEvent>
{
    public void Configure(EntityTypeBuilder<UsageEvent> builder)
    {
        builder.ToTable("UsageEvents");
        builder.HasKey(usage => usage.Id);
        builder.Property(usage => usage.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(usage => new { usage.UserId, usage.Type, usage.CreatedAt })
            .HasDatabaseName("IX_UsageEvents_UserId_Type_CreatedAt");
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(usage => usage.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Trip>()
            .WithMany()
            .HasForeignKey(usage => usage.TripId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

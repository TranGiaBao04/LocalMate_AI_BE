using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PendingRegistrationConfiguration : IEntityTypeConfiguration<PendingRegistration>
{
    public void Configure(EntityTypeBuilder<PendingRegistration> builder)
    {
        builder.ToTable("PendingRegistrations");

        builder.HasKey(pending => pending.Id);

        builder.Property(pending => pending.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(pending => pending.FullName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(pending => pending.PasswordHash)
            .IsRequired();

        builder.Property(pending => pending.ExpiresAt)
            .IsRequired();

        builder.HasIndex(pending => pending.Email)
            .IsUnique()
            .HasDatabaseName("UX_PendingRegistrations_Email");
    }
}

using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_Users_Status",
                "\"Status\" IN ('Active', 'Locked')"));

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(user => user.FullName)
            .HasColumnType("character varying(200)")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(user => user.Email)
            .HasColumnType("character varying(254)")
            .HasMaxLength(254)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("UX_Users_Email");

        builder.Property(user => user.PasswordHash)
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(user => user.RoleId)
            .IsRequired();

        builder.HasOne(user => user.Role)
            .WithMany()
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(user => user.Status)
            .HasConversion<string>()
            .HasColumnType("character varying(20)")
            .HasMaxLength(20)
            .HasDefaultValue(UserStatus.Active)
            .IsRequired();

        builder.Property(user => user.LockedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(user => user.LockReason)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(user => user.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
    }
}

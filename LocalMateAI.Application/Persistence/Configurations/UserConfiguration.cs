using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Application.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("USERS", tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_USERS_Role",
                "\"Role\" IN ('User', 'Admin')"));

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
            .HasDatabaseName("UX_USERS_Email");

        builder.Property(user => user.PasswordHash)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(user => user.Role)
            .HasConversion<string>()
            .HasColumnType("character varying(10)")
            .HasMaxLength(10)
            .HasDefaultValue(UserRole.User)
            .IsRequired();

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

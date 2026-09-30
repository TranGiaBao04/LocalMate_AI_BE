using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id)
            .ValueGeneratedNever();

        builder.Property(role => role.Name)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(role => role.NormalizedName)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("UX_Roles_NormalizedName");

        builder.Property(role => role.Description)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(role => role.IsSystem)
            .IsRequired();

        builder.HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // 2 role hệ thống (User, Admin) do migration AddRbacAndUserStatus chèn bằng SQL, Id do Postgres sinh.
    }
}

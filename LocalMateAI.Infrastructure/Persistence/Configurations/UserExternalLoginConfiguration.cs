using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.ToTable("USER_EXTERNAL_LOGINS");

        builder.HasKey(externalLogin => new
            {
                externalLogin.Provider,
                externalLogin.ProviderSubject
            })
            .HasName("PK_USER_EXTERNAL_LOGINS");

        builder.Property(externalLogin => externalLogin.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(externalLogin => externalLogin.Provider)
            .HasColumnType("character varying(50)")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(externalLogin => externalLogin.ProviderSubject)
            .HasColumnType("character varying(255)")
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(externalLogin => new
            {
                externalLogin.UserId,
                externalLogin.Provider
            })
            .IsUnique()
            .HasDatabaseName("UX_USER_EXTERNAL_LOGINS_UserId_Provider");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(externalLogin => externalLogin.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_USER_EXTERNAL_LOGINS_USERS_UserId");
    }
}

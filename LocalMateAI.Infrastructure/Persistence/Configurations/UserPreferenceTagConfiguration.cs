using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class UserPreferenceTagConfiguration : IEntityTypeConfiguration<UserPreferenceTag>
{
    public void Configure(EntityTypeBuilder<UserPreferenceTag> builder)
    {
        builder.ToTable("UserPreferenceTags");

        builder.HasKey(preferenceTag => new
            {
                preferenceTag.UserId,
                preferenceTag.TagId
            })
            .HasName("PK_UserPreferenceTags");

        builder.Property(preferenceTag => preferenceTag.UserId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(preferenceTag => preferenceTag.TagId)
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany(user => user.PreferenceTags)
            .HasForeignKey(preferenceTag => preferenceTag.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_UserPreferenceTags_Users_UserId");

        builder.HasOne(preferenceTag => preferenceTag.Tag)
            .WithMany()
            .HasForeignKey(preferenceTag => preferenceTag.TagId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_UserPreferenceTags_Tags_TagId");

        builder.HasIndex(preferenceTag => preferenceTag.TagId)
            .HasDatabaseName("IX_UserPreferenceTags_TagId");
    }
}

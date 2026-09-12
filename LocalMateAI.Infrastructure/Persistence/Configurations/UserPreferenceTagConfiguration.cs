using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class UserPreferenceTagConfiguration : IEntityTypeConfiguration<UserPreferenceTag>
{
    public void Configure(EntityTypeBuilder<UserPreferenceTag> builder)
    {
        builder.ToTable("USER_PREFERENCE_TAGS");

        builder.HasKey(preferenceTag => new
            {
                preferenceTag.UserId,
                preferenceTag.TagId
            })
            .HasName("PK_USER_PREFERENCE_TAGS");

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
            .HasConstraintName("FK_USER_PREFERENCE_TAGS_USERS_UserId");

        builder.HasOne(preferenceTag => preferenceTag.Tag)
            .WithMany()
            .HasForeignKey(preferenceTag => preferenceTag.TagId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_USER_PREFERENCE_TAGS_TAGS_TagId");

        builder.HasIndex(preferenceTag => preferenceTag.TagId)
            .HasDatabaseName("IX_USER_PREFERENCE_TAGS_TagId");
    }
}

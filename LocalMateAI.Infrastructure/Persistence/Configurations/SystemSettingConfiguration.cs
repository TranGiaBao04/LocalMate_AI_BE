using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");

        builder.HasKey(setting => setting.Key);

        builder.Property(setting => setting.Key)
            .HasMaxLength(100);

        builder.Property(setting => setting.Value)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(setting => setting.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

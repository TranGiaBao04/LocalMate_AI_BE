using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class PlaceOpeningHourConfiguration : IEntityTypeConfiguration<PlaceOpeningHour>
{
    public void Configure(EntityTypeBuilder<PlaceOpeningHour> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DayOfWeek)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.IsClosed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasOne(x => x.Place)
            .WithMany(x => x.OpeningHours)
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mỗi địa điểm chỉ có tối đa 1 bản ghi cho mỗi ngày trong tuần (7 ngày).
        builder.HasIndex(x => new { x.PlaceId, x.DayOfWeek })
            .IsUnique();
    }
}

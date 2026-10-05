using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class LlmCallLogConfiguration : IEntityTypeConfiguration<LlmCallLog>
{
    public void Configure(EntityTypeBuilder<LlmCallLog> builder)
    {
        builder.ToTable("LlmCallLogs");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(log => log.Outcome)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(log => log.Model)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Trip>()
            .WithMany()
            .HasForeignKey(log => log.TripId)
            .OnDelete(DeleteBehavior.SetNull);

        // Đếm trần theo ngày của một người dùng, và trần theo từng chuyến đi.
        builder.HasIndex(log => new { log.UserId, log.CreatedAt })
            .HasDatabaseName("IX_LlmCallLogs_UserId_CreatedAt");

        builder.HasIndex(log => new { log.TripId, log.Kind })
            .HasDatabaseName("IX_LlmCallLogs_TripId_Kind");
    }
}

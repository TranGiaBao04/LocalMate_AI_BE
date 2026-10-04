using LocalMateAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LocalMateAI.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Type)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(notification => notification.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(notification => notification.Body)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(notification => notification.TargetType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(notification => notification.TargetId)
            .IsRequired(false);

        builder.Property(notification => notification.ReadAt)
            .IsRequired(false);

        builder.Property(notification => notification.DeduplicationKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Enqueue dùng ON CONFLICT trên index này để không bao giờ tạo trùng thông báo.
        builder.HasIndex(notification => notification.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("UX_Notifications_DeduplicationKey");

        // Danh sách hộp thư: của một người, mới nhất trước.
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Notifications_UserId_CreatedAt");

        // Số chưa đọc trên chuông được hỏi mỗi phút: chỉ đánh index các dòng chưa đọc.
        builder.HasIndex(notification => notification.UserId)
            .HasFilter("\"ReadAt\" IS NULL")
            .HasDatabaseName("IX_Notifications_UserId_Unread");
    }
}

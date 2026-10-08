using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

// Thông báo trong hộp thư của người dùng. Tiêu đề và nội dung lưu cứng lúc tạo nên lịch sử không đổi
// khi tên gói hay ngày đi thay đổi về sau.
public sealed class Notification : BaseEntity
{
    public Guid UserId { get; set; }

    // Mã loại thông báo (NotificationTypes), ví dụ "trip_finalized".
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public NotificationTargetType TargetType { get; set; } = NotificationTargetType.None;

    // Chỉ có khi TargetType = Trip (Id chuyến đi).
    public Guid? TargetId { get; set; }

    // null = chưa đọc.
    public DateTime? ReadAt { get; set; }

    // Chống tạo trùng, ví dụ "trip-finalized:{Trip.Id}", "payment:{PaymentOrder.Id}", "welcome:{User.Id}".
    public string DeduplicationKey { get; set; } = string.Empty;
}

using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

// Mail chờ gửi (Transactional Outbox): ghi cùng transaction với nghiệp vụ, job nền gửi và thử lại.
// Chỉ là hàng chờ tạm — mail đã gửi bị xoá sau 7 ngày, gửi lỗi hẳn bị xoá sau 30 ngày.
public sealed class EmailOutboxMessage : BaseEntity
{
    // Email của user lúc sự kiện xảy ra (user đổi email sau đó không ảnh hưởng).
    public string ToEmail { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    // Tên template trong EmailTemplateNames, dùng để dựng HTML lúc gửi.
    public string TemplateName { get; set; } = string.Empty;

    // Dữ liệu điền vào template (JSON, đã định dạng sẵn). Không lưu HTML để bảng nhỏ.
    public string Model { get; set; } = "{}";

    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;

    public int AttemptCount { get; set; }

    // Thời điểm sớm nhất được thử gửi (tiếp theo); lúc đang gửi dùng làm hạn giữ chỗ.
    public DateTime NextAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    // Chống tạo trùng, ví dụ "payment-receipt:{PaymentOrder.Id}", "trip-itinerary:{Trip.Id}".
    public string DeduplicationKey { get; set; } = string.Empty;
}

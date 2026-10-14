using LocalMateAI.Domain.Common;

namespace LocalMateAI.Domain.Entities;

// Chương trình dùng thử khi đăng ký: ai tạo tài khoản trong khoảng ngày thì được gói Trip Pass TrialDays ngày.
public sealed class TrialProgram : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    // Ngày theo giờ Việt Nam, tính cả hai đầu.
    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    public int TrialDays { get; set; }

    // null = không giới hạn số người nhận.
    public int? MaxRecipients { get; set; }

    public bool IsEnabled { get; set; }

    public Guid? CreatedByUserId { get; init; }
}

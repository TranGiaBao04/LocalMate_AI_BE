using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

// Gói trả phí được tặng (admin tặng tay hoặc dùng thử khi đăng ký). Không gắn đơn thanh toán nên KHÔNG nằm
// trong SubscriptionPeriods: luồng thanh toán, nâng cấp và repair-entitlement không biết bảng này.
public sealed class PlanGrant : BaseEntity
{
    public Guid UserId { get; init; }

    public Guid PlanId { get; init; }

    // Phiên bản hiện hành của gói lúc tặng; quyền lợi giữ theo phiên bản này.
    public Guid PlanVersionId { get; init; }

    public DateTime StartsAt { get; init; }

    public DateTime EndsAt { get; init; }

    public int Days { get; init; }

    public PlanGrantSource Source { get; init; }

    // Bắt buộc với nguồn Admin.
    public string? Reason { get; init; }

    // null với nguồn SignupTrial.
    public Guid? GrantedByUserId { get; init; }

    // Chương trình dùng thử đã cấp gói này (Phần C).
    public Guid? TrialProgramId { get; init; }

    // Chống cấp trùng cho nguồn tự động, ví dụ "signup-trial:{User.Id}".
    public string? DeduplicationKey { get; init; }
}

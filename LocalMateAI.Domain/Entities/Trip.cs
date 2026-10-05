using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Domain.Entities;

public sealed class Trip : BaseEntity
{
    public Guid? UserId { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }

    // Ga người dùng chọn xuất phát. Null = xuất phát từ toạ độ (StartLatitude/StartLongitude luôn có giá trị).
    public Guid? StartStationId { get; set; }

    // Ga người dùng chọn để chơi quanh đó. Null = "gần tôi" (quanh ga gần điểm xuất phát nhất).
    public Guid? DestinationStationId { get; set; }
    public int DurationHours { get; set; }
    public decimal BudgetMin { get; set; }
    public decimal BudgetMax { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Draft;
    public TravelMode TravelMode { get; set; } = TravelMode.Auto;
    public DateTime? FinalizedAt { get; set; }

    // Ngày + giờ rời điểm xuất phát theo giờ Việt Nam (giờ trên đồng hồ, không kèm múi giờ). Null = trip cũ chưa đặt ngày.
    public DateTime? PlannedStartAt { get; set; }

    // Ghi chú tự do người dùng nhập khi tạo lịch (đã chuẩn hoá khoảng trắng). Null = không ghi chú.
    public string? Note { get; set; }

    // true khi ghi chú đã được dùng để xếp hạng địa điểm lúc tạo lịch.
    public bool NoteApplied { get; set; }

    // Lần gần nhất AI viết lý do cho các chặng. Null = lý do đang là câu mặc định của hệ thống.
    public DateTime? AiExplainedAt { get; set; }

    // Xoá mềm: null = còn hoạt động. Giữ dòng lại vì Feedback/PlaceReview trỏ tới Trip bằng FK Restrict.
    public DateTime? DeletedAt { get; set; }

    public ICollection<ItineraryItem> Items { get; set; } = [];
    public ICollection<TripTag> Tags { get; set; } = [];
}

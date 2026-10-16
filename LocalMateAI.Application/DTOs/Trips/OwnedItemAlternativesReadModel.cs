using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record OwnedItemAlternativesReadModel(
    Guid ItemId,
    Guid TripId,
    TripStatus TripStatus,
    Guid PlaceId,
    IReadOnlyList<Guid> TripPlaceIds,
    DateTime? TripPlannedStartAt = null, // giờ rời điểm xuất phát; null với trip cũ chưa đặt giờ
    int TripDurationHours = 0,
    int VisitStartMinute = 0, // chặng này bắt đầu ở phút thứ mấy tính từ 00:00 ngày đi (≥ 1440 = hôm sau)
    int VisitDurationMinutes = 0); // thời lượng chặng; giữ nguyên khi thay địa điểm

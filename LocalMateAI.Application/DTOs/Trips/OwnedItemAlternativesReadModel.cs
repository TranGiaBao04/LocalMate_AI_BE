using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record OwnedItemAlternativesReadModel(
    Guid ItemId,
    Guid TripId,
    TripStatus TripStatus,
    Guid PlaceId,
    IReadOnlyList<Guid> TripPlaceIds,
    DateTime? TripPlannedStartAt = null, // giờ rời điểm xuất phát; null với trip cũ chưa đặt giờ
    int TripDurationHours = 0);

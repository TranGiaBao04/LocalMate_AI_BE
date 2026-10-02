using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record OwnedItemAlternativesReadModel(
    Guid ItemId,
    Guid TripId,
    TripStatus TripStatus,
    Guid PlaceId,
    IReadOnlyList<Guid> TripPlaceIds);

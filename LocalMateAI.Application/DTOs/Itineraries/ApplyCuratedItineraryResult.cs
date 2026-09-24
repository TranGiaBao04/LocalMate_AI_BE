using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.DTOs.Itineraries;

public enum ApplyCuratedItineraryResultStatus
{
    Success,
    InvalidId,
    InvalidStart,
    UserNotFound,
    ItineraryNotFound,
    ItineraryUnavailable
}

public sealed record ApplyCuratedItineraryResult(
    ApplyCuratedItineraryResultStatus Status,
    TripDetailResponse? Response = null)
{
    public static ApplyCuratedItineraryResult Succeeded(TripDetailResponse response) =>
        new(ApplyCuratedItineraryResultStatus.Success, response);

    public static ApplyCuratedItineraryResult InvalidCuratedId() =>
        new(ApplyCuratedItineraryResultStatus.InvalidId);

    public static ApplyCuratedItineraryResult InvalidStartLocation() =>
        new(ApplyCuratedItineraryResultStatus.InvalidStart);

    public static ApplyCuratedItineraryResult MissingUser() =>
        new(ApplyCuratedItineraryResultStatus.UserNotFound);

    public static ApplyCuratedItineraryResult MissingItinerary() =>
        new(ApplyCuratedItineraryResultStatus.ItineraryNotFound);

    public static ApplyCuratedItineraryResult UnavailableItinerary() =>
        new(ApplyCuratedItineraryResultStatus.ItineraryUnavailable);
}

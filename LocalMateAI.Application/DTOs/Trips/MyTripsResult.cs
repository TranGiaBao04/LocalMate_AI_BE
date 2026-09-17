namespace LocalMateAI.Application.DTOs.Trips;

public sealed record MyTripsResult(
    bool UserFound,
    IReadOnlyList<MyTripResponse>? Trips = null);

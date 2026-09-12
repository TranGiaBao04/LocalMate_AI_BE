using LocalMateAI.Application.DTOs.Geo;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripOriginResolution(
    NearestStationResult NearestStation,
    bool IsWithinServiceArea);

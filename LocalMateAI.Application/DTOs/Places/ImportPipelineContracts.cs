using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Places;

public sealed record ParsedPlaceRow(
    int RowNumber,
    string RawName,
    string RawAddress,
    string RawCategory,
    string RawCoordinates,
    string RawLatitude,
    string RawLongitude,
    string RawPriceMin,
    string RawPriceMax,
    string RawStations,
    string RawOpenHours,
    string RawTags,
    string RawDescription
);

public sealed record PriceNormalizationResult(
    bool IsValid,
    int? EstimatedCostMin,
    int? EstimatedCostMax,
    string? ErrorMessage
);

public sealed record CoordinateNormalizationResult(
    bool IsValid,
    double? Latitude,
    double? Longitude,
    string? ErrorMessage
);

public sealed record StationMappingResult(
    bool IsValid,
    IReadOnlyList<Guid> StationIds,
    IReadOnlyList<string> MatchedStationNames,
    IReadOnlyList<string> UnmatchedStationNames,
    string? ErrorMessage
);

public sealed record CategoryValidationResult(
    bool IsValid,
    PlaceCategory? Category,
    string? ErrorMessage
);

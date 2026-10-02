using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Stations;

public sealed record AdminStationReadModel(Guid Id, int Order, string Name, double Latitude, double Longitude);

/// <summary>Số địa điểm chưa xoá mềm theo (ga, category, trạng thái). StationId = null ⇒ ngoài vùng phủ của mọi ga.</summary>
public sealed record StationPlaceCount(Guid? StationId, PlaceCategory Category, PlaceStatus Status, int Count);

/// <summary>Ga và số đếm lấy trong cùng một truy vấn nên luôn khớp nhau.</summary>
public sealed record AdminStationSnapshot(
    IReadOnlyList<AdminStationReadModel> Stations,
    IReadOnlyList<StationPlaceCount> Counts);

public sealed record PlaceStatusCounts(int Pending, int Active, int Inactive, int Total);

public sealed record StationCategoryCounts(PlaceCategory Category, int Pending, int Active, int Inactive, int Total);

public sealed record AdminStationResponse(
    Guid Id,
    int Order,
    string Name,
    double Latitude,
    double Longitude,
    PlaceStatusCounts Totals,
    IReadOnlyList<StationCategoryCounts> Categories,
    bool IsUnderstocked,
    int Shortfall,
    IReadOnlyList<PlaceCategory> MissingCategories);

public sealed record AdminStationsResponse(
    double RadiusMeters,
    int MinActivePlacesPerStation,
    int UnderstockedStationCount,
    PlaceStatusCounts OutsideCoverage,
    IReadOnlyList<AdminStationResponse> Stations);

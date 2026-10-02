namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>Giờ tàu dự kiến tại ga theo một chiều; Headways đã dời theo số phút tàu chạy tới ga.</summary>
public sealed record MetroDirectionDeparturesResponse(
    MetroDirection Direction,
    string TowardStationName,
    IReadOnlyList<TimeOnly> Departures,
    IReadOnlyList<MetroHeadway> Headways);

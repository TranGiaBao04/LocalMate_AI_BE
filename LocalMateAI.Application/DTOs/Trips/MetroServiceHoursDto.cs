namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>Giờ chuyến đầu/cuối rời ga lên trong ngày đi; trả kèm lý do MetroNotRunning để FE giải thích.</summary>
public sealed record MetroServiceHoursDto(
    int StationOrder,
    string StationName,
    TimeOnly FirstDeparture,
    TimeOnly LastDeparture);

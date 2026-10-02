using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

/// <param name="RemainingItems">Các item còn lại sau khi xoá.</param>
/// <param name="TripStartTime">Giờ của chặng đầu TRƯỚC khi xoá, để chặng đầu mới thừa hưởng mốc bắt đầu chuyến.</param>
/// <param name="TravelMode">Phương tiện của trip, dùng để tính lại thời gian di chuyển.</param>
public sealed record TimelineRecalculationInput(
    IReadOnlyList<TimelineItemSnapshot> RemainingItems,
    TimeOnly TripStartTime,
    TravelMode TravelMode);

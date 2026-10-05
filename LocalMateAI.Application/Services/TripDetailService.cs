using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripDetailService(
    IUserRepository userRepository,
    ITripRepository tripRepository,
    ISystemSettingProvider settings,
    IMetroTimetableSource metroTimetableSource) : ITripDetailService
{
    public async Task<GetTripDetailResult> GetAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return GetTripDetailResult.InvalidTrip();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return GetTripDetailResult.MissingUser();
        }

        var trip = await tripRepository.GetOwnedDetailAsync(tripId, userId, cancellationToken);
        if (trip is null)
        {
            return GetTripDetailResult.MissingTrip();
        }

        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);

        // Trip Metro: lịch tàu của ngày đi để phân rã từng đoạn thành ra ga / chờ / ngồi tàu / đi bộ.
        var metroTimetable = trip.TravelMode == TravelMode.Metro && trip.PlannedStartAt is { } plannedStart
            ? MetroDayTimetable.For(metroTimetableSource.Timetable, DateOnly.FromDateTime(plannedStart))
            : null;

        return GetTripDetailResult.Succeeded(ToResponse(trip, planning, metroTimetable));
    }

    // Dựng response từ read model (logic thuần). FinalizeTripCommand dùng lại để mail lịch trình khớp đúng số liệu API.
    // Trip Metro mà không truyền metroTimetable thì không có Leg (mail lịch trình không dùng Leg).
    public static TripDetailResponse ToResponse(
        TripDetailReadModel trip,
        TripPlanningSettings? settings = null,
        MetroDayTimetable? metroTimetable = null)
    {
        var planning = settings ?? TripPlanningSettings.Default;
        var stops = trip.Items
            .Select(item => new TripStopSnapshot(item.ScheduledTime, item.EstimatedDurationMinutes, item.EstimatedBudget))
            .ToList();

        // Đoạn đi từ điểm xuất phát tới chặng đầu = giờ chặng đầu - giờ rời. Không lưu riêng nên suy ra như đoạn giữa
        // các chặng; trip cũ chưa có PlannedStartAt thì để null và không đổi các tổng.
        int? originMinutes = null;
        if (trip.PlannedStartAt is { } plannedStart && trip.Items.Count > 0)
        {
            var gap = (int)(trip.Items[0].ScheduledTime.ToTimeSpan()
                            - TimeOnly.FromDateTime(plannedStart).ToTimeSpan()).TotalMinutes;
            originMinutes = Math.Max(0, gap);
        }

        // Cách đi từng đoạn tính lại bằng đúng bộ luật lúc xếp giờ (TripLegPlanner), theo giờ đã lưu của lịch.
        var legInputs = trip.Items
            .Select(item => new ScheduleInput(
                item.Latitude, item.Longitude, item.EstimatedDurationMinutes, item.EstimatedBudget,
                item.StationOrder, item.DistanceFromStationMeters ?? 0))
            .ToList();
        var boardingStation = ToStationRef(trip.StationOrder, trip.StationName);
        var metroBoarding = trip.TravelMode == TravelMode.Metro && metroTimetable is not null && boardingStation is not null
            ? new MetroBoarding(
                boardingStation.Order,
                trip.DistanceToStationMeters ?? 0,
                StartsAtStation: trip.StartStation is not null,
                metroTimetable)
            : null;
        var canPlanLegs = trip.TravelMode != TravelMode.Metro || metroBoarding is not null;

        var items = trip.Items
            .Select((item, index) =>
            {
                int? travelMinutes = null;
                int? distanceMeters = null;
                int? walkingMinutes = null;
                int? motorbikeMinutes = null;
                TripLegResponse? leg = null;

                if (index > 0)
                {
                    var previous = trip.Items[index - 1];
                    var roadKm = TravelTimeEstimator.RoadDistanceKm(
                        previous.Latitude, previous.Longitude, item.Latitude, item.Longitude, settings);
                    travelMinutes = TripTotalsCalculator.TravelGapMinutes(stops[index - 1], stops[index]);
                    distanceMeters = (int)Math.Round(roadKm * 1000);
                    walkingMinutes = TravelTimeEstimator.WalkingMinutes(roadKm, settings);
                    motorbikeMinutes = TravelTimeEstimator.MotorbikeMinutes(roadKm, settings);

                    if (canPlanLegs)
                    {
                        leg = ToLegResponse(
                            TripLegPlanner.BetweenStops(
                                legInputs[index - 1],
                                legInputs[index],
                                MinuteOfDay(previous.ScheduledTime) + previous.EstimatedDurationMinutes,
                                trip.TravelMode,
                                metroTimetable,
                                planning),
                            ToStationRef(previous.StationOrder, previous.StationName),
                            ToStationRef(item.StationOrder, item.StationName));
                    }
                }
                else if (canPlanLegs && originMinutes > 0 && trip.PlannedStartAt is { } leaveAt)
                {
                    // originMinutes = 0 nghĩa là xuất phát ngay tại chặng đầu (lịch mẫu không gửi toạ độ): không có đoạn đi.
                    leg = ToLegResponse(
                        TripLegPlanner.FromOrigin(
                            new ScheduleOrigin(trip.StartLatitude, trip.StartLongitude, metroBoarding),
                            legInputs[0],
                            MinuteOfDay(TimeOnly.FromDateTime(leaveAt)),
                            trip.TravelMode,
                            planning),
                        boardingStation,
                        ToStationRef(item.StationOrder, item.StationName));
                }

                return new TripItemResponse(
                    item.ItemId,
                    item.PlaceId,
                    item.PlaceName,
                    item.Category.ToString(),
                    item.ImageUrl,
                    item.Latitude,
                    item.Longitude,
                    item.StationName,
                    item.OrderIndex,
                    item.ScheduledTime,
                    item.EstimatedDurationMinutes,
                    item.EstimatedBudget,
                    item.Reasoning,
                    item.IsVisited,
                    item.VisitedAt,
                    travelMinutes,
                    distanceMeters,
                    walkingMinutes,
                    motorbikeMinutes,
                    leg);
            })
            .ToList();

        var totals = TripTotalsCalculator.Calculate(stops);
        var travelMinutes = totals.TotalTravelMinutes + (originMinutes ?? 0);

        return new TripDetailResponse(
            trip.Id,
            trip.Status.ToString(),
            trip.TravelMode.ToString(),
            trip.StartLatitude,
            trip.StartLongitude,
            trip.StationName,
            trip.DurationHours,
            trip.BudgetMin,
            trip.BudgetMax,
            totals.TotalBudget,
            totals.TotalVisitMinutes, // TotalDurationMinutes: giữ nghĩa cũ, chưa gồm di chuyển
            totals.TotalVisitMinutes,
            travelMinutes, // đã gồm đoạn đi tới chặng đầu
            totals.TotalVisitMinutes + travelMinutes,
            totals.EndTime,
            trip.TagIds,
            items,
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.FinalizedAt,
            trip.PlannedStartAt is { } date ? DateOnly.FromDateTime(date) : null,
            trip.PlannedStartAt is { } time ? TimeOnly.FromDateTime(time) : null,
            originMinutes,
            trip.StartStation,
            trip.DestinationStation,
            trip.Note,
            trip.NoteApplied);
    }

    private static TripLegResponse ToLegResponse(
        TripLegPlan plan, StationRefDto? boardStation, StationRefDto? alightStation) =>
        plan.Metro is { } metro
            ? new TripLegResponse(
                plan.Mode.ToString(),
                plan.TotalMinutes,
                plan.Fallback,
                metro.ToStationMode?.ToString(),
                metro.ToStationMinutes,
                boardStation,
                alightStation,
                metro.WaitMinutes,
                metro.RideMinutes,
                metro.StopCount,
                metro.WalkMinutes,
                IsEstimated: true)
            : new TripLegResponse(plan.Mode.ToString(), plan.TotalMinutes, plan.Fallback);

    private static StationRefDto? ToStationRef(int? order, string? name) =>
        order is { } stationOrder && name is not null ? new StationRefDto(stationOrder, name) : null;

    private static int MinuteOfDay(TimeOnly time) => time.Hour * 60 + time.Minute;
}

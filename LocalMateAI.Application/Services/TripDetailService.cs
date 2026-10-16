using LocalMateAI.Application.DTOs.Places;
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
    IMetroTimetableSource metroTimetableSource,
    IPlaceOpeningHourRepository openingHourRepository) : ITripDetailService
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

        // Giờ mở cửa HIỆN TẠI của các địa điểm trong trip, để báo chặng nào rơi vào giờ đóng cửa.
        var openingHours = await openingHourRepository.GetForPlacesAsync(
            trip.Items.Select(item => item.PlaceId).Distinct().ToList(),
            cancellationToken);

        return GetTripDetailResult.Succeeded(ToResponse(trip, planning, metroTimetable, openingHours));
    }

    // Dựng response từ read model (logic thuần). FinalizeTripCommand dùng lại để mail lịch trình khớp đúng số liệu API.
    // Trip Metro mà không truyền metroTimetable thì không có Leg (mail lịch trình không dùng Leg).
    // Không truyền openingHours thì OpeningStatus của mọi chặng là null (mail lịch trình không dùng).
    public static TripDetailResponse ToResponse(
        TripDetailReadModel trip,
        TripPlanningSettings? settings = null,
        MetroDayTimetable? metroTimetable = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlaceOpeningHourDto>>? openingHours = null)
    {
        var planning = settings ?? TripPlanningSettings.Default;
        var stops = trip.Items
            .Select(item => new TripStopSnapshot(item.ScheduledTime, item.EstimatedDurationMinutes, item.EstimatedBudget))
            .ToList();

        // Số phút tính từ 00:00 ngày đi (chặng sau nửa đêm ≥ 1440), dùng chung cho đoạn đi tới chặng đầu, khoảng đi và leg.
        TimeOnly? leaveTime = trip.PlannedStartAt is { } plannedStart ? TimeOnly.FromDateTime(plannedStart) : null;
        var startMinutes = TripClock.StartMinutes(leaveTime, stops);
        var travelGaps = TripTotalsCalculator.TravelGaps(stops);

        // Đoạn đi từ điểm xuất phát tới chặng đầu = giờ chặng đầu - giờ rời. Không lưu riêng nên suy ra như đoạn giữa
        // các chặng; trip cũ chưa có PlannedStartAt thì để null và không đổi các tổng.
        int? originMinutes = leaveTime is { } leave && trip.Items.Count > 0
            ? Math.Max(0, startMinutes[0] - TripClock.MinuteOfDay(leave))
            : null;

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
                    travelMinutes = travelGaps[index];
                    distanceMeters = (int)Math.Round(roadKm * 1000);
                    walkingMinutes = TravelTimeEstimator.WalkingMinutes(roadKm, settings);
                    motorbikeMinutes = TravelTimeEstimator.MotorbikeMinutes(roadKm, settings);

                    if (canPlanLegs)
                    {
                        leg = ToLegResponse(
                            TripLegPlanner.BetweenStops(
                                legInputs[index - 1],
                                legInputs[index],
                                startMinutes[index - 1] + previous.EstimatedDurationMinutes, // có thể ≥ 1440
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
                            TripClock.MinuteOfDay(TimeOnly.FromDateTime(leaveAt)),
                            trip.TravelMode,
                            planning),
                        boardingStation,
                        ToStationRef(item.StationOrder, item.StationName));
                }

                // Trip cũ chưa đặt ngày đi thì không biết là thứ mấy nên không kết luận được.
                PlaceOpeningStatus? openingStatus = openingHours is null
                    ? null
                    : trip.PlannedStartAt is not { } plannedAt
                        ? PlaceOpeningStatus.Unknown
                        : OpeningWindows.StatusFor(
                            openingHours.GetValueOrDefault(item.PlaceId) ?? [],
                            DateOnly.FromDateTime(plannedAt),
                            startMinutes[index],
                            startMinutes[index] + item.EstimatedDurationMinutes);

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
                    leg,
                    item.GooglePlaceId,
                    openingStatus);
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
            trip.NoteApplied,
            trip.AiExplainedAt);
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
}

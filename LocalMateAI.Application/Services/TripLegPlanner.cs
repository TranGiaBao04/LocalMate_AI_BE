using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>Cách đi một đoạn (từ điểm xuất phát tới chặng đầu, hoặc giữa hai chặng) và số phút của đoạn đó.</summary>
/// <param name="Mode">Cách đi thực tế: Walking, Motorbike hoặc Metro (không bao giờ là Auto).</param>
/// <param name="TotalMinutes">Toàn bộ thời gian của đoạn, gồm cả ra ga, chờ tàu, ngồi tàu và đi bộ từ ga.</param>
/// <param name="Metro">Chỉ có khi đoạn này đi tàu.</param>
/// <param name="Fallback"><see cref="TripLegPlanner.MetroUnavailable"/> khi đoạn cần tàu nhưng đã hết chuyến.</param>
public sealed record TripLegPlan(TravelMode Mode, int TotalMinutes, MetroLegDetail? Metro = null, string? Fallback = null);

/// <param name="ToStationMode">Cách ra ga lên; null khi đang đứng sẵn tại ga.</param>
/// <param name="WalkMinutes">Đi bộ từ ga xuống tới địa điểm.</param>
public sealed record MetroLegDetail(
    TravelMode? ToStationMode,
    int ToStationMinutes,
    int BoardStationOrder,
    int AlightStationOrder,
    int WaitMinutes,
    int RideMinutes,
    int StopCount,
    int WalkMinutes);

/// <summary>
/// Quyết định cách đi từng đoạn của lịch trình. Thuần. Với chế độ Metro: chỉ lên tàu khi ga đích khác ga đang đứng
/// VÀ hai điểm cách nhau quá ngưỡng đi bộ; còn lại đi thẳng. Hết tàu thì đoạn đó tính xe máy đi thẳng.
/// </summary>
public static class TripLegPlanner
{
    public const string MetroUnavailable = "metro_unavailable";

    /// <param name="departMinuteOfDay">Giờ rời điểm xuất phát, tính bằng số phút từ 00:00.</param>
    public static TripLegPlan FromOrigin(
        ScheduleOrigin origin,
        ScheduleInput to,
        int departMinuteOfDay,
        TravelMode mode,
        TripPlanningSettings settings)
    {
        var roadKm = TravelTimeEstimator.RoadDistanceKm(
            origin.Latitude, origin.Longitude, to.Latitude, to.Longitude, settings);

        var metro = mode == TravelMode.Metro ? origin.Metro : null;
        if (metro is null)
        {
            return Direct(roadKm, mode, settings);
        }

        if (!NeedsTrain(metro.StationOrder, to.StationOrder, roadKm, settings))
        {
            // Đứng sẵn ở ga thì đi bộ; xuất phát từ toạ độ thì gần đi bộ, xa đi xe.
            return Direct(roadKm, metro.StartsAtStation ? TravelMode.Walking : TravelMode.Auto, settings);
        }

        var toStation = metro.StartsAtStation
            ? null
            : Direct(
                TravelTimeEstimator.RoadDistanceKmFromMeters(metro.DistanceToStationMeters, settings),
                TravelMode.Auto,
                settings);

        return ByTrain(
            metro.Timetable, metro.StationOrder, to, toStation?.Mode, toStation?.TotalMinutes ?? 0,
            departMinuteOfDay, roadKm, settings);
    }

    /// <param name="departMinuteOfDay">Giờ rời chặng trước (đã tham quan xong), tính bằng số phút từ 00:00.</param>
    /// <param name="timetable">Lịch tàu của ngày đi; null thì chế độ Metro được tính như Auto.</param>
    public static TripLegPlan BetweenStops(
        ScheduleInput from,
        ScheduleInput to,
        int departMinuteOfDay,
        TravelMode mode,
        MetroDayTimetable? timetable,
        TripPlanningSettings settings)
    {
        var roadKm = TravelTimeEstimator.RoadDistanceKm(
            from.Latitude, from.Longitude, to.Latitude, to.Longitude, settings);

        if (mode != TravelMode.Metro || timetable is null)
        {
            return Direct(roadKm, mode, settings);
        }

        if (from.StationOrder is not { } fromStationOrder
            || !NeedsTrain(fromStationOrder, to.StationOrder, roadKm, settings))
        {
            // Trong chế độ Metro người đi không có xe: giữa hai chặng không cần tàu thì luôn đi bộ.
            return Direct(roadKm, TravelMode.Walking, settings);
        }

        var walkToStationMinutes = TravelTimeEstimator.WalkingMinutes(
            TravelTimeEstimator.RoadDistanceKmFromMeters(from.DistanceFromStationMeters, settings), settings);

        return ByTrain(
            timetable, fromStationOrder, to, TravelMode.Walking, walkToStationMinutes,
            departMinuteOfDay, roadKm, settings);
    }

    private static bool NeedsTrain(
        int currentStationOrder, int? destinationStationOrder, double roadKm, TripPlanningSettings settings) =>
        destinationStationOrder is { } destination
        && destination != currentStationOrder
        && roadKm * 1000 > settings.AutoWalkingMaxMeters;

    private static TripLegPlan ByTrain(
        MetroDayTimetable timetable,
        int boardStationOrder,
        ScheduleInput to,
        TravelMode? toStationMode,
        int toStationMinutes,
        int departMinuteOfDay,
        double roadKm,
        TripPlanningSettings settings)
    {
        var alightStationOrder = to.StationOrder!.Value;
        var ride = timetable.NextRide(boardStationOrder, alightStationOrder, departMinuteOfDay + toStationMinutes);
        if (ride is null)
        {
            // Hết tàu hoặc không có chuyến: không chặn tạo lịch, đoạn này tính xe máy đi thẳng.
            return new TripLegPlan(
                TravelMode.Motorbike,
                TravelTimeEstimator.MotorbikeMinutes(roadKm, settings),
                Fallback: MetroUnavailable);
        }

        var walkMinutes = TravelTimeEstimator.WalkingMinutes(
            TravelTimeEstimator.RoadDistanceKmFromMeters(to.DistanceFromStationMeters, settings), settings);

        return new TripLegPlan(
            TravelMode.Metro,
            toStationMinutes + ride.WaitMinutes + ride.RideMinutes + walkMinutes,
            new MetroLegDetail(
                toStationMode,
                toStationMinutes,
                boardStationOrder,
                alightStationOrder,
                ride.WaitMinutes,
                ride.RideMinutes,
                ride.StopCount,
                walkMinutes));
    }

    private static TripLegPlan Direct(double roadKm, TravelMode mode, TripPlanningSettings settings) => new(
        TravelTimeEstimator.ResolveDirectMode(roadKm, mode, settings),
        TravelTimeEstimator.EstimateMinutes(roadKm, mode, settings));
}

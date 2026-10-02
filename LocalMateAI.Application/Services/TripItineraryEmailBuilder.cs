using System.Globalization;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

// Dựng mail lịch trình cho 1 trip vừa chốt (logic thuần, không chạm DB).
// trip: read model (có địa chỉ, loại địa điểm); detail: response đã tính giờ/tổng — cùng thứ tự chặng.
public static class TripItineraryEmailBuilder
{
    public static EmailOutboxEntry Build(User user, TripDetailReadModel trip, TripDetailResponse detail)
    {
        var stops = detail.Items
            .Select((item, index) =>
            {
                var source = trip.Items[index];
                var travelMinutes = index == 0 ? detail.TravelMinutesFromOrigin : item.TravelMinutesFromPrevious;
                return new TripItineraryStopEmailModel(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    FormatTime(item.ScheduledTime),
                    item.PlaceName,
                    CategoryLabel(source.Category),
                    source.Address ?? string.Empty,
                    item.StationName ?? string.Empty,
                    EmailDisplayFormat.Duration(item.EstimatedDurationMinutes),
                    Cost(item.EstimatedBudget),
                    TravelNote(travelMinutes, isFirstStop: index == 0));
            })
            .ToList();

        var model = new TripItineraryEmailModel(
            user.FullName,
            trip.PlannedStartAt is { } plannedStart ? FormatDate(plannedStart) : "Chưa đặt ngày",
            TimeRange(detail),
            EmailDisplayFormat.Duration(detail.TotalMinutes),
            detail.EstimatedBudget == 0 ? "Miễn phí" : $"{EmailDisplayFormat.Money(detail.EstimatedBudget)} / người",
            TravelModeLabel(trip.TravelMode),
            detail.StationName ?? string.Empty,
            $"{stops.Count} chặng",
            stops);

        var subject = trip.PlannedStartAt is { } date
            ? $"Lịch trình chuyến đi ngày {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} - LocalMate AI"
            : "Lịch trình chuyến đi của bạn - LocalMate AI";

        return new EmailOutboxEntry(
            user.Email,
            subject,
            EmailTemplateNames.TripItinerary,
            EmailOutboxModelRegistry.Serialize(model),
            $"trip-itinerary:{trip.Id}");
    }

    private static string TimeRange(TripDetailResponse detail)
    {
        var start = detail.StartTime ?? detail.Items.FirstOrDefault()?.ScheduledTime;
        if (start is null)
        {
            return string.Empty;
        }

        return detail.EndTime is { } end
            ? $"{FormatTime(start.Value)} – {FormatTime(end)}"
            : FormatTime(start.Value);
    }

    private static string TravelNote(int? minutes, bool isFirstStop) =>
        minutes is > 0
            ? $"{minutes} phút di chuyển từ {(isFirstStop ? "điểm xuất phát" : "chặng trước")}"
            : string.Empty;

    private static string Cost(decimal amount) =>
        amount == 0 ? "Miễn phí" : EmailDisplayFormat.Money(amount);

    // PlannedStartAt là giờ đồng hồ Việt Nam (không phải UTC) nên không đổi múi giờ.
    private static string FormatDate(DateTime plannedStart) =>
        $"{WeekdayLabel(plannedStart.DayOfWeek)}, {plannedStart.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    private static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string WeekdayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Thứ Hai",
        DayOfWeek.Tuesday => "Thứ Ba",
        DayOfWeek.Wednesday => "Thứ Tư",
        DayOfWeek.Thursday => "Thứ Năm",
        DayOfWeek.Friday => "Thứ Sáu",
        DayOfWeek.Saturday => "Thứ Bảy",
        _ => "Chủ Nhật"
    };

    private static string CategoryLabel(PlaceCategory category) => category switch
    {
        PlaceCategory.Cafe => "Cà phê",
        PlaceCategory.Food => "Ẩm thực",
        PlaceCategory.Culture => "Văn hoá",
        PlaceCategory.CheckIn => "Check-in",
        _ => category.ToString()
    };

    private static string TravelModeLabel(TravelMode travelMode) => travelMode switch
    {
        TravelMode.Walking => "Đi bộ",
        TravelMode.Motorbike => "Xe máy",
        _ => "Tự chọn (đi bộ hoặc xe máy)"
    };
}

namespace LocalMateAI.Application.DTOs.Email;

// Dữ liệu mail lịch trình khi chốt trip, đã định dạng sẵn để template chỉ việc hiển thị.
public sealed record TripItineraryEmailModel(
    string FullName,
    string PlannedDate,
    string TimeRange,
    string TotalDuration,
    string EstimatedBudget,
    string TravelModeLabel,
    string StartStationName,
    string StopCount,
    IReadOnlyList<TripItineraryStopEmailModel> Stops);

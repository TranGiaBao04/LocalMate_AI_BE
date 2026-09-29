namespace LocalMateAI.Application.DTOs.Email;

// Một chặng trong mail lịch trình. Chuỗi rỗng = không hiển thị dòng đó.
public sealed record TripItineraryStopEmailModel(
    string OrderNumber,
    string Time,
    string PlaceName,
    string CategoryLabel,
    string Address,
    string StationName,
    string Duration,
    string Cost,
    string TravelNote);

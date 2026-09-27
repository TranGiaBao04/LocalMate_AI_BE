namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>Một chuyến: giờ rời ga đi và giờ tới ga đến (dự kiến).</summary>
public sealed record MetroJourneyTripResponse(
    TimeOnly Departure,
    TimeOnly Arrival);

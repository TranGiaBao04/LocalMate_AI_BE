namespace LocalMateAI.Application.DTOs.Trips;

public sealed record FinalizeTripResponse(Guid TripId, string Status);
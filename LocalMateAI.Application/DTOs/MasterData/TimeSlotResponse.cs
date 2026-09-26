namespace LocalMateAI.Application.DTOs.MasterData;

public sealed record TimeSlotResponse(string Code, string Label, TimeOnly StartTime, int MaxDurationHours);

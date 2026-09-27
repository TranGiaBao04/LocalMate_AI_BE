namespace LocalMateAI.Application.DTOs.Metro;

/// <summary>Trong khoảng [From, To) cứ Minutes phút có một chuyến.</summary>
public sealed record MetroHeadway(
    TimeOnly From,
    TimeOnly To,
    int Minutes);

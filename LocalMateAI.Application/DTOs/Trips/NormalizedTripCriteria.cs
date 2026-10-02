namespace LocalMateAI.Application.DTOs.Trips;

public enum TripDurationCategory
{
    Short,      // ≤ 3 giờ
    HalfDay,    // 4-6 giờ
    FullDay     // > 6 giờ
}

public enum BudgetTier
{
    Economy,    // ≤ 200,000đ
    Standard,   // 200,001 - 500,000đ
    Premium     // > 500,000đ
}

/// <param name="BudgetMax">Ngân sách tối đa của cả chuyến (VNĐ). Số chặng do ItineraryScheduler quyết định.</param>
public sealed record NormalizedTripCriteria(
    TripDurationCategory DurationCategory,
    BudgetTier BudgetTier,
    decimal BudgetMax);

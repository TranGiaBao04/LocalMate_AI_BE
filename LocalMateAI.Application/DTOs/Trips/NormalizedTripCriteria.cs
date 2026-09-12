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

public sealed record NormalizedTripCriteria(
    TripDurationCategory DurationCategory,
    int EstimatedStopCount,
    BudgetTier BudgetTier,
    decimal BudgetPerStop);

using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripCriteriaNormalizationService : ITripCriteriaNormalizationService
{
    private const int AverageMinutesPerStop = 90;
    private const int AverageMinutesTravelBetweenStops = 20;

    public NormalizedTripCriteria Normalize(TripRequestDto request)
    {
        var estimatedStopCount = EstimateStopCount(request.DurationHours);
        var budgetPerStop = request.BudgetMax / estimatedStopCount;

        return new NormalizedTripCriteria(
            ClassifyDuration(request.DurationHours),
            estimatedStopCount,
            ClassifyBudget(request.BudgetMax),
            budgetPerStop);
    }

    private static TripDurationCategory ClassifyDuration(int hours) => hours switch
    {
        <= 3 => TripDurationCategory.Short,
        <= 6 => TripDurationCategory.HalfDay,
        _ => TripDurationCategory.FullDay
    };

    private static int EstimateStopCount(int hours)
    {
        var totalMinutes = hours * 60;
        var minutesPerStop = AverageMinutesPerStop + AverageMinutesTravelBetweenStops;
        return Math.Max(1, totalMinutes / minutesPerStop);
    }

    private static BudgetTier ClassifyBudget(decimal budgetMax) => budgetMax switch
    {
        <= 200_000m => BudgetTier.Economy,
        <= 500_000m => BudgetTier.Standard,
        _ => BudgetTier.Premium
    };
}

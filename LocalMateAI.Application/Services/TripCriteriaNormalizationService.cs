using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripCriteriaNormalizationService : ITripCriteriaNormalizationService
{
    public NormalizedTripCriteria Normalize(TripRequestDto request) => new(
        ClassifyDuration(request.DurationHours),
        ClassifyBudget(request.BudgetMax),
        request.BudgetMax);

    private static TripDurationCategory ClassifyDuration(int hours) => hours switch
    {
        <= 3 => TripDurationCategory.Short,
        <= 6 => TripDurationCategory.HalfDay,
        _ => TripDurationCategory.FullDay
    };

    private static BudgetTier ClassifyBudget(decimal budgetMax) => budgetMax switch
    {
        <= 200_000m => BudgetTier.Economy,
        <= 500_000m => BudgetTier.Standard,
        _ => BudgetTier.Premium
    };
}

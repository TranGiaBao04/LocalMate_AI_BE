using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class CandidateFilterService : ICandidateFilterService
{
    public CandidateFilterResult Filter(
        IReadOnlyList<PlaceCandidateDto> candidates,
        NormalizedTripCriteria criteria)
    {
        var budgetPerStop = criteria.BudgetPerStop;
        var excluded = new List<ExcludedCandidateDto>();

        var passed = candidates
            .Where(candidate =>
            {
                var isFree = candidate.EstimatedCostMax <= 0;
                var withinBudget = candidate.EstimatedCostMax <= budgetPerStop;
                if (!isFree && !withinBudget)
                {
                    excluded.Add(new ExcludedCandidateDto(
                        candidate.PlaceId, candidate.PlaceName, "budget"));
                }
                return isFree || withinBudget;
            })
            .ToList();

        return new CandidateFilterResult(passed, excluded);
    }
}
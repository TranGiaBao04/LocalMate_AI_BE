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
        var excluded = new List<ExcludedCandidateDto>();

        // Một địa điểm không được vượt tổng ngân sách; tổng cả lịch do ItineraryScheduler giữ trong ngân sách.
        var passed = candidates
            .Where(candidate =>
            {
                var withinBudget = candidate.EstimatedCostMax <= criteria.BudgetMax;
                if (!withinBudget)
                {
                    excluded.Add(new ExcludedCandidateDto(
                        candidate.PlaceId, candidate.PlaceName, "budget"));
                }
                return withinBudget;
            })
            .ToList();

        return new CandidateFilterResult(passed, excluded);
    }
}

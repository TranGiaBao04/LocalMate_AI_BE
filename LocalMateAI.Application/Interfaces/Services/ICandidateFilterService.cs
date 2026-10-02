using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ICandidateFilterService
{
    CandidateFilterResult Filter(
        IReadOnlyList<PlaceCandidateDto> candidates,
        NormalizedTripCriteria criteria);
}
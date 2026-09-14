using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripCriteriaNormalizationService
{
    NormalizedTripCriteria Normalize(TripRequestDto request);
}

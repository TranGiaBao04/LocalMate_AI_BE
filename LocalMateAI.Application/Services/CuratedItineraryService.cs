using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class CuratedItineraryService(
    ICuratedItineraryRepository repository,
    IGeoService geoService) : ICuratedItineraryService
{
    public async Task<IReadOnlyList<CuratedItineraryResponse>> GetCuratedItinerariesAsync(
        CancellationToken cancellationToken = default)
    {
        var itineraries = await repository.GetAllAsync(cancellationToken);
        var result = new List<CuratedItineraryResponse>(itineraries.Count);

        foreach (var itinerary in itineraries)
        {
            var first = itinerary.Items.OrderBy(item => item.OrderIndex).FirstOrDefault();
            var station = first is null
                ? null
                : await geoService.FindNearestStationForPlaceAsync(first.PlaceId, cancellationToken);

            result.Add(itinerary with { StationName = station?.StationName });
        }

        return result;
    }
}

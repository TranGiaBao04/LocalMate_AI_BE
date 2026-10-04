using LocalMateAI.Application.DTOs.Search;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ISearchRepository
{
    Task<IReadOnlyList<SearchStationItem>> SearchStationsAsync(
        string keyword,
        int take,
        double clusterRadiusMeters,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchPlaceItem>> SearchPlacesAsync(
        string keyword,
        int take,
        double clusterRadiusMeters,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchCuratedItineraryItem>> SearchCuratedItinerariesAsync(
        string keyword,
        int take,
        CancellationToken cancellationToken = default);
}

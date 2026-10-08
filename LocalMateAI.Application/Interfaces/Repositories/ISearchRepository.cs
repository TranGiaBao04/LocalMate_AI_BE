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

    /// <summary>Thông tin hiển thị của các địa điểm khớp theo nghĩa. Chỉ trả địa điểm Active chưa xoá.</summary>
    Task<IReadOnlyList<SearchPlaceItem>> GetSemanticPlacesAsync(
        IReadOnlyCollection<Guid> placeIds,
        double clusterRadiusMeters,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchCuratedItineraryItem>> SearchCuratedItinerariesAsync(
        string keyword,
        int take,
        CancellationToken cancellationToken = default);
}

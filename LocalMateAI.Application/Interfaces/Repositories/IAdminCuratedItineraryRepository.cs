using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Itineraries;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminCuratedItineraryRepository
{
    Task<PagedResult<AdminCuratedItineraryResponse>> GetPagedAsync(
        AdminCuratedItineraryQuery query,
        CancellationToken cancellationToken = default);

    // Trả null nếu lịch mẫu không tồn tại hoặc đã xoá.
    Task<AdminCuratedItineraryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Trả các địa điểm tìm thấy, kể cả đã xoá mềm hoặc chưa Active; id không có trong DB thì vắng mặt.
    Task<IReadOnlyList<CuratedPlaceCandidate>> GetPlacesAsync(
        IReadOnlyCollection<Guid> placeIds,
        CancellationToken cancellationToken = default);

    Task<Guid> AddAsync(CuratedItineraryDraft draft, CancellationToken cancellationToken = default);

    // Thay toàn bộ nội dung và các chặng. Trả false nếu lịch mẫu không tồn tại hoặc đã xoá.
    Task<bool> UpdateAsync(Guid id, CuratedItineraryDraft draft, CancellationToken cancellationToken = default);

    // Trả false nếu lịch mẫu không tồn tại hoặc đã xoá.
    Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

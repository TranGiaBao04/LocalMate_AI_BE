using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Itineraries;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminCuratedItineraryService
{
    Task<PagedResult<AdminCuratedItineraryResponse>> GetPagedAsync(
        AdminCuratedItineraryQuery query,
        CancellationToken cancellationToken = default);

    // Trả null nếu lịch mẫu không tồn tại hoặc đã xoá.
    Task<AdminCuratedItineraryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AdminCuratedItineraryResult> CreateAsync(
        SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken = default);

    Task<AdminCuratedItineraryResult> UpdateAsync(
        Guid id,
        SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeleteAdminCuratedItineraryResultStatus> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

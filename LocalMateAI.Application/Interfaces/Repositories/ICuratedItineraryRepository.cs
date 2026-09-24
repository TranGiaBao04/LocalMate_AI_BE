using LocalMateAI.Application.DTOs.Itineraries;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ICuratedItineraryRepository
{
    Task<IReadOnlyList<CuratedItineraryResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    // Trả null nếu lịch trình không tồn tại. Chỉ gồm các địa điểm Active, theo đúng thứ tự.
    Task<CuratedItineraryForApplyReadModel?> GetForApplyAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

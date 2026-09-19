using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminPlaceService
{
    Task<AdminPlaceOperationResult> CreateAsync(
        CreateAdminPlaceRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPlaceResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<AdminPlaceResponse?> GetByIdAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task<AdminPlaceOperationResult> UpdateAsync(
        Guid placeId,
        UpdateAdminPlaceRequest request,
        CancellationToken cancellationToken = default);

    Task<DeleteAdminPlaceResult> DeleteAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);
}

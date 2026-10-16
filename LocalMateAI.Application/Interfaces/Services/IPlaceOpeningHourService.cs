using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceOpeningHourService
{
    /// <summary>Giờ mở cửa đủ 7 thứ; null khi địa điểm không có hoặc đã xoá mềm.</summary>
    Task<PlaceOpeningHoursResponse?> GetAsync(Guid placeId, CancellationToken cancellationToken = default);

    Task<SavePlaceOpeningHoursResult> SaveAsync(
        Guid placeId,
        SavePlaceOpeningHoursRequest request,
        CancellationToken cancellationToken = default);
}

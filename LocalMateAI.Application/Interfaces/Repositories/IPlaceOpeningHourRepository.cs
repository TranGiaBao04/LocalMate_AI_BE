using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPlaceOpeningHourRepository
{
    /// <summary>Các dòng giờ mở cửa đã lưu; null khi địa điểm không có hoặc đã xoá mềm.</summary>
    Task<IReadOnlyList<PlaceOpeningHourDto>?> GetByPlaceAsync(Guid placeId, CancellationToken cancellationToken = default);

    /// <summary>Giờ mở cửa của nhiều địa điểm; địa điểm chưa có dòng nào thì không có trong kết quả.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PlaceOpeningHourDto>>> GetForPlacesAsync(
        IReadOnlyCollection<Guid> placeIds,
        CancellationToken cancellationToken = default);

    /// <summary>Ghi đè cả tuần trong 1 transaction; false khi địa điểm không có hoặc đã xoá mềm (không ghi gì).</summary>
    Task<bool> ReplaceAsync(
        Guid placeId,
        IReadOnlyList<PlaceOpeningHourDto> days,
        CancellationToken cancellationToken = default);
}

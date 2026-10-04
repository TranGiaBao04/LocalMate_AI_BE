using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IStationSuggestionService
{
    /// <summary>
    /// Các ga gần điểm xuất phát nhất có đủ địa điểm, để gợi ý khi quanh ga cột mốc không có địa điểm nào.
    /// Không bao giờ trả lại chính ga cột mốc.
    /// </summary>
    Task<IReadOnlyList<SuggestedStationDto>> SuggestAsync(
        TripOriginResolution origin,
        CancellationToken cancellationToken = default);
}

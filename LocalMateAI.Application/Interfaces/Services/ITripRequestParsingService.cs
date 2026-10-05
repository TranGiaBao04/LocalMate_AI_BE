using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripRequestParsingService
{
    /// <summary>
    /// Nhờ AI đọc câu người dùng gõ và điền sẵn các tiêu chí tạo lịch. Không tạo lịch, không lưu gì.
    /// </summary>
    Task<ParseTripRequestResult> ParseAsync(
        Guid userId,
        ParseTripRequestRequest request,
        CancellationToken cancellationToken = default);
}

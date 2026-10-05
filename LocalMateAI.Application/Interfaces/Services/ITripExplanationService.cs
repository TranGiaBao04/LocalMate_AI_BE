using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripExplanationService
{
    /// <summary>Nhờ AI viết lý do cho từng chặng của một trip bản nháp. AI chỉ viết chữ, không đổi lịch.</summary>
    Task<ExplainTripResult> ExplainAsync(Guid userId, Guid tripId, CancellationToken cancellationToken = default);
}

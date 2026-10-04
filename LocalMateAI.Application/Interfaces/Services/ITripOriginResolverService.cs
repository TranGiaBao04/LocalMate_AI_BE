using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripOriginResolverService
{
    /// <summary>
    /// Xác định ga lên, ga cột mốc và vùng phục vụ. Request phải được validate trước.
    /// Trả null khi DB thiếu ga cần dùng (lỗi dữ liệu, không phải lỗi người dùng).
    /// </summary>
    Task<TripOriginResolution?> ResolveAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default);
}

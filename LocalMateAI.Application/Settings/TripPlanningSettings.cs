using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Settings;

/// <summary>
/// Thông số xếp lịch và ước tính di chuyển do admin chỉnh (SystemSettings). Đọc một lần mỗi request rồi truyền vào
/// ItineraryScheduler/TravelTimeEstimator để mọi bước trong cùng request dùng chung một bộ số.
/// </summary>
public sealed record TripPlanningSettings(
    int CafeVisitMinutes,
    int FoodVisitMinutes,
    int CultureVisitMinutes,
    int CheckInVisitMinutes,
    double WalkingSpeedKmH,
    double MotorbikeSpeedKmH,
    double RoadDetourFactor,
    int AutoWalkingMaxMeters)
{
    /// <summary>Đúng bằng mặc định khai báo trong SystemSettingDefinitions (có test kiểm khớp).</summary>
    public static readonly TripPlanningSettings Default = new(60, 75, 90, 45, 4.8, 24.0, 1.3, 700);

    public static async Task<TripPlanningSettings> LoadAsync(ISystemSettingProvider settings,
        CancellationToken cancellationToken = default) => new(
        await settings.GetIntAsync(SystemSettingKeys.CafeVisitMinutes, cancellationToken),
        await settings.GetIntAsync(SystemSettingKeys.FoodVisitMinutes, cancellationToken),
        await settings.GetIntAsync(SystemSettingKeys.CultureVisitMinutes, cancellationToken),
        await settings.GetIntAsync(SystemSettingKeys.CheckInVisitMinutes, cancellationToken),
        (double)await settings.GetDecimalAsync(SystemSettingKeys.WalkingSpeedKmH, cancellationToken),
        (double)await settings.GetDecimalAsync(SystemSettingKeys.MotorbikeSpeedKmH, cancellationToken),
        (double)await settings.GetDecimalAsync(SystemSettingKeys.RoadDetourFactor, cancellationToken),
        await settings.GetIntAsync(SystemSettingKeys.AutoWalkingMaxMeters, cancellationToken));
}

using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Application.Services;

public sealed class MasterDataService(
    IMetroStationRepository metroStationRepository,
    IMemoryCache cache) : IMasterDataService
{
    private const string CacheKey = "master-data";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private static readonly IReadOnlyDictionary<string, string> QuickTagLabels = new Dictionary<string, string>
    {
        [ReviewQuickTags.WorthVisiting] = "Đáng ghé thăm",
        [ReviewQuickTags.NearMetro] = "Gần Metro",
        [ReviewQuickTags.EasyToReach] = "Dễ đi tới",
        [ReviewQuickTags.GoodValue] = "Giá hợp lý",
        [ReviewQuickTags.NiceAtmosphere] = "Không khí dễ chịu",
        [ReviewQuickTags.GoodForGroups] = "Hợp đi nhóm",
        [ReviewQuickTags.TooCrowded] = "Quá đông",
        [ReviewQuickTags.HardToFind] = "Khó tìm",
        [ReviewQuickTags.Overpriced] = "Giá hơi cao",
        [ReviewQuickTags.BelowExpectations] = "Chưa như kỳ vọng",
        [ReviewQuickTags.InaccurateDescription] = "Mô tả chưa đúng",
        [ReviewQuickTags.WantsReplacement] = "Muốn thay địa điểm khác"
    };

    private static readonly IReadOnlyList<TimeSlotResponse> TimeSlots =
    [
        Slot("morning", "Buổi sáng", 8),
        Slot("afternoon", "Buổi chiều", 13),
        Slot("evening", "Buổi tối", 18)
    ];

    public async Task<MasterDataResponse> GetMasterDataAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out MasterDataResponse? cached) && cached is not null)
        {
            return cached;
        }

        var stations = await metroStationRepository.GetAllAsync(cancellationToken);

        var result = new MasterDataResponse(
            stations,
            Enum.GetNames<PlaceCategory>(),
            ReviewQuickTags.All
                .Select(code => new ReviewQuickTagResponse(code, QuickTagLabels.GetValueOrDefault(code, code)))
                .ToList(),
            Enum.GetNames<TripStatus>(),
            Enum.GetNames<TravelMode>(),
            new TripLimitsResponse(TripRequestValidator.MinDurationHours, TripRequestValidator.MaxDurationHours),
            TimeSlots);

        cache.Set(CacheKey, result, CacheDuration);

        return result;
    }

    // MaxDurationHours = số giờ tối đa còn lại tới 24:00, để FE giới hạn lựa chọn thời lượng (chưa hỗ trợ qua nửa đêm).
    private static TimeSlotResponse Slot(string code, string label, int startHour) =>
        new(code, label, new TimeOnly(startHour, 0), 24 - startHour);
}

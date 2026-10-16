using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;

namespace LocalMateAI.Tests;

/// <summary>Repository giờ mở cửa giả: giờ đặt sẵn theo PlaceId, chỉ hỗ trợ đọc nhiều địa điểm.</summary>
internal sealed class FakePlaceOpeningHourRepository : IPlaceOpeningHourRepository
{
    public Dictionary<Guid, IReadOnlyList<PlaceOpeningHourDto>> Hours { get; } = [];

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<PlaceOpeningHourDto>>> GetForPlacesAsync(
        IReadOnlyCollection<Guid> placeIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<PlaceOpeningHourDto>>>(
            Hours.Where(entry => placeIds.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value));

    public Task<IReadOnlyList<PlaceOpeningHourDto>?> GetByPlaceAsync(
        Guid placeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> ReplaceAsync(
        Guid placeId, IReadOnlyList<PlaceOpeningHourDto> days, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal static class TestOpeningHours
{
    /// <summary>Mở cùng một khung giờ cả 7 ngày.</summary>
    public static IReadOnlyList<PlaceOpeningHourDto> EveryDay(int openHour, int closeHour) =>
        Enum.GetValues<DayOfWeek>()
            .Select(day => new PlaceOpeningHourDto(day, false, new TimeOnly(openHour, 0), new TimeOnly(closeHour, 0)))
            .ToList();

    /// <summary>Đóng cửa cả 7 ngày.</summary>
    public static IReadOnlyList<PlaceOpeningHourDto> ClosedEveryDay() =>
        Enum.GetValues<DayOfWeek>().Select(day => new PlaceOpeningHourDto(day, true, null, null)).ToList();
}

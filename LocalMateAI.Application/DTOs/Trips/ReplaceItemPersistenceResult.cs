namespace LocalMateAI.Application.DTOs.Trips;

public enum ReplaceItemPersistenceStatus
{
    Replaced,

    /// <summary>Item không còn thuộc user, trip đã Finalized, hoặc địa điểm mới vừa xuất hiện trong trip.</summary>
    NotEligible,

    /// <summary>Tính lại giờ thì lịch dài quá 24 giờ; không có gì được ghi.</summary>
    TooLong
}

/// <param name="Item">Chặng vừa được thay, với giờ sau khi tính lại.</param>
/// <param name="Items">Toàn bộ các chặng của trip sau khi tính lại giờ, theo thứ tự đi.</param>
public sealed record ReplaceItemPersistenceResult(
    ReplaceItemPersistenceStatus Status,
    ReplacedItemReadModel? Item = null,
    IReadOnlyList<ItineraryTimelineItemResponse>? Items = null);

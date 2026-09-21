using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class ItineraryTimelineRecalculatorTests
{
    private readonly ItineraryTimelineRecalculator _sut = new();

    [Fact]
    public void Recalculate_RemovedMiddleItem_ShiftsLaterItemsUp()
    {
        // Đã xoá item OrderIndex 1 (09:30) khỏi lịch 08:00 / 09:30 / 11:00 / 12:30
        var first = Snap(order: 0, hour: 8, minute: 0);
        var third = Snap(order: 2, hour: 11, minute: 0);
        var fourth = Snap(order: 3, hour: 12, minute: 30);

        var updates = _sut.Recalculate([first, third, fourth]);

        Assert.Equal(
            new[]
            {
                new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(9, 30)),
                new TimelineItemUpdate(fourth.ItemId, 2, new TimeOnly(11, 0))
            },
            updates.ToArray());
    }

    [Fact]
    public void Recalculate_RemovedFirstItem_KeepsNewFirstItemTime()
    {
        var second = Snap(order: 1, hour: 9, minute: 30);
        var third = Snap(order: 2, hour: 11, minute: 0);

        var updates = _sut.Recalculate([second, third]);

        Assert.Equal(
            new[]
            {
                new TimelineItemUpdate(second.ItemId, 0, new TimeOnly(9, 30)),
                new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(11, 0))
            },
            updates.ToArray());
    }

    [Fact]
    public void Recalculate_RemovedLastItem_ReturnsNoUpdates()
    {
        var first = Snap(order: 0, hour: 8, minute: 0);
        var second = Snap(order: 1, hour: 9, minute: 30);

        Assert.Empty(_sut.Recalculate([first, second]));
    }

    [Fact]
    public void Recalculate_GapBetweenItems_ClosesGap()
    {
        // Item 0 kéo dài 60' (08:00-09:00), item 1 bắt đầu 09:30 → dồn sát về 09:00
        var first = Snap(order: 0, hour: 8, minute: 0, duration: 60);
        var second = Snap(order: 1, hour: 9, minute: 30);

        var update = Assert.Single(_sut.Recalculate([first, second]));

        Assert.Equal(new TimelineItemUpdate(second.ItemId, 1, new TimeOnly(9, 0)), update);
    }

    [Fact]
    public void Recalculate_OverlappingItems_NeverPushesLater()
    {
        // Item 0 kết thúc 10:00 nhưng item 1 bắt đầu 09:00 (chồng lấn) → giữ 09:00, không đẩy muộn
        var first = Snap(order: 0, hour: 8, minute: 0, duration: 120);
        var second = Snap(order: 1, hour: 9, minute: 0);

        Assert.Empty(_sut.Recalculate([first, second]));
    }

    [Fact]
    public void Recalculate_UnorderedInput_SortsByOrderIndex()
    {
        var first = Snap(order: 0, hour: 8, minute: 0);
        var third = Snap(order: 2, hour: 11, minute: 0);

        var update = Assert.Single(_sut.Recalculate([third, first]));

        Assert.Equal(new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(9, 30)), update);
    }

    [Fact]
    public void Recalculate_SingleItemWithGapInOrderIndex_RenumbersToZero()
    {
        var only = Snap(order: 3, hour: 14, minute: 0);

        var update = Assert.Single(_sut.Recalculate([only]));

        Assert.Equal(new TimelineItemUpdate(only.ItemId, 0, new TimeOnly(14, 0)), update);
    }

    [Fact]
    public void Recalculate_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(_sut.Recalculate([]));
    }

    private static TimelineItemSnapshot Snap(int order, int hour, int minute, int duration = 90) =>
        new(Guid.NewGuid(), order, new TimeOnly(hour, minute), duration);
}

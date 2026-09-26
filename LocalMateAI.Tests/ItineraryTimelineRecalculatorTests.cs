using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class ItineraryTimelineRecalculatorTests
{
    private static readonly TimeOnly Start = new(8, 0);
    private readonly ItineraryTimelineRecalculator _sut = new();

    // Mọi item mặc định cùng một vị trí nên mỗi lần di chuyển tốn tối thiểu 1 phút.

    [Fact]
    public void Recalculate_RemovedMiddleItem_ShiftsLaterItemsUpWithTravelTime()
    {
        // Đã xoá item OrderIndex 1 khỏi lịch 08:00 / 09:30 / 11:00 / 12:30, mỗi chặng 60'
        var first = Snap(order: 0, hour: 8, minute: 0);
        var third = Snap(order: 2, hour: 11, minute: 0);
        var fourth = Snap(order: 3, hour: 12, minute: 30);

        var updates = Recalculate([first, third, fourth]);

        Assert.Equal(
            new[]
            {
                new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(9, 1)),
                new TimelineItemUpdate(fourth.ItemId, 2, new TimeOnly(10, 2))
            },
            updates.ToArray());
    }

    [Fact]
    public void Recalculate_RemovedFirstItem_NewFirstItemInheritsTripStartTime()
    {
        // Chặng đầu (08:00) bị xoá: chặng thứ hai lên đầu và nhận mốc 08:00
        var second = Snap(order: 1, hour: 9, minute: 30);
        var third = Snap(order: 2, hour: 11, minute: 0);

        var updates = Recalculate([second, third], start: Start);

        Assert.Equal(
            new[]
            {
                new TimelineItemUpdate(second.ItemId, 0, new TimeOnly(8, 0)),
                new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(9, 1))
            },
            updates.ToArray());
    }

    [Fact]
    public void Recalculate_RemovedLastItem_ReturnsNoUpdatesWhenScheduleAlreadyConsistent()
    {
        var first = Snap(order: 0, hour: 8, minute: 0);
        var second = Snap(order: 1, hour: 9, minute: 1);

        Assert.Empty(Recalculate([first, second]));
    }

    [Fact]
    public void Recalculate_UsesDistanceAndTravelMode()
    {
        // Hai chặng cách ~2,89 km đường bộ, mỗi chặng 30'
        var first = Snap(order: 0, hour: 8, minute: 0, duration: 30, latitude: 10.770);
        var second = Snap(order: 1, hour: 9, minute: 0, duration: 30, latitude: 10.790);

        var auto = Assert.Single(Recalculate([first, second], mode: TravelMode.Auto));
        var walking = Assert.Single(Recalculate([first, second], mode: TravelMode.Walking));

        Assert.Equal(new TimeOnly(8, 38), auto.ScheduledTime);   // 30' + 8' xe máy
        Assert.Equal(new TimeOnly(9, 7), walking.ScheduledTime); // 30' + 37' đi bộ
    }

    [Fact]
    public void Recalculate_UnorderedInput_SortsByOrderIndex()
    {
        var first = Snap(order: 0, hour: 8, minute: 0);
        var third = Snap(order: 2, hour: 11, minute: 0);

        var update = Assert.Single(Recalculate([third, first]));

        Assert.Equal(new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(9, 1)), update);
    }

    [Fact]
    public void Recalculate_SingleItemWithGapInOrderIndex_RenumbersToZero()
    {
        var only = Snap(order: 3, hour: 14, minute: 0);

        var update = Assert.Single(Recalculate([only], start: new TimeOnly(14, 0)));

        Assert.Equal(new TimelineItemUpdate(only.ItemId, 0, new TimeOnly(14, 0)), update);
    }

    [Fact]
    public void Recalculate_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(Recalculate([]));
    }

    private IReadOnlyList<TimelineItemUpdate> Recalculate(
        IReadOnlyList<TimelineItemSnapshot> remaining,
        TimeOnly? start = null,
        TravelMode mode = TravelMode.Auto) =>
        _sut.Recalculate(new TimelineRecalculationInput(remaining, start ?? Start, mode));

    private static TimelineItemSnapshot Snap(
        int order, int hour, int minute, int duration = 60, double latitude = 10.770) =>
        new(Guid.NewGuid(), order, new TimeOnly(hour, minute), duration, latitude, 106.70);
}

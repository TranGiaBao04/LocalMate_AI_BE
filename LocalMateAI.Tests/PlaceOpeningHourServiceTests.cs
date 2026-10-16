using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class PlaceOpeningHourServiceTests
{
    private static readonly Guid PlaceId = Guid.NewGuid();

    [Fact]
    public async Task Get_MissingPlace_ReturnsNull()
    {
        var service = new PlaceOpeningHourService(new FakeRepository(placeExists: false));

        Assert.Null(await service.GetAsync(PlaceId));
    }

    [Fact]
    public async Task Get_ExistingPlace_ReturnsSevenDays()
    {
        var repository = new FakeRepository(placeExists: true);
        repository.Stored.Add(new PlaceOpeningHourDto(DayOfWeek.Monday, true, null, null));

        var response = await new PlaceOpeningHourService(repository).GetAsync(PlaceId);

        Assert.NotNull(response);
        Assert.True(response.HasData);
        Assert.Equal(7, response.Days.Count);
    }

    [Fact]
    public async Task Save_InvalidBody_FailsWithoutTouchingTheRepository()
    {
        var repository = new FakeRepository(placeExists: true);

        var result = await new PlaceOpeningHourService(repository).SaveAsync(
            PlaceId, new SavePlaceOpeningHoursRequest(null));

        Assert.Equal(SavePlaceOpeningHoursResultStatus.ValidationFailed, result.Status);
        Assert.True(result.ValidationErrors!.ContainsKey("Days"));
        Assert.Equal(0, repository.ReplaceCalls);
    }

    [Fact]
    public async Task Save_MissingPlace_ReturnsNotFound()
    {
        var result = await new PlaceOpeningHourService(new FakeRepository(placeExists: false)).SaveAsync(
            PlaceId, new SavePlaceOpeningHoursRequest([]));

        Assert.Equal(SavePlaceOpeningHoursResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Save_ValidBody_StoresSortedDaysAndReturnsTheWeek()
    {
        var repository = new FakeRepository(placeExists: true);

        var result = await new PlaceOpeningHourService(repository).SaveAsync(
            PlaceId,
            new SavePlaceOpeningHoursRequest(
            [
                new SavePlaceOpeningHourItem(DayOfWeek.Sunday, false, new TimeOnly(17, 0), new TimeOnly(2, 0)),
                new SavePlaceOpeningHourItem(DayOfWeek.Monday, true, null, null)
            ]));

        Assert.Equal(SavePlaceOpeningHoursResultStatus.Success, result.Status);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Sunday], repository.Stored.Select(day => day.DayOfWeek));
        Assert.Equal(PlaceId, result.Response!.PlaceId);
        Assert.Equal(7, result.Response.Days.Count);
        Assert.True(result.Response.Days[0].IsClosed);
    }

    private sealed class FakeRepository(bool placeExists) : IPlaceOpeningHourRepository
    {
        public List<PlaceOpeningHourDto> Stored { get; } = [];
        public int ReplaceCalls { get; private set; }

        public Task<IReadOnlyList<PlaceOpeningHourDto>?> GetByPlaceAsync(
            Guid placeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceOpeningHourDto>?>(placeExists ? Stored : null);

        public Task<bool> ReplaceAsync(
            Guid placeId, IReadOnlyList<PlaceOpeningHourDto> days, CancellationToken cancellationToken = default)
        {
            ReplaceCalls++;
            if (!placeExists)
            {
                return Task.FromResult(false);
            }

            Stored.Clear();
            Stored.AddRange(days);
            return Task.FromResult(true);
        }
    }
}

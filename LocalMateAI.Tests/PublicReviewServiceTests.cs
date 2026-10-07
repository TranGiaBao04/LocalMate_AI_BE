using LocalMateAI.Application.DTOs.PublicReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.PublicReviews;
using LocalMateAI.Domain.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Tests;

public sealed class PublicReviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReadsTheMaxLimitOnce_AndCutsToTheRequestedLimit()
    {
        var repository = new FakeRepository { Items = Reviews(5) };
        var service = CreateService(repository);

        var three = await service.GetAsync(new PublicReviewQuery());
        var one = await service.GetAsync(new PublicReviewQuery { Limit = 1 });
        var twelve = await service.GetAsync(new PublicReviewQuery { Limit = 12 });

        Assert.Equal(PublicReviewsResultStatus.Success, three.Status);
        Assert.Equal(Ids(repository.Items.Take(3)), Ids(three.Response!.Items));
        Assert.Equal(Now.UtcDateTime, three.Response.GeneratedAt);
        Assert.Equal(Ids(repository.Items.Take(1)), Ids(one.Response!.Items));
        Assert.Equal(Ids(repository.Items), Ids(twelve.Response!.Items));

        Assert.Equal(1, repository.Calls);
        Assert.Equal((PublicReviewService.MinRating, PublicReviewQuery.MaxLimit), repository.LastArguments);
    }

    [Fact]
    public async Task Get_ServesRepeatCallsFromCache()
    {
        var repository = new FakeRepository { Items = Reviews(2) };
        var service = CreateService(repository);

        var first = await service.GetAsync(new PublicReviewQuery());
        repository.Items = Reviews(4);
        var second = await service.GetAsync(new PublicReviewQuery());

        Assert.Equal(Ids(first.Response!.Items), Ids(second.Response!.Items));
        Assert.Equal(2, second.Response.Items.Count);
        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task Get_KeepsOnlyPositiveQuickTags_InTheOrderTheyWereSent()
    {
        var repository = new FakeRepository
        {
            Items =
            [
                Review(1, ReviewQuickTags.NearMetro, ReviewQuickTags.TooCrowded, ReviewQuickTags.WorthVisiting),
                Review(2, ReviewQuickTags.Overpriced, ReviewQuickTags.HardToFind),
                Review(3)
            ]
        };

        var result = await CreateService(repository).GetAsync(new PublicReviewQuery());

        var items = result.Response!.Items;
        Assert.Equal([ReviewQuickTags.NearMetro, ReviewQuickTags.WorthVisiting], items[0].QuickTags);
        Assert.Empty(items[1].QuickTags);
        Assert.Empty(items[2].QuickTags);

        // Các field khác của thẻ không bị đụng tới.
        Assert.Equal(repository.Items[0].Comment, items[0].Comment);
        Assert.Equal(repository.Items[0].Place, items[0].Place);
    }

    [Fact]
    public void PositiveQuickTags_AreTheSixPraiseCodes_AndAllAreKnownTags()
    {
        Assert.Equal(
            [
                ReviewQuickTags.WorthVisiting,
                ReviewQuickTags.NearMetro,
                ReviewQuickTags.EasyToReach,
                ReviewQuickTags.GoodValue,
                ReviewQuickTags.NiceAtmosphere,
                ReviewQuickTags.GoodForGroups
            ],
            ReviewQuickTags.Positive);
        Assert.All(ReviewQuickTags.Positive, tag => Assert.Contains(tag, ReviewQuickTags.All));
    }

    [Fact]
    public async Task NoMatchingReview_IsStillSuccessWithEmptyItems()
    {
        var result = await CreateService(new FakeRepository()).GetAsync(new PublicReviewQuery());

        Assert.Equal(PublicReviewsResultStatus.Success, result.Status);
        Assert.Empty(result.Response!.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task InvalidLimit_ReturnsValidationError_WithoutTouchingTheRepository(int limit)
    {
        var repository = new FakeRepository { Items = Reviews(3) };

        var result = await CreateService(repository).GetAsync(new PublicReviewQuery { Limit = limit });

        Assert.Equal(PublicReviewsResultStatus.InvalidQuery, result.Status);
        Assert.Null(result.Response);
        Assert.Equal("Limit", Assert.Single(result.ValidationErrors!).Key);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task RepositoryFailure_IsNotCached()
    {
        var repository = new FakeRepository { Items = Reviews(1), FailNext = true };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(new PublicReviewQuery()));
        var result = await service.GetAsync(new PublicReviewQuery());

        Assert.Single(result.Response!.Items);
        Assert.Equal(2, repository.Calls);
    }

    private static PublicReviewService CreateService(FakeRepository repository) =>
        new(
            repository,
            new PublicReviewQueryValidator(),
            new MemoryCache(new MemoryCacheOptions()),
            new FixedTimeProvider(Now));

    private static IReadOnlyList<PublicReviewItem> Reviews(int count) =>
        Enumerable.Range(1, count).Select(index => Review(index)).ToList();

    private static PublicReviewItem Review(int index, params string[] quickTags) =>
        new(
            Guid.NewGuid(),
            5,
            $"Nhận xét {index}",
            quickTags,
            Now.UtcDateTime.AddHours(-index),
            $"Người dùng {index}",
            new PublicReviewPlace(Guid.NewGuid(), $"Địa điểm {index}"));

    // Thẻ chứa một danh sách nên không so cả record được: so theo Id.
    private static IEnumerable<Guid> Ids(IEnumerable<PublicReviewItem> items) =>
        items.Select(item => item.Id);

    private sealed class FakeRepository : IPublicReviewRepository
    {
        public IReadOnlyList<PublicReviewItem> Items { get; set; } = [];
        public bool FailNext { get; set; }
        public int Calls { get; private set; }
        public (int MinRating, int Take)? LastArguments { get; private set; }

        public Task<IReadOnlyList<PublicReviewItem>> GetFeaturedAsync(
            int minRating,
            int take,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastArguments = (minRating, take);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Database unavailable.");
            }

            return Task.FromResult<IReadOnlyList<PublicReviewItem>>(Items.Take(take).ToList());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

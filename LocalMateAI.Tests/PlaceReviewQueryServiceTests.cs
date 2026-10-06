using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.PlaceReviews;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class PlaceReviewQueryServiceTests
{
    private static readonly Guid PlaceId = Guid.NewGuid();

    [Fact]
    public async Task InvalidQuery_ReturnsFieldErrors_WithoutTouchingRepository()
    {
        var repository = new FakeRepository();

        var result = await Service(repository).GetPlaceReviewsAsync(
            PlaceId, new PlaceReviewQuery { Page = 0, Rating = 6 });

        Assert.Equal(PlaceReviewListResultStatus.InvalidQuery, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(["Page", "Rating"], result.ValidationErrors!.Keys.Order());
        Assert.Empty(repository.VisibilityChecks);
        Assert.Empty(repository.PagedCalls);
    }

    [Fact]
    public async Task HiddenPlace_ReturnsPlaceNotFound_WithoutLoadingReviews()
    {
        var repository = new FakeRepository { PlaceVisible = false };

        var result = await Service(repository).GetPlaceReviewsAsync(PlaceId, new PlaceReviewQuery());

        Assert.Equal(PlaceReviewListResultStatus.PlaceNotFound, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(PlaceId, Assert.Single(repository.VisibilityChecks));
        Assert.Empty(repository.PagedCalls);
    }

    [Fact]
    public async Task VisiblePlace_ReturnsThePageOfThatPlace()
    {
        var item = new PublicPlaceReviewResponse(
            Guid.NewGuid(), 5, ["WorthVisiting"], "Tuyệt vời", DateTime.UtcNow, "Nguyễn Văn A");
        var repository = new FakeRepository
        {
            Page = PagedResult<PublicPlaceReviewResponse>.Create([item], 1, 20, 1)
        };
        var query = new PlaceReviewQuery { Rating = 5 };

        var result = await Service(repository).GetPlaceReviewsAsync(PlaceId, query);

        Assert.Equal(PlaceReviewListResultStatus.Success, result.Status);
        Assert.Equal(item, Assert.Single(result.Response!.Items));
        Assert.Equal((PlaceId, query), Assert.Single(repository.PagedCalls));
    }

    private static PlaceReviewQueryService Service(FakeRepository repository) =>
        new(repository, new PlaceReviewQueryValidator());

    private sealed class FakeRepository : IPlaceReviewRepository
    {
        public bool PlaceVisible { get; init; } = true;

        public PagedResult<PublicPlaceReviewResponse> Page { get; init; } =
            PagedResult<PublicPlaceReviewResponse>.Create([], 1, 20, 0);

        public List<Guid> VisibilityChecks { get; } = [];

        public List<(Guid PlaceId, PlaceReviewQuery Query)> PagedCalls { get; } = [];

        public Task<bool> IsPlaceVisibleAsync(Guid placeId, CancellationToken cancellationToken = default)
        {
            VisibilityChecks.Add(placeId);
            return Task.FromResult(PlaceVisible);
        }

        public Task<PagedResult<PublicPlaceReviewResponse>> GetPagedByPlaceAsync(
            Guid placeId,
            PlaceReviewQuery query,
            CancellationToken cancellationToken = default)
        {
            PagedCalls.Add((placeId, query));
            return Task.FromResult(Page);
        }

        public Task<PlaceReviewSummary> GetSummaryAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OwnedItemForReviewReadModel?> GetOwnedItemAsync(
            Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PlaceReview?> GetAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(PlaceReview review, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteAsync(Guid userId, Guid itemId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

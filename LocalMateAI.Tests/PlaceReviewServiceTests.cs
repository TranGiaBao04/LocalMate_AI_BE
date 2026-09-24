using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class PlaceReviewServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();

    [Fact]
    public async Task Create_EmptyItemId_ReturnsInvalidItemWithoutLookup()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, Guid.Empty, Valid());

        Assert.Equal(CreatePlaceReviewResultStatus.InvalidItemId, result.Status);
        Assert.Equal(0, repository.ItemReads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Create_RatingOutsideOneToFive_ReturnsValidationFailed(int rating)
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid() with { Rating = rating });

        Assert.Equal(CreatePlaceReviewResultStatus.ValidationFailed, result.Status);
        Assert.Contains("Rating", result.ValidationErrors!.Keys);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Create_UnknownQuickTag_ReturnsValidationFailed()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid() with { QuickTags = ["WorthVisiting", "NotATag"] });

        Assert.Equal(CreatePlaceReviewResultStatus.ValidationFailed, result.Status);
        Assert.Contains("QuickTags", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Create_MoreThanThreeQuickTags_ReturnsValidationFailed()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid() with
        {
            QuickTags = [ReviewQuickTags.WorthVisiting, ReviewQuickTags.NearMetro, ReviewQuickTags.GoodValue, ReviewQuickTags.TooCrowded]
        });

        Assert.Equal(CreatePlaceReviewResultStatus.ValidationFailed, result.Status);
        Assert.Contains("QuickTags", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Create_NullQuickTagElement_ReturnsValidationFailed()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid() with { QuickTags = [null!] });

        Assert.Equal(CreatePlaceReviewResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Create_CommentOverOneThousandCharacters_ReturnsValidationFailed()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid() with { Comment = new string('x', 1001) });

        Assert.Equal(CreatePlaceReviewResultStatus.ValidationFailed, result.Status);
        Assert.Contains("Comment", result.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Create_CommentOfExactlyOneThousandCharacters_IsAccepted()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid() with { Comment = new string('x', 1000) });

        Assert.Equal(CreatePlaceReviewResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task Create_NonPersistedUser_ReturnsUserNotFoundBeforeItemLookup()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(), repository);

        var result = await service.CreateAsync(Guid.NewGuid(), ItemId, Valid());

        Assert.Equal(CreatePlaceReviewResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, repository.ItemReads);
    }

    [Fact]
    public async Task Create_MissingOrForeignItem_ReturnsItemNotFound()
    {
        var repository = new FakeReviewRepository(item: null);
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid());

        Assert.Equal(CreatePlaceReviewResultStatus.ItemNotFound, result.Status);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Create_ItemNotVisited_ReturnsItemNotVisitedWithoutWrite()
    {
        var repository = new FakeReviewRepository(new OwnedItemForReviewReadModel(ItemId, PlaceId, IsVisited: false));
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid());

        Assert.Equal(CreatePlaceReviewResultStatus.ItemNotVisited, result.Status);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Create_ExistingReview_ReturnsAlreadyExistsWithoutWrite()
    {
        var repository = new FakeReviewRepository(VisitedItem(), existing: new PlaceReview { UserId = UserId, ItineraryItemId = ItemId });
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid());

        Assert.Equal(CreatePlaceReviewResultStatus.AlreadyExists, result.Status);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Create_RaceOnUniqueIndex_ReturnsAlreadyExists()
    {
        var repository = new FakeReviewRepository(VisitedItem(), tryAddResult: false);
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, Valid());

        Assert.Equal(CreatePlaceReviewResultStatus.AlreadyExists, result.Status);
    }

    [Fact]
    public async Task Create_ValidRequest_SavesReviewWithPlaceFromItemAndNormalizedValues()
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, new CreatePlaceReviewRequest(
            4,
            [ReviewQuickTags.GoodValue, ReviewQuickTags.GoodValue, ReviewQuickTags.NearMetro],
            "  Quán ngon  "));

        Assert.Equal(CreatePlaceReviewResultStatus.Success, result.Status);
        var saved = Assert.Single(repository.Added);
        Assert.Equal(UserId, saved.UserId);
        Assert.Equal(PlaceId, saved.PlaceId);
        Assert.Equal(ItemId, saved.ItineraryItemId);
        Assert.Equal(4, saved.Rating);
        Assert.Equal([ReviewQuickTags.GoodValue, ReviewQuickTags.NearMetro], saved.QuickTags);
        Assert.Equal("Quán ngon", saved.Comment);
        Assert.Equal(PlaceId, result.Response!.PlaceId);
        Assert.Equal(4, result.Response.Rating);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankCommentAndNoTags_StoresNullCommentAndEmptyTags(string? comment)
    {
        var repository = new FakeReviewRepository(VisitedItem());
        var service = new PlaceReviewService(new FakeUserRepository(UserId), repository);

        var result = await service.CreateAsync(UserId, ItemId, new CreatePlaceReviewRequest(5, null, comment));

        Assert.Equal(CreatePlaceReviewResultStatus.Success, result.Status);
        var saved = Assert.Single(repository.Added);
        Assert.Null(saved.Comment);
        Assert.Empty(saved.QuickTags);
    }

    [Fact]
    public async Task Get_EmptyItemId_ReturnsInvalidItem()
    {
        var service = new PlaceReviewService(new FakeUserRepository(UserId), new FakeReviewRepository(VisitedItem()));

        var result = await service.GetAsync(UserId, Guid.Empty);

        Assert.Equal(GetPlaceReviewResultStatus.InvalidItemId, result.Status);
    }

    [Fact]
    public async Task Get_NonPersistedUser_ReturnsUserNotFound()
    {
        var service = new PlaceReviewService(new FakeUserRepository(), new FakeReviewRepository(VisitedItem()));

        var result = await service.GetAsync(Guid.NewGuid(), ItemId);

        Assert.Equal(GetPlaceReviewResultStatus.UserNotFound, result.Status);
    }

    [Fact]
    public async Task Get_MissingOrForeignItem_ReturnsItemNotFound()
    {
        var service = new PlaceReviewService(new FakeUserRepository(UserId), new FakeReviewRepository(item: null));

        var result = await service.GetAsync(UserId, ItemId);

        Assert.Equal(GetPlaceReviewResultStatus.ItemNotFound, result.Status);
    }

    [Fact]
    public async Task Get_ItemWithoutReview_ReturnsReviewNotFound()
    {
        var service = new PlaceReviewService(new FakeUserRepository(UserId), new FakeReviewRepository(VisitedItem()));

        var result = await service.GetAsync(UserId, ItemId);

        Assert.Equal(GetPlaceReviewResultStatus.ReviewNotFound, result.Status);
    }

    [Fact]
    public async Task Get_ExistingReview_ReturnsIt()
    {
        var existing = new PlaceReview
        {
            UserId = UserId,
            PlaceId = PlaceId,
            ItineraryItemId = ItemId,
            Rating = 3,
            QuickTags = [ReviewQuickTags.TooCrowded],
            Comment = "Đông"
        };
        var service = new PlaceReviewService(new FakeUserRepository(UserId), new FakeReviewRepository(VisitedItem(), existing));

        var result = await service.GetAsync(UserId, ItemId);

        Assert.Equal(GetPlaceReviewResultStatus.Success, result.Status);
        Assert.Equal(3, result.Response!.Rating);
        Assert.Equal([ReviewQuickTags.TooCrowded], result.Response.QuickTags);
        Assert.Equal("Đông", result.Response.Comment);
    }

    private static CreatePlaceReviewRequest Valid() =>
        new(5, [ReviewQuickTags.WorthVisiting], "Tuyệt vời");

    private static OwnedItemForReviewReadModel VisitedItem() =>
        new(ItemId, PlaceId, IsVisited: true);

    private sealed class FakeReviewRepository(
        OwnedItemForReviewReadModel? item,
        PlaceReview? existing = null,
        bool tryAddResult = true) : IPlaceReviewRepository
    {
        public int ItemReads { get; private set; }

        public List<PlaceReview> Added { get; } = [];

        public Task<OwnedItemForReviewReadModel?> GetOwnedItemAsync(
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ItemReads++;
            return Task.FromResult(item);
        }

        public Task<PlaceReview?> GetAsync(
            Guid userId,
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(existing);

        public Task<bool> ExistsAsync(
            Guid userId,
            Guid itemId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(existing is not null);

        public Task<bool> TryAddAsync(
            PlaceReview review,
            CancellationToken cancellationToken = default)
        {
            if (tryAddResult)
            {
                Added.Add(review);
            }

            return Task.FromResult(tryAddResult);
        }
    }

    private sealed class FakeUserRepository(Guid? userId = null) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == requestedUserId ? new User { Id = requestedUserId } : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

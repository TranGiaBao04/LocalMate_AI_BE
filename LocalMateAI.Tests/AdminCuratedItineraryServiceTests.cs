using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminCuratedItineraryServiceTests
{
    private const double Lat = 10.77;
    private const double Lng = 106.70;

    private readonly FakeRepository repository = new();
    private readonly FakeSystemSettingProvider settings = new();

    [Fact]
    public async Task Create_SavesNormalizedContent_SumsCosts_AndMatchesTheApplyDuration()
    {
        var cafe = AddPlace(PlaceCategory.Cafe, costMin: 10000, costMax: 30000);
        var museum = AddPlace(PlaceCategory.Culture, costMin: 0, costMax: 50000, longitude: 106.71);

        var result = await Service().CreateAsync(new SaveCuratedItineraryRequest(
            "  Nửa ngày   quận 1 ", "   ", " https://img.test/a.jpg ", [museum.Id, cafe.Id]));

        Assert.Equal(AdminCuratedItineraryResultStatus.Success, result.Status);
        Assert.Equal(repository.NewId, result.Response!.Id);

        var saved = repository.Saved!;
        Assert.Equal("Nửa ngày quận 1", saved.Title);
        Assert.Null(saved.Description);
        Assert.Equal("https://img.test/a.jpg", saved.CoverImageUrl);
        Assert.Equal([museum.Id, cafe.Id], saved.PlaceIds);
        Assert.Equal(10000m, saved.EstimatedCostMin);
        Assert.Equal(80000m, saved.EstimatedCostMax);

        // Đúng con số mà luồng áp dụng lịch mẫu sẽ tính cho cùng các chặng, cùng thứ tự.
        var applySource = new CuratedItineraryForApplyReadModel(Guid.Empty, string.Empty, 0, 0, 0,
        [
            new CuratedPlaceForApplyReadModel(museum.Id, 0, Lat, 106.71, 50000, PlaceCategory.Culture),
            new CuratedPlaceForApplyReadModel(cafe.Id, 1, Lat, Lng, 30000, PlaceCategory.Cafe)
        ]);
        Assert.Equal(
            CuratedTripBuilder.TotalMinutes(applySource, TravelMode.Auto, null),
            saved.EstimatedDurationMinutes);
        // 90 phút bảo tàng + 60 phút cà phê + đoạn đi giữa hai nơi.
        Assert.True(saved.EstimatedDurationMinutes > 150);
    }

    [Fact]
    public async Task Create_ReadsVisitMinutesFromSettings()
    {
        var first = AddPlace(PlaceCategory.Cafe);
        var second = AddPlace(PlaceCategory.Cafe);
        var request = Request([first.Id, second.Id]);

        await Service().CreateAsync(request);
        var withDefaults = repository.Saved!.EstimatedDurationMinutes;

        settings.Set(SystemSettingKeys.CafeVisitMinutes, 120);
        await Service().CreateAsync(request);

        // Hai quán cà phê, mỗi quán thêm 60 phút.
        Assert.Equal(withDefaults + 120, repository.Saved!.EstimatedDurationMinutes);
    }

    [Theory]
    [InlineData(null, 0, 0, "Title")]
    [InlineData("   ", 0, 0, "Title")]
    [InlineData("201", 0, 0, "Title")]
    [InlineData("Lịch mẫu", 1001, 0, "Description")]
    [InlineData("Lịch mẫu", 0, 501, "CoverImageUrl")]
    public async Task InvalidTextFields_FailOnThatField(
        string? title, int descriptionLength, int coverLength, string expectedField)
    {
        var first = AddPlace();
        var second = AddPlace();
        var request = new SaveCuratedItineraryRequest(
            title == "201" ? new string('a', 201) : title,
            descriptionLength == 0 ? null : new string('a', descriptionLength),
            coverLength == 0 ? null : new string('a', coverLength),
            [first.Id, second.Id]);

        var result = await Service().CreateAsync(request);

        Assert.Equal(AdminCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Equal([expectedField], result.ValidationErrors!.Keys);
        Assert.Null(repository.Saved);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("one")]
    [InlineData("eleven")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    public async Task MalformedPlaceIds_FailWithoutReadingPlaces(string shape)
    {
        var known = Enumerable.Range(0, 11).Select(_ => AddPlace().Id).ToList();
        IReadOnlyList<Guid>? placeIds = shape switch
        {
            "null" => null,
            "one" => [known[0]],
            "eleven" => known,
            "duplicate" => [known[0], known[1], known[0]],
            "empty" => [known[0], Guid.Empty],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var result = await Service().CreateAsync(Request(placeIds));

        Assert.Equal(AdminCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Equal(["PlaceIds"], result.ValidationErrors!.Keys);
        Assert.Equal(0, repository.PlaceRequests);
        Assert.Null(repository.Saved);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("deleted")]
    [InlineData("pending")]
    public async Task UnusablePlaces_FailOnPlaceIds_WithoutShowingIds(string kind)
    {
        var usable = AddPlace();
        var other = kind switch
        {
            "unknown" => Guid.NewGuid(),
            "deleted" => AddPlace(status: PlaceStatus.Inactive, isDeleted: true, name: "Quán đã xoá").Id,
            "pending" => AddPlace(status: PlaceStatus.Pending, name: "Quán chờ duyệt").Id,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var result = await Service().CreateAsync(Request([usable.Id, other]));

        Assert.Equal(AdminCuratedItineraryResultStatus.ValidationFailed, result.Status);
        // Địa điểm đã xoá (cũng mang Status Inactive) chỉ được báo một lần, là "đã bị xoá".
        var message = Assert.Single(result.ValidationErrors!["PlaceIds"]);
        var expected = kind switch
        {
            "unknown" => "Có 1 địa điểm không còn tồn tại",
            "deleted" => "Địa điểm đã bị xoá: Quán đã xoá.",
            _ => "Địa điểm chưa ở trạng thái Active: Quán chờ duyệt."
        };
        Assert.Contains(expected, message);
        // Câu lỗi hiển thị thẳng cho admin nên không được chứa id.
        Assert.DoesNotContain(other.ToString(), message);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task EveryKindOfUnusablePlace_IsReportedInOneResponse()
    {
        var deleted = AddPlace(status: PlaceStatus.Inactive, isDeleted: true, name: "Quán đã xoá");
        var pending = AddPlace(status: PlaceStatus.Pending, name: "Quán chờ duyệt");
        var hidden = AddPlace(status: PlaceStatus.Inactive, name: "Quán đang ẩn");

        var result = await Service().CreateAsync(
            Request([Guid.NewGuid(), deleted.Id, pending.Id, Guid.NewGuid(), hidden.Id]));

        Assert.Equal(AdminCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Equal(
            [
                "Có 2 địa điểm không còn tồn tại. Hãy chọn lại.",
                "Địa điểm đã bị xoá: Quán đã xoá.",
                "Địa điểm chưa ở trạng thái Active: Quán chờ duyệt, Quán đang ẩn."
            ],
            result.ValidationErrors!["PlaceIds"].AsEnumerable());
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task Duration_AtLimitIsAccepted_AboveLimitIsRejected()
    {
        var cafe = AddPlace(PlaceCategory.Cafe);
        var museum = AddPlace(PlaceCategory.Culture);
        var request = Request([cafe.Id, museum.Id]);

        await Service().CreateAsync(request);
        var travelMinutes = repository.Saved!.EstimatedDurationMinutes - (60 + 90);

        settings.Set(SystemSettingKeys.CafeVisitMinutes, 400);
        settings.Set(SystemSettingKeys.CultureVisitMinutes, 560 - travelMinutes);
        var atLimit = await Service().CreateAsync(request);
        Assert.Equal(AdminCuratedItineraryResultStatus.Success, atLimit.Status);
        Assert.Equal(AdminCuratedItineraryService.MaxDurationMinutes, repository.Saved!.EstimatedDurationMinutes);

        settings.Set(SystemSettingKeys.CultureVisitMinutes, 561 - travelMinutes);
        var aboveLimit = await Service().CreateAsync(request);
        Assert.Equal(AdminCuratedItineraryResultStatus.ValidationFailed, aboveLimit.Status);
        Assert.Equal(["PlaceIds"], aboveLimit.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task AllFieldErrors_AreReportedTogether()
    {
        var only = AddPlace();

        var result = await Service().CreateAsync(Request([only.Id], title: " "));

        Assert.Equal(AdminCuratedItineraryResultStatus.ValidationFailed, result.Status);
        Assert.Equal(["PlaceIds", "Title"], result.ValidationErrors!.Keys.Order());
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenItineraryIsMissing()
    {
        var first = AddPlace();
        var second = AddPlace();
        repository.Exists = false;

        var result = await Service().UpdateAsync(Guid.NewGuid(), Request([first.Id, second.Id]));

        Assert.Equal(AdminCuratedItineraryResultStatus.NotFound, result.Status);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task Update_Succeeds_ForExistingItinerary()
    {
        var first = AddPlace();
        var second = AddPlace();
        var id = Guid.NewGuid();

        var result = await Service().UpdateAsync(id, Request([second.Id, first.Id], title: "Tên mới"));

        Assert.Equal(AdminCuratedItineraryResultStatus.Success, result.Status);
        Assert.Equal(id, repository.UpdatedId);
        Assert.Equal(id, result.Response!.Id);
        Assert.Equal("Tên mới", repository.Saved!.Title);
        Assert.Equal([second.Id, first.Id], repository.Saved.PlaceIds);
    }

    [Theory]
    [InlineData(true, DeleteAdminCuratedItineraryResultStatus.Success)]
    [InlineData(false, DeleteAdminCuratedItineraryResultStatus.NotFound)]
    public async Task DeleteAsync_MapsRepositoryResult(bool deleted, DeleteAdminCuratedItineraryResultStatus expected)
    {
        repository.Deleted = deleted;

        Assert.Equal(expected, await Service().DeleteAsync(Guid.NewGuid()));
    }

    private AdminCuratedItineraryService Service() => new(repository, settings);

    private static SaveCuratedItineraryRequest Request(IReadOnlyList<Guid>? placeIds, string? title = "Lịch mẫu") =>
        new(title, null, null, placeIds);

    private CuratedPlaceCandidate AddPlace(
        PlaceCategory category = PlaceCategory.Cafe,
        PlaceStatus status = PlaceStatus.Active,
        bool isDeleted = false,
        decimal costMin = 0,
        decimal costMax = 0,
        double longitude = Lng,
        string name = "Quán")
    {
        var place = new CuratedPlaceCandidate(
            Guid.NewGuid(), name, status, isDeleted, category, Lat, longitude, costMin, costMax);
        repository.Places.Add(place);
        return place;
    }

    private sealed class FakeRepository : IAdminCuratedItineraryRepository
    {
        public List<CuratedPlaceCandidate> Places { get; } = [];
        public int PlaceRequests { get; private set; }
        public CuratedItineraryDraft? Saved { get; private set; }
        public Guid? UpdatedId { get; private set; }
        public Guid NewId { get; } = Guid.NewGuid();
        public bool Exists { get; set; } = true;
        public bool Deleted { get; set; }

        public Task<PagedResult<AdminCuratedItineraryResponse>> GetPagedAsync(
            AdminCuratedItineraryQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminCuratedItineraryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Exists && Saved is not null
                ? new AdminCuratedItineraryResponse(
                    id, Saved.Title, Saved.Description, Saved.CoverImageUrl, Saved.EstimatedDurationMinutes,
                    Saved.EstimatedCostMin, Saved.EstimatedCostMax, null, 0, [], DateTime.UtcNow, DateTime.UtcNow)
                : null);

        public Task<IReadOnlyList<CuratedPlaceCandidate>> GetPlacesAsync(
            IReadOnlyCollection<Guid> placeIds, CancellationToken cancellationToken = default)
        {
            PlaceRequests++;
            return Task.FromResult<IReadOnlyList<CuratedPlaceCandidate>>(
                Places.Where(place => placeIds.Contains(place.Id)).ToList());
        }

        public Task<Guid> AddAsync(CuratedItineraryDraft draft, CancellationToken cancellationToken = default)
        {
            Saved = draft;
            return Task.FromResult(NewId);
        }

        public Task<bool> UpdateAsync(
            Guid id, CuratedItineraryDraft draft, CancellationToken cancellationToken = default)
        {
            if (!Exists)
            {
                return Task.FromResult(false);
            }

            Saved = draft;
            UpdatedId = id;
            return Task.FromResult(true);
        }

        public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Deleted);
    }
}

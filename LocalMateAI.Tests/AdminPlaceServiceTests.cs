using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;
using System.Text.Json;

namespace LocalMateAI.Tests;

public sealed class AdminPlaceServiceTests
{
    [Theory]
    [InlineData("{\"status\":\"Active\"}", PlaceStatus.Active)]
    [InlineData("{\"status\":\"inactive\"}", PlaceStatus.Inactive)]
    public void UpdatePlaceStatusRequest_ValidStatus_DeserializesString(
        string json,
        PlaceStatus expected)
    {
        var request = JsonSerializer.Deserialize<UpdatePlaceStatusRequest>(json);

        Assert.NotNull(request);
        Assert.Equal(expected, request.Status);
    }

    [Theory]
    [InlineData("{\"status\":\"NotAStatus\"}")]
    [InlineData("{}")]
    public void UpdatePlaceStatusRequest_InvalidStatus_DeserializesSentinel(string json)
    {
        var request = JsonSerializer.Deserialize<UpdatePlaceStatusRequest>(json);

        Assert.NotNull(request);
        Assert.False(Enum.IsDefined(request.Status));
    }

    [Fact]
    public async Task Create_ValidRequest_CreatesPendingPlaceWithCorrectCoordinates()
    {
        var repository = new FakePlaceRepository();
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.CreateAsync(ValidCreateRequest());

        Assert.Equal(AdminPlaceOperationResultStatus.Success, result.Status);
        Assert.NotNull(repository.AddedPlace);
        Assert.Equal(PlaceStatus.Pending, repository.AddedPlace.Status);
        Assert.Equal(106.70, repository.AddedPlace.Location.X, 6);
        Assert.Equal(10.78, repository.AddedPlace.Location.Y, 6);
        Assert.Equal(4326, repository.AddedPlace.Location.SRID);
        Assert.Equal(nameof(PlaceStatus.Pending), result.Response!.Status);
        Assert.False(result.Response.IsVerified);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Theory]
    [MemberData(nameof(InvalidCreateRequests))]
    public async Task Create_InvalidRequest_ReturnsValidationWithoutPersistence(
        CreateAdminPlaceRequest request)
    {
        var repository = new FakePlaceRepository();
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.CreateAsync(request);

        Assert.Equal(AdminPlaceOperationResultStatus.ValidationFailed, result.Status);
        Assert.NotEmpty(result.ValidationErrors!);
        Assert.Null(repository.AddedPlace);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task Update_ValidRequest_PreservesStatusIdCreatedAtAndTags()
    {
        var createdAt = DateTime.UtcNow.AddDays(-1);
        var tag = new PlaceTag { PlaceId = Guid.NewGuid(), TagId = Guid.NewGuid() };
        var place = new Place
        {
            Id = tag.PlaceId,
            Name = "Old",
            Address = "Old address",
            Location = new Point(106.68, 10.77) { SRID = 4326 },
            Category = PlaceCategory.Cafe,
            Status = PlaceStatus.Active,
            IsVerified = true,
            EstimatedCostMin = 10,
            EstimatedCostMax = 20,
            CreatedAt = createdAt,
            Tags = [tag]
        };
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());
        var request = new UpdateAdminPlaceRequest(
            "Updated",
            "Description",
            "Updated address",
            10.79,
            106.72,
            PlaceCategory.Culture,
            100,
            200,
            "image.png");

        var result = await service.UpdateAsync(place.Id, request);

        Assert.Equal(AdminPlaceOperationResultStatus.Success, result.Status);
        Assert.Equal(PlaceStatus.Active, place.Status);
        Assert.True(place.IsVerified);
        Assert.Equal(createdAt, place.CreatedAt);
        Assert.Same(tag, Assert.Single(place.Tags));
        Assert.Equal(106.72, place.Location.X, 6);
        Assert.Equal(10.79, place.Location.Y, 6);
        Assert.Equal(4326, place.Location.SRID);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Theory]
    [InlineData(PlaceStatus.Pending, PlaceStatus.Active)]
    [InlineData(PlaceStatus.Active, PlaceStatus.Inactive)]
    [InlineData(PlaceStatus.Inactive, PlaceStatus.Active)]
    public async Task UpdateStatus_AllowedTransition_UpdatesStatusOnly(
        PlaceStatus current,
        PlaceStatus requested)
    {
        var place = ExistingPlace(current, isVerified: true);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateStatusAsync(
            place.Id,
            new UpdatePlaceStatusRequest(requested));

        Assert.Equal(AdminPlaceModerationResultStatus.Success, result.Status);
        Assert.Equal(requested, place.Status);
        Assert.True(place.IsVerified);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Theory]
    [InlineData(PlaceStatus.Pending, PlaceStatus.Inactive)]
    [InlineData(PlaceStatus.Active, PlaceStatus.Pending)]
    [InlineData(PlaceStatus.Inactive, PlaceStatus.Pending)]
    public async Task UpdateStatus_RejectedTransition_ReturnsConflictWithoutWrite(
        PlaceStatus current,
        PlaceStatus requested)
    {
        var place = ExistingPlace(current);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateStatusAsync(
            place.Id,
            new UpdatePlaceStatusRequest(requested));

        Assert.Equal(AdminPlaceModerationResultStatus.InvalidStatusTransition, result.Status);
        Assert.Equal(current, place.Status);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateStatus_SameStatus_ReturnsSuccessWithoutWrite()
    {
        var place = ExistingPlace(PlaceStatus.Active, isVerified: true);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateStatusAsync(
            place.Id,
            new UpdatePlaceStatusRequest(PlaceStatus.Active));

        Assert.Equal(AdminPlaceModerationResultStatus.Success, result.Status);
        Assert.True(result.Response!.IsVerified);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateStatus_UndefinedStatus_ReturnsInvalidWithoutWrite()
    {
        var place = ExistingPlace(PlaceStatus.Active);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateStatusAsync(
            place.Id,
            new UpdatePlaceStatusRequest((PlaceStatus)999));

        Assert.Equal(AdminPlaceModerationResultStatus.InvalidStatus, result.Status);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateStatus_MissingPlace_ReturnsNotFoundWithoutWrite()
    {
        var repository = new FakePlaceRepository();
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateStatusAsync(
            Guid.NewGuid(),
            new UpdatePlaceStatusRequest(PlaceStatus.Active));

        Assert.Equal(AdminPlaceModerationResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateVerification_ActivePlace_CanVerifyAndUnverify()
    {
        var place = ExistingPlace(PlaceStatus.Active);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var verified = await service.UpdateVerificationAsync(
            place.Id,
            new UpdatePlaceVerificationRequest(true));
        var unverified = await service.UpdateVerificationAsync(
            place.Id,
            new UpdatePlaceVerificationRequest(false));

        Assert.Equal(AdminPlaceModerationResultStatus.Success, verified.Status);
        Assert.Equal(AdminPlaceModerationResultStatus.Success, unverified.Status);
        Assert.False(place.IsVerified);
        Assert.Equal(2, repository.SaveChangesCalls);
    }

    [Theory]
    [InlineData(PlaceStatus.Pending)]
    [InlineData(PlaceStatus.Inactive)]
    public async Task UpdateVerification_NonActivePlace_CannotEnableVerification(PlaceStatus status)
    {
        var place = ExistingPlace(status);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateVerificationAsync(
            place.Id,
            new UpdatePlaceVerificationRequest(true));

        Assert.Equal(AdminPlaceModerationResultStatus.VerificationRequiresActive, result.Status);
        Assert.False(place.IsVerified);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateVerification_InactiveVerifiedPlace_CanUnverify()
    {
        var place = ExistingPlace(PlaceStatus.Inactive, isVerified: true);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateVerificationAsync(
            place.Id,
            new UpdatePlaceVerificationRequest(false));

        Assert.Equal(AdminPlaceModerationResultStatus.Success, result.Status);
        Assert.False(place.IsVerified);
        Assert.Equal(1, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateVerification_SameValue_ReturnsSuccessWithoutWrite()
    {
        var place = ExistingPlace(PlaceStatus.Inactive, isVerified: true);
        var repository = new FakePlaceRepository(place);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateVerificationAsync(
            place.Id,
            new UpdatePlaceVerificationRequest(true));

        Assert.Equal(AdminPlaceModerationResultStatus.Success, result.Status);
        Assert.True(result.Response!.IsVerified);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task UpdateVerification_MissingPlace_ReturnsNotFoundWithoutWrite()
    {
        var repository = new FakePlaceRepository();
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateVerificationAsync(
            Guid.NewGuid(),
            new UpdatePlaceVerificationRequest(true));

        Assert.Equal(AdminPlaceModerationResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Fact]
    public async Task Update_MissingPlace_ReturnsNotFoundWithoutWrite()
    {
        var repository = new FakePlaceRepository();
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.UpdateAsync(
            Guid.NewGuid(),
            new UpdateAdminPlaceRequest(
                "Place",
                null,
                "Address",
                10.78,
                106.70,
                PlaceCategory.Food,
                0,
                100,
                null));

        Assert.Equal(AdminPlaceOperationResultStatus.NotFound, result.Status);
        Assert.Equal(0, repository.SaveChangesCalls);
    }

    [Theory]
    [InlineData(DeletePlacePersistenceResult.Deleted, DeleteAdminPlaceResultStatus.Success)]
    [InlineData(DeletePlacePersistenceResult.NotFound, DeleteAdminPlaceResultStatus.NotFound)]
    [InlineData(DeletePlacePersistenceResult.InUse, DeleteAdminPlaceResultStatus.InUse)]
    public async Task Delete_MapsPersistenceOutcome(
        DeletePlacePersistenceResult persistenceResult,
        DeleteAdminPlaceResultStatus expectedStatus)
    {
        var repository = new FakePlaceRepository(deleteResult: persistenceResult);
        var service = new AdminPlaceService(repository, new CoordinatesValidationService());

        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.Equal(expectedStatus, result.Status);
    }

    public static TheoryData<CreateAdminPlaceRequest> InvalidCreateRequests =>
        new()
        {
            ValidCreateRequest() with { Name = " " },
            ValidCreateRequest() with { Address = new string('x', 301) },
            ValidCreateRequest() with { Latitude = double.NaN },
            ValidCreateRequest() with { Longitude = double.PositiveInfinity },
            ValidCreateRequest() with { Latitude = 9 },
            ValidCreateRequest() with { Category = (PlaceCategory)999 },
            ValidCreateRequest() with { EstimatedCostMin = -1 },
            ValidCreateRequest() with { EstimatedCostMax = 1_000_000_000_000m },
            ValidCreateRequest() with { EstimatedCostMin = 10.5m },
            ValidCreateRequest() with { EstimatedCostMin = 200, EstimatedCostMax = 100 }
        };

    private static CreateAdminPlaceRequest ValidCreateRequest() =>
        new(
            "Test place",
            "Description",
            "Test address",
            10.78,
            106.70,
            PlaceCategory.Food,
            0,
            100,
            null);

    private static Place ExistingPlace(PlaceStatus status, bool isVerified = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Existing place",
            Address = "Existing address",
            Location = new Point(106.70, 10.78) { SRID = 4326 },
            Category = PlaceCategory.Food,
            Status = status,
            IsVerified = isVerified,
            EstimatedCostMin = 0,
            EstimatedCostMax = 100
        };

    private sealed class FakePlaceRepository(
        Place? place = null,
        DeletePlacePersistenceResult deleteResult = DeletePlacePersistenceResult.Deleted) : IPlaceRepository
    {
        public Place? AddedPlace { get; private set; }

        public int SaveChangesCalls { get; private set; }

        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminPlaceResponse>>([]);

        public Task<AdminPlaceResponse?> GetAdminByIdAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AdminPlaceResponse?>(null);

        public Task<Place?> GetByIdAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(place?.Id == placeId ? place : null);

        public Task AddAsync(
            Place newPlace,
            CancellationToken cancellationToken = default)
        {
            AddedPlace = newPlace;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            var now = DateTime.UtcNow;
            if (AddedPlace is not null)
            {
                AddedPlace.CreatedAt = now;
                AddedPlace.UpdatedAt = now;
            }

            return Task.CompletedTask;
        }

        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(deleteResult);

        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(
            double radiusMeters,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Point?> GetLocationAsync(
            Guid placeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(
            double latitude,
            double longitude,
            double radiusMeters,
            PlaceCategory? category,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
            IReadOnlyList<Guid> placeIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

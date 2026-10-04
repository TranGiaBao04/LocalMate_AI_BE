using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Services;
using LocalMateAI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class PlaceImportEngineTests
{
    private static (PlaceImportEngineService Engine, IMemoryCache Cache, ImportEngineTestPlaceRepo PlaceRepo) CreateEngine()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var stationRepo = new ImportEngineTestStationRepo();
        var placeRepo = new ImportEngineTestPlaceRepo();

        var parser = new PlaceImportParser();
        var categoryValidator = new CategoryValidationService();
        var coordService = new CoordinatesValidationService();
        var coordNormalizer = new CoordinateNormalizer(coordService);
        var priceNormalizer = new PriceNormalizer();
        var stationMapper = new StationMapperService(stationRepo);

        var engine = new PlaceImportEngineService(
            parser,
            categoryValidator,
            coordNormalizer,
            priceNormalizer,
            stationMapper,
            placeRepo,
            cache);

        return (engine, cache, placeRepo);
    }

    [Fact]
    public async Task PreviewAsync_ValidFile_ReturnsPreviewWithImportIdAndStoresInCache()
    {
        var (engine, cache, _) = CreateEngine();
        var templateService = new PlaceImportService();
        var template = await templateService.GetImportTemplateAsync("xlsx");

        await using var stream = new MemoryStream(template.Content);
        var preview = await engine.PreviewAsync(stream, template.FileName);

        Assert.NotNull(preview);
        Assert.StartsWith("imp_", preview.ImportId);
        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(3, preview.ValidRowsCount);
        Assert.Equal(0, preview.ErrorRowsCount);

        // Verify cached
        var cached = cache.Get<PlaceImportPreviewResponse>("import_session_" + preview.ImportId);
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task CommitAsync_ValidPreview_CommitsEntitiesAndEvictsCache()
    {
        var (engine, cache, placeRepo) = CreateEngine();
        var templateService = new PlaceImportService();
        var template = await templateService.GetImportTemplateAsync("xlsx");

        await using var stream = new MemoryStream(template.Content);
        var preview = await engine.PreviewAsync(stream, template.FileName);

        var commitRes = await engine.CommitAsync(new CommitPlaceImportRequest(preview.ImportId));

        Assert.Equal(PlaceImportCommitResultStatus.Success, commitRes.Status);
        Assert.NotNull(commitRes.Response);
        Assert.Equal(3, commitRes.Response.CommittedCount);

        // Verify entities added to repository
        Assert.Equal(3, placeRepo.AddedPlaces.Count);

        // Verify cache session evicted
        Assert.Null(cache.Get<PlaceImportPreviewResponse>("import_session_" + preview.ImportId));
    }

    [Fact]
    public async Task CommitAsync_IdempotencyGuard_PreventsDoubleSubmit()
    {
        var (engine, cache, _) = CreateEngine();
        var templateService = new PlaceImportService();
        var template = await templateService.GetImportTemplateAsync("xlsx");

        await using var stream = new MemoryStream(template.Content);
        var preview = await engine.PreviewAsync(stream, template.FileName);

        // First commit
        var res1 = await engine.CommitAsync(new CommitPlaceImportRequest(preview.ImportId));
        Assert.Equal(PlaceImportCommitResultStatus.Success, res1.Status);

        // Second commit with same ImportId
        var res2 = await engine.CommitAsync(new CommitPlaceImportRequest(preview.ImportId));
        Assert.Equal(PlaceImportCommitResultStatus.SessionNotFoundOrExpired, res2.Status);
    }

    [Fact]
    public async Task ExportErrorReportCsv_NoErrors_ReturnsNull()
    {
        var (engine, _, _) = CreateEngine();
        var templateService = new PlaceImportService();
        var template = await templateService.GetImportTemplateAsync("xlsx");

        await using var stream = new MemoryStream(template.Content);
        var preview = await engine.PreviewAsync(stream, template.FileName);

        var csvBytes = engine.ExportErrorReportCsv(preview.ImportId);
        Assert.Null(csvBytes);
    }

    private sealed class ImportEngineTestStationRepo : Application.Interfaces.Repositories.IMetroStationRepository
    {
        public Task<Application.DTOs.Geo.NearestStationResult?> FindNearestAsync(double latitude, double longitude, CancellationToken cancellationToken = default) => Task.FromResult<Application.DTOs.Geo.NearestStationResult?>(null);
        public Task<Application.DTOs.Geo.NearestStationResult?> GetDistanceToStationAsync(Guid stationId, double latitude, double longitude, CancellationToken cancellationToken = default) => Task.FromResult<Application.DTOs.Geo.NearestStationResult?>(null);
        public Task<IReadOnlyList<Application.DTOs.MasterData.MetroStationSummaryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Application.DTOs.MasterData.MetroStationSummaryResponse> list = [
                new Application.DTOs.MasterData.MetroStationSummaryResponse(Guid.NewGuid(), "Ba Son", 1, 10.78, 106.70)
            ];
            return Task.FromResult(list);
        }
    }

    private sealed class ImportEngineTestPlaceRepo : Application.Interfaces.Repositories.IPlaceRepository
    {
        public List<Domain.Entities.Place> AddedPlaces { get; } = [];
        public Task AddAsync(Domain.Entities.Place place, CancellationToken cancellationToken = default)
        {
            AddedPlaces.Add(place);
            return Task.CompletedTask;
        }
        public Task AddTagAsync(Guid placeId, Guid tagId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult(DeletePlacePersistenceResult.Deleted);
        public Task<IReadOnlyList<DuplicatePlaceCandidate>> FindNearbyPlacesAsync(double latitude, double longitude, double radiusMeters, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DuplicatePlaceCandidate>>([]);
        public Task<PlaceReadModel?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PlaceReadModel?>(null);
        public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(double latitude, double longitude, double radiusMeters, Domain.Enums.PlaceCategory? category, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaceSummaryResponse>>([]);
        public Task<AdminPlaceResponse?> GetAdminByIdAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<AdminPlaceResponse?>(null);
        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminPlaceResponse>>([]);
        public Task<Domain.Entities.Place?> GetByIdAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.Place?>(null);
        public Task<NetTopologySuite.Geometries.Point?> GetLocationAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<NetTopologySuite.Geometries.Point?>(null);
        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(double radiusMeters, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MetroClusterPlaceReadModel>>([]);
        public Task<Application.DTOs.Common.PagedResult<AdminPlaceResponse>> GetPagedForAdminAsync(AdminPlaceQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new Application.DTOs.Common.PagedResult<AdminPlaceResponse>([], 1, 10, 0, 0));
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(IReadOnlyList<Guid> placeIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(new Dictionary<Guid, IReadOnlyList<Guid>>());
        public Task<IReadOnlyList<Guid>> GetTagIdsAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> RemoveTagAsync(Guid placeId, Guid tagId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}

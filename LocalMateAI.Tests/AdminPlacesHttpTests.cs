using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LocalMateAI.API.Authorization;
using LocalMateAI.API.Controllers;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class AdminPlacesHttpTests
{
    private static readonly byte[] ValidPngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
    ];

    [Fact]
    public async Task UploadImage_ValidPngStream_Returns200WithUrl()
    {
        using var host = new Host();
        host.Authenticate();

        using var content = new MultipartFormDataContent();
        var byteArrayContent = new ByteArrayContent(ValidPngBytes);
        byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(byteArrayContent, "file", "test.png");

        var response = await host.Client.PostAsync("/api/admin/places/upload-image", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImageUploadResult>();
        Assert.NotNull(result);
        Assert.Equal(ImageUploadResultStatus.Success, result.Status);
        Assert.Contains("cloudinary.com", result.Url);
    }

    [Fact]
    public async Task UploadImage_InvalidMimeType_Returns400BadRequest()
    {
        using var host = new Host();
        host.Authenticate();

        using var content = new MultipartFormDataContent();
        var byteArrayContent = new ByteArrayContent("<html>evil script</html>"u8.ToArray());
        byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(byteArrayContent, "file", "evil.html");

        var response = await host.Client.PostAsync("/api/admin/places/upload-image", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_image_type", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ValidateDistance_ValidCoordinates_Returns200WithResult()
    {
        using var host = new Host();
        host.Authenticate();

        var request = new ValidatePlaceDistanceRequest(10.7769, 106.7009);
        var response = await host.Client.PostAsJsonAsync("/api/admin/places/validate-distance", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<PlaceDistanceValidationResult>();
        Assert.NotNull(result);
        Assert.Equal(PlaceDistanceValidationStatus.Success, result.Status);
    }

    [Fact]
    public async Task ValidatePlaceDistance_NonExistentPlace_Returns404NotFound()
    {
        using var host = new Host();
        host.Authenticate();

        var response = await host.Client.GetAsync($"/api/admin/places/{Guid.NewGuid()}/validate-distance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("place_not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetImportTemplate_Returns200WithFile()
    {
        using var host = new Host();
        host.Authenticate();

        var response = await host.Client.GetAsync("/api/admin/places/import-template?format=xlsx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public async Task PreviewImport_Returns200WithPreviewResult()
    {
        using var host = new Host();
        host.Authenticate();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "test.xlsx");

        var response = await host.Client.PostAsync("/api/admin/places/import/preview", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("imp_123456789", json.RootElement.GetProperty("importId").GetString());
    }

    [Fact]
    public async Task CommitImport_Returns200WithCommitResult()
    {
        using var host = new Host();
        host.Authenticate();

        var body = new CommitPlaceImportRequest("imp_123456789", PlaceImportCommitMode.ValidOnly);
        var response = await host.Client.PostAsJsonAsync("/api/admin/places/import/commit", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, json.RootElement.GetProperty("committedCount").GetInt32());
    }

    internal sealed class Host : IDisposable
    {
        private readonly WebApplication app;
        internal HttpClient Client { get; }
        private readonly string tempFolder;

        internal Host()
        {
            tempFolder = Path.Combine(Path.GetTempPath(), "test_uploads_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "ContractTests" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            var stationRepo = new FakeStationRepo();
            var geoService = new FakeGeoService(stationRepo);
            var placeRepo = new FakePlaceRepo();
            var coordService = new CoordinatesValidationService();
            var settings = new TestSystemSettings();
            var tagRepo = new FakeTagRepo();
            var adminPlaceService = new AdminPlaceService(placeRepo, coordService, tagRepo);
            var distanceValidationService = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService, settings);
            var imageStorageService = new FakeCloudinaryImageStorageService();

            builder.Services.AddSingleton<IAdminPlaceService>(adminPlaceService);
            builder.Services.AddSingleton<IPlaceDistanceValidationService>(distanceValidationService);
            builder.Services.AddSingleton<IImageStorageService>(imageStorageService);
            builder.Services.AddSingleton<IPlaceImportService>(new PlaceImportService());
            builder.Services.AddSingleton<IPlaceImportEngineService>(new FakePlaceImportEngineService());
            builder.Services.AddSingleton<ICoordinatesValidationService>(coordService);
            builder.Services.AddSingleton<ISystemSettingProvider>(settings);
            builder.Services.AddSingleton<IUserAccessService>(new TestUserAccessService());
            builder.Services.AddSingleton<FluentValidation.IValidator<ValidatePlaceDistanceRequest>, LocalMateAI.Application.Validators.Places.ValidatePlaceDistanceRequestValidator>();

            builder.Services.AddAuthentication("AdminTest").AddScheme<AuthenticationSchemeOptions, AuthenticationHandler>("AdminTest", _ => { });
            builder.Services.AddLocalMateAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(AdminPlacesController).Assembly);

            app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseMiddleware<AccountAccessMiddleware>();
            app.UseAuthorization();
            app.MapControllers();

            app.StartAsync().GetAwaiter().GetResult();
            Client = app.GetTestClient();
        }

        internal void Authenticate()
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AdminTest");
        }

        public void Dispose()
        {
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().GetAwaiter().GetResult();
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, true); } catch { }
            }
        }
    }

    private sealed class AuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Guid.NewGuid();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(AuthClaimNames.Subject, userId.ToString()),
                new Claim("permission", Permissions.ManagePlaces)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeStationRepo : IMetroStationRepository
    {
        public Task<NearestStationResult?> FindNearestAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<NearestStationResult?>(new NearestStationResult(Guid.NewGuid(), "Bến Thành", 10.77, 106.69, 500));

        public Task<NearestStationResult?> GetDistanceToStationAsync(Guid stationId, double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<NearestStationResult?>(new NearestStationResult(stationId, "Bến Thành", 10.77, 106.69, 500));

        public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MetroStationSummaryResponse>>([]);
    }

    private sealed class FakeGeoService(IMetroStationRepository repo) : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
            => repo.FindNearestAsync(latitude, longitude, cancellationToken);

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(Guid placeId, CancellationToken cancellationToken = default)
            => Task.FromResult<NearestStationResult?>(null);
    }

    private sealed class FakePlaceRepo : IPlaceRepository
    {
        public Task AddAsync(Place place, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult(DeletePlacePersistenceResult.Deleted);
        public Task<PlaceReadModel?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PlaceReadModel?>(null);
        public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(double latitude, double longitude, double radiusMeters, Domain.Enums.PlaceCategory? category, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaceSummaryResponse>>([]);
        public Task<AdminPlaceResponse?> GetAdminByIdAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<AdminPlaceResponse?>(null);
        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminPlaceResponse>>([]);
        public Task<Place?> GetByIdAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<Place?>(null);
        public Task<Point?> GetLocationAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<Point?>(null);
        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(double radiusMeters, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MetroClusterPlaceReadModel>>([]);
        public Task<PagedResult<AdminPlaceResponse>> GetPagedForAdminAsync(AdminPlaceQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<AdminPlaceResponse>([], 1, 10, 0, 0));
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(IReadOnlyList<Guid> placeIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(new Dictionary<Guid, IReadOnlyList<Guid>>());
        public Task<IReadOnlyList<Guid>> GetTagIdsAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task AddTagAsync(Guid placeId, Guid tagId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> RemoveTagAsync(Guid placeId, Guid tagId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<DuplicatePlaceCandidate>> FindNearbyPlacesAsync(double latitude, double longitude, double radiusMeters, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DuplicatePlaceCandidate>>([]);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeTagRepo : ITagRepository
    {
        public Task<IReadOnlyList<Tag>> GetByIdsAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Tag>>([]);
        public Task<IReadOnlyList<Tag>> GetActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Tag>>([]);
    }

    private sealed class TestSystemSettings : ISystemSettingProvider
    {
        public Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(1500);
        public Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(1500m);
        public void Invalidate() { }
    }

    private sealed class TestUserAccessService : IUserAccessService
    {
        public Task<UserAccessSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserAccessSnapshot?>(new UserAccessSnapshot(
                UserStatus.Active,
                "Admin",
                new HashSet<string> { Permissions.ManagePlaces }));
    }

    private sealed class FakeCloudinaryImageStorageService : IImageStorageService
    {
        public async Task<ImageUploadResult> SaveAsync(
            Stream content,
            string fileName,
            string contentType,
            long length,
            CancellationToken cancellationToken = default)
        {
            if (length <= 0 || length > 5 * 1024 * 1024)
            {
                return new ImageUploadResult(ImageUploadResultStatus.TooLarge);
            }

            var normalizedType = contentType.ToLowerInvariant().Trim();
            if (normalizedType is not ("image/jpeg" or "image/png" or "image/webp"))
            {
                return new ImageUploadResult(ImageUploadResultStatus.InvalidContentType);
            }

            var header = new byte[4];
            var read = await content.ReadAsync(header.AsMemory(0, 4), cancellationToken);
            if (read < 3)
            {
                return new ImageUploadResult(ImageUploadResultStatus.InvalidContentType);
            }

            var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            var isPng = read >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
            var isWebp = read >= 4 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46;

            if (!isJpeg && !isPng && !isWebp)
            {
                return new ImageUploadResult(ImageUploadResultStatus.InvalidContentType);
            }

            return new ImageUploadResult(ImageUploadResultStatus.Success, $"https://res.cloudinary.com/localmateai/image/upload/v12345/{Guid.NewGuid():N}.png");
        }
    }

    private sealed class FakePlaceImportEngineService : IPlaceImportEngineService
    {
        public Task<PlaceImportPreviewResponse> PreviewAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlaceImportPreviewResponse("imp_123456789", 3, 3, 0, 0, []));

        public Task<PlaceImportCommitResult> CommitAsync(CommitPlaceImportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlaceImportCommitResult(PlaceImportCommitResultStatus.Success, new PlaceImportCommitResponse(request.ImportId, 3, 0, 0, DateTime.UtcNow)));

        public byte[]? ExportErrorReportCsv(string importId) => null;
    }
}




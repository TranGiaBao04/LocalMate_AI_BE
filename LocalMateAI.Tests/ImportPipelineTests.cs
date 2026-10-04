using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Stations;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Services;
using MiniExcelLibs;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class ImportPipelineTests
{
    [Fact]
    public async Task PlaceImportParser_XlsxStream_ReturnsParsedRows()
    {
        var parser = new PlaceImportParser();
        var templateService = new PlaceImportService();
        var template = await templateService.GetImportTemplateAsync("xlsx");

        await using var stream = new MemoryStream(template.Content);
        var rows = await parser.ParseAsync(stream, "xlsx");

        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows[0].RowNumber);
        Assert.Equal("Phở Phượng Sài Gòn", rows[0].RawName);
        Assert.Equal("Food", rows[0].RawCategory);
        Assert.Equal("Ba Son", rows[0].RawStations);
    }

    [Fact]
    public void PriceNormalizer_FreeOrZero_ReturnsZero()
    {
        var normalizer = new PriceNormalizer();

        var resFree = normalizer.Normalize("free", null);
        Assert.True(resFree.IsValid);
        Assert.Equal(0, resFree.EstimatedCostMin);

        var resMienPhi = normalizer.Normalize("Miễn phí", "0đ");
        Assert.True(resMienPhi.IsValid);
        Assert.Equal(0, resMienPhi.EstimatedCostMin);
        Assert.Equal(0, resMienPhi.EstimatedCostMax);
    }

    [Fact]
    public void PriceNormalizer_ValidRange_ReturnsMinMaxCost()
    {
        var normalizer = new PriceNormalizer();

        var resRange = normalizer.Normalize("30.000 - 50.000 VNĐ", null);
        Assert.True(resRange.IsValid);
        Assert.Equal(30000, resRange.EstimatedCostMin);
        Assert.Equal(50000, resRange.EstimatedCostMax);

        var resK = normalizer.Normalize("30k", "50k");
        Assert.True(resK.IsValid);
        Assert.Equal(30000, resK.EstimatedCostMin);
        Assert.Equal(50000, resK.EstimatedCostMax);
    }

    [Fact]
    public void PriceNormalizer_InvalidFormatOrMinGreaterThanMax_ReturnsError()
    {
        var normalizer = new PriceNormalizer();

        var resInvalid = normalizer.Normalize("nhiều tiền", "50000");
        Assert.False(resInvalid.IsValid);
        Assert.NotNull(resInvalid.ErrorMessage);

        var resMinGreater = normalizer.Normalize("60000", "50000");
        Assert.False(resMinGreater.IsValid);
        Assert.Contains("không được lớn hơn", resMinGreater.ErrorMessage);
    }

    [Fact]
    public void CoordinateNormalizer_MergedString_ParsesAndValidatesHcmc()
    {
        var coordService = new CoordinatesValidationService();
        var normalizer = new CoordinateNormalizer(coordService);

        var resMerged = normalizer.Normalize("10.7769, 106.7009", null, null);
        Assert.True(resMerged.IsValid);
        Assert.Equal(10.7769, resMerged.Latitude);
        Assert.Equal(106.7009, resMerged.Longitude);

        var resDoubleDot = normalizer.Normalize(null, "10..7769", "106,7009");
        Assert.True(resDoubleDot.IsValid);
        Assert.Equal(10.7769, resDoubleDot.Latitude);
        Assert.Equal(106.7009, resDoubleDot.Longitude);
    }

    [Fact]
    public void CoordinateNormalizer_OutOfHcmc_ReturnsError()
    {
        var coordService = new CoordinatesValidationService();
        var normalizer = new CoordinateNormalizer(coordService);

        var resHanoi = normalizer.Normalize("21.0285, 105.8542", null, null);
        Assert.False(resHanoi.IsValid);
        Assert.NotNull(resHanoi.ErrorMessage);
    }

    [Fact]
    public async Task StationMapperService_ValidStations_MapsIdsAndNames()
    {
        var stationRepo = new FakeStationRepoForImport();
        var mapper = new StationMapperService(stationRepo);

        var res = await mapper.MapStationsAsync("Ga Bến Thành, Nhà Hát Thành Phố");
        Assert.True(res.IsValid);
        Assert.Equal(2, res.StationIds.Count);
        Assert.Equal(2, res.MatchedStationNames.Count);
    }

    [Fact]
    public async Task StationMapperService_UnknownStation_ReturnsUnmatchedError()
    {
        var stationRepo = new FakeStationRepoForImport();
        var mapper = new StationMapperService(stationRepo);

        var res = await mapper.MapStationsAsync("Ga Bến Thành, Ga Không Tồn Tại");
        Assert.False(res.IsValid);
        Assert.Single(res.UnmatchedStationNames);
        Assert.Equal("Ga Không Tồn Tại", res.UnmatchedStationNames[0]);
    }

    [Fact]
    public void CategoryValidationService_ValidAndAliases_MapsToEnum()
    {
        var val = new CategoryValidationService();

        Assert.Equal(PlaceCategory.Food, val.Validate("Food").Category);
        Assert.Equal(PlaceCategory.Food, val.Validate("Ăn uống").Category);
        Assert.Equal(PlaceCategory.Cafe, val.Validate("Cà phê").Category);
        Assert.Equal(PlaceCategory.Culture, val.Validate("Văn hóa").Category);
        Assert.Equal(PlaceCategory.CheckIn, val.Validate("Sống ảo").Category);

        var invalidRes = val.Validate("Danh mục không có");
        Assert.False(invalidRes.IsValid);
        Assert.NotNull(invalidRes.ErrorMessage);
    }

    private sealed class FakeStationRepoForImport : IMetroStationRepository
    {
        private static readonly Guid BenThanhId = Guid.NewGuid();
        private static readonly Guid NhaHatId = Guid.NewGuid();

        public Task<NearestStationResult?> FindNearestAsync(double latitude, double longitude, CancellationToken cancellationToken = default) => Task.FromResult<NearestStationResult?>(null);
        public Task<NearestStationResult?> GetDistanceToStationAsync(Guid stationId, double latitude, double longitude, CancellationToken cancellationToken = default) => Task.FromResult<NearestStationResult?>(null);
        public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MetroStationSummaryResponse> list = [
                new MetroStationSummaryResponse(BenThanhId, "Ga Bến Thành", 1, 10.77, 106.69),
                new MetroStationSummaryResponse(NhaHatId, "Ga Nhà Hát Thành Phố", 2, 10.78, 106.70)
            ];
            return Task.FromResult(list);
        }
    }
}

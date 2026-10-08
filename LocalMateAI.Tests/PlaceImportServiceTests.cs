using LocalMateAI.Infrastructure.Services;
using MiniExcelLibs;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class PlaceImportServiceTests
{
    [Fact]
    public async Task GetImportTemplateAsync_DefaultOrXlsxFormat_ReturnsXlsxFileWithSampleRows()
    {
        var service = new PlaceImportService();

        var file = await service.GetImportTemplateAsync("xlsx");

        Assert.NotNull(file);
        Assert.NotNull(file.Content);
        Assert.True(file.Content.Length > 0);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.Equal("Place_Import_Template.xlsx", file.FileName);

        using var stream = new MemoryStream(file.Content);
        var rows = (await stream.QueryAsync<LocalMateAI.Application.DTOs.Places.PlaceImportTemplateRow>()).ToList();
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task GetImportTemplateAsync_CsvFormat_ReturnsCsvFileWithSampleRows()
    {
        var service = new PlaceImportService();

        var file = await service.GetImportTemplateAsync("csv");

        Assert.NotNull(file);
        Assert.NotNull(file.Content);
        Assert.True(file.Content.Length > 0);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("Place_Import_Template.csv", file.FileName);

        using var stream = new MemoryStream(file.Content);
        var rows = (await stream.QueryAsync<LocalMateAI.Application.DTOs.Places.PlaceImportTemplateRow>(excelType: ExcelType.CSV)).ToList();
        Assert.Equal(3, rows.Count);
    }
}

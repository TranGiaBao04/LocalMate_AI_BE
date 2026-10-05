using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using MiniExcelLibs;

namespace LocalMateAI.Infrastructure.Services;

public sealed class PlaceImportService : IPlaceImportService
{
    public async Task<PlaceImportTemplateFile> GetImportTemplateAsync(
        string? format,
        CancellationToken cancellationToken = default)
    {
        var sampleRows = GetSampleRows();
        var isCsv = string.Equals(format?.Trim(), "csv", StringComparison.OrdinalIgnoreCase);
        var excelType = isCsv ? ExcelType.CSV : ExcelType.XLSX;

        using var stream = new MemoryStream();
        await stream.SaveAsAsync(sampleRows, excelType: excelType, cancellationToken: cancellationToken);

        var bytes = stream.ToArray();
        var contentType = isCsv
            ? "text/csv; charset=utf-8"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var fileName = isCsv ? "Place_Import_Template.csv" : "Place_Import_Template.xlsx";

        return new PlaceImportTemplateFile(bytes, contentType, fileName);
    }

    private static List<PlaceImportTemplateRow> GetSampleRows() =>
    [
        new()
        {
            Name = "Phở Phượng Sài Gòn",
            Category = "Food",
            Address = "25 Hoàng Sa, Phường Đa Kao, Quận 1",
            Latitude = 10.7885,
            Longitude = 106.7025,
            EstimatedCostMin = 50000,
            EstimatedCostMax = 100000,
            MetroStationName = "Ba Son",
            Description = "Quán phở truyền thống nổi tiếng gần ga Ba Son",
            OpeningHours = "Mon-Sun 06:00-21:00",
            ImageUrl = "https://images.unsplash.com/photo-1555396273-367ea4eb4db5?w=500"
        },
        new()
        {
            Name = "Cà Phê Trứng 3T",
            Category = "Cafe",
            Address = "1B Tôn Đức Thắng, Phường Bến Nghé, Quận 1",
            Latitude = 10.7812,
            Longitude = 106.7068,
            EstimatedCostMin = 35000,
            EstimatedCostMax = 65000,
            MetroStationName = "Ba Son",
            Description = "Quán cà phê trứng đậm đà view sông Sài Gòn",
            OpeningHours = "Mon-Sun 07:00-22:30",
            ImageUrl = "https://images.unsplash.com/photo-1501339847302-ac426a4a7cbb?w=500"
        },
        new()
        {
            Name = "Bảo Tàng Lịch Sử TP.HCM",
            Category = "Culture",
            Address = "2 Nguyễn Bỉnh Khiêm, Phường Bến Nghé, Quận 1",
            Latitude = 10.7878,
            Longitude = 106.7052,
            EstimatedCostMin = 30000,
            EstimatedCostMax = 30000,
            MetroStationName = "Ba Son",
            Description = "Bảo tàng trưng bày di sản lịch sử và văn hóa Việt Nam",
            OpeningHours = "Tue-Sun 08:00-17:00",
            ImageUrl = "https://images.unsplash.com/photo-1565008447742-97f6f38c985c?w=500"
        }
    ];
}

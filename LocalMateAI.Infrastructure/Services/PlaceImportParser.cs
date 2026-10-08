using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using MiniExcelLibs;

namespace LocalMateAI.Infrastructure.Services;

public sealed class PlaceImportParser : IPlaceImportParser
{
    public async Task<IReadOnlyList<ParsedPlaceRow>> ParseAsync(
        Stream stream,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var excelType = string.Equals(fileExtension.TrimStart('.'), "csv", StringComparison.OrdinalIgnoreCase)
            ? ExcelType.CSV
            : ExcelType.XLSX;

        var rows = (await stream.QueryAsync(useHeaderRow: true, excelType: excelType)).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var parsedRows = new List<ParsedPlaceRow>();
        int rowNumber = 1;

        foreach (IDictionary<string, object> row in rows.Cast<IDictionary<string, object>>())
        {
            rowNumber++;

            string GetVal(params string[] keys)
            {
                foreach (var k in keys)
                {
                    var match = row.FirstOrDefault(kvp => string.Equals(kvp.Key?.Trim(), k, StringComparison.OrdinalIgnoreCase));
                    if (match.Key != null && match.Value != null)
                    {
                        var valStr = match.Value.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(valStr))
                        {
                            return valStr;
                        }
                    }
                }
                return string.Empty;
            }

            var name = GetVal("Tên địa điểm", "Name", "Tên");
            var address = GetVal("Địa chỉ", "Address");
            var category = GetVal("Danh mục (Food/Cafe/CheckIn/Culture)", "Danh mục", "Category");
            var coords = GetVal("Tọa độ (Lat, Lng)", "Tọa độ", "Coordinates");
            var lat = GetVal("Vĩ độ (Latitude)", "Vĩ độ", "Latitude", "Lat");
            var lng = GetVal("Kinh độ (Longitude)", "Kinh độ", "Longitude", "Lng");
            var priceMin = GetVal("Giá từ (VNĐ)", "Giá từ", "Min Price", "PriceMin", "EstimatedCostMin");
            var priceMax = GetVal("Giá đến (VNĐ)", "Giá đến", "Max Price", "PriceMax", "EstimatedCostMax");
            var stations = GetVal("Cụm ga Metro liên kết", "Ga Metro", "Stations", "Ga", "MetroStationName", "MetroStation");
            var openHours = GetVal("Khung giờ mở cửa", "Giờ mở cửa", "OpenHours", "OpeningHours");
            var tags = GetVal("Tags (cách nhau bởi dấu phẩy)", "Tags", "Thẻ");
            var description = GetVal("Mô tả chi tiết", "Mô tả", "Description");
            var imageUrl = GetVal("Link ảnh đại diện", "Ảnh đại diện", "Link ảnh", "ImageUrl", "Image", "Picture");

            // Skip completely empty rows
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(category))
            {
                continue;
            }

            parsedRows.Add(new ParsedPlaceRow(
                RowNumber: rowNumber,
                RawName: name,
                RawAddress: address,
                RawCategory: category,
                RawCoordinates: coords,
                RawLatitude: lat,
                RawLongitude: lng,
                RawPriceMin: priceMin,
                RawPriceMax: priceMax,
                RawStations: stations,
                RawOpenHours: openHours,
                RawTags: tags,
                RawDescription: description,
                RawImageUrl: imageUrl
            ));
        }

        return parsedRows;
    }
}

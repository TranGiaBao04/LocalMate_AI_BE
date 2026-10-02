using System.Globalization;

namespace LocalMateAI.Application.Settings;

public enum SystemSettingValueType
{
    Integer,
    Decimal
}

public sealed record SystemSettingDefinition(
    string Key,
    string Group,
    string Name,
    string Description,
    SystemSettingValueType ValueType,
    string? Unit,
    decimal DefaultValue,
    decimal MinValue,
    decimal MaxValue);

public static class SystemSettingKeys
{
    public const string MinActivePlacesPerStation = "Stations.MinActivePlacesPerStation";
    public const string BreakEvenMonthlyRevenue = "Dashboard.BreakEvenMonthlyRevenue";
    public const string MaxServiceAreaDistanceMeters = "Trips.MaxServiceAreaDistanceMeters";
    public const string AlternativeMaxCostIncreasePercent = "Trips.AlternativeMaxCostIncreasePercent";
}

/// <summary>
/// Danh sách thông số admin chỉnh được. Thêm thông số = thêm khoá + 1 dòng ở đây (không cần migration);
/// admin chỉ sửa giá trị, không tạo khoá mới.
/// </summary>
public static class SystemSettingDefinitions
{
    public static readonly IReadOnlyList<SystemSettingDefinition> All =
    [
        new(SystemSettingKeys.MinActivePlacesPerStation, "Stations", "Số địa điểm tối thiểu mỗi ga",
            "Ga có ít địa điểm đang hoạt động hơn số này bị đánh dấu thiếu dữ liệu trên màn quản lý ga.",
            SystemSettingValueType.Integer, "địa điểm", DefaultValue: 5, MinValue: 1, MaxValue: 100),
        new(SystemSettingKeys.BreakEvenMonthlyRevenue, "Dashboard", "Mục tiêu doanh thu hoà vốn mỗi tháng",
            "Doanh thu (VNĐ, chưa trừ phí cổng thanh toán) cần đạt mỗi tháng; dùng cho tiến độ hoà vốn trên dashboard.",
            SystemSettingValueType.Integer, "VNĐ", DefaultValue: 5_000_000, MinValue: 1, MaxValue: 10_000_000_000),
        new(SystemSettingKeys.MaxServiceAreaDistanceMeters, "Trips", "Bán kính vùng phục vụ",
            "Khoảng cách đường chim bay tối đa từ điểm xuất phát tới ga Metro gần nhất để còn tạo được lịch trình. "
            + "12.000 m phủ các quận nội thành, TP Thủ Đức và Nhà Bè.",
            SystemSettingValueType.Integer, "m", DefaultValue: 12_000, MinValue: 1_000, MaxValue: 50_000),
        new(SystemSettingKeys.AlternativeMaxCostIncreasePercent, "Trips", "Mức đắt hơn tối đa khi gợi ý thay thế",
            "Địa điểm gợi ý thay thế được đắt hơn địa điểm hiện tại tối đa bao nhiêu phần trăm. "
            + "Địa điểm miễn phí chỉ được thay bằng địa điểm miễn phí.",
            SystemSettingValueType.Integer, "%", DefaultValue: 25, MinValue: 0, MaxValue: 200)
    ];

    public static SystemSettingDefinition? Find(string key) =>
        All.FirstOrDefault(definition => string.Equals(definition.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Trả thông báo lỗi tiếng Việt, hoặc null nếu hợp lệ.</summary>
    public static string? Validate(SystemSettingDefinition definition, decimal value)
    {
        if (definition.ValueType == SystemSettingValueType.Integer && value != decimal.Truncate(value))
        {
            return "Giá trị phải là số nguyên.";
        }

        return value < definition.MinValue || value > definition.MaxValue
            ? $"Giá trị phải từ {Format(definition, definition.MinValue)} đến {Format(definition, definition.MaxValue)}."
            : null;
    }

    public static bool TryParse(SystemSettingDefinition definition, string stored, out decimal value) =>
        decimal.TryParse(stored, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        && Validate(definition, value) is null;

    public static string Format(SystemSettingDefinition definition, decimal value) =>
        definition.ValueType == SystemSettingValueType.Integer
            ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.############################", CultureInfo.InvariantCulture);
}

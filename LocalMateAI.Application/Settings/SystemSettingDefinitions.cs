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
            SystemSettingValueType.Integer, "địa điểm", DefaultValue: 5, MinValue: 1, MaxValue: 100)
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

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
    public const string StationClusterRadiusMeters = "Stations.ClusterRadiusMeters";
    public const string BreakEvenMonthlyRevenue = "Dashboard.BreakEvenMonthlyRevenue";
    public const string MaxServiceAreaDistanceMeters = "Trips.MaxServiceAreaDistanceMeters";
    public const string AlternativeMaxCostIncreasePercent = "Trips.AlternativeMaxCostIncreasePercent";
    public const string AdjacentStationWindow = "Trips.AdjacentStationWindow";
    public const string CafeVisitMinutes = "Planning.VisitMinutes.Cafe";
    public const string FoodVisitMinutes = "Planning.VisitMinutes.Food";
    public const string CultureVisitMinutes = "Planning.VisitMinutes.Culture";
    public const string CheckInVisitMinutes = "Planning.VisitMinutes.CheckIn";
    public const string WalkingSpeedKmH = "Travel.WalkingSpeedKmH";
    public const string MotorbikeSpeedKmH = "Travel.MotorbikeSpeedKmH";
    public const string RoadDetourFactor = "Travel.RoadDetourFactor";
    public const string AutoWalkingMaxMeters = "Travel.AutoWalkingMaxMeters";
    public const string NoteWeightPercent = "Planning.NoteWeightPercent";
    public const string SemanticMinSimilarityPercent = "Semantic.MinSimilarityPercent";
    public const string SemanticMaxGapFromTopPercent = "Semantic.MaxGapFromTopPercent";
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
            "Ga có ít địa điểm đang hoạt động hơn số này bị đánh dấu thiếu dữ liệu trên màn quản lý ga. "
            + "Khi tạo lịch mà khu người dùng chọn chưa có địa điểm, chỉ gợi ý những ga có từ ngần này địa điểm "
            + "trở lên (tính cả các ga kề).",
            SystemSettingValueType.Integer, "địa điểm", DefaultValue: 5, MinValue: 1, MaxValue: 100),
        new(SystemSettingKeys.BreakEvenMonthlyRevenue, "Dashboard", "Mục tiêu doanh thu hoà vốn mỗi tháng",
            "Doanh thu (VNĐ, chưa trừ phí cổng thanh toán) cần đạt mỗi tháng; dùng cho tiến độ hoà vốn trên dashboard.",
            SystemSettingValueType.Integer, "VNĐ", DefaultValue: 5_000_000, MinValue: 1, MaxValue: 10_000_000_000),
        new(SystemSettingKeys.MaxServiceAreaDistanceMeters, "Trips", "Khoảng cách tối đa tới ga khi đi Metro",
            "Chỉ áp dụng khi người dùng chọn phương tiện Metro: khoảng cách đường chim bay tối đa từ điểm xuất phát "
            + "tới ga lên. Các phương tiện khác không xét khoảng cách này, chỉ cần điểm xuất phát nằm trong TP.HCM.",
            SystemSettingValueType.Integer, "m", DefaultValue: 12_000, MinValue: 1_000, MaxValue: 50_000),
        new(SystemSettingKeys.AlternativeMaxCostIncreasePercent, "Trips", "Mức đắt hơn tối đa khi gợi ý thay thế",
            "Địa điểm gợi ý thay thế được đắt hơn địa điểm hiện tại tối đa bao nhiêu phần trăm. "
            + "Địa điểm miễn phí chỉ được thay bằng địa điểm miễn phí.",
            SystemSettingValueType.Integer, "%", DefaultValue: 25, MinValue: 0, MaxValue: 200),
        new(SystemSettingKeys.StationClusterRadiusMeters, "Stations", "Bán kính cụm ga",
            "Địa điểm cách ga Metro gần nhất trong bán kính này mới thuộc cụm ga đó (800 m ≈ 10 phút đi bộ). "
            + "Dùng chung cho gợi ý lịch trình, gợi ý thay thế, danh sách cụm ga, tìm quanh ga và màn quản lý ga.",
            SystemSettingValueType.Integer, "m", DefaultValue: 800, MinValue: 200, MaxValue: 2_000),
        new(SystemSettingKeys.AdjacentStationWindow, "Trips", "Số ga kề được lấy thêm địa điểm",
            "Khi tạo lịch trình, ngoài ga cột mốc còn lấy địa điểm của bao nhiêu ga kề mỗi bên dọc tuyến. "
            + "0 = chỉ ga cột mốc.",
            SystemSettingValueType.Integer, "ga", DefaultValue: 1, MinValue: 0, MaxValue: 3),
        VisitMinutes(SystemSettingKeys.CafeVisitMinutes, "Cafe", 60),
        VisitMinutes(SystemSettingKeys.FoodVisitMinutes, "Ăn uống", 75),
        VisitMinutes(SystemSettingKeys.CultureVisitMinutes, "Văn hoá", 90),
        VisitMinutes(SystemSettingKeys.CheckInVisitMinutes, "Check-in", 45),
        new(SystemSettingKeys.WalkingSpeedKmH, "Travel", "Tốc độ đi bộ",
            "Dùng để ước tính thời gian đi bộ giữa các chặng.",
            SystemSettingValueType.Decimal, "km/h", DefaultValue: 4.8m, MinValue: 2m, MaxValue: 8m),
        new(SystemSettingKeys.MotorbikeSpeedKmH, "Travel", "Tốc độ xe máy",
            "Tốc độ trung bình nội thành, dùng để ước tính thời gian đi xe máy.",
            SystemSettingValueType.Decimal, "km/h", DefaultValue: 24m, MinValue: 10m, MaxValue: 60m),
        new(SystemSettingKeys.RoadDetourFactor, "Travel", "Hệ số đường vòng",
            "Quãng đường bộ ước tính = khoảng cách đường chim bay × hệ số này.",
            SystemSettingValueType.Decimal, "lần", DefaultValue: 1.3m, MinValue: 1m, MaxValue: 2m),
        new(SystemSettingKeys.AutoWalkingMaxMeters, "Travel", "Ngưỡng tự chọn đi bộ",
            "Ở chế độ Tự động, đoạn đường bộ không quá ngưỡng này thì tính đi bộ, xa hơn thì tính xe máy.",
            SystemSettingValueType.Integer, "m", DefaultValue: 700, MinValue: 100, MaxValue: 2_000),
        new(SystemSettingKeys.NoteWeightPercent, "Planning", "Trọng số ghi chú khi tạo lịch",
            "Ghi chú của người dùng ảnh hưởng bao nhiêu phần trăm tới thứ hạng địa điểm, phần còn lại là tag sở thích. "
            + "0 = bỏ qua ghi chú.",
            SystemSettingValueType.Integer, "%", DefaultValue: 50, MinValue: 0, MaxValue: 100),
        new(SystemSettingKeys.SemanticMinSimilarityPercent, "Semantic", "Độ tương đồng tối thiểu",
            "Khi tìm theo nghĩa, nếu địa điểm khớp nhất có độ tương đồng với câu của người dùng thấp hơn mức này "
            + "thì coi như không có kết quả liên quan. Đặt cao hơn thì ít gợi ý sai nhưng dễ bỏ sót.",
            SystemSettingValueType.Integer, "%", DefaultValue: 66, MinValue: 0, MaxValue: 100),
        new(SystemSettingKeys.SemanticMaxGapFromTopPercent, "Semantic", "Khoảng cách tối đa tới kết quả khớp nhất",
            "Chỉ lấy những địa điểm có độ tương đồng thấp hơn địa điểm khớp nhất không quá số này. "
            + "Đặt nhỏ hơn thì kết quả gọn hơn; 0 = chỉ lấy địa điểm khớp nhất.",
            SystemSettingValueType.Integer, "điểm %", DefaultValue: 4, MinValue: 0, MaxValue: 100)
    ];

    private static SystemSettingDefinition VisitMinutes(string key, string categoryLabel, int defaultMinutes) =>
        new(key, "Planning", $"Thời gian tham quan: {categoryLabel}",
            $"Số phút dự kiến ở mỗi địa điểm loại {categoryLabel}; quyết định số chặng xếp được trong lịch trình.",
            SystemSettingValueType.Integer, "phút", defaultMinutes, MinValue: 15, MaxValue: 240);

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

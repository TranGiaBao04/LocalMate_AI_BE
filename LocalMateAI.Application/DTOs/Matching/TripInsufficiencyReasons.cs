namespace LocalMateAI.Application.DTOs.Matching;

/// <summary>
/// Lý do không dựng được lịch trình, dùng chung cho /match, /fallback-itinerary, /feasibility-check và /generate.
/// </summary>
public static class TripInsufficiencyReasons
{
    /// <summary>Toạ độ xuất phát nằm ngoài TP.HCM.</summary>
    public const string OutOfServiceArea = "OutOfServiceArea";

    /// <summary>Chọn đi Metro nhưng điểm xuất phát cách ga lên quá xa.</summary>
    public const string TooFarFromStationForMetro = "TooFarFromStationForMetro";

    /// <summary>Quanh ga cột mốc không có địa điểm nào trong ngân sách.</summary>
    public const string InsufficientCandidates = "InsufficientCandidates";

    /// <summary>Có địa điểm nhưng không chặng nào vừa số giờ rảnh (tính cả đoạn đi tới chặng đầu).</summary>
    public const string DurationTooShort = "DurationTooShort";

    /// <summary>Có địa điểm vừa thời gian và ngân sách nhưng không nơi nào mở cửa vào giờ đó.</summary>
    public const string NoPlacesOpen = "NoPlacesOpen";

    /// <summary>Chọn đi Metro nhưng lúc bắt đầu ga lên không có tàu chạy (đêm khuya hoặc trước chuyến đầu).</summary>
    public const string MetroNotRunning = "MetroNotRunning";
}

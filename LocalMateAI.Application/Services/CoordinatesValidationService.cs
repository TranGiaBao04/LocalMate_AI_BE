using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class CoordinatesValidationService : ICoordinatesValidationService
{
    // Bán kính TP.HCM hợp lệ
    private const double MinLat = 10.370000; // Điểm cực Nam: Xã Long Hòa, huyện Cần Giờ
    private const double MaxLat = 11.160000; // Điểm cực Bắc: Xã Phú Mỹ Hưng, huyện Củ Chi
    private const double MinLng = 106.360000; // Điểm cực Tây: Xã Thái Mỹ, huyện Củ Chi
    private const double MaxLng = 107.030000; // Điểm cực Đông: Xã Thạnh An (Đảo Thạnh An), Cần Giờ

    public bool IsValidHcmcCoordinate(double lat, double lng)
    {
        return lat >= MinLat && lat <= MaxLat && lng >= MinLng && lng <= MaxLng;
    }

    public (bool IsValid, string Reason) ValidateCoordinate(double lat, double lng)
    {
        if (double.IsNaN(lat) || double.IsNaN(lng))
            return (false, "Tọa độ không hợp lệ (NaN).");

        if (lat < MinLat || lat > MaxLat)
            return (false, $"Vĩ độ ({lat}) nằm ngoài phạm vi TP.HCM [{MinLat} - {MaxLat}].");

        if (lng < MinLng || lng > MaxLng)
            return (false, $"Kinh độ ({lng}) nằm ngoài phạm vi TP.HCM [{MinLng} - {MaxLng}].");

        return (true, "Tọa độ hợp lệ tại TP.HCM.");
    }
}

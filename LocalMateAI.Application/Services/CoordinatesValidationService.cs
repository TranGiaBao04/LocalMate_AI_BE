namespace LocalMateAI.Application.Services;

public interface ICoordinatesValidationService
{
    bool IsValidHcmcCoordinate(double lat, double lng);
    (bool IsValid, string Reason) ValidateCoordinate(double lat, double lng);
}

public sealed class CoordinatesValidationService : ICoordinatesValidationService
{
    // Bán kính TP.HCM hợp lệ
    private const double MinLat = 10.3;
    private const double MaxLat = 11.2;
    private const double MinLng = 106.3;
    private const double MaxLng = 107.1;

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

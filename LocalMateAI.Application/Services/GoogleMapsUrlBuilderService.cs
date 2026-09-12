using System.Web;

namespace LocalMateAI.Application.Services;

public interface IGoogleMapsUrlBuilderService
{
    string BuildSearchUrl(double lat, double lng, string? placeName = null);
    string BuildDirectionsUrl(double originLat, double originLng, double destLat, double destLng, string? originName = null, string? destName = null, string travelMode = "walking");
}

public sealed class GoogleMapsUrlBuilderService : IGoogleMapsUrlBuilderService
{
    private const string BaseSearchUrl = "https://www.google.com/maps/search/?api=1";
    private const string BaseDirectionsUrl = "https://www.google.com/maps/dir/?api=1";

    public string BuildSearchUrl(double lat, double lng, string? placeName = null)
    {
        var query = !string.IsNullOrWhiteSpace(placeName)
            ? $"{placeName}@{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        return $"{BaseSearchUrl}&query={HttpUtility.UrlEncode(query)}";
    }

    public string BuildDirectionsUrl(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null,
        string travelMode = "walking")
    {
        var origin = !string.IsNullOrWhiteSpace(originName)
            ? $"{originName}@{originLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{originLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{originLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{originLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        var destination = !string.IsNullOrWhiteSpace(destName)
            ? $"{destName}@{destLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{destLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{destLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{destLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        var mode = travelMode.ToLowerInvariant() switch
        {
            "driving" => "driving",
            "bicycling" => "bicycling",
            "transit" => "transit",
            _ => "walking"
        };

        return $"{BaseDirectionsUrl}&origin={HttpUtility.UrlEncode(origin)}&destination={HttpUtility.UrlEncode(destination)}&travelmode={mode}";
    }
}

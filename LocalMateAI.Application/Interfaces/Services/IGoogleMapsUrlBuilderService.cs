namespace LocalMateAI.Application.Interfaces.Services;

public interface IGoogleMapsUrlBuilderService
{
    string BuildSearchUrl(double lat, double lng, string? placeName = null);
    string BuildDirectionsUrl(double originLat, double originLng, double destLat, double destLng, string? originName = null, string? destName = null, string travelMode = "walking");
}

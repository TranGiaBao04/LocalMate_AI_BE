using System.Globalization;
using System.Text.RegularExpressions;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Infrastructure.Services;

public sealed class CoordinateNormalizer(ICoordinatesValidationService coordinatesValidationService) : ICoordinateNormalizer
{
    public CoordinateNormalizationResult Normalize(string? rawCoordinates, string? rawLatitude, string? rawLongitude)
    {
        string latStr = (rawLatitude ?? string.Empty).Trim();
        string lngStr = (rawLongitude ?? string.Empty).Trim();

        // If merged rawCoordinates string provided (e.g., "10.7769, 106.7009" or "10.7769;106.7009")
        if (!string.IsNullOrWhiteSpace(rawCoordinates) && (string.IsNullOrEmpty(latStr) || string.IsNullOrEmpty(lngStr)))
        {
            var parts = rawCoordinates.Split([',', ';', '|', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                latStr = parts[0];
                lngStr = parts[1];
            }
        }

        if (string.IsNullOrEmpty(latStr) || string.IsNullOrEmpty(lngStr))
        {
            return new CoordinateNormalizationResult(false, null, null, "Thiếu tọa độ vĩ độ (Latitude) hoặc kinh độ (Longitude).");
        }

        latStr = SanitizeCoordinateString(latStr);
        lngStr = SanitizeCoordinateString(lngStr);

        if (!double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lat))
        {
            return new CoordinateNormalizationResult(false, null, null, $"Vĩ độ (Latitude) '{rawLatitude}' không hợp lệ.");
        }

        if (!double.TryParse(lngStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var lng))
        {
            return new CoordinateNormalizationResult(false, null, null, $"Kinh độ (Longitude) '{rawLongitude}' không hợp lệ.");
        }

        var validation = coordinatesValidationService.ValidateCoordinate(lat, lng);
        if (!validation.IsValid)
        {
            return new CoordinateNormalizationResult(false, lat, lng, validation.Reason);
        }

        return new CoordinateNormalizationResult(true, lat, lng, null);
    }

    private static string SanitizeCoordinateString(string input)
    {
        var clean = input.Trim();
        // Replace multiple dots with a single dot
        clean = Regex.Replace(clean, @"\.+", ".");
        // If comma used instead of dot decimal separator
        clean = clean.Replace(',', '.');
        return clean;
    }
}

using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ICoordinateNormalizer
{
    CoordinateNormalizationResult Normalize(string? rawCoordinates, string? rawLatitude, string? rawLongitude);
}

using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPriceNormalizer
{
    PriceNormalizationResult Normalize(string? rawPriceMin, string? rawPriceMax);
}

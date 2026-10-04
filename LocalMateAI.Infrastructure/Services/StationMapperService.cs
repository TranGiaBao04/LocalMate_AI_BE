using System.Globalization;
using System.Text;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Infrastructure.Services;

public sealed class StationMapperService(IMetroStationRepository stationRepository) : IStationMapperService
{
    public async Task<StationMappingResult> MapStationsAsync(string? rawStations, CancellationToken cancellationToken = default)
    {
        var cleanInput = (rawStations ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(cleanInput))
        {
            return new StationMappingResult(true, [], [], [], null);
        }

        var allStations = await stationRepository.GetAllAsync(cancellationToken);
        if (allStations.Count == 0)
        {
            return new StationMappingResult(false, [], [], [cleanInput], "Không tìm thấy dữ liệu ga Metro trong hệ thống.");
        }

        var tokens = cleanInput.Split([',', ';', '|', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matchedIds = new List<Guid>();
        var matchedNames = new List<string>();
        var unmatchedNames = new List<string>();

        foreach (var rawToken in tokens)
        {
            var normalizedToken = RemoveDiacritics(rawToken);
            if (normalizedToken.StartsWith("ga ", StringComparison.OrdinalIgnoreCase))
            {
                normalizedToken = normalizedToken[3..].Trim();
            }

            var match = allStations.FirstOrDefault(s =>
            {
                var sNorm = RemoveDiacritics(s.Name);
                if (sNorm.StartsWith("ga ", StringComparison.OrdinalIgnoreCase))
                {
                    sNorm = sNorm[3..].Trim();
                }

                return string.Equals(sNorm, normalizedToken, StringComparison.OrdinalIgnoreCase) ||
                       sNorm.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase) ||
                       normalizedToken.Contains(sNorm, StringComparison.OrdinalIgnoreCase);
            });

            if (match != null)
            {
                if (!matchedIds.Contains(match.Id))
                {
                    matchedIds.Add(match.Id);
                    matchedNames.Add(match.Name);
                }
            }
            else
            {
                unmatchedNames.Add(rawToken);
            }
        }

        if (unmatchedNames.Count > 0)
        {
            var err = $"Không tìm thấy ga Metro tương ứng trong hệ thống: {string.Join(", ", unmatchedNames)}";
            return new StationMappingResult(false, matchedIds, matchedNames, unmatchedNames, err);
        }

        return new StationMappingResult(true, matchedIds, matchedNames, unmatchedNames, null);
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
    }
}

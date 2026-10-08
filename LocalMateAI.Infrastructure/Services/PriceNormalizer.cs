using System.Globalization;
using System.Text.RegularExpressions;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Infrastructure.Services;

public sealed class PriceNormalizer : IPriceNormalizer
{
    public PriceNormalizationResult Normalize(string? rawPriceMin, string? rawPriceMax)
    {
        var cleanMinStr = (rawPriceMin ?? string.Empty).Trim();
        var cleanMaxStr = (rawPriceMax ?? string.Empty).Trim();

        // If both empty, return valid nulls
        if (string.IsNullOrEmpty(cleanMinStr) && string.IsNullOrEmpty(cleanMaxStr))
        {
            return new PriceNormalizationResult(true, null, null, null);
        }

        // If min string contains range separator (e.g., "30.000 - 50.000" or "30k~50k")
        if (!string.IsNullOrEmpty(cleanMinStr) && (cleanMinStr.Contains('-') || cleanMinStr.Contains('~')) && string.IsNullOrEmpty(cleanMaxStr))
        {
            var parts = cleanMinStr.Split(['-', '~'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                cleanMinStr = parts[0];
                cleanMaxStr = parts[1];
            }
        }

        int? minCost = null;
        int? maxCost = null;

        if (!string.IsNullOrEmpty(cleanMinStr))
        {
            var parseRes = ParseSinglePrice(cleanMinStr);
            if (!parseRes.Success)
            {
                return new PriceNormalizationResult(false, null, null, $"Giá tối thiểu '{rawPriceMin}' không hợp lệ. Vui lòng nhập số nguyên.");
            }
            minCost = parseRes.Value;
        }

        if (!string.IsNullOrEmpty(cleanMaxStr))
        {
            var parseRes = ParseSinglePrice(cleanMaxStr);
            if (!parseRes.Success)
            {
                return new PriceNormalizationResult(false, null, null, $"Giá tối đa '{rawPriceMax}' không hợp lệ. Vui lòng nhập số nguyên.");
            }
            maxCost = parseRes.Value;
        }

        if (minCost < 0)
        {
            return new PriceNormalizationResult(false, null, null, "Giá tối thiểu không được âm.");
        }

        if (maxCost < 0)
        {
            return new PriceNormalizationResult(false, null, null, "Giá tối đa không được âm.");
        }

        if (minCost.HasValue && maxCost.HasValue && minCost.Value > maxCost.Value)
        {
            return new PriceNormalizationResult(false, null, null, $"Giá tối thiểu ({minCost.Value:N0}đ) không được lớn hơn giá tối đa ({maxCost.Value:N0}đ).");
        }

        return new PriceNormalizationResult(true, minCost, maxCost, null);
    }

    private static (bool Success, int Value) ParseSinglePrice(string input)
    {
        var lower = input.Trim().ToLowerInvariant();

        if (lower is "free" or "miễn phí" or "0" or "0đ" or "0 vnd" or "0 vnđ" or "0đ." or "0.0")
        {
            return (true, 0);
        }

        // Check multiplier 'k' (e.g. 30k -> 30000, 30.5k -> 30500)
        var hasK = lower.EndsWith('k');
        if (hasK)
        {
            lower = lower[..^1].Trim();
        }

        // Strip non-numeric chars except dot/comma decimal
        var clean = Regex.Replace(lower, @"[^\d\.,]", string.Empty);
        if (string.IsNullOrEmpty(clean))
        {
            return (false, 0);
        }

        // If thousand separator dot/comma e.g., 30.000 or 30,000
        if (!hasK)
        {
            // Remove dots and commas used as thousand separators
            clean = clean.Replace(".", string.Empty).Replace(",", string.Empty);
            if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val))
            {
                return (true, val);
            }
        }
        else
        {
            clean = clean.Replace(",", ".");
            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var kVal))
            {
                return (true, (int)Math.Round(kVal * 1000.0));
            }
        }

        return (false, 0);
    }
}

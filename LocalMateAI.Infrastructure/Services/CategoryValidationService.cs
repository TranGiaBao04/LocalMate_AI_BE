using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Infrastructure.Services;

public sealed class CategoryValidationService : ICategoryValidationService
{
    public CategoryValidationResult Validate(string? rawCategory)
    {
        var clean = (rawCategory ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(clean))
        {
            return new CategoryValidationResult(false, null, "Danh mục không được để trống.");
        }

        // Direct enum parse check
        if (Enum.TryParse<PlaceCategory>(clean, ignoreCase: true, out var parsedCategory))
        {
            return new CategoryValidationResult(true, parsedCategory, null);
        }

        // Vietnamese & alias mapping
        var category = clean switch
        {
            "ăn uống" or "thực phẩm" or "quán ăn" or "nhà hàng" or "food" => PlaceCategory.Food,
            "cà phê" or "nước uống" or "quán cafe" or "cafe" or "coffee" => PlaceCategory.Cafe,
            "văn hóa" or "văn hoá" or "di tích" or "bảo tàng" or "lịch sử" or "culture" => PlaceCategory.Culture,
            "sống ảo" or "tham quan" or "du lịch" or "giải trí" or "checkin" or "check-in" or "check in" => PlaceCategory.CheckIn,
            _ => (PlaceCategory?)null
        };

        if (category.HasValue)
        {
            return new CategoryValidationResult(true, category.Value, null);
        }

        return new CategoryValidationResult(
            false,
            null,
            $"Danh mục '{rawCategory}' không hợp lệ. Chỉ chấp nhận: Food (Ăn uống), Cafe (Cà phê), Culture (Văn hóa), CheckIn (Sống ảo).");
    }
}

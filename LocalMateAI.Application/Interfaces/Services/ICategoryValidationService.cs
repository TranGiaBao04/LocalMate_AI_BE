using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ICategoryValidationService
{
    CategoryValidationResult Validate(string? rawCategory);
}

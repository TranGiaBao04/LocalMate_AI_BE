using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceImportService
{
    Task<PlaceImportTemplateFile> GetImportTemplateAsync(string? format, CancellationToken cancellationToken = default);
}

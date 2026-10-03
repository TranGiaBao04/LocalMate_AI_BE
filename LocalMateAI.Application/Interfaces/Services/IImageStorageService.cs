using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IImageStorageService
{
    Task<ImageUploadResult> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long length,
        CancellationToken cancellationToken = default);
}

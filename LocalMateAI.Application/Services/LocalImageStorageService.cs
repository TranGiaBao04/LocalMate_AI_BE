using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class LocalImageStorageService(ImageUploadOptions options) : IImageStorageService
{
    private readonly ImageUploadOptions _options = options;

    public async Task<ImageUploadResult> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        if (!_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return new ImageUploadResult(ImageUploadResultStatus.InvalidContentType);
        }

        if (length <= 0 || length > _options.MaxFileBytes)
        {
            return new ImageUploadResult(ImageUploadResultStatus.TooLarge);
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        var safeFileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        Directory.CreateDirectory(_options.UploadFolder);

        var fullPath = Path.Combine(_options.UploadFolder, safeFileName);
        await using (var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var url = $"{_options.PublicUrlPrefix.TrimEnd('/')}/{safeFileName}";
        return new ImageUploadResult(ImageUploadResultStatus.Success, url);
    }
}

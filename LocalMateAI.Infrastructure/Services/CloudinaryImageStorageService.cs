using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using Microsoft.Extensions.Logging;
using AppImageUploadResult = LocalMateAI.Application.DTOs.Places.ImageUploadResult;

namespace LocalMateAI.Infrastructure.Services;

public sealed class CloudinaryImageStorageService(
    CloudinaryUploadOptions options,
    ILogger<CloudinaryImageStorageService> logger) : IImageStorageService
{
    private readonly CloudinaryUploadOptions _options = options;
    private readonly Cloudinary _cloudinary = new(new Account(options.CloudName, options.ApiKey, options.ApiSecret));
    private readonly ILogger<CloudinaryImageStorageService> _logger = logger;

    public async Task<AppImageUploadResult> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (length <= 0 || length > _options.MaxFileBytes)
        {
            return new AppImageUploadResult(ImageUploadResultStatus.TooLarge);
        }

        var extension = GetValidExtension(contentType);
        if (extension is null || !_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return new AppImageUploadResult(ImageUploadResultStatus.InvalidContentType);
        }

        if (!await ValidateMagicBytesAsync(content, contentType, cancellationToken))
        {
            return new AppImageUploadResult(ImageUploadResultStatus.InvalidContentType);
        }

        var publicId = $"{Guid.NewGuid():N}";
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, content),
            Folder = _options.Folder,
            PublicId = publicId
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);
        if (uploadResult.Error is not null || uploadResult.SecureUrl is null)
        {
            _logger.LogError("Cloudinary upload failed: {Error}", uploadResult.Error?.Message ?? "Unknown error");
            return new AppImageUploadResult(ImageUploadResultStatus.InvalidContentType);
        }

        return new AppImageUploadResult(ImageUploadResultStatus.Success, uploadResult.SecureUrl.AbsoluteUri);
    }

    private static string? GetValidExtension(string contentType) =>
        contentType.ToLowerInvariant().Trim() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => null
        };

    private static async Task<bool> ValidateMagicBytesAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var bytesRead = 0;

        if (content.CanSeek)
        {
            var originalPosition = content.Position;
            bytesRead = await content.ReadAsync(header.AsMemory(0, 12), cancellationToken);
            content.Position = originalPosition;
        }
        else
        {
            bytesRead = await content.ReadAsync(header.AsMemory(0, 12), cancellationToken);
        }

        if (bytesRead < 3)
        {
            return false;
        }

        var normalizedType = contentType.ToLowerInvariant().Trim();

        if (normalizedType is "image/jpeg" or "image/jpg")
        {
            return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }

        if (normalizedType is "image/png")
        {
            return bytesRead >= 4 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        }

        if (normalizedType is "image/webp")
        {
            return bytesRead >= 12
                   && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                   && header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x45 && header[11] == 0x50;
        }

        return false;
    }
}

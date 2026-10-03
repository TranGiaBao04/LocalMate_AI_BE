using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Settings;
using LocalMateAI.Infrastructure.Services;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class ImageStorageServiceTests
{
    private static CloudinaryImageStorageService CreateService() => new(new CloudinaryUploadOptions
    {
        CloudName = "demo",
        ApiKey = "123456789012345",
        ApiSecret = "demo_secret",
        Folder = "localmate/places"
    });

    [Fact]
    public async Task SaveAsync_UnsupportedContentType_ReturnsInvalidContentType()
    {
        var service = CreateService();
        var content = new byte[] { 0x01, 0x02 };
        await using var stream = new MemoryStream(content);

        var result = await service.SaveAsync(stream, "photo.txt", "text/plain", content.Length);

        Assert.Equal(ImageUploadResultStatus.InvalidContentType, result.Status);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task SaveAsync_TooLarge_ReturnsTooLarge()
    {
        var service = CreateService();
        var content = new byte[] { 0xFF, 0xD8 };
        await using var stream = new MemoryStream(content);

        var result = await service.SaveAsync(stream, "photo.jpg", "image/jpeg", 6 * 1024 * 1024);

        Assert.Equal(ImageUploadResultStatus.TooLarge, result.Status);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task SaveAsync_EmptyFile_ReturnsTooLarge()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var result = await service.SaveAsync(stream, "photo.jpg", "image/jpeg", 0);

        Assert.Equal(ImageUploadResultStatus.TooLarge, result.Status);
    }

    [Fact]
    public async Task SaveAsync_InvalidMagicBytes_ReturnsInvalidContentType()
    {
        var service = CreateService();
        var content = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        await using var stream = new MemoryStream(content);

        var result = await service.SaveAsync(stream, "photo.jpg", "image/jpeg", content.Length);

        Assert.Equal(ImageUploadResultStatus.InvalidContentType, result.Status);
    }
}

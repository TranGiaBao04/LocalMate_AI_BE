using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class ImageStorageServiceTests : IDisposable
{
    private readonly string _uploadFolder;

    public ImageStorageServiceTests()
    {
        _uploadFolder = Path.Combine(Path.GetTempPath(), "localmate-upload-tests", Guid.NewGuid().ToString("N"));
    }

    private LocalImageStorageService CreateService() => new(new ImageUploadOptions(
        5 * 1024 * 1024,
        ["image/jpeg", "image/png", "image/webp"],
        _uploadFolder,
        "/uploads/places"));

    [Fact]
    public async Task SaveAsync_ValidImage_ReturnsSuccessAndUrl()
    {
        var service = CreateService();
        var content = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        await using var stream = new MemoryStream(content);

        var result = await service.SaveAsync(stream, "photo.jpg", "image/jpeg", content.Length);

        Assert.Equal(ImageUploadResultStatus.Success, result.Status);
        Assert.NotNull(result.Url);
        Assert.StartsWith("/uploads/places/", result.Url);

        var fileName = result.Url!.Split('/').Last();
        Assert.True(File.Exists(Path.Combine(_uploadFolder, fileName)));
    }

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

    public void Dispose()
    {
        if (Directory.Exists(_uploadFolder))
        {
            Directory.Delete(_uploadFolder, recursive: true);
        }
    }
}

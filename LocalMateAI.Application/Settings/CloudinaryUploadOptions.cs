namespace LocalMateAI.Application.Settings;

public sealed class CloudinaryUploadOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string Folder { get; set; } = "localmate/places";
    public int MaxFileBytes { get; set; } = 5 * 1024 * 1024; // 5 MB
    public string[] AllowedContentTypes { get; set; } = ["image/jpeg", "image/png", "image/webp"];
}

namespace LocalMateAI.Application.Settings;

/// <summary>
/// Cấu hình upload ảnh địa điểm (dung lượng tối đa, định dạng cho phép, thư mục lưu).
/// </summary>
public sealed record ImageUploadOptions(
    int MaxFileBytes,
    string[] AllowedContentTypes,
    string UploadFolder,
    string PublicUrlPrefix)
{
    public const string SectionName = "ImageUpload";

    /// <summary>5 MB, JPEG/PNG/WebP, thư mục "uploads/places" (tương đối WebRootPath), URL "/uploads/places".</summary>
    public static readonly ImageUploadOptions Default = new(
        MaxFileBytes: 5 * 1024 * 1024,
        AllowedContentTypes: ["image/jpeg", "image/png", "image/webp"],
        UploadFolder: "uploads/places",
        PublicUrlPrefix: "/uploads/places");
}

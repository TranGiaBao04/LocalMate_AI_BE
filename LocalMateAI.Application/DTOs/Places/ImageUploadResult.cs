namespace LocalMateAI.Application.DTOs.Places;

public enum ImageUploadResultStatus
{
    Success,
    InvalidContentType,
    TooLarge
}

public sealed record ImageUploadResult(
    ImageUploadResultStatus Status,
    string? Url = null);

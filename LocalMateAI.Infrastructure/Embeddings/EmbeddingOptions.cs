namespace LocalMateAI.Infrastructure.Embeddings;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";
    public const int MinDimensions = 128;
    public const int MaxDimensions = 3072;

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-embedding-2";
    public int Dimensions { get; init; } = 768;

    // Thiếu khoá không phải lỗi cấu hình: tính năng chỉ tắt đi, app vẫn chạy như chưa có embedding.
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model)
        && Dimensions is >= MinDimensions and <= MaxDimensions;
}

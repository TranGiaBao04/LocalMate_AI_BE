namespace LocalMateAI.Infrastructure.Llm;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-3.5-flash-lite";
    public string ThinkingLevel { get; init; } = "minimal";

    // Thiếu khoá không phải lỗi cấu hình: tính năng AI chỉ tắt đi.
    public bool IsUsable => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}

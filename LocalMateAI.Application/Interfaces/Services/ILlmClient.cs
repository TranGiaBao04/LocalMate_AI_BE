using System.Text.Json.Nodes;

namespace LocalMateAI.Application.Interfaces.Services;

/// <param name="Schema">JSON Schema mà câu trả lời phải tuân theo.</param>
public sealed record LlmJsonRequest(
    string SystemInstruction,
    string Input,
    JsonNode Schema,
    int MaxOutputTokens,
    double Temperature = 0.4);

public sealed record LlmJsonResponse(string Json, int InputTokens, int OutputTokens);

/// <summary>Nhà cung cấp LLM không dùng được: chưa cấu hình, quá giờ, lỗi mạng hoặc trả lời không hoàn chỉnh.</summary>
public sealed class LlmUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface ILlmClient
{
    string Model { get; }

    /// <summary>false khi thiếu khoá API: tính năng AI tắt, không gọi ra ngoài.</summary>
    bool IsConfigured { get; }

    /// <summary>Trả văn bản JSON theo schema. Nơi gọi vẫn phải tự kiểm tra nội dung.</summary>
    Task<LlmJsonResponse> GenerateJsonAsync(LlmJsonRequest request, CancellationToken cancellationToken = default);
}

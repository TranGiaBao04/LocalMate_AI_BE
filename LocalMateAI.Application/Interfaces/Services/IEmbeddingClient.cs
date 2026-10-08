namespace LocalMateAI.Application.Interfaces.Services;

/// <summary>Văn bản của một địa điểm gửi đi để tính vector.</summary>
public sealed record EmbeddingDocument(string Title, string Text);

/// <summary>Nhà cung cấp embedding không dùng được: chưa cấu hình, quá giờ, lỗi mạng hoặc trả dữ liệu sai.</summary>
public sealed class EmbeddingUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IEmbeddingClient
{
    /// <summary>Tên model đang cấu hình, dùng để nhận ra vector do model khác sinh.</summary>
    string Model { get; }

    /// <summary>false khi thiếu khoá API hoặc cấu hình sai: tính năng tắt, không gọi ra ngoài.</summary>
    bool IsConfigured { get; }

    /// <summary>Trả vector đã chuẩn hoá độ dài 1, đúng thứ tự đầu vào.</summary>
    Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<EmbeddingDocument> documents,
        CancellationToken cancellationToken = default);

    Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);
}

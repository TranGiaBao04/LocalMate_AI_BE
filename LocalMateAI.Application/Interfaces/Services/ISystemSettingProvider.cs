namespace LocalMateAI.Application.Interfaces.Services;

/// <summary>Đọc giá trị thông số cho các service khác (có cache). Khoá phải khai báo trong SystemSettingDefinitions.</summary>
public interface ISystemSettingProvider
{
    /// <summary>Chỉ cho khoá Integer có MaxValue vừa kiểu int.</summary>
    Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Mọi khoá số (kể cả Integer vượt kiểu int, ví dụ tiền VNĐ).</summary>
    Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default);

    void Invalidate();
}

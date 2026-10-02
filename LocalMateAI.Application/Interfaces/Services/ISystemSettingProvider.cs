namespace LocalMateAI.Application.Interfaces.Services;

/// <summary>Đọc giá trị thông số cho các service khác (có cache). Khoá phải khai báo trong SystemSettingDefinitions.</summary>
public interface ISystemSettingProvider
{
    Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default);

    void Invalidate();
}

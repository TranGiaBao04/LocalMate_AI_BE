namespace LocalMateAI.Domain.Entities;

/// <summary>
/// Giá trị admin đã đặt cho một thông số vận hành. Khoá nào chưa có dòng thì dùng mặc định khai báo trong code
/// (Application/Settings/SystemSettingDefinitions). Không kế thừa BaseEntity vì khoá chính là Key.
/// </summary>
public sealed class SystemSetting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

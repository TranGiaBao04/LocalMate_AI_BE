namespace LocalMateAI.Infrastructure.Persistence;

/// <summary>
/// BE-81: tài khoản admin đầu tiên, đọc từ biến môi trường AdminSeed__Email / AdminSeed__Password / AdminSeed__FullName.
/// Email nên là hộp thư THẬT do nhóm quản lý; nếu không dùng hộp thư thật thì dùng tên miền dành riêng
/// (vd. admin@localmate.invalid) — tuyệt đối không dùng địa chỉ Gmail chưa ai tạo, vì người khác có thể tạo ra
/// rồi chiếm admin qua "quên mật khẩu".
/// </summary>
public sealed class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";
    public const string DefaultFullName = "LocalMate Admin";

    public string? Email { get; init; }

    public string? Password { get; init; }

    public string? FullName { get; init; }
}

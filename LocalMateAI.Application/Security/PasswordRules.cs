namespace LocalMateAI.Application.Security;

/// <summary>Luật mật khẩu dùng chung: đăng ký, đặt lại mật khẩu, seed admin (BE-81).</summary>
public static class PasswordRules
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 128;

    /// <summary>Thông báo lỗi nếu mật khẩu sai luật; null nếu hợp lệ.</summary>
    public static string? GetError(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Password is required.";
        }

        if (password.Length < MinimumLength)
        {
            return $"Password must contain at least {MinimumLength} characters.";
        }

        if (password.Length > MaximumLength)
        {
            return $"Password must not exceed {MaximumLength} characters.";
        }

        return string.IsNullOrWhiteSpace(password)
            ? "Password must contain at least one non-whitespace character."
            : null;
    }
}

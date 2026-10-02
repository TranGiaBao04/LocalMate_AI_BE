using System.Security.Cryptography;

namespace LocalMateAI.Application.Security;

/// <summary>
/// BE-57: Utility sinh ShareToken Base64URL 128-bit (mặc định) dùng cho chia sẻ lịch trình.
/// Dùng RandomNumberGenerator (CSPRNG) — KHÔNG dùng Random (không an toàn mật mã).
/// Token chỉ là entropy ngẫu nhiên, không mã hóa dữ liệu.
/// </summary>
public static class TokenGenerator
{
    public const int DefaultShareTokenBytes = 16; // 128-bit

    public static string GenerateShareToken(int byteLength = DefaultShareTokenBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);

        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
using System.Security.Cryptography;
using System.Text;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Security;

public sealed class HmacOtpCodeHasher(IOptions<OtpOptions> otpOptions) : IOtpCodeHasher
{
    public string Hash(string email, OtpPurpose purpose, string code) =>
        Convert.ToHexString(ComputeHash(email, purpose, code));

    public bool Verify(string email, OtpPurpose purpose, string code, string codeHash)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(codeHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ComputeHash(email, purpose, code), expected);
    }

    private byte[] ComputeHash(string email, OtpPurpose purpose, string code)
    {
        // Kiểm tra lại dù đã có OtpOptionsValidator: tuyệt đối không băm với khoá rỗng/quá ngắn.
        if (!OtpOptionsValidator.TryDecodeHashKey(otpOptions.Value.HashKey, out var key))
        {
            throw new InvalidOperationException(
                "Otp:HashKey must be valid Base64 and decode to at least 32 bytes.");
        }

        // Gắn email + mục đích vào dữ liệu băm để mã của email/mục đích này không dùng được cho cái khác.
        var payload = Encoding.UTF8.GetBytes($"{email}\n{purpose}\n{code}");
        return HMACSHA256.HashData(key, payload);
    }
}

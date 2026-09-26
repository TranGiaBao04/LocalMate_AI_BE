using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IOtpCodeHasher
{
    string Hash(string email, OtpPurpose purpose, string code);

    bool Verify(string email, OtpPurpose purpose, string code, string codeHash);
}

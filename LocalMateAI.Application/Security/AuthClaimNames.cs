namespace LocalMateAI.Application.Security;

/// <summary>Tên claim trong JWT do hệ thống phát (JwtAccessTokenService).</summary>
public static class AuthClaimNames
{
    public const string Subject = "sub";
    public const string SessionType = "session_type";
    public const string DemoSession = "demo";
}

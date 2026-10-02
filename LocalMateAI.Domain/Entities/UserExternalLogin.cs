namespace LocalMateAI.Domain.Entities;

public sealed class UserExternalLogin
{
    public Guid UserId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string ProviderSubject { get; set; } = string.Empty;
}

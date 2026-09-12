namespace LocalMateAI.Application.DTOs.Auth;

public sealed record VerifiedExternalIdentity(
    string ProviderSubject,
    string Email,
    string FullName);

namespace LocalMateAI.Application.DTOs.Email;

public sealed record OtpEmailModel(string Code, int ExpiresInMinutes);

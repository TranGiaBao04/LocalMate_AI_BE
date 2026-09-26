namespace LocalMateAI.Application.DTOs.Email;

// Tên template = tên file .liquid trong Infrastructure/Email/Templates (không kèm đuôi).
public static class EmailTemplateNames
{
    public const string OtpRegistration = "otp-registration";
    public const string OtpPasswordReset = "otp-password-reset";
}

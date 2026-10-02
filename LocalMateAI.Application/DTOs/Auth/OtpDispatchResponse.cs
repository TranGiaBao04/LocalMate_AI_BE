namespace LocalMateAI.Application.DTOs.Auth;

// Trả về sau khi yêu cầu gửi mã OTP, để FE hiển thị thời hạn mã và đếm ngược nút "Gửi lại".
public sealed record OtpDispatchResponse(
    string Email,
    int CodeExpiresInSeconds,
    int ResendAfterSeconds);

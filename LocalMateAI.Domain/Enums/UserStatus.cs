namespace LocalMateAI.Domain.Enums;

// BE-131: trạng thái tài khoản. Locked ⇒ bị chặn đăng nhập và mọi API cần đăng nhập (BE-83).
public enum UserStatus
{
    Active,
    Locked
}

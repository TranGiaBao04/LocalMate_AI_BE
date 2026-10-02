namespace LocalMateAI.Tests;

/// <summary>
/// Các test Postgres tạo/đếm tài khoản có quyền quản lý role (Admin) không được chạy song song với nhau,
/// vì test này tạo admin sẽ làm lệch số đếm "người quản lý role" của test kia.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AdminAccountsCollection
{
    public const string Name = "Postgres admin accounts";
}

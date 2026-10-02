using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.DTOs.Settings;

/// <summary>Một dòng đã lưu trong SystemSettings, kèm người sửa cuối (null nếu tài khoản không còn).</summary>
public sealed record SystemSettingRow(
    string Key,
    string Value,
    DateTime UpdatedAt,
    Guid? UpdatedByUserId,
    string? UpdatedByFullName,
    string? UpdatedByEmail);

public sealed record SystemSettingUpdatedBy(Guid UserId, string FullName, string Email);

public sealed record SystemSettingResponse(
    string Key,
    string Group,
    string Name,
    string Description,
    SystemSettingValueType ValueType,
    string? Unit,
    decimal Value,
    decimal DefaultValue,
    decimal MinValue,
    decimal MaxValue,
    bool IsDefault,
    DateTime? UpdatedAt,
    SystemSettingUpdatedBy? UpdatedBy);

public sealed record UpdateSystemSettingRequest(decimal? Value);

public enum AdminSystemSettingResultStatus
{
    Success,
    NotFound,
    InvalidValue
}

public sealed record AdminSystemSettingResult(
    AdminSystemSettingResultStatus Status,
    SystemSettingResponse? Response = null,
    string? Error = null);

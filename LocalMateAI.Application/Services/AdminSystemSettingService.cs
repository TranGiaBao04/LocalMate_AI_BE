using LocalMateAI.Application.DTOs.Settings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class AdminSystemSettingService(
    ISystemSettingRepository repository,
    ISystemSettingProvider provider,
    TimeProvider timeProvider) : IAdminSystemSettingService
{
    public async Task<IReadOnlyList<SystemSettingResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await repository.GetAllAsync(cancellationToken);

        return SystemSettingDefinitions.All
            .OrderBy(definition => definition.Group, StringComparer.Ordinal)
            .ThenBy(definition => definition.Key, StringComparer.Ordinal)
            .Select(definition => ToResponse(definition, rows))
            .ToList();
    }

    public async Task<AdminSystemSettingResult> UpdateAsync(string key, UpdateSystemSettingRequest request,
        Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var definition = SystemSettingDefinitions.Find(key);
        if (definition is null)
        {
            return new AdminSystemSettingResult(AdminSystemSettingResultStatus.NotFound);
        }

        if (request.Value is not { } value)
        {
            return new AdminSystemSettingResult(AdminSystemSettingResultStatus.InvalidValue, Error: "Giá trị là bắt buộc.");
        }

        if (SystemSettingDefinitions.Validate(definition, value) is { } error)
        {
            return new AdminSystemSettingResult(AdminSystemSettingResultStatus.InvalidValue, Error: error);
        }

        await repository.UpsertAsync(definition.Key, SystemSettingDefinitions.Format(definition, value),
            timeProvider.GetUtcNow().UtcDateTime, actorUserId, cancellationToken);
        provider.Invalidate();

        return await CurrentAsync(definition, cancellationToken);
    }

    public async Task<AdminSystemSettingResult> ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = SystemSettingDefinitions.Find(key);
        if (definition is null)
        {
            return new AdminSystemSettingResult(AdminSystemSettingResultStatus.NotFound);
        }

        await repository.DeleteAsync(definition.Key, cancellationToken);
        provider.Invalidate();

        return await CurrentAsync(definition, cancellationToken);
    }

    private async Task<AdminSystemSettingResult> CurrentAsync(SystemSettingDefinition definition,
        CancellationToken cancellationToken)
    {
        var rows = await repository.GetAllAsync(cancellationToken);
        return new AdminSystemSettingResult(AdminSystemSettingResultStatus.Success, ToResponse(definition, rows));
    }

    // Dòng có giá trị không hợp lệ được coi như chưa đặt (giống SystemSettingProvider).
    private static SystemSettingResponse ToResponse(SystemSettingDefinition definition,
        IReadOnlyList<SystemSettingRow> rows)
    {
        var row = rows.FirstOrDefault(item => string.Equals(item.Key, definition.Key, StringComparison.OrdinalIgnoreCase));
        var hasValue = row is not null && SystemSettingDefinitions.TryParse(definition, row.Value, out _);
        var value = hasValue && SystemSettingDefinitions.TryParse(definition, row!.Value, out var parsed)
            ? parsed
            : definition.DefaultValue;

        var updatedBy = hasValue && row!.UpdatedByUserId is { } userId && row.UpdatedByEmail is not null
            ? new SystemSettingUpdatedBy(userId, row.UpdatedByFullName ?? "", row.UpdatedByEmail)
            : null;

        return new SystemSettingResponse(definition.Key, definition.Group, definition.Name, definition.Description,
            definition.ValueType, definition.Unit, value, definition.DefaultValue, definition.MinValue,
            definition.MaxValue, IsDefault: !hasValue, hasValue ? row!.UpdatedAt : null, updatedBy);
    }
}

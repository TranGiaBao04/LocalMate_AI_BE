using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

/// <summary>Admin quản lý chương trình dùng thử khi đăng ký. Việc cấp dùng thử cho tài khoản mới nằm ở luồng đăng ký.</summary>
public sealed class TrialProgramService(
    ITrialProgramRepository repository,
    IAdminOperationExecutor operationExecutor,
    TimeProvider timeProvider) : ITrialProgramService
{
    public async Task<IReadOnlyList<TrialProgramResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var today = TrialProgramRules.Today(timeProvider);
        return (await repository.GetAllAsync(cancellationToken))
            .Select(program => TrialProgramRules.ToResponse(program, today))
            .ToList();
    }

    public async Task<TrialProgramResult> CreateAsync(SaveTrialProgramRequest request, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (draft, errors) = Validate(request);
        if (draft is null)
        {
            return new TrialProgramResult(TrialProgramResultStatus.ValidationFailed, ValidationErrors: errors);
        }

        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.TrialPrograms,
            async operationCancellationToken =>
            {
                if (await OverlapsAsync(draft, exceptId: null, operationCancellationToken))
                {
                    return new TrialProgramResult(TrialProgramResultStatus.Overlap);
                }

                var program = new TrialProgram
                {
                    Name = draft.Name,
                    StartsOn = draft.StartsOn,
                    EndsOn = draft.EndsOn,
                    TrialDays = draft.TrialDays,
                    MaxRecipients = draft.MaxRecipients,
                    IsEnabled = draft.IsEnabled,
                    CreatedByUserId = actorUserId
                };
                await repository.AddAsync(program, operationCancellationToken);
                return await SucceededAsync(program.Id, operationCancellationToken);
            },
            cancellationToken);
    }

    public async Task<TrialProgramResult> UpdateAsync(Guid id, SaveTrialProgramRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (draft, errors) = Validate(request);
        if (draft is null)
        {
            return new TrialProgramResult(TrialProgramResultStatus.ValidationFailed, ValidationErrors: errors);
        }

        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.TrialPrograms,
            async operationCancellationToken =>
            {
                if (await repository.GetAsync(id, operationCancellationToken) is null)
                {
                    return new TrialProgramResult(TrialProgramResultStatus.NotFound);
                }

                if (await OverlapsAsync(draft, id, operationCancellationToken))
                {
                    return new TrialProgramResult(TrialProgramResultStatus.Overlap);
                }

                await repository.UpdateAsync(id, draft, operationCancellationToken);
                return await SucceededAsync(id, operationCancellationToken);
            },
            cancellationToken);
    }

    // Chỉ chương trình đang bật mới phải tránh trùng ngày với chương trình đang bật khác.
    private async Task<bool> OverlapsAsync(TrialProgramDraft draft, Guid? exceptId, CancellationToken cancellationToken) =>
        draft.IsEnabled
        && await repository.HasEnabledOverlapAsync(draft.StartsOn, draft.EndsOn, exceptId, cancellationToken);

    private async Task<TrialProgramResult> SucceededAsync(Guid id, CancellationToken cancellationToken)
    {
        var saved = await repository.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Chương trình dùng thử {id} vừa ghi nhưng không đọc lại được.");
        return new TrialProgramResult(
            TrialProgramResultStatus.Success,
            TrialProgramRules.ToResponse(saved, TrialProgramRules.Today(timeProvider)));
    }

    private (TrialProgramDraft? Draft, IReadOnlyDictionary<string, string[]> Errors) Validate(SaveTrialProgramRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > TrialProgramRules.MaxNameLength)
        {
            errors[nameof(SaveTrialProgramRequest.Name)] =
                [$"Tên bắt buộc, tối đa {TrialProgramRules.MaxNameLength} ký tự."];
        }

        if (request.StartsOn is null)
        {
            errors[nameof(SaveTrialProgramRequest.StartsOn)] = ["Ngày bắt đầu là bắt buộc."];
        }

        if (request.EndsOn is null)
        {
            errors[nameof(SaveTrialProgramRequest.EndsOn)] = ["Ngày kết thúc là bắt buộc."];
        }
        else if (request.StartsOn is { } startsOn && request.EndsOn < startsOn)
        {
            errors[nameof(SaveTrialProgramRequest.EndsOn)] = ["Ngày kết thúc không được trước ngày bắt đầu."];
        }
        else if (request.IsEnabled == true && request.EndsOn < TrialProgramRules.Today(timeProvider))
        {
            errors[nameof(SaveTrialProgramRequest.EndsOn)] = ["Chương trình đang bật không được kết thúc trong quá khứ."];
        }

        if (request.TrialDays is not (>= TrialProgramRules.MinTrialDays and <= TrialProgramRules.MaxTrialDays))
        {
            errors[nameof(SaveTrialProgramRequest.TrialDays)] =
                [$"Số ngày dùng thử phải từ {TrialProgramRules.MinTrialDays} đến {TrialProgramRules.MaxTrialDays}."];
        }

        if (request.MaxRecipients is < TrialProgramRules.MinRecipients or > TrialProgramRules.MaxRecipients)
        {
            errors[nameof(SaveTrialProgramRequest.MaxRecipients)] =
                [$"Số người nhận tối đa phải từ {TrialProgramRules.MinRecipients} đến {TrialProgramRules.MaxRecipients}, hoặc bỏ trống."];
        }

        if (request.IsEnabled is null)
        {
            errors[nameof(SaveTrialProgramRequest.IsEnabled)] = ["Trạng thái bật/tắt là bắt buộc."];
        }

        return errors.Count > 0
            ? (null, errors)
            : (new TrialProgramDraft(name!, request.StartsOn!.Value, request.EndsOn!.Value, request.TrialDays!.Value,
                request.MaxRecipients, request.IsEnabled!.Value), errors);
    }
}

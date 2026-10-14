using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>Admin tặng một gói trả phí cho một user trong N ngày, không qua thanh toán.</summary>
public sealed class PlanGrantService(
    IPlanGrantRepository repository,
    ISubscriptionRepository subscriptionRepository,
    ITripGenerationQuotaExecutor userLock,
    INotificationRepository notificationRepository,
    TimeProvider timeProvider) : IPlanGrantService
{
    public async Task<PlanGrantResult> GrantAsync(
        Guid userId,
        GrantPlanRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = request.Reason?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (request.Days is < PlanGrantRules.MinDays or > PlanGrantRules.MaxDays)
        {
            errors[nameof(GrantPlanRequest.Days)] =
                [$"Số ngày phải từ {PlanGrantRules.MinDays} đến {PlanGrantRules.MaxDays}."];
        }

        if (string.IsNullOrEmpty(reason) || reason.Length > PlanGrantRules.MaxReasonLength)
        {
            errors[nameof(GrantPlanRequest.Reason)] =
                [$"Lý do bắt buộc, tối đa {PlanGrantRules.MaxReasonLength} ký tự."];
        }

        if (errors.Count > 0)
        {
            return new PlanGrantResult(PlanGrantResultStatus.InvalidRequest, ValidationErrors: errors);
        }

        // Cùng khoá hàng Users với tạo lịch và lượt AI: hai lệnh tặng đồng thời cho một user chạy lần lượt,
        // lệnh sau thấy gói tặng của lệnh trước và xếp nối sau nó.
        var execution = await userLock.ExecuteForUserAsync(
            userId,
            async operationCancellationToken =>
            {
                if (await repository.GetUserStatusAsync(userId, operationCancellationToken) != UserStatus.Active)
                {
                    return new PlanGrantResult(PlanGrantResultStatus.UserLocked);
                }

                var plan = await subscriptionRepository.GetPlanAsync(request.PlanId, operationCancellationToken);
                var version = plan is { IsActive: true, CurrentVersionId: { } versionId } && plan.Code != PlanIdentity.Free
                    ? await subscriptionRepository.GetVersionAsync(versionId, operationCancellationToken)
                    : null;
                if (plan is null || version is null || version.PlanId != plan.Id)
                {
                    return new PlanGrantResult(
                        PlanGrantResultStatus.InvalidRequest,
                        ValidationErrors: new Dictionary<string, string[]>
                        {
                            [nameof(GrantPlanRequest.PlanId)] = ["Gói phải là gói trả phí đang bật và đã có phiên bản."]
                        });
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var priorityByPlanId = (await subscriptionRepository.GetPlansAsync(operationCancellationToken))
                    .ToDictionary(item => item.Id, item => item.EntitlementPriority);
                var startsAt = PlanGrantRules.StartsAt(
                    nowUtc,
                    plan.EntitlementPriority,
                    await subscriptionRepository.GetPeriodsAsync(userId, operationCancellationToken),
                    await subscriptionRepository.GetGrantsAsync(userId, operationCancellationToken),
                    priorityByPlanId);
                var endsAt = startsAt.AddDays(request.Days);
                if (endsAt > nowUtc.AddDays(PlanGrantRules.MaxHorizonDays))
                {
                    return new PlanGrantResult(PlanGrantResultStatus.ExceedsMaxHorizon);
                }

                var grant = new PlanGrant
                {
                    UserId = userId,
                    PlanId = plan.Id,
                    PlanVersionId = version.Id,
                    StartsAt = startsAt,
                    EndsAt = endsAt,
                    Days = request.Days,
                    Source = PlanGrantSource.Admin,
                    Reason = reason,
                    GrantedByUserId = actorUserId
                };
                await repository.AddAsync(grant, operationCancellationToken);
                await notificationRepository.EnqueueAsync(
                    NotificationBuilder.PlanGranted(userId, grant.Id, plan.Name, startsAt, endsAt, nowUtc),
                    nowUtc,
                    operationCancellationToken);

                return new PlanGrantResult(
                    PlanGrantResultStatus.Success,
                    await repository.GetAsync(grant.Id, operationCancellationToken)
                        ?? throw new InvalidOperationException($"Gói tặng {grant.Id} vừa ghi nhưng không đọc lại được."));
            },
            cancellationToken);

        return execution.PersistedUserExists
            ? execution.Result!
            : new PlanGrantResult(PlanGrantResultStatus.UserNotFound);
    }

    public async Task<IReadOnlyList<PlanGrantResponse>?> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await repository.GetUserStatusAsync(userId, cancellationToken) is null
            ? null
            : await repository.GetByUserAsync(userId, cancellationToken);
}

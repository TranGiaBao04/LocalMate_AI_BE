using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

/// <summary>Tự cấp gói Trip Pass dùng thử cho tài khoản vừa tạo khi đang có chương trình dùng thử chạy.</summary>
public sealed class SignupTrialService(
    ITrialProgramRepository trialProgramRepository,
    IPlanGrantRepository planGrantRepository,
    ISubscriptionRepository subscriptionRepository,
    IAdminOperationExecutor operationExecutor,
    INotificationRepository notificationRepository,
    TimeProvider timeProvider,
    ILogger<SignupTrialService> logger) : ISignupTrialService
{
    public async Task<SignupTrialOutcome> GrantAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var today = TrialProgramRules.Today(timeProvider);

        // Phần lớn thời gian không có chương trình nào: thoát sớm, không mở transaction hay giữ khoá.
        if (await trialProgramRepository.GetEnabledForDateAsync(today, cancellationToken) is null)
        {
            return SignupTrialOutcome.NoProgram;
        }

        // Cùng khoá với lúc admin tạo/sửa chương trình: hai lượt đăng ký không cùng lấy suất cuối,
        // và admin không đổi số suất đúng lúc đang đếm.
        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.TrialPrograms,
            async operationCancellationToken =>
            {
                var program = await trialProgramRepository.GetEnabledForDateAsync(today, operationCancellationToken);
                if (program is null)
                {
                    return SignupTrialOutcome.NoProgram;
                }

                if (TrialProgramRules.Status(program, today) != TrialProgramStatus.Running)
                {
                    return SignupTrialOutcome.Full;
                }

                var plan = await subscriptionRepository.GetPlanByCodeAsync(PlanIdentity.TripPass, operationCancellationToken);
                var version = plan is { IsActive: true, CurrentVersionId: { } versionId }
                    ? await subscriptionRepository.GetVersionAsync(versionId, operationCancellationToken)
                    : null;
                if (plan is null || version is null || version.PlanId != plan.Id)
                {
                    logger.LogWarning(
                        "Signup trial skipped for user {UserId}: plan {PlanCode} is disabled or has no current version",
                        userId, PlanIdentity.TripPass);
                    return SignupTrialOutcome.PlanUnavailable;
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var grant = new PlanGrant
                {
                    UserId = userId,
                    PlanId = plan.Id,
                    PlanVersionId = version.Id,
                    StartsAt = nowUtc,
                    EndsAt = nowUtc.AddDays(program.TrialDays),
                    Days = program.TrialDays,
                    Source = PlanGrantSource.SignupTrial,
                    TrialProgramId = program.Id,
                    DeduplicationKey = $"signup-trial:{userId}"
                };
                if (!await planGrantRepository.TryAddOnceAsync(grant, nowUtc, operationCancellationToken))
                {
                    return SignupTrialOutcome.AlreadyGranted;
                }

                await notificationRepository.EnqueueAsync(
                    NotificationBuilder.SignupTrialGranted(userId, grant.Id, plan.Name, grant.EndsAt),
                    nowUtc,
                    operationCancellationToken);

                return SignupTrialOutcome.Granted;
            },
            cancellationToken);
    }
}

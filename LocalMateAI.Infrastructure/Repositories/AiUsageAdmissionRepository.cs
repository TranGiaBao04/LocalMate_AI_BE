using System.Data;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Domain.Services;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

// All quota writers must use the same user lock; network work is never part of this repository.
public sealed class AiUsageAdmissionRepository(AppDbContext context, ISystemSettingProvider settings,
    TimeProvider clock) : IAiUsageAdmissionRepository
{
    public static readonly TimeSpan ReservedLease = TimeSpan.FromSeconds(30);

    public async Task<AiUsageAdmissionResult> AdmitAsync(Guid attemptId, Guid userId, LlmCallKind kind,
        Guid? tripId = null, CancellationToken cancellationToken = default)
    {
        if (attemptId == Guid.Empty || userId == Guid.Empty || !Enum.IsDefined(kind)
            || (kind == LlmCallKind.Explain ? tripId is null || tripId == Guid.Empty : tripId is not null))
            throw new ArgumentException("Admission requires a valid attempt, owner and kind/Trip binding.");

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockUserAsync(userId, cancellationToken);
        // Free's mutable current pointer is protected from concurrent publication before resolution.
        await context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "SubscriptionPlans" WHERE "Code"={PlanIdentity.Free} FOR SHARE""")
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        if (await context.AiUsageAdmissions.AnyAsync(a => a.Id == attemptId, cancellationToken))
            throw new InvalidOperationException("An admission attempt cannot be reused.");
        if (tripId is { } id && !await context.Trips.AnyAsync(t => t.Id == id && t.UserId == userId
                && t.DeletedAt == null && t.Status == TripStatus.Draft, cancellationToken))
            throw new InvalidOperationException("Explain admission requires an owned draft Trip.");

        var effective = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(context), userId, now, cancellationToken);
        int? explainLimit = null;
        if (kind == LlmCallKind.Explain)
        {
            explainLimit = await AiEntitlementLimits.ExplainPerTripAsync(effective.Version, settings, cancellationToken);
            if (await CountExplainAsync(tripId!.Value, cancellationToken) >= explainLimit)
                return new(new(AiUsageStatus.TripLimitReached));
        }
        var dailyLimit = await AiEntitlementLimits.DailyAsync(effective.Version, settings, cancellationToken);
        var date = DateOnly.FromDateTime(now.AddHours(7));
        if (await CountDailyAsync(userId, date, cancellationToken) >= dailyLimit)
            return new(new(AiUsageStatus.DailyLimitReached, VietnamTime.StartOfDayUtc(now).AddDays(1)));

        var admission = new AiUsageAdmission
        {
            Id = attemptId, UserId = userId, TripId = tripId, TripIdSnapshot = tripId, Kind = kind,
            VietnamUsageDate = date, State = AiUsageAdmissionState.Reserved, AdmittedAt = now,
            ReservedUntil = now.Add(ReservedLease), ResolvedPlanVersionId = effective.Version.Id,
            AdmittedDailyLimit = dailyLimit, AdmittedExplainLimit = explainLimit,
            FencingToken = Guid.NewGuid(), FencingGeneration = 1
        };
        context.AiUsageAdmissions.Add(admission);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(new(AiUsageStatus.Allowed), admission);
    }

    public async Task<bool> ReleaseReservedAsync(Guid userId, Guid attemptId, Guid fencingToken,
        long fencingGeneration, bool expiredOnly = false, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockUserAsync(userId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var nextToken = Guid.NewGuid();
        var changed = await context.AiUsageAdmissions.Where(a => a.Id == attemptId && a.UserId == userId
                && a.FencingToken == fencingToken && a.FencingGeneration == fencingGeneration
                && a.State == AiUsageAdmissionState.Reserved && a.DispatchAuthorizedAt == null
                && a.AdmittedAt <= now && (!expiredOnly || a.ReservedUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AiUsageAdmissionState.Released)
                .SetProperty(a => a.CompletedAt, now).SetProperty(a => a.FencingToken, nextToken)
                .SetProperty(a => a.FencingGeneration, a => a.FencingGeneration + 1), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    public async Task<bool> AuthorizeDispatchAsync(Guid userId, Guid attemptId, Guid fencingToken,
        long fencingGeneration, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockUserAsync(userId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await context.AiUsageAdmissions.Where(a => a.Id == attemptId && a.UserId == userId
                && a.FencingToken == fencingToken && a.FencingGeneration == fencingGeneration
                && a.State == AiUsageAdmissionState.Reserved && a.DispatchAuthorizedAt == null
                && a.AdmittedAt <= now && a.ReservedUntil > now)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AiUsageAdmissionState.DispatchAuthorized)
                .SetProperty(a => a.DispatchAuthorizedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1;
    }

    private async Task LockUserAsync(Guid userId, CancellationToken ct)
    {
        if (await context.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={userId} FOR UPDATE""")
                .SingleOrDefaultAsync(ct) != 1)
            throw new InvalidOperationException("Admission owner is not persisted.");
    }

    private async Task<int> CountDailyAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7);
        var end = start.AddDays(1);
        var legacy = await context.LlmCallLogs.CountAsync(l => l.UserId == userId && l.CreatedAt >= start && l.CreatedAt < end
            && !context.AiUsageAdmissions.Any(a => a.LlmCallLogId == l.Id), ct);
        var ledger = await context.AiUsageAdmissions.CountAsync(a => a.UserId == userId && a.VietnamUsageDate == date
            && a.State != AiUsageAdmissionState.Released, ct);
        return legacy + ledger;
    }

    private async Task<int> CountExplainAsync(Guid tripId, CancellationToken ct)
    {
        var legacy = await context.LlmCallLogs.CountAsync(l => l.TripId == tripId && l.Kind == LlmCallKind.Explain
            && l.Outcome == LlmCallOutcome.Succeeded && !context.AiUsageAdmissions.Any(a => a.LlmCallLogId == l.Id), ct);
        var ledger = await context.AiUsageAdmissions.CountAsync(a => a.TripIdSnapshot == tripId && a.Kind == LlmCallKind.Explain
            && (a.State == AiUsageAdmissionState.Reserved || a.State == AiUsageAdmissionState.DispatchAuthorized
                || (a.State == AiUsageAdmissionState.Completed && a.Outcome == LlmCallOutcome.Succeeded)), ct);
        return legacy + ledger;
    }
}

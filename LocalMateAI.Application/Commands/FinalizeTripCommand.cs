using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Commands;

/// <summary>
/// Business Command (BE-56): chuyển trạng thái trip Draft → Finalized.
/// Chứa toàn bộ logic nghiệp vụ (ownership, idempotency, transition guard);
/// TripService chỉ delegate — một nguồn sự thật cho luồng finalize.
/// </summary>
public sealed class FinalizeTripCommand(
    ITripRepository tripRepository,
    ISubscriptionRepository subscriptionRepository,
    ITripFinalizeQuotaExecutor quotaExecutor,
    TimeProvider timeProvider) : IFinalizeTripCommand
{
    public async Task<FinalizeTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return FinalizeTripResult.InvalidTrip();
        }

        var execution = await quotaExecutor.ExecuteForUserAsync(
            userId,
            async transactionCancellationToken =>
            {
                var trip = await tripRepository.GetByIdAsync(tripId, transactionCancellationToken);
                if (trip is null || trip.UserId != userId)
                {
                    return FinalizeTripResult.MissingTrip();
                }

                if (trip.Status == TripStatus.Finalized)
                {
                    return FinalizeTripResult.AlreadyFinalized();
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var subscriptions = await subscriptionRepository.GetByUserIdAsync(
                    userId,
                    transactionCancellationToken);
                var effective = SubscriptionCatalog.ResolveEffectivePaid(subscriptions, nowUtc);
                var plan = SubscriptionCatalog.Get(effective?.PlanCode ?? PlanCode.Free);

                if (plan.SavedTripLimit is { } limit)
                {
                    var used = await tripRepository.CountFinalizedByUserAsync(
                        userId,
                        transactionCancellationToken);
                    if (used >= limit)
                    {
                        return FinalizeTripResult.QuotaExceeded(used, limit);
                    }
                }

                var finalized = await tripRepository.FinalizeTripAsync(
                    tripId,
                    userId,
                    transactionCancellationToken);

                return finalized
                    ? FinalizeTripResult.Succeeded(
                        new FinalizeTripResponse(tripId, TripStatus.Finalized.ToString()))
                    : FinalizeTripResult.MissingTrip();
            },
            cancellationToken);

        return execution.PersistedUserExists && execution.Result is not null
            ? execution.Result
            : FinalizeTripResult.MissingTrip();
    }
}

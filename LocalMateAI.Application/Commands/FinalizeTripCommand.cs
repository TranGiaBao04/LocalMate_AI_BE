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
    IUserRepository userRepository,
    IEmailOutboxRepository emailOutboxRepository,
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
                var effective = await EffectiveSubscriptionResolver.ResolveAsync(
                    subscriptionRepository, userId, nowUtc, transactionCancellationToken);
                var plan = effective.Version;

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
                if (!finalized)
                {
                    return FinalizeTripResult.MissingTrip();
                }

                await EnqueueItineraryEmailAsync(userId, tripId, nowUtc, transactionCancellationToken);
                return FinalizeTripResult.Succeeded(
                    new FinalizeTripResponse(tripId, TripStatus.Finalized.ToString()));
            },
            cancellationToken);

        return execution.PersistedUserExists && execution.Result is not null
            ? execution.Result
            : FinalizeTripResult.MissingTrip();
    }

    // Xếp mail lịch trình vào outbox trong CÙNG transaction chốt trip: chốt thành công thì chắc chắn có mail chờ gửi.
    private async Task EnqueueItineraryEmailAsync(
        Guid userId,
        Guid tripId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        var trip = await tripRepository.GetOwnedDetailAsync(tripId, userId, cancellationToken);
        // Không xảy ra thực tế: user đang bị khoá và trip vừa được chốt trong cùng transaction.
        if (user is null || trip is null)
        {
            return;
        }

        var entry = TripItineraryEmailBuilder.Build(user, trip, TripDetailService.ToResponse(trip));
        await emailOutboxRepository.EnqueueAsync(entry, nowUtc, cancellationToken);
    }
}

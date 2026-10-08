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
    TimeProvider timeProvider,
    ISingleItineraryRepository? singleRepository = null,
    INotificationRepository? notificationRepository = null) : IFinalizeTripCommand
{
    public Task<FinalizeTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default) => ExecuteAsync(userId, tripId, new FinalizeTripRequest(), cancellationToken);

    public async Task<FinalizeTripResult> ExecuteAsync(Guid userId, Guid tripId, FinalizeTripRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.IsValid) return new(FinalizeTripResultStatus.InvalidFunding);
        if (tripId == Guid.Empty)
        {
            return FinalizeTripResult.InvalidTrip();
        }

        var execution = await quotaExecutor.ExecuteForUserAsync(
            userId,
            async transactionCancellationToken =>
            {
                var trip = await tripRepository.LockOwnedForFinalizeAsync(tripId, userId, transactionCancellationToken);
                if (trip is null || trip.UserId != userId)
                {
                    return FinalizeTripResult.MissingTrip();
                }

                LocalMateAI.Domain.Entities.SingleItineraryEntitlement? entitlement = null;
                if (request.FundingSource == "SingleEntitlement")
                {
                    entitlement = singleRepository is null ? null :
                        await singleRepository.LockOwnedAsync(request.EntitlementId!.Value, userId, transactionCancellationToken);
                    if (entitlement is null) return new FinalizeTripResult(FinalizeTripResultStatus.EntitlementNotFound);
                    if (entitlement.ConsumedAt is not null)
                    {
                        if (entitlement.ConsumedTripId == tripId && trip.Status == TripStatus.Finalized)
                            return FinalizeTripResult.Succeeded(new(tripId, "Finalized")
                            { FundingSource = "SingleEntitlement", EntitlementId = entitlement.Id, ConsumedAt = entitlement.ConsumedAt });
                        return new FinalizeTripResult(FinalizeTripResultStatus.EntitlementConsumed);
                    }
                }
                if (trip.Status == TripStatus.Finalized)
                {
                    return FinalizeTripResult.AlreadyFinalized();
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var limit = entitlement is null
                    ? (await EffectiveSubscriptionResolver.ResolveAsync(subscriptionRepository, userId, nowUtc,
                        transactionCancellationToken)).Version.SavedTripLimit : null;
                if (limit is { } finiteLimit)
                {
                    var used = await tripRepository.CountNormalFinalizedByUserAsync(
                        userId,
                        transactionCancellationToken);
                    if (used >= finiteLimit)
                    {
                        return FinalizeTripResult.QuotaExceeded(used, finiteLimit);
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

                if (entitlement is not null)
                {
                    entitlement.Consume(tripId, nowUtc);
                    await singleRepository!.SaveChangesAsync(transactionCancellationToken);
                }
                await EnqueueFinalizeMessagesAsync(userId, tripId, nowUtc, transactionCancellationToken);
                return FinalizeTripResult.Succeeded(
                    new FinalizeTripResponse(tripId, TripStatus.Finalized.ToString())
                    { FundingSource = request.FundingSource, EntitlementId = entitlement?.Id, ConsumedAt = entitlement?.ConsumedAt });
            },
            cancellationToken);

        return execution.PersistedUserExists && execution.Result is not null
            ? execution.Result
            : FinalizeTripResult.MissingTrip();
    }

    // Xếp mail lịch trình và thông báo hộp thư trong CÙNG transaction chốt trip:
    // chốt thành công thì chắc chắn có cả hai.
    private async Task EnqueueFinalizeMessagesAsync(
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

        if (notificationRepository is not null)
        {
            await notificationRepository.EnqueueAsync(
                NotificationBuilder.TripFinalized(userId, tripId, trip.PlannedStartAt), nowUtc, cancellationToken);
        }
    }
}

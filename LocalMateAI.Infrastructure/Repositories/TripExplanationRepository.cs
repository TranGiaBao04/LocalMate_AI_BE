using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class TripExplanationRepository(AppDbContext context) : ITripExplanationRepository
{
    public async Task<TripExplanationReadModel?> GetOwnedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var trip = await context.Trips
            .AsNoTracking()
            .Where(candidate => candidate.Id == tripId
                                && candidate.UserId == userId
                                && candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Status,
                candidate.DurationHours,
                candidate.Note,
                InterestTagNames = candidate.Tags
                    .Where(tripTag => tripTag.Tag.IsActive)
                    .Select(tripTag => tripTag.Tag.Name)
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (trip is null)
        {
            return null;
        }

        var stops = await context.ItineraryItems
            .AsNoTracking()
            .Where(item => item.TripId == tripId)
            .OrderBy(item => item.OrderIndex)
            .Select(item => new TripExplanationStopReadModel(
                item.Id,
                item.PlaceId,
                item.Place.Name,
                item.Place.Category,
                item.Place.Description,
                item.Place.Tags
                    .Where(placeTag => placeTag.Tag.IsActive)
                    .Select(placeTag => placeTag.Tag.Name)
                    .ToList(),
                item.ScheduledTime,
                item.EstimatedDurationMinutes))
            .ToListAsync(cancellationToken);

        return new TripExplanationReadModel(
            trip.Id,
            trip.Status,
            trip.DurationHours,
            trip.Note,
            trip.InterestTagNames,
            stops);
    }

    public async Task<IReadOnlyList<TripExplanationUpdate>?> ApplyAsync(
        Guid tripId,
        Guid userId,
        IReadOnlyList<TripExplanationUpdate> updates,
        DateTime explainedAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var marked = await context.Trips
            .Where(trip => trip.Id == tripId
                           && trip.UserId == userId
                           && trip.DeletedAt == null
                           && trip.Status == TripStatus.Draft)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(trip => trip.AiExplainedAt, explainedAt)
                    .SetProperty(trip => trip.UpdatedAt, explainedAt),
                cancellationToken);

        if (marked == 0)
        {
            return null;
        }

        var written = new List<TripExplanationUpdate>();
        foreach (var update in updates)
        {
            // Chặng đã bị thay địa điểm hoặc bị xoá trong lúc chờ AI thì bỏ qua: lý do không còn đúng.
            var rows = await context.ItineraryItems
                .Where(item => item.Id == update.ItemId
                               && item.TripId == tripId
                               && item.PlaceId == update.PlaceId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.Reasoning, update.Reasoning)
                        .SetProperty(item => item.UpdatedAt, explainedAt),
                    cancellationToken);

            if (rows == 1)
            {
                written.Add(update);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return written;
    }
}

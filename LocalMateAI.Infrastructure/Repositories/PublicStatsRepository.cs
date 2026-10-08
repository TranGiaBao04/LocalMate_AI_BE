using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PublicStatsRepository(AppDbContext context) : IPublicStatsRepository
{
    // Cùng luật đếm với AdminDashboardRepository.GetDailyFinalizedTripsAsync: không lọc DeletedAt.
    public Task<long> CountFinalizedTripsAsync(CancellationToken cancellationToken = default) =>
        context.Trips.AsNoTracking()
            .LongCountAsync(trip => trip.Status == TripStatus.Finalized, cancellationToken);
}

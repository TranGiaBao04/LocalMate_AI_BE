using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class CuratedItineraryRepository(AppDbContext dbContext) : ICuratedItineraryRepository
{
    public async Task<IReadOnlyList<CuratedItineraryResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var itineraries = await dbContext.CuratedItineraries
            .Include(itinerary => itinerary.Items)
                .ThenInclude(item => item.Place)
            .OrderBy(itinerary => itinerary.Title)
            .ToListAsync(cancellationToken);

        return itineraries
            .Select(itinerary => new CuratedItineraryResponse(
                itinerary.Id,
                itinerary.Title,
                itinerary.Description,
                itinerary.CoverImageUrl,
                itinerary.EstimatedDurationMinutes,
                itinerary.EstimatedCostMin,
                itinerary.EstimatedCostMax,
                itinerary.Items
                    .OrderBy(item => item.OrderIndex)
                    .Select(item => new CuratedItineraryItemResponse(item.PlaceId, item.Place.Name, item.OrderIndex))
                    .ToList()))
            .ToList();
    }
}

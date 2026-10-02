using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class CuratedItineraryRepository(AppDbContext dbContext) : ICuratedItineraryRepository
{
    public async Task<IReadOnlyList<CuratedItineraryResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        // Chỉ hiện địa điểm Active (địa điểm bị xoá mềm luôn Inactive); lịch trình không còn địa điểm nào thì ẩn.
        var itineraries = await dbContext.CuratedItineraries
            .Include(itinerary => itinerary.Items.Where(item => item.Place.Status == PlaceStatus.Active))
                .ThenInclude(item => item.Place)
            .OrderBy(itinerary => itinerary.Title)
            .ToListAsync(cancellationToken);

        return itineraries
            .Where(itinerary => itinerary.Items.Count > 0)
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

    public async Task<CuratedItineraryForApplyReadModel?> GetForApplyAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var itinerary = await dbContext.CuratedItineraries
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Title,
                candidate.EstimatedDurationMinutes,
                candidate.EstimatedCostMin,
                candidate.EstimatedCostMax
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (itinerary is null)
        {
            return null;
        }

        var places = await dbContext.CuratedItineraryItems
            .AsNoTracking()
            .Where(item => item.CuratedItineraryId == id && item.Place.Status == PlaceStatus.Active)
            .OrderBy(item => item.OrderIndex)
            .Select(item => new CuratedPlaceForApplyReadModel(
                item.PlaceId,
                item.OrderIndex,
                item.Place.Location.Y,
                item.Place.Location.X,
                item.Place.EstimatedCostMax,
                item.Place.Category))
            .ToListAsync(cancellationToken);

        return new CuratedItineraryForApplyReadModel(
            itinerary.Id,
            itinerary.Title,
            itinerary.EstimatedDurationMinutes,
            itinerary.EstimatedCostMin,
            itinerary.EstimatedCostMax,
            places);
    }
}

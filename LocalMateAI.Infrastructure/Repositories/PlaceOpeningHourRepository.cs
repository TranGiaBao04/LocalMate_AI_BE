using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlaceOpeningHourRepository(AppDbContext dbContext) : IPlaceOpeningHourRepository
{
    public async Task<IReadOnlyList<PlaceOpeningHourDto>?> GetByPlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Places.AnyAsync(
            place => place.Id == placeId && place.DeletedAt == null,
            cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await dbContext.PlaceOpeningHours
            .AsNoTracking()
            .Where(hour => hour.PlaceId == placeId)
            .Select(hour => new PlaceOpeningHourDto(hour.DayOfWeek, hour.IsClosed, hour.OpenTime, hour.CloseTime))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ReplaceAsync(
        Guid placeId,
        IReadOnlyList<PlaceOpeningHourDto> days,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Lệnh đầu cập nhật chính dòng địa điểm ⇒ Postgres giữ khoá dòng tới khi commit,
        // nên 2 admin cùng lưu sẽ chạy lần lượt (không đụng unique (PlaceId, DayOfWeek)).
        var touched = await dbContext.Places
            .Where(place => place.Id == placeId && place.DeletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(place => place.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
        if (touched == 0)
        {
            return false;
        }

        await dbContext.PlaceOpeningHours
            .Where(hour => hour.PlaceId == placeId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.PlaceOpeningHours.AddRange(days.Select(day => new PlaceOpeningHour
        {
            PlaceId = placeId,
            DayOfWeek = day.DayOfWeek,
            IsClosed = day.IsClosed,
            OpenTime = day.OpenTime,
            CloseTime = day.CloseTime
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}

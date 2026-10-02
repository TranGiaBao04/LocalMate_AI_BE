using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class SingleItineraryRepository(AppDbContext db) : ISingleItineraryRepository
{
    public Task<SingleItineraryProductVersion?> GetCurrentVersionAsync(DateTime nowUtc, CancellationToken ct = default) =>
        db.SingleItineraryProductVersions.AsNoTracking().Where(v => v.PublishedAt <= nowUtc)
            .OrderByDescending(v => v.VersionNumber).FirstOrDefaultAsync(ct);
    public Task<SingleItineraryProductVersion?> GetVersionAsync(Guid id, CancellationToken ct = default) =>
        db.SingleItineraryProductVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct);
    public Task<PaymentOrder?> GetAttemptAsync(Guid userId, Guid attemptId, CancellationToken ct = default) =>
        db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.UserId == userId
            && o.ProductKind == PaymentProductKind.SingleItinerary && o.CheckoutAttemptId == attemptId, ct);
    public Task<SingleItineraryEntitlement?> GetForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        db.SingleItineraryEntitlements.AsNoTracking().SingleOrDefaultAsync(e => e.SourcePaymentOrderId == orderId, ct);
    public async Task<SingleItineraryEntitlement?> LockOwnedAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var row = await db.SingleItineraryEntitlements.FromSqlInterpolated(
            $"""SELECT * FROM "SingleItineraryEntitlements" WHERE "Id" = {id} AND "UserId" = {userId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
        if (row is not null) await db.Entry(row).ReloadAsync(ct);
        return row;
    }
    public async Task<IReadOnlyList<SingleItineraryEntitlement>> GetOwnedAsync(Guid userId, CancellationToken ct = default) =>
        await db.SingleItineraryEntitlements.AsNoTracking().Where(e => e.UserId == userId)
            .OrderBy(e => e.GrantedAt).ThenBy(e => e.Id).ToListAsync(ct);
    public async Task AddAsync(SingleItineraryEntitlement entitlement, CancellationToken ct = default)
    {
        db.SingleItineraryEntitlements.Add(entitlement);
        await db.SaveChangesAsync(ct);
    }
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

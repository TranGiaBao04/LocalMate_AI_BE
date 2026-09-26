using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentOrderRepository(AppDbContext dbContext) : IPaymentOrderRepository
{
    public Task<PaymentOrder?> GetPendingAsync(
        Guid userId,
        PlanCode planCode,
        PaymentOrderType type,
        CancellationToken cancellationToken = default) =>
        dbContext.PaymentOrders
            .Where(order => order.UserId == userId
                            && order.PlanCode == planCode
                            && order.Type == type
                            && order.Status == PaymentOrderStatus.Pending)
            .OrderByDescending(order => order.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PaymentOrder?> GetOwnedByIdAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        dbContext.PaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                order => order.Id == orderId && order.UserId == userId,
                cancellationToken);

    public async Task AddAsync(
        PaymentOrder order,
        CancellationToken cancellationToken = default)
    {
        dbContext.PaymentOrders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}

using System.Linq.Expressions;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PlanGrantRepository(AppDbContext dbContext) : IPlanGrantRepository
{
    public async Task<UserStatus?> GetUserStatusAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (UserStatus?)user.Status)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(PlanGrant grant, CancellationToken cancellationToken = default)
    {
        dbContext.PlanGrants.Add(grant);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlanGrantResponse?> GetAsync(Guid grantId, CancellationToken cancellationToken = default) =>
        (await QueryAsync(grant => grant.Id == grantId, cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<PlanGrantResponse>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        QueryAsync(grant => grant.UserId == userId, cancellationToken);

    private async Task<IReadOnlyList<PlanGrantResponse>> QueryAsync(
        Expression<Func<PlanGrant, bool>> filter,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from grant in dbContext.PlanGrants.AsNoTracking().Where(filter)
                join plan in dbContext.SubscriptionPlans.AsNoTracking() on grant.PlanId equals plan.Id
                join granter in dbContext.Users.AsNoTracking()
                    on grant.GrantedByUserId equals (Guid?)granter.Id into granters
                from granter in granters.DefaultIfEmpty()
                orderby grant.CreatedAt descending, grant.Id
                select new
                {
                    Grant = grant,
                    PlanCode = plan.Code,
                    PlanName = plan.Name,
                    GranterName = granter == null ? null : granter.FullName
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new PlanGrantResponse(
                row.Grant.Id,
                row.Grant.UserId,
                new PlanGrantPlanResponse(PlanIdentity.PublicCode(row.PlanCode), row.PlanName),
                row.Grant.Days,
                row.Grant.StartsAt,
                row.Grant.EndsAt,
                row.Grant.Source.ToString(),
                row.Grant.Reason,
                row.Grant.GrantedByUserId is { } granterId && row.GranterName is not null
                    ? new PlanGrantActorResponse(granterId, row.GranterName)
                    : null,
                row.Grant.CreatedAt))
            .ToList();
    }
}

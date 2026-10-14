using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class PlanGrantPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Grant_IsReadBackPerUser_AndResolvedAsTrialOnRealSchema()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await AddUserAsync(c, "trial@test.local");
        var other = await AddUserAsync(c, "other@test.local");
        var plan = await c.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Code == "TRIP_PASS");
        c.PlanGrants.Add(Grant(user.Id, plan.Id, plan.CurrentVersionId!.Value));
        await c.SaveChangesAsync();
        var repository = new SubscriptionRepository(c);

        Assert.Single(await repository.GetGrantsAsync(user.Id));
        Assert.Empty(await repository.GetGrantsAsync(other.Id));
        var effective = await EffectiveSubscriptionResolver.ResolveAsync(repository, user.Id, Now.AddDays(1));
        Assert.Equal("TRIP_PASS", effective.Plan.Code);
        Assert.True(effective.IsTrial);
        Assert.Equal(Now.AddDays(7), effective.EffectiveUntil);
        Assert.Equal("FREE", (await EffectiveSubscriptionResolver.ResolveAsync(repository, user.Id, Now.AddDays(7))).Plan.Code);
        Assert.Equal("FREE", (await EffectiveSubscriptionResolver.ResolveAsync(repository, other.Id, Now.AddDays(1))).Plan.Code);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("days")]
    [InlineData("version-of-another-plan")]
    [InlineData("duplicate-key")]
    public async Task Database_RejectsInvalidGrant(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await AddUserAsync(c, "trial@test.local");
        var tripPass = await c.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Code == "TRIP_PASS");
        var membership = await c.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Code == "MEMBERSHIP");
        var version = tripPass.CurrentVersionId!.Value;
        if (mutation == "duplicate-key")
        {
            c.PlanGrants.Add(Grant(user.Id, tripPass.Id, version, key: "signup-trial:1"));
            await c.SaveChangesAsync();
        }
        c.PlanGrants.Add(mutation switch
        {
            "range" => Grant(user.Id, tripPass.Id, version, endsAt: Now),
            "days" => Grant(user.Id, tripPass.Id, version, days: 0),
            "version-of-another-plan" => Grant(user.Id, membership.Id, version),
            _ => Grant(user.Id, tripPass.Id, version, key: "signup-trial:1")
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
    }

    private static PlanGrant Grant(Guid userId, Guid planId, Guid versionId,
        DateTime? endsAt = null, int days = 7, string? key = null) => new()
    {
        UserId = userId,
        PlanId = planId,
        PlanVersionId = versionId,
        StartsAt = Now,
        EndsAt = endsAt ?? Now.AddDays(7),
        Days = days,
        Source = key is null ? PlanGrantSource.Admin : PlanGrantSource.SignupTrial,
        Reason = key is null ? "test" : null,
        DeduplicationKey = key
    };

    private static async Task<User> AddUserAsync(AppDbContext context, string email)
    {
        var user = new User
        {
            Email = email,
            FullName = email,
            PasswordHash = "hash",
            RoleId = await TestRoles.GetUserRoleIdAsync(context)
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}

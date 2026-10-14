using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SignupTrialPostgresTests
{
    [Fact]
    public async Task Grant_WritesOneTrialPerUser_AndTheUserResolvesAsTripPassTrial()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await AddUserAsync(c, "new@test.local");
        var program = await AddProgramAsync(c, maxRecipients: 100);
        var service = Service(c);

        Assert.Equal(SignupTrialOutcome.Granted, await service.GrantAsync(user.Id));
        Assert.Equal(SignupTrialOutcome.AlreadyGranted, await service.GrantAsync(user.Id));

        var grant = await c.PlanGrants.AsNoTracking().SingleAsync();
        Assert.Equal((user.Id, PlanGrantSource.SignupTrial, (Guid?)program.Id, 7, $"signup-trial:{user.Id}"),
            (grant.UserId, grant.Source, grant.TrialProgramId, grant.Days, grant.DeduplicationKey));
        Assert.Null(grant.Reason);
        Assert.Null(grant.GrantedByUserId);
        Assert.Equal(grant.StartsAt.AddDays(7), grant.EndsAt);

        var effective = await EffectiveSubscriptionResolver.ResolveAsync(new SubscriptionRepository(c), user.Id, DateTime.UtcNow);
        Assert.Equal("TRIP_PASS", effective.Plan.Code);
        Assert.True(effective.IsTrial);

        var notification = await c.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal((user.Id, NotificationTypes.PlanGranted, "Bạn được dùng thử gói Trip Pass"),
            (notification.UserId, notification.Type, notification.Title));
        Assert.Equal(1, (await new TrialProgramRepository(c).GetAsync(program.Id))!.GrantedCount);
    }

    [Fact]
    public async Task TwoSignupsRacingForTheLastSlot_OnlyOneGetsTheTrial()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid first, second;
        await using (var seed = db.Context())
        {
            first = (await AddUserAsync(seed, "a@test.local")).Id;
            second = (await AddUserAsync(seed, "b@test.local")).Id;
            await AddProgramAsync(seed, maxRecipients: 1);
        }

        async Task<SignupTrialOutcome> GrantAsync(Guid userId)
        {
            await using var c = db.Context();
            return await Service(c).GrantAsync(userId);
        }

        var outcomes = await Task.WhenAll(GrantAsync(first), GrantAsync(second));

        Assert.Equal([SignupTrialOutcome.Granted, SignupTrialOutcome.Full], outcomes.Order());
        await using var check = db.Context();
        Assert.Equal(1, await check.PlanGrants.CountAsync());
        Assert.Equal(1, await check.Notifications.CountAsync());
    }

    [Fact]
    public async Task DisabledOrOutOfRangeProgram_GrantsNothing()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await AddUserAsync(c, "new@test.local");
        await AddProgramAsync(c, isEnabled: false);
        await AddProgramAsync(c, startOffsetDays: 5, endOffsetDays: 9);

        Assert.Equal(SignupTrialOutcome.NoProgram, await Service(c).GrantAsync(user.Id));
        Assert.Equal(0, await c.PlanGrants.CountAsync());
    }

    private static SignupTrialService Service(AppDbContext c) => new(new TrialProgramRepository(c),
        new PlanGrantRepository(c), new SubscriptionRepository(c), new AdminOperationExecutor(c),
        new NotificationRepository(c), TimeProvider.System, NullLogger<SignupTrialService>.Instance);

    private static async Task<TrialProgram> AddProgramAsync(AppDbContext c, int? maxRecipients = null, bool isEnabled = true,
        int startOffsetDays = -1, int endOffsetDays = 1)
    {
        var today = TrialProgramRules.Today(TimeProvider.System);
        var program = new TrialProgram
        {
            Name = "Khai trương",
            StartsOn = today.AddDays(startOffsetDays),
            EndsOn = today.AddDays(endOffsetDays),
            TrialDays = 7,
            MaxRecipients = maxRecipients,
            IsEnabled = isEnabled
        };
        c.TrialPrograms.Add(program);
        await c.SaveChangesAsync();
        return program;
    }

    private static async Task<User> AddUserAsync(AppDbContext c, string email)
    {
        var user = new User
        {
            Email = email,
            FullName = email,
            PasswordHash = "hash",
            RoleId = await TestRoles.GetUserRoleIdAsync(c)
        };
        c.Users.Add(user);
        await c.SaveChangesAsync();
        return user;
    }
}

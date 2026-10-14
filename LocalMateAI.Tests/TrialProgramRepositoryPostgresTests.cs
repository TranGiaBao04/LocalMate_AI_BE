using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class TrialProgramRepositoryPostgresTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public async Task List_IsNewestFirst_WithGrantedCountPerProgram()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repository = new TrialProgramRepository(c);
        var older = Program("Đợt 1", Day, Day.AddDays(5));
        var newer = Program("Đợt 2", Day.AddDays(10), Day.AddDays(15), maxRecipients: 100);
        await repository.AddAsync(older);
        await repository.AddAsync(newer);
        var earlier = DateTime.UtcNow.AddMinutes(-5);
        await c.TrialPrograms.Where(p => p.Id == older.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, earlier));
        await AddTrialGrantAsync(c, older.Id, "a@test.local");
        await AddTrialGrantAsync(c, older.Id, "b@test.local");

        var all = await repository.GetAllAsync();

        Assert.Equal(["Đợt 2", "Đợt 1"], all.Select(p => p.Name));
        Assert.Equal([0, 2], all.Select(p => p.GrantedCount));
        Assert.Equal(100, all[0].MaxRecipients);
        Assert.Equal(2, (await repository.GetAsync(older.Id))!.GrantedCount);
        Assert.Null(await repository.GetAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(-10, -1, false)] // kết thúc ngay trước ngày đầu
    [InlineData(-10, 0, true)]   // chạm ngày đầu (tính cả hai đầu)
    [InlineData(2, 3, true)]     // nằm trong
    [InlineData(5, 9, true)]     // chạm ngày cuối
    [InlineData(6, 9, false)]    // bắt đầu ngay sau ngày cuối
    public async Task Overlap_IsInclusive_AndOnlyCountsOtherEnabledPrograms(int startOffset, int endOffset, bool expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var repository = new TrialProgramRepository(c);
        var enabled = Program("Đang bật", Day, Day.AddDays(5));
        await repository.AddAsync(enabled);
        await repository.AddAsync(Program("Đang tắt", Day.AddDays(-30), Day.AddDays(30), isEnabled: false));

        Assert.Equal(expected, await repository.HasEnabledOverlapAsync(Day.AddDays(startOffset), Day.AddDays(endOffset), null));
        Assert.False(await repository.HasEnabledOverlapAsync(Day.AddDays(startOffset), Day.AddDays(endOffset), enabled.Id));
    }

    [Fact]
    public async Task Update_ChangesFields_AndReportsMissingProgram()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        var program = Program("Cũ", Day, Day.AddDays(5), maxRecipients: 10);
        await using (var c = db.Context())
        {
            await new TrialProgramRepository(c).AddAsync(program);
        }

        await using var update = db.Context();
        var repository = new TrialProgramRepository(update);
        Assert.True(await repository.UpdateAsync(program.Id,
            new TrialProgramDraft("Mới", Day.AddDays(1), Day.AddDays(9), 3, null, false)));
        Assert.False(await repository.UpdateAsync(Guid.NewGuid(),
            new TrialProgramDraft("Mới", Day, Day, 3, null, false)));

        var saved = (await repository.GetAsync(program.Id))!;
        Assert.Equal(("Mới", Day.AddDays(1), Day.AddDays(9), 3, (int?)null, false),
            (saved.Name, saved.StartsOn, saved.EndsOn, saved.TrialDays, saved.MaxRecipients, saved.IsEnabled));
        Assert.True(saved.UpdatedAt >= saved.CreatedAt);
    }

    [Fact]
    public async Task TwoAdminsCreatingOverlappingPrograms_OnlyOneSucceeds()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid adminId;
        await using (var seed = db.Context())
        {
            adminId = (await AddUserAsync(seed, "admin@test.local")).Id;
        }

        async Task<TrialProgramResult> CreateAsync(string name)
        {
            await using var c = db.Context();
            var service = new TrialProgramService(new TrialProgramRepository(c), new AdminOperationExecutor(c), TimeProvider.System);
            var today = TrialProgramRules.Today(TimeProvider.System);
            return await service.CreateAsync(new SaveTrialProgramRequest(name, today, today.AddDays(7), 7, null, true), adminId);
        }

        var results = await Task.WhenAll(CreateAsync("A"), CreateAsync("B"));

        Assert.Equal([TrialProgramResultStatus.Success, TrialProgramResultStatus.Overlap],
            results.Select(r => r.Status).Order());
        await using var check = db.Context();
        Assert.Equal(1, await check.TrialPrograms.CountAsync());
    }

    [Theory]
    [InlineData("range")]
    [InlineData("trial-days")]
    [InlineData("max-recipients")]
    [InlineData("grant-of-unknown-program")]
    public async Task Database_RejectsInvalidRows(string mutation)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        if (mutation == "grant-of-unknown-program")
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => AddTrialGrantAsync(c, Guid.NewGuid(), "a@test.local"));
            return;
        }

        c.TrialPrograms.Add(mutation switch
        {
            "range" => Program("x", Day, Day.AddDays(-1)),
            "trial-days" => Program("x", Day, Day, trialDays: 0),
            _ => Program("x", Day, Day, maxRecipients: 0)
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
    }

    private static TrialProgram Program(string name, DateOnly startsOn, DateOnly endsOn, int trialDays = 7,
        int? maxRecipients = null, bool isEnabled = true) => new()
    {
        Name = name,
        StartsOn = startsOn,
        EndsOn = endsOn,
        TrialDays = trialDays,
        MaxRecipients = maxRecipients,
        IsEnabled = isEnabled
    };

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

    private static async Task AddTrialGrantAsync(AppDbContext c, Guid programId, string email)
    {
        var user = await AddUserAsync(c, email);
        var plan = await c.SubscriptionPlans.AsNoTracking().SingleAsync(p => p.Code == "TRIP_PASS");
        var now = DateTime.UtcNow;
        c.PlanGrants.Add(new PlanGrant
        {
            UserId = user.Id,
            PlanId = plan.Id,
            PlanVersionId = plan.CurrentVersionId!.Value,
            StartsAt = now,
            EndsAt = now.AddDays(7),
            Days = 7,
            Source = PlanGrantSource.SignupTrial,
            TrialProgramId = programId,
            DeduplicationKey = $"signup-trial:{user.Id}"
        });
        await c.SaveChangesAsync();
    }
}

using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class TrialProgramServiceTests
{
    // 10:00 giờ VN ngày 14/10/2026.
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly Guid AdminId = Guid.NewGuid();

    [Fact]
    public async Task Create_SavesTrimmedProgram_UnderTheTrialProgramLock()
    {
        var f = new Fixture();

        var result = await f.Service.CreateAsync(Request(name: "  Khai trương  ", maxRecipients: 100), AdminId);

        Assert.Equal(TrialProgramResultStatus.Success, result.Status);
        var saved = Assert.Single(f.Repository.Programs);
        Assert.Equal(("Khai trương", Today, Today.AddDays(16), 7, (int?)100, true, (Guid?)AdminId),
            (saved.Name, saved.StartsOn, saved.EndsOn, saved.TrialDays, saved.MaxRecipients, saved.IsEnabled, saved.CreatedByUserId));
        Assert.Equal((TrialProgramStatus.Running, 0, (int?)100),
            (result.Response!.Status, result.Response.GrantedCount, result.Response.Remaining));
        Assert.Equal([AdminLocks.TrialPrograms], f.Executor.LockNames);
    }

    [Theory]
    [InlineData("name-missing", "Name")]
    [InlineData("name-blank", "Name")]
    [InlineData("name-long", "Name")]
    [InlineData("starts-missing", "StartsOn")]
    [InlineData("ends-missing", "EndsOn")]
    [InlineData("ends-before-start", "EndsOn")]
    [InlineData("enabled-but-ended", "EndsOn")]
    [InlineData("days-missing", "TrialDays")]
    [InlineData("days-0", "TrialDays")]
    [InlineData("days-31", "TrialDays")]
    [InlineData("max-0", "MaxRecipients")]
    [InlineData("max-too-big", "MaxRecipients")]
    [InlineData("enabled-missing", "IsEnabled")]
    public async Task Create_InvalidField_IsRejectedBeforeTakingTheLock(string flaw, string field)
    {
        var f = new Fixture();
        var request = flaw switch
        {
            "name-missing" => Request() with { Name = null },
            "name-blank" => Request(name: "   "),
            "name-long" => Request(name: new string('a', TrialProgramRules.MaxNameLength + 1)),
            "starts-missing" => Request() with { StartsOn = null },
            "ends-missing" => Request() with { EndsOn = null },
            "ends-before-start" => Request() with { EndsOn = Today.AddDays(-1) },
            "enabled-but-ended" => Request() with { StartsOn = Today.AddDays(-9), EndsOn = Today.AddDays(-1) },
            "days-missing" => Request() with { TrialDays = null },
            "days-0" => Request() with { TrialDays = 0 },
            "days-31" => Request() with { TrialDays = 31 },
            "max-0" => Request(maxRecipients: 0),
            "max-too-big" => Request(maxRecipients: TrialProgramRules.MaxRecipients + 1),
            _ => Request() with { IsEnabled = null }
        };

        var result = await f.Service.CreateAsync(request, AdminId);

        Assert.Equal(TrialProgramResultStatus.ValidationFailed, result.Status);
        Assert.Equal([field], result.ValidationErrors!.Keys);
        Assert.Empty(f.Executor.LockNames);
        Assert.Empty(f.Repository.Programs);
    }

    [Fact]
    public async Task Create_DisabledProgram_MayHaveEndedAndMayOverlap()
    {
        var f = new Fixture { OverlapExists = true };

        var result = await f.Service.CreateAsync(
            Request() with { StartsOn = Today.AddDays(-9), EndsOn = Today.AddDays(-1), IsEnabled = false }, AdminId);

        Assert.Equal(TrialProgramResultStatus.Success, result.Status);
        Assert.Equal(TrialProgramStatus.Disabled, result.Response!.Status);
        Assert.Equal(0, f.Repository.OverlapChecks);
    }

    [Fact]
    public async Task Create_EnabledProgramOverlappingAnother_IsRejected()
    {
        var f = new Fixture { OverlapExists = true };

        var result = await f.Service.CreateAsync(Request(), AdminId);

        Assert.Equal(TrialProgramResultStatus.Overlap, result.Status);
        Assert.Empty(f.Repository.Programs);
        Assert.Null(f.Repository.LastOverlapExceptId);
    }

    [Fact]
    public async Task Update_ReplacesEveryField_AndIgnoresItselfWhenCheckingOverlap()
    {
        var f = new Fixture();
        var id = (await f.Service.CreateAsync(Request(), AdminId)).Response!.Id;

        var result = await f.Service.UpdateAsync(id, new SaveTrialProgramRequest(
            "Tên mới", Today.AddDays(1), Today.AddDays(5), 3, null, true));

        Assert.Equal(TrialProgramResultStatus.Success, result.Status);
        Assert.Equal(("Tên mới", Today.AddDays(1), Today.AddDays(5), 3, (int?)null, TrialProgramStatus.Upcoming),
            (result.Response!.Name, result.Response.StartsOn, result.Response.EndsOn, result.Response.TrialDays,
                result.Response.MaxRecipients, result.Response.Status));
        Assert.Equal(id, f.Repository.LastOverlapExceptId);
    }

    [Fact]
    public async Task Update_UnknownProgram_ReturnsNotFound_ButInvalidBodyWinsFirst()
    {
        var f = new Fixture();

        Assert.Equal(TrialProgramResultStatus.NotFound, (await f.Service.UpdateAsync(Guid.NewGuid(), Request())).Status);
        Assert.Equal(TrialProgramResultStatus.ValidationFailed,
            (await f.Service.UpdateAsync(Guid.NewGuid(), Request() with { TrialDays = 0 })).Status);
    }

    [Fact]
    public async Task Update_LoweringTheCapBelowGrantedCount_IsAllowedAndReportsFull()
    {
        var f = new Fixture();
        var id = (await f.Service.CreateAsync(Request(maxRecipients: 100), AdminId)).Response!.Id;
        f.Repository.GrantedCounts[id] = 80;

        var result = await f.Service.UpdateAsync(id, Request(maxRecipients: 50));

        Assert.Equal((TrialProgramStatus.Full, 80, (int?)0),
            (result.Response!.Status, result.Response.GrantedCount, result.Response.Remaining));
    }

    [Fact]
    public async Task GetAll_ComputesStatusFromTheVietnamDate()
    {
        var f = new Fixture();
        await f.Service.CreateAsync(Request() with { StartsOn = Today, EndsOn = Today }, AdminId);

        Assert.Equal(TrialProgramStatus.Running, Assert.Single(await f.Service.GetAllAsync()).Status);
        f.Clock.Now = Now.AddHours(14); // 00:00 giờ VN ngày 15/10
        Assert.Equal(TrialProgramStatus.Ended, Assert.Single(await f.Service.GetAllAsync()).Status);
    }

    private static SaveTrialProgramRequest Request(string name = "Khai trương", int? maxRecipients = null) =>
        new(name, Today, Today.AddDays(16), 7, maxRecipients, true);

    private sealed class Fixture
    {
        public Fixture()
        {
            Repository = new FakeRepository(this);
            Service = new TrialProgramService(Repository, Executor, Clock);
        }

        public bool OverlapExists { get; init; }
        public MutableClock Clock { get; } = new();
        public RecordingExecutor Executor { get; } = new();
        public FakeRepository Repository { get; }
        public TrialProgramService Service { get; }
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = TrialProgramServiceTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingExecutor : IAdminOperationExecutor
    {
        public List<string> LockNames { get; } = [];

        public Task<T> ExecuteExclusiveAsync<T>(string lockName, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            LockNames.Add(lockName);
            return operation(cancellationToken);
        }
    }

    private sealed class FakeRepository(Fixture fixture) : ITrialProgramRepository
    {
        public List<TrialProgram> Programs { get; } = [];
        public Dictionary<Guid, int> GrantedCounts { get; } = [];
        public int OverlapChecks { get; private set; }
        public Guid? LastOverlapExceptId { get; private set; }

        public Task<IReadOnlyList<TrialProgramReadModel>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrialProgramReadModel>>(Programs.Select(ToReadModel).ToArray());

        public Task<TrialProgramReadModel?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Programs.Where(p => p.Id == id).Select(ToReadModel).SingleOrDefault());

        public Task<bool> HasEnabledOverlapAsync(DateOnly startsOn, DateOnly endsOn, Guid? exceptId,
            CancellationToken ct = default)
        {
            OverlapChecks++;
            LastOverlapExceptId = exceptId;
            return Task.FromResult(fixture.OverlapExists);
        }

        public Task AddAsync(TrialProgram program, CancellationToken ct = default)
        {
            Programs.Add(program);
            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(Guid id, TrialProgramDraft draft, CancellationToken ct = default)
        {
            var program = Programs.SingleOrDefault(p => p.Id == id);
            if (program is null) return Task.FromResult(false);
            (program.Name, program.StartsOn, program.EndsOn, program.TrialDays, program.MaxRecipients, program.IsEnabled) =
                (draft.Name, draft.StartsOn, draft.EndsOn, draft.TrialDays, draft.MaxRecipients, draft.IsEnabled);
            return Task.FromResult(true);
        }

        private TrialProgramReadModel ToReadModel(TrialProgram p) => new(p.Id, p.Name, p.StartsOn, p.EndsOn, p.TrialDays,
            p.MaxRecipients, p.IsEnabled, GrantedCounts.GetValueOrDefault(p.Id), p.CreatedAt, p.UpdatedAt);
    }
}

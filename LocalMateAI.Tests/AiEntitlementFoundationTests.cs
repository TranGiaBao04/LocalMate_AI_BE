using LocalMateAI.Domain.Enums;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class AiEntitlementFoundationTests
{
    [Theory]
    [InlineData(PlanCode.Free, 3, 1)]
    [InlineData(PlanCode.TripPass, 15, 3)]
    [InlineData(PlanCode.Membership, 30, 3)]
    public void Baseline_HasExplicitCanonicalAiTerms(PlanCode code, int daily, int explain)
    {
        var version = SubscriptionBaseline.Versions().Single(v => v.PlanId == SubscriptionBaseline.PlanId(code));
        Assert.Equal(daily, version.AiDailyCallLimit);
        Assert.Equal(explain, version.AiExplainCallsPerTripLimit);
    }

    [Theory]
    [InlineData(null, 3, "aiDailyCallLimit")]
    [InlineData(15, null, "aiExplainCallsPerTripLimit")]
    [InlineData(null, null, "aiDailyCallLimit")]
    [InlineData(-1, 3, "aiDailyCallLimit")]
    [InlineData(15, -1, "aiExplainCallsPerTripLimit")]
    [InlineData(1001, 3, "aiDailyCallLimit")]
    [InlineData(15, 21, "aiExplainCallsPerTripLimit")]
    public async Task InvalidAiTerms_RejectCreateAndUpdateWithoutPublication(int? daily, int? explain, string field)
    {
        var repo = new AdminPlanServiceTests.MemoryPlans();
        var service = AdminPlanServiceTests.Service(repo);
        var result = await service.CreateAsync(AdminPlanServiceTests.Create() with
        { AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain });
        Assert.Equal(AdminPlanResultStatus.ValidationFailed, result.Status);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Empty(repo.Versions);
        var plan = (await service.CreateAsync(AdminPlanServiceTests.Create())).Response!;
        result = await service.UpdateAsync(plan.Id, AdminPlanServiceTests.Update() with
        { AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain });
        Assert.Equal(AdminPlanResultStatus.ValidationFailed, result.Status);
        Assert.Contains(field, result.ValidationErrors!.Keys);
        Assert.Equal(1, repo.Publications);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 20)]
    public async Task InclusiveAiBounds_AreValid(int daily, int explain)
    {
        var repo = new AdminPlanServiceTests.MemoryPlans();
        var result = await AdminPlanServiceTests.Service(repo).CreateAsync(AdminPlanServiceTests.Create() with
        { AiDailyCallLimit = daily, AiExplainCallsPerTripLimit = explain });
        Assert.Equal(AdminPlanResultStatus.Success, result.Status);
        var version = Assert.Single(repo.Versions.Values);
        Assert.Equal(daily, version.AiDailyCallLimit);
        Assert.Equal(explain, version.AiExplainCallsPerTripLimit);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AiChange_PublishesNewVersionPreservingHistoryAndNoops(bool dailyChange)
    {
        var repo = new AdminPlanServiceTests.MemoryPlans();
        var service = AdminPlanServiceTests.Service(repo);
        var first = (await service.CreateAsync(AdminPlanServiceTests.Create())).Response!;
        var terms = dailyChange
            ? AdminPlanServiceTests.Update() with { AiDailyCallLimit = 20 }
            : AdminPlanServiceTests.Update() with { AiExplainCallsPerTripLimit = 2 };
        var updated = (await service.UpdateAsync(first.Id, terms)).Response!;
        Assert.Equal(2, updated.CurrentVersion!.VersionNumber);
        Assert.Equal(15, repo.Versions[first.CurrentVersion!.Id].AiDailyCallLimit);
        Assert.Equal(3, repo.Versions[first.CurrentVersion.Id].AiExplainCallsPerTripLimit);
        var next = repo.Versions[updated.CurrentVersion.Id];
        Assert.Equal(dailyChange ? 20 : 15, next.AiDailyCallLimit);
        Assert.Equal(dailyChange ? 3 : 2, next.AiExplainCallsPerTripLimit);
        await service.UpdateAsync(first.Id, terms);
        await service.UpdateAsync(first.Id, terms with { Name = "Renamed" });
        Assert.Equal(2, repo.Publications);
        Assert.Equal(1, repo.Saves);
    }
}

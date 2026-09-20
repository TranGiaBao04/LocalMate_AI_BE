using LocalMateAI.Application.Security;

namespace LocalMateAI.Tests;

public sealed class TripActionGuardPolicyTests
{
    [Fact]
    public void ShouldGuard_GetRequest_ReturnsFalse()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "GET", "/api/trips/11111111-1111-1111-1111-111111111111/items", out _);

        Assert.False(guarded);
    }

    [Fact]
    public void ShouldGuard_PostTimelineItems_GuardsAndParsesTripId()
    {
        var tripId = Guid.NewGuid();

        var guarded = TripActionGuardPolicy.ShouldGuard(
            "POST", $"/api/trips/{tripId}/items", out var parsedTripId);

        Assert.True(guarded);
        Assert.Equal(tripId, parsedTripId);
    }

    [Fact]
    public void ShouldGuard_DeleteItem_Guards()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "DELETE", "/api/trips/11111111-1111-1111-1111-111111111111/items/22222222-2222-2222-2222-222222222222", out _);

        Assert.True(guarded);
    }

    [Fact]
    public void ShouldGuard_PostReorder_Guards()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "POST", "/api/trips/11111111-1111-1111-1111-111111111111/items/reorder", out _);

        Assert.True(guarded);
    }

    [Fact]
    public void ShouldGuard_PutTripBareId_Guards()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "PUT", "/api/trips/11111111-1111-1111-1111-111111111111", out _);

        Assert.True(guarded);
    }

    [Fact]
    public void ShouldGuard_PostFinalize_ReturnsFalse()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "POST", "/api/trips/11111111-1111-1111-1111-111111111111/finalize", out _);

        Assert.False(guarded);
    }

    [Fact]
    public void ShouldGuard_PostFork_ReturnsFalse()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "POST", "/api/trips/11111111-1111-1111-1111-111111111111/fork", out _);

        Assert.False(guarded);
    }

    [Fact]
    public void ShouldGuard_PostSave_ReturnsFalse()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard("POST", "/api/trips/save", out _);

        Assert.False(guarded);
    }

    [Fact]
    public void ShouldGuard_PutVisitItem_ReturnsFalse()
    {
        // BE-73: segment 3 là "items" không phải Guid → không guard (visit chỉ hợp lệ trên trip Finalized)
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "PUT", "/api/trips/items/22222222-2222-2222-2222-222222222222/visit", out _);

        Assert.False(guarded);
    }

    [Fact]
    public void ShouldGuard_NonGuidTripSegment_ReturnsFalse()
    {
        var guarded = TripActionGuardPolicy.ShouldGuard(
            "POST", "/api/trips/not-a-guid/items", out _);

        Assert.False(guarded);
    }
}
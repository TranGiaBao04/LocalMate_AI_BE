using System.Text.Json;
using LocalMateAI.Application.DTOs.ItineraryPurchases;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Email;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class SingleItineraryContractTests
{
    [Fact]
    public void FrozenProduct_IsSeparatePermanentFinalize_29000()
    {
        Assert.Equal(29000m, SingleItineraryBaseline.Version().Price);
        Assert.Equal(new[] { "Purchase", "Renewal", "Upgrade" }, Enum.GetNames<PaymentOrderType>());
        Assert.Equal(new[] { "Generate" }, Enum.GetNames<UsageEventType>());
        Assert.DoesNotContain(typeof(SingleItineraryEntitlement).GetProperties(), p => p.Name is "ExpiresAt" or "DurationDays");
    }

    [Theory]
    [InlineData("Normal", false, true)]
    [InlineData("Normal", true, false)]
    [InlineData("SingleEntitlement", false, false)]
    [InlineData("SingleEntitlement", true, true)]
    [InlineData("Generate", true, false)]
    [InlineData("Renewal", true, false)]
    [InlineData("normal", false, false)]
    public void Funding_IsExplicitAndExclusive(string source, bool id, bool valid) =>
        Assert.Equal(valid, new FinalizeTripRequest { FundingSource = source, EntitlementId = id ? Guid.NewGuid() : null }.IsValid);

    [Theory]
    [InlineData("amount")]
    [InlineData("userId")]
    [InlineData("version")]
    [InlineData("price")]
    [InlineData("durationDays")]
    [InlineData("productKind")]
    public void ClientCannotSetFinancialContract(string property)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ItineraryPurchaseCheckoutRequest>(
            "{\"clientAttemptId\":\""+Guid.NewGuid()+"\",\""+property+"\":1}", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FinalizeTripRequest>(
            "{\"fundingSource\":\"Normal\",\""+property+"\":1}", options));
    }

    [Fact]
    public void ConsumeOnce_CannotReopenOrRetarget()
    {
        var now = SingleItineraryPostgresTests.Now;
        var e = new SingleItineraryEntitlement { GrantedAt = now };
        e.Consume(Guid.NewGuid(), now);
        Assert.Throws<InvalidOperationException>(() => e.Consume(Guid.NewGuid(), now));
        Assert.False(SingleItineraryEntitlementResponse.From(e).Available);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("delete")]
    [InlineData("owner")]
    [InlineData("reopen")]
    public async Task EfImmutability(string mutation)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=localmate_s1b_test_guard;Username=design",
                o => o.UseNetTopologySuite()).Options);
        var e = new SingleItineraryEntitlement { GrantedAt = SingleItineraryPostgresTests.Now,
            ConsumedAt = SingleItineraryPostgresTests.Now, ConsumedTripId = Guid.NewGuid() };
        db.Attach(e);
        if (mutation == "version")
        {
            var v = SingleItineraryBaseline.Version(); db.Attach(v); v.Price = 1;
        }
        else if (mutation == "delete") db.Remove(e);
        else if (mutation == "owner") e.UserId = Guid.NewGuid();
        else { e.ConsumedAt = null; e.ConsumedTripId = null; }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void SubscriptionRepair_IsNotApplicable()
    {
        var order = new RepairOrderEvidence(Guid.NewGuid(), Guid.NewGuid(), null, null, null,
            PaymentOrderStatus.Paid, 29000m, SingleItineraryPostgresTests.Now) { ProductKind = PaymentProductKind.SingleItinerary };
        var result = EntitlementRepairAssessmentPolicy.Assess(new(true, new(order, null), [], []), SingleItineraryPostgresTests.Now);
        Assert.Equal("NotApplicable", result.Entitlement.GrantStatus);
        Assert.Equal("product_not_applicable", result.Eligibility.Code);
        Assert.False(result.Eligibility.Eligible);
    }

    [Fact]
    public async Task Receipt_HasActualPurchaseWithoutFakePeriod()
    {
        var user = new User { Email = "test@example.invalid", FullName = "Minh" };
        var order = new PaymentOrder { Amount = 29000m };
        var entry = SingleItineraryReceiptEmailBuilder.Build(user, order, SingleItineraryPostgresTests.Now);
        var model = EmailOutboxModelRegistry.Default.Deserialize(entry.TemplateName, entry.ModelJson)!;
        var html = await new FluidEmailTemplateRenderer().RenderAsync(entry.TemplateName, model);
        Assert.Contains("vĩnh viễn", html);
        Assert.DoesNotContain("AddedDays", entry.ModelJson);
        Assert.DoesNotContain("ValidUntil", entry.ModelJson);
        Assert.Equal("payment-receipt:" + order.Id, entry.DeduplicationKey);
    }
}

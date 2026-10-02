using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeFoundationGuardTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 4, 0, 0, DateTimeKind.Utc);
    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=localmate_s1b_test_guard;Username=design",
            o => o.UseNetTopologySuite()).Options);

    [Fact]
    public void HistoricalFixtureModelDoesNotSelectFutureColumns()
    {
        using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=localmate_s1b_test_guard;Username=design", o => o.UseNetTopologySuite())
            .ReplaceService<IModelCustomizer, BeforeSingleItineraryModelCustomizer>().Options);
        var period = c.Model.FindEntityType(typeof(SubscriptionPeriod))!;
        Assert.Null(period.FindProperty(nameof(SubscriptionPeriod.TerminatedAt)));
        Assert.Null(period.FindProperty(nameof(SubscriptionPeriod.TerminatedByOrderId)));
        Assert.Null(c.Model.FindEntityType(typeof(PaymentOrderCredit)));
        Assert.Null(c.Model.FindEntityType(typeof(PaymentOrder))!.FindProperty(nameof(PaymentOrder.CreditAmount)));
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("fraction")]
    [InlineData("purchase")]
    [InlineData("renewal")]
    [InlineData("single")]
    [InlineData("modified")]
    public async Task FinancialEfGuard(string kind)
    {
        using var c = Context();
        var order = new PaymentOrder { Type = PaymentOrderType.Upgrade, CreditAmount = 1 };
        if (kind == "modified") { c.Attach(order); order.CreditAmount = 2; }
        else
        {
            if (kind == "negative") order.CreditAmount = -1;
            if (kind == "fraction") order.CreditAmount = 0.5m;
            if (kind == "purchase") order.Type = PaymentOrderType.Purchase;
            if (kind == "renewal") order.Type = PaymentOrderType.Renewal;
            if (kind == "single") order.ProductKind = PaymentProductKind.SingleItinerary;
            c.Add(order);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    [Theory]
    [InlineData("snapshot")]
    [InlineData("delete")]
    [InlineData("reopen")]
    [InlineData("retime")]
    [InlineData("nonutc")]
    [InlineData("preReleased")]
    public async Task CreditEfGuard(string kind)
    {
        using var c = Context();
        var row = new PaymentOrderCredit { OrderId = Guid.NewGuid(), PeriodId = Guid.NewGuid(),
            OriginalEndsAt = Now, ReleasedAt = kind is "reopen" or "retime" or "preReleased" ? Now : null };
        if (kind == "preReleased") c.Add(row);
        else
        {
            c.Attach(row);
            if (kind == "delete") c.Remove(row);
            if (kind == "snapshot") c.Entry(row).Property(x => x.RemainingDays).CurrentValue = 1;
            if (kind == "reopen") row.ReleasedAt = null;
            if (kind == "retime") row.ReleasedAt = Now.AddDays(1);
            if (kind == "nonutc") row.ReleasedAt = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    [Theory]
    [InlineData("ends")]
    [InlineData("source")]
    [InlineData("delete")]
    [InlineData("reopen")]
    [InlineData("retime")]
    [InlineData("pair")]
    [InlineData("preTerminated")]
    [InlineData("audit")]
    public async Task PeriodEfGuard(string kind)
    {
        using var c = Context();
        var period = new SubscriptionPeriod { StartsAt = Now, EndsAt = Now.AddDays(7),
            TerminatedAt = kind is "reopen" or "retime" or "preTerminated" ? Now : null,
            TerminatedByOrderId = kind is "reopen" or "retime" or "preTerminated" ? Guid.NewGuid() : null };
        if (kind == "preTerminated") c.Add(period);
        else
        {
            c.Attach(period);
            if (kind == "delete") c.Remove(period);
            if (kind == "ends") c.Entry(period).Property(x => x.EndsAt).CurrentValue = Now.AddDays(9);
            if (kind == "source") c.Entry(period).Property(x => x.SourcePaymentOrderId).CurrentValue = Guid.NewGuid();
            if (kind == "reopen") { period.TerminatedAt = null; period.TerminatedByOrderId = null; }
            if (kind == "retime") period.TerminatedAt = Now.AddDays(1);
            if (kind == "pair") period.TerminatedAt = Now;
            if (kind == "audit") { period.Terminate(Now, Guid.NewGuid()); period.UpdatedAt = Now.AddDays(1); }
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }
}

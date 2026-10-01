using System.Net;
using System.Text.Json;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class PaymentReconciliationHttpTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static string Path => $"/api/admin/transactions/{Id}/reconcile";

    [Fact]
    public async Task Permission_IsManagePlansOnly_NotViewRevenue_AndMiddlewareStillApplies()
    {
        var core = new RecordingCore(PaymentReconciliationStatus.NoChange);
        using var host = new AdminTransactionsHttpTests.Host(reconciliation: core);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync(Path, null)).StatusCode);
        foreach (var mode in new[] { "revenue", "none", "demo", "locked" })
        {
            host.Authenticate(mode);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.PostAsync(Path, null)).StatusCode);
        }
        Assert.Empty(core.Contexts);
        host.Authenticate("manage");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync(Path, null)).StatusCode);
        var context = Assert.Single(core.Contexts);
        Assert.Equal(PaymentStatusChangeSource.AdminReconcile, context.Source);
        Assert.Equal(PaymentReconciliationTests.Actor, context.ActorUserId);
        Assert.Null(context.WebhookReceiptId);
    }

    [Theory]
    [InlineData(PaymentReconciliationStatus.NotFound, 404, "transaction_not_found")]
    [InlineData(PaymentReconciliationStatus.ProviderUnavailable, 503, "payment_provider_unavailable")]
    [InlineData(PaymentReconciliationStatus.ProviderMismatch, 502, "payment_provider_mismatch")]
    public async Task Failure_IsSafeProblemDetails(PaymentReconciliationStatus status, int http, string code)
    {
        using var host = new AdminTransactionsHttpTests.Host(reconciliation: new RecordingCore(status));
        host.Authenticate("manage");
        var response = await host.Client.PostAsync(Path, null);
        Assert.Equal((HttpStatusCode)http, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        AdminTransactionDetailHttpTests.AssertSafe(json.RootElement);
    }

    [Theory]
    [InlineData(PaymentReconciliationStatus.Reconciled)]
    [InlineData(PaymentReconciliationStatus.NoChange)]
    [InlineData(PaymentReconciliationStatus.AlreadyPaid)]
    public async Task Success_HasNormalizedFieldsOnly(PaymentReconciliationStatus status)
    {
        using var host = new AdminTransactionsHttpTests.Host(reconciliation: new RecordingCore(status));
        host.Authenticate("manage");
        var response = await host.Client.PostAsync(Path, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(status.ToString(), root.GetProperty("status").GetString());
        Assert.Equal(Id, root.GetProperty("orderId").GetGuid());
        Assert.Equal(new[] { "checkedAt", "localStatusAfter", "localStatusBefore", "orderId", "providerChecked",
            "providerOrderCode", "providerStatus", "settlementStatus", "status", "statusChanged" }, root.EnumerateObject().Select(p => p.Name).Order());
        AdminTransactionDetailHttpTests.AssertSafe(root);
    }

    private sealed class RecordingCore(PaymentReconciliationStatus status) : IPaymentReconciliationService
    {
        public List<PaymentTransitionContext> Contexts { get; } = [];
        public Task<PaymentReconciliationResult> ReconcileAsync(Guid id, PaymentTransitionContext context, CancellationToken ct = default)
        {
            Contexts.Add(context);
            return Task.FromResult(new PaymentReconciliationResult(status, id, 12345, status != PaymentReconciliationStatus.AlreadyPaid,
                PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Pending, PaymentOrderStatus.Paid,
                status == PaymentReconciliationStatus.Reconciled, PaymentReconciliationTests.Now));
        }
    }
}

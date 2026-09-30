using System.Security.Cryptography;
using System.Text;
using LocalMateAI.API.BackgroundJobs;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Payments;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class PaymentEvidenceTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void RetentionConfiguration_IsBounded(int days, bool valid) => Assert.Equal(valid,
        new PaymentEvidenceOptionsValidator().Validate(null, new() { RawWebhookRetentionDays = days }).Succeeded);

    [Fact]
    public async Task InvalidSignature_PersistsNothing()
    {
        var (service, gateway, evidence, settlement) = Create(false);
        Assert.Equal(PaymentWebhookStatus.InvalidSignature, (await service.ProcessAsync("invalid")).Status);
        Assert.Equal(1, gateway.VerifyCalls);
        Assert.Empty(evidence.Receipts);
        Assert.Empty(settlement.Contexts);
    }

    [Theory]
    [InlineData("a", 65537)]
    [InlineData("đ", 32769)]
    public async Task Oversize_IsMeasuredInUtf8Bytes_AndRejectedBeforeVerification(string glyph, int count)
    {
        var (service, gateway, evidence, settlement) = Create(true);
        var raw = string.Concat(Enumerable.Repeat(glyph, count));
        Assert.Equal(PaymentWebhookStatus.PayloadTooLarge, (await service.ProcessAsync(raw)).Status);
        Assert.Equal(0, gateway.VerifyCalls);
        Assert.Empty(evidence.Receipts);
        Assert.Empty(settlement.Contexts);
    }

    [Fact]
    public async Task ExactRawHash_DuplicatesAndContext_AreRetained()
    {
        var (service, gateway, evidence, settlement) = Create(true);
        const string raw = "\uFEFF {\r\n\"description\":\"Bến Thành\" } ";
        await service.ProcessAsync(raw);
        await service.ProcessAsync(raw);
        Assert.Equal(raw, gateway.VerifiedBody);
        Assert.Equal(2, evidence.Receipts.Count);
        Assert.NotEqual(evidence.Receipts[0].Id, evidence.Receipts[1].Id);
        foreach (var receipt in evidence.Receipts)
        {
            Assert.Equal(raw, receipt.RawPayload);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), receipt.RawPayloadSha256);
            Assert.Equal(Now.AddDays(30), receipt.RawPayloadRetainUntil);
            Assert.Equal(Now, receipt.ReceivedAt);
        }
        Assert.All(settlement.Contexts, c => Assert.Equal(PaymentStatusChangeSource.Webhook, c.Source));
        Assert.Equal(evidence.Receipts.Select(r => (Guid?)r.Id), settlement.Contexts.Select(c => c.WebhookReceiptId));
    }

    [Fact]
    public async Task SettlementFailure_LeavesVerifiedEvidence()
    {
        var (service, _, evidence, settlement) = Create(true);
        settlement.Throw = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcessAsync("verified"));
        Assert.Single(evidence.Receipts);
    }

    [Fact]
    public async Task Worker_UsesTimeProvider_AndPurgesOnlyDueRaw_Idempotently()
    {
        var (_, _, evidence, _) = Create(true);
        var clock = new PaymentEvidenceTestClock(Now);
        var receipt = Receipt(Now.AddDays(-29));
        evidence.Receipts.Add(receipt);
        var services = new ServiceCollection().AddSingleton<IPaymentEvidenceRepository>(evidence).BuildServiceProvider();
        await using var provider = services;
        var worker = new PaymentEvidenceRetentionWorker(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            NullLogger<PaymentEvidenceRetentionWorker>.Instance);
        await worker.RunOnceAsync();
        Assert.NotNull(receipt.RawPayload);
        clock.Now = Now.AddDays(1);
        await worker.RunOnceAsync();
        Assert.Null(receipt.RawPayload);
        Assert.Equal(clock.Now, receipt.RawPayloadPurgedAt);
        var hash = receipt.RawPayloadSha256;
        clock.Now = Now.AddDays(2);
        await worker.RunOnceAsync();
        Assert.Equal(Now.AddDays(1), receipt.RawPayloadPurgedAt);
        Assert.Equal(hash, receipt.RawPayloadSha256);
        Assert.Equal(Now.AddDays(-29), receipt.ReceivedAt);
    }

    [Theory]
    [InlineData("history_update")]
    [InlineData("history_delete")]
    [InlineData("receipt_delete")]
    [InlineData("receipt_metadata")]
    [InlineData("receipt_early_purge")]
    [InlineData("receipt_raw_replace")]
    public async Task EfGuard_RejectsInvalidEvidenceMutationsBeforeSql(string action)
    {
        await using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=invalid;Database=not_used;Username=not_used", o => o.UseNetTopologySuite()).Options, new PaymentEvidenceTestClock(Now));
        if (action.StartsWith("history_"))
        {
            var history = new PaymentOrderStatusHistory { PaymentOrderId = Guid.NewGuid(), ToStatus = PaymentOrderStatus.Pending };
            c.Attach(history);
            if (action.EndsWith("delete")) c.Remove(history);
            else c.Entry(history).Property(h => h.ReasonCode).CurrentValue = "changed";
        }
        else
        {
            var receipt = Receipt(Now);
            c.Attach(receipt);
            switch (action)
            {
                case "receipt_delete": c.Remove(receipt); break;
                case "receipt_metadata": c.Entry(receipt).Property(r => r.Amount).CurrentValue = 1; break;
                case "receipt_early_purge": receipt.RawPayload = null; receipt.RawPayloadPurgedAt = Now.AddDays(30); break;
                case "receipt_raw_replace": receipt.RawPayload = "replacement"; break;
            }
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    internal static PaymentWebhookReceipt Receipt(DateTime received) => new()
    {
        ProviderOrderCode = 999999, Amount = 19000, IsSuccessful = true, ReceivedAt = received,
        RawPayload = "verified-body", RawPayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("verified-body"))),
        RawPayloadRetainUntil = received.AddDays(30)
    };

    private static (PaymentWebhookService, PaymentEvidenceTestGateway, PaymentEvidenceTestRepository, PaymentEvidenceTestSettlement) Create(bool valid)
    {
        var gateway = new PaymentEvidenceTestGateway { Verification = valid
            ? PaymentWebhookVerificationResult.Valid(new(12345, 19000, true)) : PaymentWebhookVerificationResult.Invalid() };
        var evidence = new PaymentEvidenceTestRepository();
        var settlement = new PaymentEvidenceTestSettlement();
        return (new(gateway, settlement, evidence, Options.Create(new PaymentEvidenceOptions()),
            new PaymentEvidenceTestClock(Now), NullLogger<PaymentWebhookService>.Instance), gateway, evidence, settlement);
    }
}

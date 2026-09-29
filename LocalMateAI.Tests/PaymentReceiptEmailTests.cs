using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Email;

namespace LocalMateAI.Tests;

public sealed class PaymentReceiptEmailTests
{
    private static readonly DateTime PaidAtUtc = new(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(19000, "19.000đ")]
    [InlineData(59000, "59.000đ")]
    [InlineData(1234567, "1.234.567đ")]
    [InlineData(0, "0đ")]
    public void Money_UsesDotThousandsSeparator(decimal amount, string expected) =>
        Assert.Equal(expected, EmailDisplayFormat.Money(amount));

    [Fact]
    public void VietnamDateTime_ConvertsUtcToUtcPlus7() =>
        Assert.Equal("06/10/2026 00:30", EmailDisplayFormat.VietnamDateTime(new DateTime(2026, 10, 5, 17, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void Build_Purchase_FormatsReceiptModel()
    {
        var (user, order, subscription) = Sample(PaymentOrderType.Purchase);

        var entry = PaymentReceiptEmailBuilder.Build(
            user, order, subscription, SubscriptionCatalog.Get(PlanCode.Membership), PaidAtUtc);

        Assert.Equal("an@example.com", entry.ToEmail);
        Assert.Equal("Biên nhận thanh toán gói Membership - LocalMate AI", entry.Subject);
        Assert.Equal($"payment-receipt:{order.Id}", entry.DeduplicationKey);
        var model = Assert.IsType<PaymentReceiptEmailModel>(
            EmailOutboxModelRegistry.Default.Deserialize(entry.TemplateName, entry.ModelJson));
        Assert.Equal(
            new PaymentReceiptEmailModel(
                "Nguyễn <An>", "123456789", "Membership", "Mua mới",
                "59.000đ", "05/10/2026 11:00", "30 ngày", "04/11/2026 11:00"),
            model);
    }

    [Fact]
    public async Task Render_RealTemplate_ShowsReceiptAndEncodesHtml()
    {
        var (user, order, subscription) = Sample(PaymentOrderType.Renewal);
        var entry = PaymentReceiptEmailBuilder.Build(
            user, order, subscription, SubscriptionCatalog.Get(PlanCode.Membership), PaidAtUtc);
        var model = EmailOutboxModelRegistry.Default.Deserialize(entry.TemplateName, entry.ModelJson)!;

        var html = await new FluidEmailTemplateRenderer().RenderAsync(entry.TemplateName, model);

        Assert.Contains("59.000đ", html);
        Assert.Contains("123456789", html);
        Assert.Contains("Gói Membership · Gia hạn", html);
        Assert.Contains("05/10/2026 11:00", html);
        Assert.Contains("04/11/2026 11:00", html);
        Assert.Contains("Nguyễn &lt;An&gt;", html);
        Assert.DoesNotContain("<An>", html);
    }

    private static (User User, PaymentOrder Order, UserSubscription Subscription) Sample(PaymentOrderType type)
    {
        var user = new User { FullName = "Nguyễn <An>", Email = "an@example.com" };
        var order = new PaymentOrder
        {
            UserId = user.Id,
            PlanCode = PlanCode.Membership,
            Type = type,
            Amount = 59000,
            Status = PaymentOrderStatus.Paid,
            ProviderOrderCode = 123456789,
            PaidAt = PaidAtUtc
        };
        var subscription = new UserSubscription
        {
            UserId = user.Id,
            PlanCode = PlanCode.Membership,
            StartsAt = PaidAtUtc,
            EndsAt = PaidAtUtc.AddDays(30)
        };
        return (user, order, subscription);
    }
}

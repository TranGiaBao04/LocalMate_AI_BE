using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LocalMateAI.Tests;

public sealed class PaymentSchemaTests
{
    [Fact]
    public void CheckoutRequest_AcceptsOnlyPlanCodeFromClient()
    {
        Assert.Equal([nameof(CreateCheckoutRequest.PlanCode)],
            typeof(CreateCheckoutRequest).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void PaymentOrder_UsesDatabaseSequenceAndUniqueProviderCode()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var sequence = Assert.Single(model.GetSequences(), sequence =>
            sequence.Name == "PaymentOrderCodeSequence");
        Assert.Equal(1, sequence.StartValue);
        Assert.Equal(1, sequence.IncrementBy);

        var entity = model.FindEntityType(typeof(PaymentOrder))!;
        var providerCode = entity.FindProperty(nameof(PaymentOrder.ProviderOrderCode))!;
        Assert.Equal("nextval('\"PaymentOrderCodeSequence\"')", providerCode.GetDefaultValueSql());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PaymentOrder.ProviderOrderCode)]));
    }

    [Fact]
    public void PaymentOrder_AllowsCheckoutDataOnlyToBeTemporarilyNull()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PaymentOrder))!;

        Assert.True(entity.FindProperty(nameof(PaymentOrder.CheckoutUrl))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(PaymentOrder.QrCode))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(PaymentOrder.Amount))!.IsNullable);
        Assert.Equal("numeric(12,0)", entity.FindProperty(nameof(PaymentOrder.Amount))!.GetColumnType());
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=payment_model;Username=model;Password=model",
                npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }
}

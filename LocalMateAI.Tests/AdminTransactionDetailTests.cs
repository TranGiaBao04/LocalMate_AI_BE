using System.Text.Json;
using LocalMateAI.Application.DTOs.Payments;

namespace LocalMateAI.Tests;

public sealed class AdminTransactionDetailTests
{
    internal static AdminTransactionDetailResponse Detail => new(
        new(AdminTransactionTests.Row, Guid.NewGuid(), Guid.NewGuid(), "Native", AdminTransactionTests.Now),
        [new(Guid.NewGuid(), null, "Pending", "Checkout", "order_created", AdminTransactionTests.Now,
            AdminTransactionTests.Row.UserId, null)],
        [new(Guid.NewGuid(), 12345, 19000m, true, AdminTransactionTests.Now, new string('a', 64), true,
            AdminTransactionTests.Now.AddDays(30), null)]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Service_DelegatesIdentityCancellationAndNullableResult_WithoutFilters(bool found)
    {
        var repo = new AdminTransactionTests.RecordingRepository { Detail = found ? Detail : null };
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        var result = await AdminTransactionTests.Service(repo).GetDetailAsync(id, cancellation.Token);
        Assert.Same(repo.Detail, result);
        Assert.Equal((id, cancellation.Token), Assert.Single(repo.DetailCalls));
        Assert.Empty(repo.Filters);
    }

    [Fact]
    public void Detail_ReusesListContractAndAddsOnlySafeVersionMetadata()
    {
        var detail = Detail;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var list = JsonDocument.Parse(JsonSerializer.Serialize(AdminTransactionTests.Row, options));
        using var transaction = JsonDocument.Parse(JsonSerializer.Serialize(detail.Transaction, options));
        foreach (var property in list.RootElement.EnumerateObject())
            Assert.Equal(property.Value.GetRawText(), transaction.RootElement.GetProperty(property.Name).GetRawText());
        var additional = transaction.RootElement.EnumerateObject().Select(p => p.Name)
            .Except(list.RootElement.EnumerateObject().Select(p => p.Name)).Order();
        Assert.Equal(new[] { "planId", "planVersionBinding", "planVersionId", "updatedAt" }, additional);
    }
}

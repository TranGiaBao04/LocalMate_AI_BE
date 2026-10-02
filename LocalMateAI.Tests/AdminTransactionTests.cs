using System.Text;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Payments;
using LocalMateAI.Domain.Enums;
using Microsoft.VisualBasic.FileIO;

namespace LocalMateAI.Tests;

public sealed class AdminTransactionTests
{
    internal static readonly DateTime Now = new(2026, 10, 1, 2, 3, 4, DateTimeKind.Utc);
    internal static AdminTransactionService Service(IAdminTransactionRepository repository) => new(repository,
        new AdminTransactionFilterQueryValidator(), new AdminTransactionPagingValidator(), new PaymentEvidenceTestClock(Now));
    internal static AdminTransactionResponse Row => new(Guid.Parse("00000000-0000-0000-0000-000000000001"), 12345,
        Guid.Parse("00000000-0000-0000-0000-000000000002"), "Đặng Minh", "minh@fixture.local", "SubscriptionPlan",
        "TRIP_PASS", "Trip Pass", "Purchase", "Paid", 19000m, "VND", Now, Now.AddHours(1), Now);

    [Theory]
    [InlineData("status", "unknown")]
    [InlineData("status", "1")]
    [InlineData("status", "")]
    [InlineData("operationType", "SingleItinerary")]
    [InlineData("operationType", "0")]
    [InlineData("operationType", "")]
    [InlineData("createdFrom", "2026-10-01")]
    [InlineData("createdFrom", "2026-10-01T09:00:00")]
    [InlineData("createdFrom", "2026-02-30T09:00:00Z")]
    [InlineData("createdFrom", "2026-10-01T25:00:00Z")]
    [InlineData("createdFrom", "2026-10-01T09:00:00+15:00")]
    [InlineData("createdTo", "bad")]
    [InlineData("createdTo", "")]
    public async Task InvalidFilters_RejectAllReadPathsWithoutCallingRepository(string field, string value)
    {
        var query = new AdminTransactionQuery();
        query = field switch
        {
            "status" => query with { Status = value }, "operationType" => query with { OperationType = value },
            "createdFrom" => query with { CreatedFrom = value }, _ => query with { CreatedTo = value }
        };
        var repository = new RecordingRepository();
        var service = Service(repository);
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, (await service.GetTransactionsAsync(query)).Status);
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, (await service.GetSummaryAsync(query)).Status);
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, (await service.ExportAsync(query)).Status);
        Assert.Empty(repository.Filters);
    }

    [Theory]
    [InlineData("2026-10-01T02:00:00Z", "2026-10-01T02:00:00Z")]
    [InlineData("2026-10-01T03:00:00Z", "2026-10-01T02:00:00Z")]
    [InlineData("2026-10-01T09:00:00+07:00", "2026-10-01T02:00:00Z")]
    public async Task EmptyOrReversedUtcRange_IsInvalid(string from, string to)
    {
        var result = await Service(new RecordingRepository()).GetSummaryAsync(new() { CreatedFrom = from, CreatedTo = to });
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, result.Status);
    }

    [Fact]
    public async Task Normalization_IsIdenticalAcrossListSummaryAndCsv()
    {
        var repository = new RecordingRepository();
        var service = Service(repository);
        var query = new AdminTransactionQuery { Search = "  Nguyễn  ", Status = " pAiD ", OperationType = " renewal ",
            CreatedFrom = "2026-10-01T09:00:00+07:00", CreatedTo = "2026-10-01T10:00:00+07:00" };
        await service.GetTransactionsAsync(query);
        await service.GetSummaryAsync(query);
        await service.ExportAsync(query);
        Assert.Equal(3, repository.Filters.Count);
        var expected = new AdminTransactionFilter("Nguyễn", PaymentOrderStatus.Paid, PaymentOrderType.Renewal,
            new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc));
        Assert.All(repository.Filters, f => Assert.Equal(expected, f));
    }

    [Fact]
    public async Task SearchBoundary_IsSharedByAllReadPaths()
    {
        var repository = new RecordingRepository();
        var service = Service(repository);
        var query = new AdminTransactionQuery { Search = new string('x', 100) };
        Assert.Equal(AdminTransactionResultStatus.Success, (await service.GetTransactionsAsync(query)).Status);
        Assert.Equal(AdminTransactionResultStatus.Success, (await service.GetSummaryAsync(query)).Status);
        Assert.Equal(AdminTransactionResultStatus.Success, (await service.ExportAsync(query)).Status);
        query = query with { Search = new string('x', 101) };
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, (await service.GetTransactionsAsync(query)).Status);
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, (await service.GetSummaryAsync(query)).Status);
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, (await service.ExportAsync(query)).Status);
        Assert.Equal(3, repository.Filters.Count);
    }

    [Theory]
    [InlineData(0, 20, null, null)]
    [InlineData(10001, 20, null, null)]
    [InlineData(1, 0, null, null)]
    [InlineData(1, 101, null, null)]
    [InlineData(1, 20, "checkoutUrl", null)]
    [InlineData(1, 20, "createdAt", "sideways")]
    public async Task InvalidPagingSort_IsRejectedBeforeRead(int page, int size, string? sort, string? direction)
    {
        var repo = new RecordingRepository();
        var result = await Service(repo).GetTransactionsAsync(new() { Page = page, PageSize = size, SortBy = sort, SortDirection = direction });
        Assert.Equal(AdminTransactionResultStatus.InvalidQuery, result.Status);
        Assert.NotEmpty(result.ValidationErrors!);
        Assert.Empty(repo.Filters);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10000, 100)]
    public async Task PagingBoundaries_AreAccepted(int page, int size)
    {
        var result = await Service(new RecordingRepository()).GetTransactionsAsync(new() { Page = page, PageSize = size });
        Assert.Equal(AdminTransactionResultStatus.Success, result.Status);
        Assert.Equal(page, result.Response!.Page);
        Assert.Equal(size, result.Response.PageSize);
    }

    [Fact]
    public async Task ExportCap_RejectsWithoutProducingPartialCsv()
    {
        var result = await Service(new RecordingRepository { ExportCount = 10001 }).ExportAsync(new());
        Assert.Equal(AdminTransactionResultStatus.ExportLimitExceeded, result.Status);
        Assert.Equal(10001, result.MatchingRows);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task SummaryAndExport_DoNotUseListPaging_AndFilenameUsesUtcClock()
    {
        var repository = new RecordingRepository();
        var service = Service(repository);
        var query = new AdminTransactionQuery { Page = 0, PageSize = 0, SortBy = "not-a-sort" };
        Assert.Equal(AdminTransactionResultStatus.Success, (await service.GetSummaryAsync(query)).Status);
        var export = await service.ExportAsync(query);
        Assert.Equal(AdminTransactionResultStatus.Success, export.Status);
        Assert.Equal("localmate-transactions-20261001-020304.csv", export.Response!.FileName);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, export.Response.Content.Take(3));
        Assert.Equal(2, ParseCsv(export.Response.Content).Count);
    }

    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData("+123")]
    [InlineData("-123")]
    [InlineData("@cmd")]
    [InlineData("\tcmd")]
    [InlineData("\rcmd")]
    public void Csv_NeutralizesFormulasInAllFreeTextColumns(string dangerous)
    {
        var row = Row with { UserFullName = dangerous, UserEmail = dangerous, PlanCode = dangerous, PlanName = dangerous };
        var cells = ParseCsv(AdminTransactionCsv.Write([row]))[1];
        foreach (var column in new[] { 6, 7, 9, 10 }) Assert.Equal("'" + dangerous, cells[column]);
        Assert.Equal(row.Id.ToString(), cells[0]);
        Assert.Equal("19000", cells[13]);
    }

    [Theory]
    [InlineData("Tên, dấu phẩy")]
    [InlineData("Tên \"trích dẫn\"")]
    [InlineData("Tên\r\nhai dòng")]
    [InlineData("Tên\nhai dòng")]
    [InlineData("Đường Nguyễn Huệ - Thảo Điền")]
    [InlineData("")]
    [InlineData(null)]
    public void Csv_QuotesAndUtf8RoundTrip(string? name)
    {
        var row = Row with { PlanName = name, PaidAt = null };
        var rows = ParseCsv(AdminTransactionCsv.Write([row]));
        Assert.Equal(AdminTransactionCsv.Header.Split(','), rows[0]);
        Assert.Equal(16, rows[1].Length);
        Assert.Equal(name ?? "", rows[1][10]);
        Assert.Equal("", rows[1][3]);
        Assert.Equal("SubscriptionPlan", rows[1][8]);
        Assert.Equal("0", rows[1][14]);
        Assert.Equal("VND", rows[1][15]);
    }

    internal static List<string[]> ParseCsv(byte[] content)
    {
        using var reader = new TextFieldParser(new StringReader(Encoding.UTF8.GetString(content).TrimStart('\ufeff')))
        { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        reader.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!reader.EndOfData) rows.Add(reader.ReadFields()!);
        return rows;
    }

    internal sealed class RecordingRepository : IAdminTransactionRepository
    {
        public List<AdminTransactionFilter> Filters { get; } = [];
        public long ExportCount { get; set; } = 1;
        public AdminTransactionDetailResponse? Detail { get; set; }
        public List<(Guid Id, CancellationToken CancellationToken)> DetailCalls { get; } = [];
        public Task<AdminTransactionDetailResponse?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        { DetailCalls.Add((id, cancellationToken)); return Task.FromResult(Detail); }
        public Task<PagedResult<AdminTransactionResponse>> GetTransactionsAsync(AdminTransactionFilter filter, PagedQuery paging,
            CancellationToken cancellationToken = default)
        { Filters.Add(filter); return Task.FromResult(PagedResult<AdminTransactionResponse>.Create([Row], paging.Page, paging.PageSize, 1)); }
        public Task<AdminTransactionSummary> GetSummaryAsync(AdminTransactionFilter filter, CancellationToken cancellationToken = default)
        { Filters.Add(filter); return Task.FromResult(new AdminTransactionSummary(1, 1, 0, 0, 0, 19000m, "VND")); }
        public Task<AdminTransactionExportRows> GetExportRowsAsync(AdminTransactionFilter filter, int maxRows, CancellationToken cancellationToken = default)
        { Filters.Add(filter); return Task.FromResult(new AdminTransactionExportRows(ExportCount, ExportCount > maxRows ? [] : [Row])); }
    }
}

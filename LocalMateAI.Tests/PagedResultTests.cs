using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Tests;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(45, 20, 3)]
    [InlineData(45, 100, 1)]
    [InlineData(int.MaxValue, 100, 21_474_837)]
    public void Create_ComputesTotalPagesRoundingUp(int totalCount, int pageSize, int expectedTotalPages)
    {
        var result = PagedResult<int>.Create([], 1, pageSize, totalCount);

        Assert.Equal(expectedTotalPages, result.TotalPages);
        Assert.Equal(totalCount, result.TotalCount);
    }

    [Fact]
    public void Create_PageSizeZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagedResult<int>.Create([], 1, 0, 10));
    }

    [Fact]
    public void Create_NegativeTotalCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PagedResult<int>.Create([], 1, 20, -1));
    }
}

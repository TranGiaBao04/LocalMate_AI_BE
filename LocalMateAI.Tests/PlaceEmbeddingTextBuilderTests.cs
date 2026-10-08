using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class PlaceEmbeddingTextBuilderTests
{
    [Fact]
    public void Build_ComposesCategoryTagsAndDescription()
    {
        var document = PlaceEmbeddingTextBuilder.Build(Source(
            name: "  Tonkin Specialty Coffee ",
            description: " Không gian yên tĩnh, ấm cúng ",
            tags: ["Cà phê", "Chụp ảnh"]));

        Assert.Equal("Tonkin Specialty Coffee", document.Title);
        // Tag sắp theo mã ký tự (không theo bảng chữ cái tiếng Việt): "h" đứng trước "à".
        Assert.Equal("Loại: Quán cà phê. Tag: Chụp ảnh, Cà phê. Không gian yên tĩnh, ấm cúng", document.Text);
    }

    [Theory]
    [InlineData(PlaceCategory.Cafe, "Loại: Quán cà phê.")]
    [InlineData(PlaceCategory.Food, "Loại: Ăn uống.")]
    [InlineData(PlaceCategory.Culture, "Loại: Văn hoá.")]
    [InlineData(PlaceCategory.CheckIn, "Loại: Check-in.")]
    public void Build_WithoutTagsOrDescription_KeepsOnlyCategory(PlaceCategory category, string expected)
    {
        Assert.Equal(expected, PlaceEmbeddingTextBuilder.Build(Source(category: category)).Text);
        Assert.Equal(expected, PlaceEmbeddingTextBuilder.Build(Source(category: category, description: "   ")).Text);
    }

    [Fact]
    public void Hash_SameContentWithTagsInAnotherOrder_IsIdentical()
    {
        var first = Hash(Source(tags: ["Cà phê", "Chụp ảnh", "Ẩm thực"]));
        var second = Hash(Source(tags: ["Ẩm thực", "Chụp ảnh", "Cà phê"]));

        Assert.Equal(first, second);
        Assert.Matches("^[0-9A-F]{64}$", first);
    }

    [Fact]
    public void Hash_ChangesWhenAnyPartOfTheContentChanges()
    {
        var original = Hash(Source(description: "Quán yên tĩnh", tags: ["Cà phê"]));

        Assert.NotEqual(original, Hash(Source(name: "Tên khác", description: "Quán yên tĩnh", tags: ["Cà phê"])));
        Assert.NotEqual(original, Hash(Source(description: "Quán ồn ào", tags: ["Cà phê"])));
        Assert.NotEqual(original, Hash(Source(description: "Quán yên tĩnh", tags: ["Cà phê", "Chụp ảnh"])));
        Assert.NotEqual(original, Hash(Source(category: PlaceCategory.Food, description: "Quán yên tĩnh", tags: ["Cà phê"])));
    }

    private static string Hash(PlaceEmbeddingSource source) =>
        PlaceEmbeddingTextBuilder.Hash(PlaceEmbeddingTextBuilder.Build(source));

    private static PlaceEmbeddingSource Source(
        string name = "Quán A",
        PlaceCategory category = PlaceCategory.Cafe,
        string? description = null,
        string[]? tags = null) =>
        new(Guid.NewGuid(), name, category, description, tags ?? []);
}

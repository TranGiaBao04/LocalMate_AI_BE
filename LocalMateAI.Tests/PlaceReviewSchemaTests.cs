using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LocalMateAI.Tests;

public sealed class PlaceReviewSchemaTests
{
    [Fact]
    public void CanonicalQuickTags_ContainExactlyTheApprovedValues()
    {
        Assert.Equal(
        [
            "WorthVisiting",
            "NearMetro",
            "EasyToReach",
            "GoodValue",
            "NiceAtmosphere",
            "GoodForGroups",
            "TooCrowded",
            "HardToFind",
            "Overpriced",
            "BelowExpectations",
            "InaccurateDescription",
            "WantsReplacement"
        ],
        ReviewQuickTags.All);
    }

    [Fact]
    public void PlaceReview_DefaultsQuickTagsToAnEmptyArray()
    {
        var review = new PlaceReview();

        Assert.NotNull(review.QuickTags);
        Assert.Empty(review.QuickTags);
    }

    [Fact]
    public void PlaceReviewConfiguration_UsesApprovedSchemaContract()
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(PlaceReview));
        Assert.NotNull(entity);
        var placeReviewEntity = entity!;

        Assert.Equal("PlaceReviews", placeReviewEntity.GetTableName());
        Assert.Equal("text[]", placeReviewEntity.FindProperty(nameof(PlaceReview.QuickTags))!.GetColumnType());
        Assert.Equal("ARRAY[]::text[]", placeReviewEntity.FindProperty(nameof(PlaceReview.QuickTags))!.GetDefaultValueSql());
        Assert.Equal(1000, placeReviewEntity.FindProperty(nameof(PlaceReview.Comment))!.GetMaxLength());
        Assert.True(placeReviewEntity.FindProperty(nameof(PlaceReview.Comment))!.IsNullable);
        Assert.False(placeReviewEntity.FindProperty(nameof(PlaceReview.QuickTags))!.IsNullable);

        Assert.Contains(placeReviewEntity.GetIndexes(), index =>
            index.GetDatabaseName() == "UX_PlaceReviews_UserId_ItineraryItemId"
            && index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PlaceReview.UserId), nameof(PlaceReview.ItineraryItemId)]));
        Assert.Contains(placeReviewEntity.GetIndexes(), index =>
            index.GetDatabaseName() == "IX_PlaceReviews_PlaceId"
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(PlaceReview.PlaceId)]));

        AssertForeignKey(placeReviewEntity, typeof(User), nameof(PlaceReview.UserId));
        AssertForeignKey(placeReviewEntity, typeof(Place), nameof(PlaceReview.PlaceId));
        AssertForeignKey(placeReviewEntity, typeof(ItineraryItem), nameof(PlaceReview.ItineraryItemId));

        var constraints = placeReviewEntity.GetCheckConstraints().ToDictionary(constraint => constraint.Name!);
        Assert.Contains("\"Rating\" >= 1 AND \"Rating\" <= 5", constraints["CK_PlaceReviews_Rating"].Sql);
        Assert.Contains("cardinality(\"QuickTags\") <= 3", constraints["CK_PlaceReviews_QuickTags_MaxThree"].Sql);
        Assert.Contains("WorthVisiting", constraints["CK_PlaceReviews_QuickTags_AllowedValues"].Sql);
        Assert.Contains("WantsReplacement", constraints["CK_PlaceReviews_QuickTags_AllowedValues"].Sql);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model;Username=model;Password=model",
                npgsql => npgsql.UseNetTopologySuite())
            .Options;

        return new AppDbContext(options);
    }

    private static void AssertForeignKey(
        IEntityType entity,
        Type principalEntityType,
        string propertyName)
    {
        Assert.Contains(entity.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == principalEntityType
            && foreignKey.DeleteBehavior == DeleteBehavior.Restrict
            && foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual([propertyName]));
    }
}

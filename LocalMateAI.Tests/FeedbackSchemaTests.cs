using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class FeedbackSchemaTests
{
    [Fact]
    public void FeedbackQuickTag_HasOnlyApprovedValues()
    {
        var values = Enum.GetNames<FeedbackQuickTag>();

        Assert.Equal(
            [
                "Suitable",
                "NotSuitable",
                "TooDense",
                "TooFewStops",
                "TooExpensive",
                "TooFar",
                "PreferenceMismatch"
            ],
            values);
    }

    [Fact]
    public void Feedback_Model_UsesRequiredForeignKeysStringQuickTagAndBoundedComment()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Feedback));

        Assert.NotNull(entityType);
        Assert.Equal("Feedbacks", entityType.GetTableName());

        var tripId = entityType.FindProperty(nameof(Feedback.TripId));
        var userId = entityType.FindProperty(nameof(Feedback.UserId));
        var quickTag = entityType.FindProperty(nameof(Feedback.QuickTag));
        var comment = entityType.FindProperty(nameof(Feedback.Comment));

        Assert.NotNull(tripId);
        Assert.NotNull(userId);
        Assert.NotNull(quickTag);
        Assert.NotNull(comment);
        Assert.False(tripId.IsNullable);
        Assert.False(userId.IsNullable);
        Assert.False(quickTag.IsNullable);
        Assert.True(comment.IsNullable);
        Assert.Equal(20, quickTag.GetMaxLength());
        Assert.Equal(1000, comment.GetMaxLength());
        Assert.Equal(typeof(string), quickTag.GetTypeMapping().Converter!.ProviderClrType);
    }

    [Fact]
    public void Feedback_Model_UsesRestrictForeignKeysAndRequiredIndexes()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(Feedback))!;

        Assert.Contains(
            entityType.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Single().Name == nameof(Feedback.UserId)
                          && foreignKey.PrincipalEntityType.ClrType == typeof(User)
                          && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(
            entityType.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Single().Name == nameof(Feedback.TripId)
                          && foreignKey.PrincipalEntityType.ClrType == typeof(Trip)
                          && foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.IsUnique
                     && index.Properties.Select(property => property.Name)
                         .SequenceEqual([nameof(Feedback.UserId), nameof(Feedback.TripId)]));
        Assert.Contains(
            entityType.GetIndexes(),
            index => !index.IsUnique
                     && index.Properties.Select(property => property.Name)
                         .SequenceEqual([nameof(Feedback.TripId)]));
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=feedback_model_metadata",
                npgsqlOptions => npgsqlOptions.UseNetTopologySuite())
            .Options;

        return new AppDbContext(options);
    }
}

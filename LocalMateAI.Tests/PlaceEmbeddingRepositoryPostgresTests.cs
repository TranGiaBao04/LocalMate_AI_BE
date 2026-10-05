using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class PlaceEmbeddingRepositoryPostgresTests
{
    private static readonly DateTime FirstWrite = new(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondWrite = new(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetSources_ReturnsOnlyVisiblePlaces_WithActiveTagNames()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();

        var tagged = Place("Quán có tag", description: "Yên tĩnh", category: PlaceCategory.Food);
        tagged.Tags.Add(new PlaceTag { Tag = new Tag { Name = "Cà phê", Type = TagType.Interest } });
        tagged.Tags.Add(new PlaceTag { Tag = new Tag { Name = "Tag đã tắt", Type = TagType.Interest, IsActive = false } });

        c.Places.AddRange(
            tagged,
            Place("Quán không tag"),
            Place("Chờ duyệt", status: PlaceStatus.Pending),
            Place("Đã ẩn", status: PlaceStatus.Inactive),
            Place("Đã xoá", status: PlaceStatus.Inactive, deletedAt: DateTime.UtcNow));
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var sources = (await new PlaceEmbeddingRepository(c).GetSourcesAsync()).OrderBy(source => source.Name).ToArray();

        Assert.Equal(["Quán có tag", "Quán không tag"], sources.Select(source => source.Name));
        Assert.Equal(tagged.Id, sources[0].PlaceId);
        Assert.Equal(PlaceCategory.Food, sources[0].Category);
        Assert.Equal("Yên tĩnh", sources[0].Description);
        Assert.Equal(["Cà phê"], sources[0].TagNames);
        Assert.Null(sources[1].Description);
        Assert.Empty(sources[1].TagNames);
    }

    [Fact]
    public async Task Upsert_InsertsThenOverwrites_AndRoundTripsTheVector()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var place = Place("Quán A");
        c.Places.Add(place);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var repository = new PlaceEmbeddingRepository(c);

        float[] firstVector = [0.25f, -0.5f, 0.123456f];
        var inserted = await repository.UpsertAsync(
            [new PlaceEmbeddingEntry(place.Id, "model-a", "HASH-1", firstVector)],
            FirstWrite);

        Assert.Equal(1, inserted);
        var stored = await c.PlaceEmbeddings.AsNoTracking().SingleAsync();
        Assert.Equal(firstVector, stored.Vector);
        Assert.Equal(FirstWrite, stored.UpdatedAt);
        Assert.Equal(
            [new PlaceEmbeddingState(place.Id, "model-a", "HASH-1")],
            await repository.GetStatesAsync());

        float[] secondVector = [1f, 0f];
        var updated = await repository.UpsertAsync(
            [new PlaceEmbeddingEntry(place.Id, "model-b", "HASH-2", secondVector)],
            SecondWrite);

        Assert.Equal(1, updated);
        stored = await c.PlaceEmbeddings.AsNoTracking().SingleAsync();
        Assert.Equal("model-b", stored.Model);
        Assert.Equal("HASH-2", stored.ContentHash);
        Assert.Equal(secondVector, stored.Vector);
        Assert.Equal(SecondWrite, stored.UpdatedAt);
    }

    [Fact]
    public async Task GetVectors_ReturnsOnlyVectorsOfTheRequestedModel()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var current = Place("Model hiện tại");
        var outdated = Place("Model cũ");
        c.Places.AddRange(current, outdated);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var repository = new PlaceEmbeddingRepository(c);
        await repository.UpsertAsync(
            [
                new PlaceEmbeddingEntry(current.Id, "model-b@768", "HASH", [0.6f, 0.8f]),
                new PlaceEmbeddingEntry(outdated.Id, "model-a@768", "HASH", [1f, 0f])
            ],
            FirstWrite);

        var vector = Assert.Single(await repository.GetVectorsAsync("model-b@768"));

        Assert.Equal(current.Id, vector.PlaceId);
        Assert.Equal([0.6f, 0.8f], vector.Vector);
        Assert.Empty(await repository.GetVectorsAsync("model-c@768"));
    }

    [Fact]
    public async Task Upsert_SkipsPlacesThatAreNoLongerVisible()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var visible = Place("Đang hiển thị");
        var pending = Place("Chờ duyệt", status: PlaceStatus.Pending);
        var deleted = Place("Đã xoá", status: PlaceStatus.Inactive, deletedAt: DateTime.UtcNow);
        c.Places.AddRange(visible, pending, deleted);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var written = await new PlaceEmbeddingRepository(c).UpsertAsync(
            [
                Entry(visible.Id),
                Entry(pending.Id),
                Entry(deleted.Id),
                Entry(Guid.NewGuid())
            ],
            FirstWrite);

        Assert.Equal(1, written);
        Assert.Equal(visible.Id, (await c.PlaceEmbeddings.AsNoTracking().SingleAsync()).PlaceId);
    }

    [Fact]
    public async Task DeleteForInactivePlaces_RemovesVectorsOfHiddenAndSoftDeletedPlaces()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var stays = Place("Vẫn hiển thị");
        var hidden = Place("Sắp ẩn");
        var softDeleted = Place("Sắp xoá");
        c.Places.AddRange(stays, hidden, softDeleted);
        await c.SaveChangesAsync();
        var repository = new PlaceEmbeddingRepository(c);
        await repository.UpsertAsync([Entry(stays.Id), Entry(hidden.Id), Entry(softDeleted.Id)], FirstWrite);

        hidden.Status = PlaceStatus.Inactive;
        softDeleted.Status = PlaceStatus.Inactive;
        softDeleted.DeletedAt = DateTime.UtcNow;
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();

        var removed = await repository.DeleteForInactivePlacesAsync();

        Assert.Equal(2, removed);
        Assert.Equal(stays.Id, (await c.PlaceEmbeddings.AsNoTracking().SingleAsync()).PlaceId);
        Assert.Equal(0, await repository.DeleteForInactivePlacesAsync());
    }

    private static PlaceEmbeddingEntry Entry(Guid placeId) => new(placeId, "model-a", "HASH", [0.6f, 0.8f]);

    private static Place Place(
        string name,
        PlaceStatus status = PlaceStatus.Active,
        string? description = null,
        PlaceCategory category = PlaceCategory.Cafe,
        DateTime? deletedAt = null) => new()
    {
        Name = name,
        Description = description,
        Address = "Test",
        Location = new Point(106.7, 10.77) { SRID = 4326 },
        Category = category,
        Status = status,
        DeletedAt = deletedAt
    };
}

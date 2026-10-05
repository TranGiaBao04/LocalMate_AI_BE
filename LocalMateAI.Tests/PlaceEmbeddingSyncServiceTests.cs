using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class PlaceEmbeddingSyncServiceTests
{
    private const string Model = "model-a";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NotConfigured_TouchesNeitherDatabaseNorProvider()
    {
        var repository = new FakeRepository { Sources = { Source("Quán A") } };
        var client = new FakeClient { IsConfigured = false };

        var result = await CreateService(repository, client).SyncAsync();

        Assert.Equal(new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.NotConfigured, 0, 0), result);
        Assert.Equal(0, repository.Calls);
        Assert.Empty(client.Batches);
    }

    [Fact]
    public async Task NewPlaces_AreEmbeddedAndStoredWithModelHashAndTime()
    {
        var place = Source("Quán A", "Yên tĩnh");
        var repository = new FakeRepository { Sources = { place } };
        var client = new FakeClient();

        var result = await CreateService(repository, client).SyncAsync();

        Assert.Equal(new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.Completed, 1, 0), result);
        var document = Assert.Single(Assert.Single(client.Batches));
        Assert.Equal(PlaceEmbeddingTextBuilder.Build(place), document);

        var stored = repository.Stored[place.PlaceId];
        Assert.Equal(Model, stored.Model);
        Assert.Equal(PlaceEmbeddingTextBuilder.Hash(document), stored.ContentHash);
        Assert.Equal(client.VectorFor(document), stored.Vector);
        Assert.Equal(Now.UtcDateTime, Assert.Single(repository.UpsertTimes));
    }

    [Fact]
    public async Task UnchangedPlaces_AreNotSentAgain()
    {
        var place = Source("Quán A", "Yên tĩnh");
        var repository = new FakeRepository { Sources = { place } };
        var client = new FakeClient();
        var service = CreateService(repository, client);
        await service.SyncAsync();

        var second = await service.SyncAsync();

        Assert.Equal(new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.Completed, 0, 0), second);
        Assert.Single(client.Batches);
    }

    [Fact]
    public async Task ChangedContent_IsEmbeddedAgain_OnlyForThatPlace()
    {
        var unchanged = Source("Quán A", "Yên tĩnh");
        var changed = Source("Quán B", "Ồn ào");
        var repository = new FakeRepository { Sources = { unchanged, changed } };
        var client = new FakeClient();
        var service = CreateService(repository, client);
        await service.SyncAsync();

        repository.Sources[1] = changed with { Description = "Đã sửa lại mô tả" };
        var result = await service.SyncAsync();

        Assert.Equal(1, result.Embedded);
        Assert.Equal("Quán B", Assert.Single(client.Batches[1]).Title);
    }

    [Fact]
    public async Task ChangedModel_ReembedsEveryPlace()
    {
        var repository = new FakeRepository { Sources = { Source("Quán A"), Source("Quán B") } };
        await CreateService(repository, new FakeClient()).SyncAsync();

        var newModelClient = new FakeClient { Model = "model-b" };
        var result = await CreateService(repository, newModelClient).SyncAsync();

        Assert.Equal(2, result.Embedded);
        Assert.All(repository.Stored.Values, entry => Assert.Equal("model-b", entry.Model));
    }

    [Fact]
    public async Task ManyPlaces_AreSentInBatchesOfFifty()
    {
        var repository = new FakeRepository();
        repository.Sources.AddRange(Enumerable.Range(0, 120).Select(index => Source($"Quán {index}")));
        var client = new FakeClient();

        var result = await CreateService(repository, client).SyncAsync();

        Assert.Equal(120, result.Embedded);
        Assert.Equal([50, 50, 20], client.Batches.Select(batch => batch.Count));
        Assert.Equal(120, repository.Stored.Count);
    }

    [Fact]
    public async Task ProviderFailsOnSecondBatch_KeepsFirstBatchAndStillCleansUp()
    {
        var repository = new FakeRepository { RemovedOnCleanup = 3 };
        repository.Sources.AddRange(Enumerable.Range(0, 70).Select(index => Source($"Quán {index}")));
        var client = new FakeClient { FailFromBatch = 2 };

        var result = await CreateService(repository, client).SyncAsync();

        Assert.Equal(new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.ProviderUnavailable, 50, 3), result);
        Assert.Equal(50, repository.Stored.Count);
    }

    [Fact]
    public async Task PlaceHiddenWhileWaitingForProvider_IsNotCounted()
    {
        var hidden = Source("Quán sắp ẩn");
        var repository = new FakeRepository { Sources = { Source("Quán A"), hidden } };
        repository.RejectedPlaceIds.Add(hidden.PlaceId);

        var result = await CreateService(repository, new FakeClient()).SyncAsync();

        Assert.Equal(1, result.Embedded);
        Assert.DoesNotContain(hidden.PlaceId, repository.Stored.Keys);
    }

    [Fact]
    public async Task CallerCancellation_IsNotSwallowed()
    {
        var repository = new FakeRepository { Sources = { Source("Quán A") } };
        var client = new FakeClient { ThrowCancellation = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(repository, client).SyncAsync());
    }

    private static PlaceEmbeddingSyncService CreateService(FakeRepository repository, FakeClient client) =>
        new(repository, client, new FixedTimeProvider(Now));

    private static PlaceEmbeddingSource Source(string name, string? description = null) =>
        new(Guid.NewGuid(), name, PlaceCategory.Cafe, description, ["Cà phê"]);

    private sealed class FakeRepository : IPlaceEmbeddingRepository
    {
        public List<PlaceEmbeddingSource> Sources { get; } = [];
        public Dictionary<Guid, PlaceEmbeddingEntry> Stored { get; } = [];
        public HashSet<Guid> RejectedPlaceIds { get; } = [];
        public List<DateTime> UpsertTimes { get; } = [];
        public int RemovedOnCleanup { get; init; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<PlaceEmbeddingSource>> GetSourcesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PlaceEmbeddingSource>>(Sources.ToArray());
        }

        public Task<IReadOnlyList<PlaceEmbeddingState>> GetStatesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PlaceEmbeddingState>>(Stored.Values
                .Select(entry => new PlaceEmbeddingState(entry.PlaceId, entry.Model, entry.ContentHash))
                .ToArray());
        }

        public Task<IReadOnlyList<PlaceEmbeddingVector>> GetVectorsAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> UpsertAsync(
            IReadOnlyList<PlaceEmbeddingEntry> entries,
            DateTime updatedAt,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            UpsertTimes.Add(updatedAt);
            var written = 0;

            foreach (var entry in entries.Where(entry => !RejectedPlaceIds.Contains(entry.PlaceId)))
            {
                Stored[entry.PlaceId] = entry;
                written++;
            }

            return Task.FromResult(written);
        }

        public Task<int> DeleteForInactivePlacesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(RemovedOnCleanup);
        }
    }

    private sealed class FakeClient : IEmbeddingClient
    {
        public string Model { get; init; } = PlaceEmbeddingSyncServiceTests.Model;
        public bool IsConfigured { get; init; } = true;
        public int? FailFromBatch { get; init; }
        public bool ThrowCancellation { get; init; }
        public List<IReadOnlyList<EmbeddingDocument>> Batches { get; } = [];

        public float[] VectorFor(EmbeddingDocument document) => [document.Title.Length, document.Text.Length];

        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
            IReadOnlyList<EmbeddingDocument> documents,
            CancellationToken cancellationToken = default)
        {
            if (ThrowCancellation)
            {
                throw new OperationCanceledException();
            }

            Batches.Add(documents);

            if (Batches.Count >= FailFromBatch)
            {
                throw new EmbeddingUnavailableException("provider down");
            }

            return Task.FromResult<IReadOnlyList<float[]>>(documents.Select(VectorFor).ToArray());
        }

        public Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

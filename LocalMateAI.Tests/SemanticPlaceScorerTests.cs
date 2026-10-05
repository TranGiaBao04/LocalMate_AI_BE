using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SemanticPlaceScorerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid Cafe = Guid.NewGuid();
    private static readonly Guid Park = Guid.NewGuid();

    [Fact]
    public async Task ScoreAsync_ReturnsCosineForEveryPlaceWithAVector()
    {
        var fixture = new Fixture();
        fixture.Client.QueryVector = [0.6f, 0.8f];

        var scores = await fixture.Scorer.ScoreAsync("quán yên tĩnh", Timeout);

        Assert.NotNull(scores);
        Assert.Equal(2, scores.Count);
        Assert.Equal(0.6, scores[Cafe], 6);
        Assert.Equal(0.8, scores[Park], 6);
        Assert.Equal("model-a@2", Assert.Single(fixture.Repository.RequestedModels));
    }

    [Fact]
    public async Task SameQuery_IgnoringExtraWhitespace_CallsProviderOnce()
    {
        var fixture = new Fixture();

        await fixture.Scorer.ScoreAsync("quán yên tĩnh", Timeout);
        await fixture.Scorer.ScoreAsync("  quán   yên tĩnh ", Timeout);
        await fixture.Scorer.ScoreAsync("quán yên tĩnh khác", Timeout);

        Assert.Equal(["quán yên tĩnh", "quán yên tĩnh khác"], fixture.Client.Queries);
    }

    [Fact]
    public async Task PlaceVectors_AreReadOnceForManyQueries()
    {
        var fixture = new Fixture();

        await fixture.Scorer.ScoreAsync("câu một", Timeout);
        await fixture.Scorer.ScoreAsync("câu hai", Timeout);

        Assert.Single(fixture.Repository.RequestedModels);
    }

    [Fact]
    public async Task NoVectorsYet_ReturnsNullWithoutCallingProvider_AndDoesNotCacheTheEmptyList()
    {
        var fixture = new Fixture();
        var vectors = fixture.Repository.Vectors.ToArray();
        fixture.Repository.Vectors.Clear();

        Assert.Null(await fixture.Scorer.ScoreAsync("quán yên tĩnh", Timeout));
        Assert.Empty(fixture.Client.Queries);

        fixture.Repository.Vectors.AddRange(vectors);

        Assert.NotNull(await fixture.Scorer.ScoreAsync("quán yên tĩnh", Timeout));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task BlankText_ReturnsNullWithoutAnyWork(string? text)
    {
        var fixture = new Fixture();

        Assert.Null(await fixture.Scorer.ScoreAsync(text!, Timeout));
        Assert.Empty(fixture.Client.Queries);
        Assert.Empty(fixture.Repository.RequestedModels);
    }

    [Fact]
    public async Task NotConfigured_ReturnsNullWithoutAnyWork()
    {
        var fixture = new Fixture();
        fixture.Client.IsConfigured = false;

        Assert.Null(await fixture.Scorer.ScoreAsync("quán yên tĩnh", Timeout));
        Assert.Empty(fixture.Client.Queries);
        Assert.Empty(fixture.Repository.RequestedModels);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsNull_ThenSkipsProviderForSixtySeconds()
    {
        var fixture = new Fixture();
        fixture.Client.Failure = new EmbeddingUnavailableException("provider down");

        Assert.Null(await fixture.Scorer.ScoreAsync("câu một", Timeout));
        Assert.Single(fixture.Client.Queries);

        fixture.Client.Failure = null;
        fixture.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Null(await fixture.Scorer.ScoreAsync("câu hai", Timeout));
        Assert.Single(fixture.Client.Queries);

        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.NotNull(await fixture.Scorer.ScoreAsync("câu hai", Timeout));
        Assert.Equal(["câu một", "câu hai"], fixture.Client.Queries);
    }

    [Fact]
    public async Task FailedQuery_IsNotCached()
    {
        var fixture = new Fixture();
        fixture.Client.Failure = new EmbeddingUnavailableException("provider down");
        await fixture.Scorer.ScoreAsync("câu một", Timeout);

        fixture.Client.Failure = null;
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.NotNull(await fixture.Scorer.ScoreAsync("câu một", Timeout));
        Assert.Equal(["câu một", "câu một"], fixture.Client.Queries);
    }

    [Fact]
    public async Task ProviderSlowerThanTimeout_ReturnsNullAndStartsCooldown()
    {
        var fixture = new Fixture();
        fixture.Client.WaitForCancellation = true;

        Assert.Null(await fixture.Scorer.ScoreAsync("câu một", TimeSpan.FromMilliseconds(50)));

        fixture.Client.WaitForCancellation = false;
        Assert.Null(await fixture.Scorer.ScoreAsync("câu hai", Timeout));
        Assert.Single(fixture.Client.Queries);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_AndDoesNotStartCooldown()
    {
        var fixture = new Fixture();
        fixture.Client.WaitForCancellation = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Scorer.ScoreAsync("câu một", Timeout, cancellation.Token));

        fixture.Client.WaitForCancellation = false;
        Assert.NotNull(await fixture.Scorer.ScoreAsync("câu hai", Timeout));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Repository.Vectors.Add(new PlaceEmbeddingVector(Cafe, [1f, 0f]));
            Repository.Vectors.Add(new PlaceEmbeddingVector(Park, [0f, 1f]));
            Scorer = new SemanticPlaceScorer(
                Client,
                Repository,
                new MemoryCache(new MemoryCacheOptions()),
                Clock,
                NullLogger<SemanticPlaceScorer>.Instance);
        }

        public FakeClient Client { get; } = new();
        public FakeRepository Repository { get; } = new();
        public MutableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero));
        public SemanticPlaceScorer Scorer { get; }
    }

    private sealed class FakeClient : IEmbeddingClient
    {
        public string Model => "model-a@2";
        public bool IsConfigured { get; set; } = true;
        public float[] QueryVector { get; set; } = [1f, 0f];
        public Exception? Failure { get; set; }
        public bool WaitForCancellation { get; set; }
        public List<string> Queries { get; } = [];

        public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);

            if (WaitForCancellation)
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Failure is null ? QueryVector : throw Failure;
        }

        public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
            IReadOnlyList<EmbeddingDocument> documents,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRepository : IPlaceEmbeddingRepository
    {
        public List<PlaceEmbeddingVector> Vectors { get; } = [];
        public List<string> RequestedModels { get; } = [];

        public Task<IReadOnlyList<PlaceEmbeddingVector>> GetVectorsAsync(
            string model,
            CancellationToken cancellationToken = default)
        {
            RequestedModels.Add(model);
            return Task.FromResult<IReadOnlyList<PlaceEmbeddingVector>>(Vectors.ToArray());
        }

        public Task<IReadOnlyList<PlaceEmbeddingSource>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaceEmbeddingState>> GetStatesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> UpsertAsync(
            IReadOnlyList<PlaceEmbeddingEntry> entries,
            DateTime updatedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteForInactivePlacesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset now = utcNow;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}

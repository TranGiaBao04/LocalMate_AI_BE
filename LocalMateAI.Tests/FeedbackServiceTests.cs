using System.Text.Json;
using LocalMateAI.Application.DTOs.Feedback;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class FeedbackServiceTests
{
    [Fact]
    public async Task Create_ValidPersistedOwnerWithFinalizedTrip_PersistsCanonicalFeedback()
    {
        var user = ExistingUser();
        var trip = OwnedTrip(user.Id, TripStatus.Finalized);
        var feedbackRepository = new FakeFeedbackRepository(trip);
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository(user));

        var result = await service.CreateAsync(
            user.Id,
            new CreateFeedbackRequest(trip.Id, FeedbackQuickTag.PreferenceMismatch, null));

        Assert.Equal(CreateFeedbackResultStatus.Success, result.Status);
        Assert.NotNull(feedbackRepository.AddedFeedback);
        Assert.Equal(user.Id, feedbackRepository.AddedFeedback.UserId);
        Assert.Equal(trip.Id, feedbackRepository.AddedFeedback.TripId);
        Assert.Equal(FeedbackQuickTag.PreferenceMismatch, feedbackRepository.AddedFeedback.QuickTag);
        Assert.Null(feedbackRepository.AddedFeedback.Comment);
        Assert.Equal(TripStatus.Finalized, trip.Status);
        Assert.Equal(1, feedbackRepository.TryAddCalls);
    }

    [Fact]
    public async Task Create_NonPersistedIdentity_IsRejectedWithoutTripLookup()
    {
        var feedbackRepository = new FakeFeedbackRepository();
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository());

        var result = await service.CreateAsync(
            Guid.NewGuid(),
            new CreateFeedbackRequest(Guid.NewGuid(), FeedbackQuickTag.Suitable, null));

        Assert.Equal(CreateFeedbackResultStatus.NonPersistedUser, result.Status);
        Assert.Equal(0, feedbackRepository.OwnedTripLookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_MissingOrForeignTrip_ReturnsSameNotFoundResult(bool foreignTrip)
    {
        var user = ExistingUser();
        var trip = foreignTrip
            ? OwnedTrip(Guid.NewGuid(), TripStatus.Finalized)
            : null;
        var feedbackRepository = new FakeFeedbackRepository(trip);
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository(user));

        var result = await service.CreateAsync(
            user.Id,
            new CreateFeedbackRequest(trip?.Id ?? Guid.NewGuid(), FeedbackQuickTag.Suitable, null));

        Assert.Equal(CreateFeedbackResultStatus.TripNotFound, result.Status);
        Assert.Equal(0, feedbackRepository.TryAddCalls);
    }

    [Fact]
    public async Task Create_OwnedDraftTrip_IsRejectedWithoutChangingStatus()
    {
        var user = ExistingUser();
        var trip = OwnedTrip(user.Id, TripStatus.Draft);
        var feedbackRepository = new FakeFeedbackRepository(trip);
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository(user));

        var result = await service.CreateAsync(
            user.Id,
            new CreateFeedbackRequest(trip.Id, FeedbackQuickTag.TooDense, "Too many stops"));

        Assert.Equal(CreateFeedbackResultStatus.TripNotFinalized, result.Status);
        Assert.Equal(TripStatus.Draft, trip.Status);
        Assert.Equal(0, feedbackRepository.TryAddCalls);
    }

    [Fact]
    public async Task Create_ExistingFeedback_ReturnsConflictResultWithoutPersistence()
    {
        var user = ExistingUser();
        var trip = OwnedTrip(user.Id, TripStatus.Finalized);
        var feedbackRepository = new FakeFeedbackRepository(trip) { ExistsResult = true };
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository(user));

        var result = await service.CreateAsync(
            user.Id,
            new CreateFeedbackRequest(trip.Id, FeedbackQuickTag.TooFar, null));

        Assert.Equal(CreateFeedbackResultStatus.AlreadyExists, result.Status);
        Assert.Equal(0, feedbackRepository.TryAddCalls);
    }

    [Fact]
    public async Task Create_UniqueConstraintRace_ReturnsConflictResult()
    {
        var user = ExistingUser();
        var trip = OwnedTrip(user.Id, TripStatus.Finalized);
        var feedbackRepository = new FakeFeedbackRepository(trip) { TryAddResult = false };
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository(user));

        var result = await service.CreateAsync(
            user.Id,
            new CreateFeedbackRequest(trip.Id, FeedbackQuickTag.NotSuitable, null));

        Assert.Equal(CreateFeedbackResultStatus.AlreadyExists, result.Status);
        Assert.Equal(1, feedbackRepository.TryAddCalls);
    }

    [Fact]
    public async Task Create_InvalidQuickTagOrLongComment_ReturnsValidationWithoutLookup()
    {
        var user = ExistingUser();
        var feedbackRepository = new FakeFeedbackRepository();
        var service = new FeedbackService(feedbackRepository, new FakeUserRepository(user));

        var result = await service.CreateAsync(
            user.Id,
            new CreateFeedbackRequest(
                Guid.NewGuid(),
                (FeedbackQuickTag)999,
                new string('x', 1001)));

        Assert.Equal(CreateFeedbackResultStatus.ValidationFailed, result.Status);
        Assert.Contains(nameof(CreateFeedbackRequest.QuickTag), result.ValidationErrors!.Keys);
        Assert.Contains(nameof(CreateFeedbackRequest.Comment), result.ValidationErrors.Keys);
        Assert.Equal(0, feedbackRepository.OwnedTripLookups);
    }

    [Theory]
    [InlineData("{\"tripId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\",\"quickTag\":\"Suitable\",\"comment\":null}")]
    [InlineData("{\"tripId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\",\"quickTag\":\"PreferenceMismatch\"}")]
    public void CreateFeedbackRequest_CanonicalJson_Deserializes(string json)
    {
        var request = JsonSerializer.Deserialize<CreateFeedbackRequest>(json);

        Assert.NotNull(request);
        Assert.True(Enum.IsDefined(request.QuickTag));
    }

    [Theory]
    [InlineData("{\"tripId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\",\"quickTag\":0}")]
    [InlineData("{\"tripId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\",\"quickTag\":\"suitable\"}")]
    [InlineData("{\"tripId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\",\"quickTag\":\"Unknown\"}")]
    [InlineData("{\"tripId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\",\"quickTag\":\"Suitable\",\"userId\":\"d6af41d1-b6cb-4f15-aad5-3dbd40b1ee1a\"}")]
    public void CreateFeedbackRequest_NonCanonicalOrUnexpectedJson_DeserializesInvalidSentinel(string json)
    {
        var request = JsonSerializer.Deserialize<CreateFeedbackRequest>(json);

        Assert.NotNull(request);
        Assert.False(Enum.IsDefined(request.QuickTag));
    }

    [Fact]
    public void FeedbackResponse_SerializesQuickTagAsCanonicalString()
    {
        var json = JsonSerializer.Serialize(
            new FeedbackResponse(
                Guid.NewGuid(),
                Guid.NewGuid(),
                FeedbackQuickTag.TooFewStops,
                null,
                DateTime.UtcNow),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"quickTag\":\"TooFewStops\"", json);
    }

    private static User ExistingUser() =>
        new() { Id = Guid.NewGuid(), FullName = "Feedback User", Email = "feedback@example.invalid" };

    private static Trip OwnedTrip(Guid userId, TripStatus status) =>
        new() { Id = Guid.NewGuid(), UserId = userId, Status = status };

    private sealed class FakeFeedbackRepository(Trip? trip = null) : IFeedbackRepository
    {
        public bool ExistsResult { get; init; }

        public bool TryAddResult { get; init; } = true;

        public Feedback? AddedFeedback { get; private set; }

        public int OwnedTripLookups { get; private set; }

        public int TryAddCalls { get; private set; }

        public Task<Trip?> GetOwnedTripAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            OwnedTripLookups++;
            return Task.FromResult(
                trip?.Id == tripId && trip.UserId == userId
                    ? trip
                    : null);
        }

        public Task<bool> ExistsAsync(
            Guid userId,
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistsResult);

        public Task<bool> TryAddAsync(
            Feedback feedback,
            CancellationToken cancellationToken = default)
        {
            TryAddCalls++;
            if (TryAddResult)
            {
                feedback.CreatedAt = DateTime.UtcNow;
                feedback.UpdatedAt = feedback.CreatedAt;
                AddedFeedback = feedback;
            }

            return Task.FromResult(TryAddResult);
        }
    }

    private sealed class FakeUserRepository(User? user = null) : IUserRepository
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Id == userId ? user : null);

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(
            User user,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

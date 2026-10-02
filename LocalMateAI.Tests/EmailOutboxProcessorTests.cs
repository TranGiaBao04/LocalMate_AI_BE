using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class EmailOutboxProcessorTests
{
    private const string TemplateName = "test-template";
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessDue_ClaimsWithLeaseAndBatchSize()
    {
        var repository = new FakeOutboxRepository();
        var processor = CreateProcessor(repository, new FakeEmailSender(true));

        await processor.ProcessDueAsync();

        Assert.Equal(Now.UtcDateTime, repository.ClaimNow);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), repository.ClaimLeaseUntil);
        Assert.Equal(EmailOutboxProcessor.BatchSize, repository.ClaimBatchSize);
    }

    [Fact]
    public async Task ProcessDue_SendSucceeds_RendersTypedModelAndMarksSent()
    {
        var message = NewMessage(attemptCount: 1);
        var repository = new FakeOutboxRepository(message);
        var renderer = new FakeRenderer();
        var sender = new FakeEmailSender(true);
        var processor = CreateProcessor(repository, sender, renderer);

        var claimed = await processor.ProcessDueAsync();

        Assert.Equal(1, claimed);
        var model = Assert.IsType<TestEmailModel>(renderer.LastModel);
        Assert.Equal("Nguyễn An", model.FullName);
        Assert.Equal(TemplateName, renderer.LastTemplateName);
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(("an@example.com", "Tiêu đề", "<p>Nguyễn An</p>"), sent);
        Assert.Equal([(message.Id, Now.UtcDateTime)], repository.SentMarks);
        Assert.Empty(repository.Retries);
        Assert.Empty(repository.FailedMarks);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 15)]
    [InlineData(4, 60)]
    public async Task ProcessDue_SendFails_SchedulesRetryByAttempt(int attemptCount, int expectedDelayMinutes)
    {
        var message = NewMessage(attemptCount);
        var repository = new FakeOutboxRepository(message);
        var processor = CreateProcessor(repository, new FakeEmailSender(false));

        await processor.ProcessDueAsync();

        var retry = Assert.Single(repository.Retries);
        Assert.Equal(message.Id, retry.Id);
        Assert.Equal(Now.UtcDateTime.AddMinutes(expectedDelayMinutes), retry.NextAttemptAt);
        Assert.Empty(repository.FailedMarks);
        Assert.Empty(repository.SentMarks);
    }

    [Fact]
    public async Task ProcessDue_SendFailsOnLastAttempt_MarksFailed()
    {
        var message = NewMessage(EmailOutboxProcessor.MaxAttempts);
        var repository = new FakeOutboxRepository(message);
        var processor = CreateProcessor(repository, new FakeEmailSender(false));

        await processor.ProcessDueAsync();

        Assert.Equal([message.Id], repository.FailedMarks);
        Assert.Empty(repository.Retries);
    }

    [Fact]
    public async Task ProcessDue_UnknownTemplate_MarksFailedWithoutSending()
    {
        var message = NewMessage(1) with { TemplateName = "unknown-template" };
        var repository = new FakeOutboxRepository(message);
        var sender = new FakeEmailSender(true);
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessDueAsync();

        Assert.Equal([message.Id], repository.FailedMarks);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ProcessDue_InvalidModelJson_MarksFailedWithoutSending()
    {
        var message = NewMessage(1) with { ModelJson = "{not json" };
        var repository = new FakeOutboxRepository(message);
        var sender = new FakeEmailSender(true);
        var processor = CreateProcessor(repository, sender);

        await processor.ProcessDueAsync();

        Assert.Equal([message.Id], repository.FailedMarks);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ProcessDue_RendererThrows_MarksFailedWithoutSending()
    {
        var message = NewMessage(1);
        var repository = new FakeOutboxRepository(message);
        var sender = new FakeEmailSender(true);
        var processor = CreateProcessor(repository, sender, new FakeRenderer(throws: true));

        await processor.ProcessDueAsync();

        Assert.Equal([message.Id], repository.FailedMarks);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ProcessDue_OneBrokenMessage_OthersStillSent()
    {
        var broken = NewMessage(1) with { TemplateName = "unknown-template" };
        var healthy = NewMessage(1);
        var repository = new FakeOutboxRepository(broken, healthy);
        var sender = new FakeEmailSender(true);
        var processor = CreateProcessor(repository, sender);

        var claimed = await processor.ProcessDueAsync();

        Assert.Equal(2, claimed);
        Assert.Equal([broken.Id], repository.FailedMarks);
        Assert.Equal([healthy.Id], repository.SentMarks.Select(mark => mark.Id));
    }

    [Fact]
    public async Task DeleteExpired_UsesRetentionCutoffs()
    {
        var repository = new FakeOutboxRepository();
        var processor = CreateProcessor(repository, new FakeEmailSender(true));

        await processor.DeleteExpiredAsync();

        Assert.Equal(Now.UtcDateTime.AddDays(-7), repository.DeleteSentBefore);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), repository.DeleteFailedBefore);
    }

    [Fact]
    public void Registry_SerializeThenDeserialize_ReturnsSameModel()
    {
        var registry = CreateRegistry();
        var json = EmailOutboxModelRegistry.Serialize(new TestEmailModel("Nguyễn An"));

        var model = registry.Deserialize(TemplateName, json);

        Assert.Equal(new TestEmailModel("Nguyễn An"), model);
        Assert.Null(registry.Deserialize("unknown-template", json));
    }

    private static EmailOutboxProcessor CreateProcessor(
        FakeOutboxRepository repository,
        FakeEmailSender sender,
        FakeRenderer? renderer = null) =>
        new(
            repository,
            renderer ?? new FakeRenderer(),
            sender,
            CreateRegistry(),
            new FixedTimeProvider(Now),
            NullLogger<EmailOutboxProcessor>.Instance);

    private static EmailOutboxModelRegistry CreateRegistry() =>
        new(new Dictionary<string, Type> { [TemplateName] = typeof(TestEmailModel) });

    private static ClaimedEmailOutboxMessage NewMessage(int attemptCount) =>
        new(
            Guid.NewGuid(),
            "an@example.com",
            "Tiêu đề",
            TemplateName,
            EmailOutboxModelRegistry.Serialize(new TestEmailModel("Nguyễn An")),
            attemptCount);

    public sealed record TestEmailModel(string FullName);

    private sealed class FakeOutboxRepository(params ClaimedEmailOutboxMessage[] dueMessages) : IEmailOutboxRepository
    {
        public DateTime? ClaimNow { get; private set; }
        public DateTime? ClaimLeaseUntil { get; private set; }
        public int? ClaimBatchSize { get; private set; }
        public List<(Guid Id, DateTime Now)> SentMarks { get; } = [];
        public List<(Guid Id, DateTime NextAttemptAt)> Retries { get; } = [];
        public List<Guid> FailedMarks { get; } = [];
        public DateTime? DeleteSentBefore { get; private set; }
        public DateTime? DeleteFailedBefore { get; private set; }

        public Task<bool> EnqueueAsync(EmailOutboxEntry entry, DateTime now, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ClaimedEmailOutboxMessage>> ClaimDueAsync(
            DateTime now,
            DateTime leaseUntil,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            ClaimNow = now;
            ClaimLeaseUntil = leaseUntil;
            ClaimBatchSize = batchSize;
            return Task.FromResult<IReadOnlyList<ClaimedEmailOutboxMessage>>(dueMessages);
        }

        public Task MarkSentAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
        {
            SentMarks.Add((id, now));
            return Task.CompletedTask;
        }

        public Task ScheduleRetryAsync(Guid id, DateTime nextAttemptAt, DateTime now, CancellationToken cancellationToken = default)
        {
            Retries.Add((id, nextAttemptAt));
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid id, DateTime now, CancellationToken cancellationToken = default)
        {
            FailedMarks.Add(id);
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(DateTime sentBefore, DateTime failedBefore, CancellationToken cancellationToken = default)
        {
            DeleteSentBefore = sentBefore;
            DeleteFailedBefore = failedBefore;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeRenderer(bool throws = false) : IEmailTemplateRenderer
    {
        public string? LastTemplateName { get; private set; }
        public object? LastModel { get; private set; }

        public Task<string> RenderAsync(string templateName, object model, CancellationToken cancellationToken = default)
        {
            if (throws)
            {
                throw new InvalidOperationException("Template is invalid.");
            }

            LastTemplateName = templateName;
            LastModel = model;
            return Task.FromResult($"<p>{((TestEmailModel)model).FullName}</p>");
        }
    }

    private sealed class FakeEmailSender(bool result) : IEmailSender
    {
        public List<(string ToEmail, string Subject, string HtmlBody)> Sent { get; } = [];

        public Task<bool> TrySendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            if (result)
            {
                Sent.Add((toEmail, subject, htmlBody));
            }

            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

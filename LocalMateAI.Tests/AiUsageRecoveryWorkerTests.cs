using LocalMateAI.API.BackgroundJobs;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Tests;

public sealed class AiUsageRecoveryWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task StartupDoesNotWaitForScan_AndCancellationStopsWorker()
    {
        var entered = Signal();
        var repository = new FakeRepository
        {
            Scan = async (_, _, ct) =>
            {
                entered.TrySetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
                return [];
            }
        };
        using var services = Services(repository);
        var clock = new ManualClock();
        using var worker = new AiUsageRecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(), clock, new TestLogger());
        await worker.StartAsync(CancellationToken.None).WaitAsync(Timeout);
        await entered.Task.WaitAsync(Timeout);
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.True(clock.Timer!.Disposed);
    }

    [Fact]
    public async Task EachPollIsBounded_AndOneItemFailureDoesNotStopLaterItems()
    {
        var reserved = Enumerable.Range(0, 50).Select(_ => Handle()).ToArray();
        var dispatched = Enumerable.Range(0, 50).Select(_ => Handle()).ToArray();
        var scans = new List<(AiUsageAdmissionState, int)>();
        var calls = new List<Guid>();
        var failed = false;
        var repository = new FakeRepository
        {
            Scan = (state, size, _) =>
            {
                scans.Add((state, size));
                return Task.FromResult<IReadOnlyList<AiUsageHandle>>(state == AiUsageAdmissionState.Reserved ? reserved : dispatched);
            },
            Release = (handle, _) =>
            {
                calls.Add(handle.AttemptId);
                if (handle == reserved[0] && !failed) { failed = true; throw new InvalidOperationException("isolated item failure"); }
                return Task.FromResult(true);
            },
            Abandon = (handle, _) => { calls.Add(handle.AttemptId); return Task.FromResult(true); }
        };
        using var services = Services(repository);
        var logger = new TestLogger();
        using var worker = new AiUsageRecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(), new ManualClock(), logger);
        await worker.RunOnceAsync();
        Assert.Equal(2, scans.Count);
        Assert.All(scans, scan => Assert.Equal(50, scan.Item2));
        Assert.Equal(100, calls.Count);
        Assert.Single(logger.Errors);
        await worker.RunOnceAsync();
        Assert.Equal(4, scans.Count);
        Assert.Equal(200, calls.Count);
        Assert.Equal(2, calls.Count(id => id == reserved[0].AttemptId));
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task ScanFailureContinuesOtherState_AndNextControlledTickRetries()
    {
        var firstDone = Signal();
        var secondDone = Signal();
        var reservedScans = 0;
        var dispatchScans = 0;
        var repository = new FakeRepository
        {
            Scan = (state, size, _) =>
            {
                Assert.Equal(50, size);
                if (state == AiUsageAdmissionState.Reserved && Interlocked.Increment(ref reservedScans) == 1)
                    throw new InvalidOperationException("scan unavailable");
                if (state == AiUsageAdmissionState.DispatchAuthorized)
                {
                    if (Interlocked.Increment(ref dispatchScans) == 1) firstDone.TrySetResult();
                    else secondDone.TrySetResult();
                }
                return Task.FromResult<IReadOnlyList<AiUsageHandle>>([]);
            }
        };
        using var services = Services(repository);
        var clock = new ManualClock();
        var logger = new TestLogger();
        using var worker = new AiUsageRecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(), clock, logger);
        await worker.StartAsync(CancellationToken.None);
        await firstDone.Task.WaitAsync(Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.Period);
        Assert.Single(logger.Errors);
        clock.Tick();
        await secondDone.Task.WaitAsync(Timeout);
        Assert.Equal(2, reservedScans);
        await worker.StopAsync(CancellationToken.None).WaitAsync(Timeout);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AlreadyCancelledPollDoesNotCreateScopeOrCallRepository()
    {
        var repository = new FakeRepository { Scan = (_, _, _) => throw new InvalidOperationException("must not scan") };
        using var services = Services(repository);
        using var worker = new AiUsageRecoveryWorker(services.GetRequiredService<IServiceScopeFactory>(), new ManualClock(), new TestLogger());
        await worker.RunOnceAsync(new CancellationToken(true));
        Assert.DoesNotContain(typeof(AiUsageRecoveryWorker).GetConstructors().Single().GetParameters(),
            parameter => parameter.ParameterType.Name.Contains("Llm") || parameter.ParameterType.Name.Contains("Settings"));
    }

    private static ServiceProvider Services(FakeRepository repository) => new ServiceCollection()
        .AddScoped<IAiUsageAdmissionRepository>(_ => repository).BuildServiceProvider();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AiUsageHandle Handle() => new(Guid.NewGuid(), Guid.NewGuid(), LlmCallKind.ParseRequest, null, Guid.NewGuid(), 1);

    private sealed class FakeRepository : IAiUsageAdmissionRepository
    {
        public Func<AiUsageAdmissionState, int, CancellationToken, Task<IReadOnlyList<AiUsageHandle>>> Scan { get; init; }
            = (_, _, _) => Task.FromResult<IReadOnlyList<AiUsageHandle>>([]);
        public Func<AiUsageHandle, CancellationToken, Task<bool>> Release { get; init; } = (_, _) => Task.FromResult(true);
        public Func<AiUsageHandle, CancellationToken, Task<bool>> Abandon { get; init; } = (_, _) => Task.FromResult(true);
        public Task<IReadOnlyList<AiUsageHandle>> GetExpiredAsync(AiUsageAdmissionState state, int batchSize, CancellationToken cancellationToken = default) => Scan(state, batchSize, cancellationToken);
        public Task<bool> ReleaseExpiredReservedAsync(AiUsageHandle handle, CancellationToken cancellationToken = default) => Release(handle, cancellationToken);
        public Task<bool> AbandonExpiredDispatchAuthorizedAsync(AiUsageHandle handle, CancellationToken cancellationToken = default) => Abandon(handle, cancellationToken);
        public Task<int> CountDailyAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiUsageAdmissionResult> AdmitAsync(Guid attemptId, Guid userId, LlmCallKind kind, Guid? tripId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReleaseReservedAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration, bool expiredOnly = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AuthorizeDispatchAsync(Guid userId, Guid attemptId, Guid fencingToken, long fencingGeneration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiUsageCompletionResult> CompleteAsync(AiUsageHandle handle, AiUsageCompletion completion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ManualClock : TimeProvider
    {
        public ManualTimer? Timer { get; private set; }
        public TimeSpan Period { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Period = period;
            return Timer = new ManualTimer(callback, state);
        }
        public void Tick() => Timer!.Tick();
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool Disposed { get; private set; }
        public void Tick() { if (!Disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class TestLogger : ILogger<AiUsageRecoveryWorker>
    {
        public List<Exception?> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Error) Errors.Add(exception); }
    }
}

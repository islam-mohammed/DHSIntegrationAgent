using System.Threading;
using System.Threading.Tasks;
using DHSIntegrationAgent.Application.Abstractions;
using DHSIntegrationAgent.Contracts.Workers;
using DHSIntegrationAgent.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using System.Collections.Generic;
using System;

namespace DHSIntegrationAgent.Tests.Unit.Workers;

public class WorkerEngineTests
{
    private readonly MockWorker _worker;
    private readonly MockLogger<WorkerEngine> _logger;
    private readonly WorkerEngine _engine;

    public WorkerEngineTests()
    {
        _worker = new MockWorker("test-worker", "Test Worker");
        _logger = new MockLogger<WorkerEngine>();
        _engine = new WorkerEngine(new[] { _worker }, _logger);
    }

    [Fact]
    public void IsRunning_InitialState_IsFalse()
    {
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public async Task HostedService_StartAsync_DoesNotStartWorkers()
    {
        // Act
        await ((IHostedService)_engine).StartAsync(CancellationToken.None);

        // Assert
        Assert.False(_engine.IsRunning);
        Assert.Equal(0, _worker.ExecuteCount);
    }

    [Fact]
    public async Task StartAsync_StartsWorkers()
    {
        // Act
        await _engine.StartAsync(CancellationToken.None);

        // Wait a bit for the task to start
        await Task.Delay(100);

        // Assert
        Assert.True(_engine.IsRunning);
        Assert.True(_worker.ExecuteCount >= 1);
    }

    [Fact]
    public async Task StopAsync_StopsWorkers()
    {
        // Arrange
        await _engine.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act
        await _engine.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public async Task StartAsync_Twice_IsIdempotent()
    {
        // Act
        await _engine.StartAsync(CancellationToken.None);
        await _engine.StartAsync(CancellationToken.None);

        await Task.Delay(100);

        // Assert
        Assert.True(_engine.IsRunning);
        Assert.Equal(1, _worker.ExecuteCount);
    }

    [Fact]
    public async Task StopAsync_Twice_IsIdempotent()
    {
        // Arrange
        await _engine.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act
        await _engine.StopAsync(CancellationToken.None);
        await _engine.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public async Task HostedService_StopAsync_StopsWorkers()
    {
        // Arrange
        await _engine.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act
        await ((IHostedService)_engine).StopAsync(CancellationToken.None);

        // Assert
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public async Task StopAsync_IsBounded_ByCancellationToken()
    {
        // Arrange
        _worker.ShouldHang = true;
        await _engine.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        // This should return when cts triggers, or shortly after.
        try
        {
            await _engine.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // This is also a valid way of "bounding"
        }

        // Assert
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public async Task ProgressChanged_IsInvoked()
    {
        // Arrange
        WorkerProgressReport? receivedReport = null;
        _engine.ProgressChanged += (s, e) => receivedReport = e;

        // Act
        await _engine.StartAsync(CancellationToken.None);

        // Wait for worker to report progress
        for (int i = 0; i < 10 && receivedReport == null; i++)
        {
            await Task.Delay(100);
        }

        // Assert
        Assert.NotNull(receivedReport);
        Assert.Equal("test-worker", receivedReport.WorkerId);
        Assert.Equal("Working...", receivedReport.Message);
    }

    private class MockWorker : IWorker
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int ExecuteCount { get; private set; }
        public bool ShouldHang { get; set; }

        public MockWorker(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public async Task ExecuteAsync(IProgress<WorkerProgressReport> progress, CancellationToken ct)
        {
            ExecuteCount++;
            progress.Report(new WorkerProgressReport(Id, "Working..."));
            if (ShouldHang)
            {
                try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            }
        }
    }

    private class MockLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}

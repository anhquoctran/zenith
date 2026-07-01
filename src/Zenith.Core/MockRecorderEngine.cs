using System;
using System.Threading.Tasks;

namespace Zenith.Core;

public class MockRecorderEngine : IRecorderEngine
{
    public event EventHandler<RecorderStatusEventArgs>? StatusChanged;
    public event EventHandler<RecorderErrorEventArgs>? ErrorOccurred;
    public event EventHandler<float>? WaveformDataAvailable;
    public event EventHandler<int>? FpsUpdated;

    public RecorderState State { get; private set; } = RecorderState.Idle;

    public Task InitializeAsync(RecordingConfig config)
    {
        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
        State = RecorderState.Recording;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State, Duration = TimeSpan.Zero, FileSizeBytes = 0 });
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        State = RecorderState.Paused;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State, Duration = TimeSpan.Zero, FileSizeBytes = 0 });
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        State = RecorderState.Recording;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State, Duration = TimeSpan.Zero, FileSizeBytes = 0 });
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        State = RecorderState.Stopped;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State, Duration = TimeSpan.Zero, FileSizeBytes = 0 });
        return Task.CompletedTask;
    }

    public Task TakeSnapshotAsync(string snapshotPath)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Cleanup mock resources
    }
}

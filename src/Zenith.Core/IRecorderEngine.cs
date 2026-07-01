using System;
using System.Threading.Tasks;

namespace Zenith.Core;

public enum RecorderState
{
    Idle,
    Recording,
    Paused,
    Draining,
    Stopped
}

public class RecorderStatusEventArgs : EventArgs
{
    public TimeSpan Duration { get; set; }
    public long FileSizeBytes { get; set; }
    public RecorderState State { get; set; }
}

public class RecorderErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public class RecordingConfig
{
    public string OutputPath { get; set; } = string.Empty;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Framerate { get; set; } = 30;
    
    // Layers
    public System.Collections.Generic.List<VideoLayer> VideoLayers { get; set; } = new();
    public System.Collections.Generic.List<AudioLayer> AudioLayers { get; set; } = new();
    
    public bool UseHardwareAcceleration { get; set; } = false;
    public string SelectedGpuId { get; set; } = "Auto";
}

public interface IRecorderEngine : IDisposable
{
    event EventHandler<RecorderStatusEventArgs>? StatusChanged;
    event EventHandler<RecorderErrorEventArgs>? ErrorOccurred;
    event EventHandler<float>? WaveformDataAvailable;
    event EventHandler<int>? FpsUpdated;

    RecorderState State { get; }

    Task InitializeAsync(RecordingConfig config);
    Task StartAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task TakeSnapshotAsync(string snapshotPath);
}

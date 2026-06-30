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
    
    // X, Y, Width, Height (null = full screen)
    public System.Drawing.Rectangle? CaptureRegion { get; set; } = null;
    public bool EnableWebcam { get; set; } = false;
    public string WebcamDeviceName { get; set; } = string.Empty;
    
    /// <summary>
    /// The HMONITOR handle for the target monitor. If IntPtr.Zero, defaults to primary.
    /// </summary>
    public IntPtr MonitorHandle { get; set; } = IntPtr.Zero;
}

public interface IRecorderEngine : IDisposable
{
    event EventHandler<RecorderStatusEventArgs>? StatusChanged;
    event EventHandler<RecorderErrorEventArgs>? ErrorOccurred;
    event EventHandler<float>? WaveformDataAvailable;

    RecorderState State { get; }

    Task InitializeAsync(RecordingConfig config);
    Task StartAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task TakeSnapshotAsync(string snapshotPath);
}

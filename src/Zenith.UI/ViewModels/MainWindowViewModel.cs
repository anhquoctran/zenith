using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Zenith.Core;
using Zenith.Data;
using Zenith.Interop;

namespace Zenith.UI.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IRecorderEngine _recorderEngine;
    private readonly AudioCaptureEngine _audioEngine;
    private readonly RecordRepository _recordRepository;
    private readonly IDeviceEnumerator _deviceEnumerator;
    private readonly Utils.HardwareMonitor _hardwareMonitor;
    
    
    private readonly DispatcherTimer _elapsedTimer;
    private readonly DispatcherTimer _statsTimer;
    
    private DateTime _recordingStartTime;
    private RecordingConfig? _currentConfig;
    
    // Observable Collections
    public ObservableCollection<VideoLayer> VideoLayers { get; } = [];
    public ObservableCollection<AudioLayer> AudioLayers { get; } = [];
    
    private List<Record> _records = [];
    public List<Record> Records
    {
        get => _records;
        private set => SetProperty(ref _records, value);
    }

    private List<GPUDevice> _gpuDevices = [];
    public List<GPUDevice> GpuDevices
    {
        get => _gpuDevices;
        private set => SetProperty(ref _gpuDevices, value);
    }

    private List<AudioSource> _audioDevices = [];
    public List<AudioSource> AudioDevices
    {
        get => _audioDevices;
        private set => SetProperty(ref _audioDevices, value);
    }

    // Status properties
    private string _elapsedTime = "00:00:00";
    public string ElapsedTime
    {
        get => _elapsedTime;
        set => SetProperty(ref _elapsedTime, value);
    }

    private string _cpuUsage = "CPU: 0%";
    public string CpuUsage
    {
        get => _cpuUsage;
        set => SetProperty(ref _cpuUsage, value);
    }

    private string _memUsage = "Mem: 0 MB";
    public string MemUsage
    {
        get => _memUsage;
        set => SetProperty(ref _memUsage, value);
    }

    private string _gpuUsage = "GPU: N/A";
    public string GpuUsage
    {
        get => _gpuUsage;
        set => SetProperty(ref _gpuUsage, value);
    }

    private string _appVersion = "Zenith v1.0.0";
    public string AppVersion
    {
        get => _appVersion;
        set => SetProperty(ref _appVersion, value);
    }

    private string _ffmpegVersion = "FFmpeg vX.X";
    public string FFmpegVersion
    {
        get => _ffmpegVersion;
        set => SetProperty(ref _ffmpegVersion, value);
    }

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(CanRecord));
                OnPropertyChanged(nameof(CanStop));
                _recordCommand?.RaiseCanExecuteChanged();
                _stopCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRecord => !IsRecording;
    public bool CanStop => IsRecording;
    
    private bool _hasHistory;
    public bool HasHistory
    {
        get => _hasHistory;
        set => SetProperty(ref _hasHistory, value);
    }

    private VideoLayer? _selectedVideoLayer;
    public VideoLayer? SelectedVideoLayer
    {
        get => _selectedVideoLayer;
        set
        {
            if (SetProperty(ref _selectedVideoLayer, value) && value != null)
            {
                value.IsSelected = true;
            }
        }
    }

    private AudioLayer? _selectedAudioLayer;
    public AudioLayer? SelectedAudioLayer
    {
        get => _selectedAudioLayer;
        set => SetProperty(ref _selectedAudioLayer, value);
    }
    
    private int _selectedVideoLayerTypeIndex;
    public int SelectedVideoLayerTypeIndex
    {
        get => _selectedVideoLayerTypeIndex;
        set => SetProperty(ref _selectedVideoLayerTypeIndex, value);
    }

    private AudioSource? _selectedAudioDevice;
    public AudioSource? SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set => SetProperty(ref _selectedAudioDevice, value);
    }

    // Settings
    public string AppSaveLocation
    {
        get => Utils.ConfigManager.CurrentConfig.SaveLocation;
        set
        {
            Utils.ConfigManager.CurrentConfig.SaveLocation = value;
            OnPropertyChanged();
        }
    }
    
    public bool AppUseHardwareAcceleration
    {
        get => Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration;
        set
        {
            Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration = value;
            OnPropertyChanged();
        }
    }
    
    public string AppSelectedGpuId
    {
        get => Utils.ConfigManager.CurrentConfig.SelectedGpuId;
        set
        {
            Utils.ConfigManager.CurrentConfig.SelectedGpuId = value;
            OnPropertyChanged();
        }
    }

    // Commands
    private RelayCommand? _recordCommand;
    private RelayCommand? _stopCommand;
    
    public ICommand RecordCommand => _recordCommand ??= new RelayCommand(
        () => { /* Recording is handled by View due to platform-specific code */ },
        () => CanRecord);
    
    public ICommand StopCommand => _stopCommand ??= new RelayCommand(
        () => { /* Stop is handled by View due to platform-specific code */ },
        () => CanStop);
    
    public ICommand AddVideoLayerCommand { get; }
    public ICommand RemoveVideoLayerCommand { get; }
    public ICommand MoveUpVideoLayerCommand { get; }
    public ICommand MoveDownVideoLayerCommand { get; }
    public ICommand AddAudioLayerCommand { get; }
    public ICommand RemoveAudioLayerCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand ClearHistoryCommand { get; }


    // Expose engines for View
    public IRecorderEngine RecorderEngine => _recorderEngine;
    public AudioCaptureEngine AudioEngine => _audioEngine;
    public IDeviceEnumerator DeviceEnumerator => _deviceEnumerator;
    public RecordRepository RecordRepository => _recordRepository;
    public DateTime RecordingStartTime => _recordingStartTime;
    public RecordingConfig? CurrentConfig => _currentConfig;
    
    public MainWindowViewModel()
    {
        // Initialize services
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _deviceEnumerator = new WindowsDeviceEnumerator();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            _deviceEnumerator = new MacOSDeviceEnumerator();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            _deviceEnumerator = new LinuxDeviceEnumerator();
        else
            _deviceEnumerator = new FallbackDeviceEnumerator();

        _recorderEngine = new FFmpegRecorderEngine();
        _audioEngine = new AudioCaptureEngine();
        
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zenith", "records.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _recordRepository = new RecordRepository(dbPath);
        
        _hardwareMonitor = new Utils.HardwareMonitor();
        
        // Setup audio engine
        _audioEngine.WaveformDataAvailable += OnWaveformDataAvailable;
        _audioEngine.Start(AudioLayers);
        
        AudioLayers.CollectionChanged += (s, e) => _audioEngine.Start(AudioLayers);
        
        // Setup recorder engine events
        _recorderEngine.ErrorOccurred += (s, ev) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[ERROR] Recorder Engine: {ev.Exception?.Message}");
            });
        };

        // Initialize FFmpeg
        AppVersion = "Zenith v1.0.0";
        try
        {
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avcodec"] = 63;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avdevice"] = 63;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avfilter"] = 12;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avformat"] = 63;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avutil"] = 61;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["swresample"] = 7;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["swscale"] = 10;
            FFmpeg.AutoGen.ffmpeg.RootPath = AppContext.BaseDirectory;
            FFmpegVersion = $"FFmpeg v{FFmpeg.AutoGen.ffmpeg.av_version_info()}";
        }
        catch { }

        // Setup timers
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (s, e) =>
        {
            var elapsed = DateTime.Now - _recordingStartTime;
            ElapsedTime = elapsed.ToString(@"hh\:mm\:ss");
        };

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (s, e) =>
        {
            var (cpu, memMB, gpu) = _hardwareMonitor.GetStats();
            CpuUsage = $"CPU: {cpu:F1}%";
            MemUsage = $"Mem: {memMB:F0} MB";
            GpuUsage = $"GPU: {gpu:F1}%";
        };
        _statsTimer.Start();

        // Initialize commands
        AddVideoLayerCommand = new RelayCommand(OnAddVideoLayer);
        RemoveVideoLayerCommand = new RelayCommand(OnRemoveVideoLayer);
        MoveUpVideoLayerCommand = new RelayCommand(OnMoveUpVideoLayer);
        MoveDownVideoLayerCommand = new RelayCommand(OnMoveDownVideoLayer);
        AddAudioLayerCommand = new RelayCommand(OnAddAudioLayer);
        RemoveAudioLayerCommand = new RelayCommand(OnRemoveAudioLayer);
        RefreshHistoryCommand = new AsyncRelayCommand(LoadHistoryAsync);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync);
    }

    public async Task InitializeAsync()
    {
        await _recordRepository.InitializeAsync();
        await LoadHistoryAsync();
        LoadDevices();
    }

    public void LoadDevices()
    {
        _gpuDevices = [new() { Name = "Auto", Id = "Auto" }, .. _deviceEnumerator.GetGPUDevices()];
        GpuDevices = _gpuDevices;

        var audioDevices = new List<AudioSource>
        {
            new() { Name = "System Audio (Default)", Id = "default_system" },
            new() { Name = "Microphone (Default)", Id = "default_mic" }
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
                    audioDevices.Add(new AudioSource { Name = "[Output] " + endpoint.FriendlyName, Id = "sys|" + endpoint.ID });
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
                    audioDevices.Add(new AudioSource { Name = "[Input] " + endpoint.FriendlyName, Id = "mic|" + endpoint.ID });
            }
            catch { }
        }

        AudioDevices = audioDevices;
        SelectedAudioDevice = audioDevices.FirstOrDefault();

        // Add default screen layer if empty
        if (VideoLayers.Count == 0)
        {
            var screens = new List<VideoSource>(_deviceEnumerator.GetVideoSources());
            if (screens.Count > 0)
            {
                var primary = screens[0];
                VideoLayers.Add(new VideoLayer
                {
                    Name = primary.Name,
                    Type = LayerType.Screen,
                    SourceId = primary.Id,
                    X = primary.X,
                    Y = primary.Y,
                    Width = primary.Width,
                    Height = primary.Height
                });
            }
        }
    }

    // Layer management
    private void AddVideoLayerSafely(VideoLayer layer)
    {
        if (VideoLayers.Count >= 10) return;
        VideoLayers.Add(layer);
    }

    private void AddAudioLayerSafely(AudioLayer layer)
    {
        if (AudioLayers.Count >= 10) return;
        AudioLayers.Add(layer);
    }

    private void OnAddVideoLayer()
    {
        var typeNames = new[] { "Screen", "Image", "Video File", "Text", "Camera", "FPS Counter" };
        var typeEnums = new[] { LayerType.Screen, LayerType.Image, LayerType.VideoFile, LayerType.Text, LayerType.Camera, LayerType.FpsCounter };

        var index = Math.Clamp(SelectedVideoLayerTypeIndex, 0, typeNames.Length - 1);
        var type = typeEnums[index];
        var typeName = typeNames[index];

        var newLayer = new VideoLayer
        {
            Name = $"New {typeName}",
            Type = type
        };

        if (type == LayerType.Text)
        {
            newLayer.TextContent = "Hello World";
            newLayer.FontSize = 48;
            var typeface = new Typeface(newLayer.FontFamily ?? "Arial");
            var formattedText = new FormattedText(
                newLayer.TextContent,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                newLayer.FontSize,
                null);
            newLayer.Width = (int)Math.Ceiling(formattedText.Width) + 10;
            newLayer.Height = (int)Math.Ceiling(formattedText.Height) + 10;
            newLayer.X = 1920 / 2 - newLayer.Width / 2;
            newLayer.Y = 1080 / 2 - newLayer.Height / 2;
        }
        else if (type == LayerType.Screen)
        {
            newLayer.Width = 1920;
            newLayer.Height = 1080;
            newLayer.X = 0;
            newLayer.Y = 0;
        }
        else if (type == LayerType.Camera)
        {
            newLayer.Width = 320;
            newLayer.Height = 240;
            newLayer.X = 1920 - 320 - 20;
            newLayer.Y = 1080 - 240 - 20;
        }
        else if (type == LayerType.FpsCounter)
        {
            newLayer.Width = 150;
            newLayer.Height = 50;
            newLayer.X = 20;
            newLayer.Y = 20;
            newLayer.FontSize = 24;
            newLayer.FontColor = "#00FF00";
        }
        else
        {
            newLayer.Width = 640;
            newLayer.Height = 360;
            newLayer.X = 1920 / 2 - newLayer.Width / 2;
            newLayer.Y = 1080 / 2 - newLayer.Height / 2;
        }

        AddVideoLayerSafely(newLayer);
    }

    private void OnRemoveVideoLayer()
    {
        if (SelectedVideoLayer != null)
            VideoLayers.Remove(SelectedVideoLayer);
    }

    private void OnMoveUpVideoLayer()
    {
        if (SelectedVideoLayer == null) return;
        var index = VideoLayers.IndexOf(SelectedVideoLayer);
        if (index > 0)
        {
            var item = VideoLayers[index];
            VideoLayers.RemoveAt(index);
            VideoLayers.Insert(index - 1, item);
            SelectedVideoLayer = item;
        }
    }

    private void OnMoveDownVideoLayer()
    {
        if (SelectedVideoLayer == null) return;
        var index = VideoLayers.IndexOf(SelectedVideoLayer);
        if (index >= 0 && index < VideoLayers.Count - 1)
        {
            var item = VideoLayers[index];
            VideoLayers.RemoveAt(index);
            VideoLayers.Insert(index + 1, item);
            SelectedVideoLayer = item;
        }
    }

    private void OnAddAudioLayer()
    {
        if (SelectedAudioDevice == null) return;
        var src = SelectedAudioDevice;

        AudioLayerType type = AudioLayerType.Microphone;
        string realId = "";
        if (src.Id == "default_system") { type = AudioLayerType.SystemAudio; }
        else if (src.Id == "default_mic") { type = AudioLayerType.Microphone; }
        else if (src.Id.StartsWith("sys|")) { type = AudioLayerType.SystemAudio; realId = src.Id.Substring(4); }
        else if (src.Id.StartsWith("mic|")) { type = AudioLayerType.Microphone; realId = src.Id.Substring(4); }

        AddAudioLayerSafely(new AudioLayer
        {
            Name = src.Name,
            Type = type,
            SourceId = realId,
            Volume = 1.0f
        });
    }

    private void OnRemoveAudioLayer()
    {
        if (SelectedAudioLayer != null)
            AudioLayers.Remove(SelectedAudioLayer);
    }

    // Audio waveform handling
    private void OnWaveformDataAvailable(object? sender, float maxAmplitude)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var rand = new Random();
            foreach (var layer in AudioLayers)
            {
                var jitter = (float)(rand.NextDouble() * 0.2 - 0.1);
                var val = Math.Clamp(maxAmplitude + jitter, 0f, 1f);
                layer.CurrentPeak = val * layer.Volume;
            }
        });
    }

    // Recording lifecycle
    public RecordingConfig PrepareRecordingConfig()
    {
        var dir = AppSaveLocation;
        if (string.IsNullOrEmpty(dir))
            dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        Directory.CreateDirectory(dir);
        
        var filename = Path.Combine(dir, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        var config = new RecordingConfig
        {
            OutputPath = filename,
            Framerate = 60,
            UseHardwareAcceleration = AppUseHardwareAcceleration,
            SelectedGpuId = AppSelectedGpuId
        };

        foreach (var vl in VideoLayers) config.VideoLayers.Add(vl);
        foreach (var al in AudioLayers) config.AudioLayers.Add(al);

        var baseLayer = VideoLayers.Count > 0 ? VideoLayers[0] : null;
        if (baseLayer != null && baseLayer.Type == LayerType.Screen)
        {
            config.Width = baseLayer.Width > 0 ? baseLayer.Width : 1920;
            config.Height = baseLayer.Height > 0 ? baseLayer.Height : 1080;
        }
        else
        {
            config.Width = 1920;
            config.Height = 1080;
        }

        _currentConfig = config;
        return config;
    }

    public void OnRecordingStarted()
    {
        IsRecording = true;
        _recordingStartTime = DateTime.Now;
        _elapsedTimer.Start();
    }

    public async Task OnRecordingStoppedAsync()
    {
        _elapsedTimer.Stop();
        ElapsedTime = "00:00:00";
        
        await _recorderEngine.StopAsync();
        IsRecording = false;

        if (_currentConfig != null && File.Exists(_currentConfig.OutputPath))
        {
            var thumbPath = await GenerateThumbnailAsync(_currentConfig.OutputPath);
            
            var fileInfo = new FileInfo(_currentConfig.OutputPath);
            var record = new Record
            {
                FileName = Path.GetFileName(_currentConfig.OutputPath),
                FilePath = _currentConfig.OutputPath,
                Duration = DateTime.Now - _recordingStartTime,
                FileSize = fileInfo.Length,
                CreatedAt = DateTime.Now,
                Resolution = $"{_currentConfig.Width}x{_currentConfig.Height}",
                Codec = "FFmpeg Native",
                ThumbnailPath = thumbPath
            };
            await _recordRepository.InsertAsync(record);
        }

        await LoadHistoryAsync();
    }

    private static async Task<string> GenerateThumbnailAsync(string videoPath)
    {
#if WINDOWS
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(videoPath);
            var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView);
            if (thumbnail != null)
            {
                var thumbPath = Path.Combine(Path.GetDirectoryName(videoPath)!, Path.GetFileNameWithoutExtension(videoPath) + "_thumb.jpg");
                using var outStream = File.Create(thumbPath);
                using var inStream = thumbnail.AsStreamForRead();
                await inStream.CopyToAsync(outStream);
                return thumbPath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Thumbnail generation failed: {ex.Message}");
        }
#endif
        return string.Empty;
    }

    // History
    public async Task LoadHistoryAsync()
    {
        var records = await _recordRepository.GetAllAsync();
        Records = new List<Record>(records);
        HasHistory = Records.Count > 0;
    }

    private async Task ClearHistoryAsync()
    {
        var records = await _recordRepository.GetAllAsync();
        foreach (var record in records)
        {
            if (File.Exists(record.FilePath))
            {
                try { File.Delete(record.FilePath); } catch { }
            }
            if (!string.IsNullOrEmpty(record.ThumbnailPath) && File.Exists(record.ThumbnailPath))
            {
                try { File.Delete(record.ThumbnailPath); } catch { }
            }
        }
        await _recordRepository.ClearAllAsync();
        await LoadHistoryAsync();
    }

    // Menu commands for adding specific layer types
    public void AddScreenLayer()
    {
        var screens = new List<VideoSource>(_deviceEnumerator.GetVideoSources());
        if (screens.Count > 0)
        {
            AddVideoLayerSafely(new VideoLayer { Name = screens[0].Name, Type = LayerType.Screen, SourceId = screens[0].Id, Width = screens[0].Width, Height = screens[0].Height, X = screens[0].X, Y = screens[0].Y });
        }
    }

    public void AddImageLayer()
    {
        AddVideoLayerSafely(new VideoLayer { Name = "Image Overlay", Type = LayerType.Image, Width = 640, Height = 360, X = 1920 / 2 - 320, Y = 1080 / 2 - 180 });
    }

    public void AddVideoFileLayer()
    {
        AddVideoLayerSafely(new VideoLayer { Name = "Video Overlay", Type = LayerType.VideoFile, Width = 640, Height = 360, X = 1920 / 2 - 320, Y = 1080 / 2 - 180 });
    }

    public void AddTextLayer()
    {
        var layer = new VideoLayer { Name = "Text Overlay", Type = LayerType.Text, TextContent = "Hello World", FontSize = 48 };
        var typeface = new Typeface(layer.FontFamily ?? "Arial");
        var formattedText = new FormattedText(layer.TextContent, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, layer.FontSize, null);
        layer.Width = (int)Math.Ceiling(formattedText.Width) + 10;
        layer.Height = (int)Math.Ceiling(formattedText.Height) + 10;
        layer.X = 1920 / 2 - layer.Width / 2;
        layer.Y = 1080 / 2 - layer.Height / 2;
        AddVideoLayerSafely(layer);
    }

    public void AddCameraLayer()
    {
        AddVideoLayerSafely(new VideoLayer { Name = "Webcam", Type = LayerType.Camera, Width = 320, Height = 240, X = 1920 - 320 - 20, Y = 1080 - 240 - 20 });
    }

    public void AddFpsLayer()
    {
        AddVideoLayerSafely(new VideoLayer { Name = "FPS Counter", Type = LayerType.FpsCounter, Width = 150, Height = 50, X = 20, Y = 20, FontSize = 24, FontColor = "#00FF00" });
    }

    public void AddAudioLayerFromSource(AudioSource src)
    {
        AudioLayerType type = AudioLayerType.Microphone;
        string realId = "";
        if (src.Id == "default_system") { type = AudioLayerType.SystemAudio; }
        else if (src.Id == "default_mic") { type = AudioLayerType.Microphone; }
        else if (src.Id.StartsWith("sys|")) { type = AudioLayerType.SystemAudio; realId = src.Id.Substring(4); }
        else if (src.Id.StartsWith("mic|")) { type = AudioLayerType.Microphone; realId = src.Id.Substring(4); }

        AddAudioLayerSafely(new AudioLayer { Name = src.Name, Type = type, SourceId = realId, Volume = 1.0f });
    }

    // Visibility / Lock toggles
    public void ToggleLayerVisibility(VideoLayer layer)
    {
        layer.IsVisible = !layer.IsVisible;
    }

    public void ToggleLayerLock(VideoLayer layer)
    {
        layer.IsLocked = !layer.IsLocked;
    }

    // Cleanup
    public void Dispose()
    {
        _elapsedTimer.Stop();
        _statsTimer.Stop();
        _audioEngine.Stop();
        _audioEngine.Dispose();
        _recorderEngine.Dispose();
    }
}

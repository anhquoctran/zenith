using Avalonia;
using Zenith.UI.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Zenith.Core;
using Zenith.Data;
using Zenith.Interop;
using System.Diagnostics;
using Avalonia.VisualTree;
using System.Linq;
using Path = System.IO.Path;

namespace Zenith.UI.Views;

public class PathToBitmapConverter : Avalonia.Data.Converters.IValueConverter
{
    private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _cache = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string path && File.Exists(path))
        {
            if (_cache.TryGetValue(path, out var bmp)) return bmp;
            try
            {
                bmp = new Avalonia.Media.Imaging.Bitmap(path);
                _cache[path] = bmp;
                return bmp;
            }
            catch { return null; }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public partial class MainWindow : Window
{
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);
    [DllImport("user32.dll")]
    private static extern uint SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
#endif
    private RecordingWidget? _widget;
    private readonly AudioCaptureEngine _audioEngine;
    private readonly IRecorderEngine _recorderEngine;
    private readonly RecordRepository _recordRepository;
    private readonly IDeviceEnumerator _deviceEnumerator;
    private readonly DispatcherTimer _previewTimer;
    private System.Drawing.Rectangle? _selectedRegion;
    private readonly DispatcherTimer _elapsedTimer;
    private DispatcherTimer _statsTimer;
    private Zenith.UI.Utils.HardwareMonitor _hardwareMonitor;
    private DateTime _recordingStartTime;
    private readonly System.Collections.Generic.List<LayerOverlayWindow> _activeOverlays = new();
    private Avalonia.Media.Imaging.WriteableBitmap? _webcamBitmap;
#if WINDOWS
    private System.Drawing.Bitmap? _previewWinBmp;
#endif
    private Avalonia.Media.Imaging.WriteableBitmap? _previewAvaloniaBmp;
    private RecordingConfig? _currentConfig;
    
    public System.Collections.ObjectModel.ObservableCollection<VideoLayer> VideoLayers { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<AudioLayer> AudioLayers { get; } = new();

    private List<GPUDevice> _gpuDevices = new();
    public string AppSaveLocation 
    { 
        get => Zenith.UI.Utils.ConfigManager.CurrentConfig.SaveLocation; 
        set => Zenith.UI.Utils.ConfigManager.CurrentConfig.SaveLocation = value; 
    }
    public bool AppUseHardwareAcceleration 
    { 
        get => Zenith.UI.Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration; 
        set => Zenith.UI.Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration = value; 
    }
    public string AppSelectedGpuId 
    { 
        get => Zenith.UI.Utils.ConfigManager.CurrentConfig.SelectedGpuId; 
        set => Zenith.UI.Utils.ConfigManager.CurrentConfig.SelectedGpuId = value; 
    }
    public MainWindow()
    {
        InitializeComponent();
        
        LayerEditor.Setup(VideoLayers);
        
        VideoLayersListBox.ItemsSource = VideoLayers;
        VideoLayersListBox.SelectionChanged += (s, e) =>
        {
            if (VideoLayersListBox.SelectedItem is VideoLayer layer)
            {
                layer.IsSelected = true;
            }
        };
        
        AudioLayersListBox.ItemsSource = AudioLayers;

        AudioLayers.CollectionChanged += (s, e) => 
        {
            _audioEngine.Start(AudioLayers);
        };

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			_deviceEnumerator = new WindowsDeviceEnumerator();
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			_deviceEnumerator = new MacOSDeviceEnumerator();
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			_deviceEnumerator = new LinuxDeviceEnumerator();
		}
		else
		{
			_deviceEnumerator = new FallbackDeviceEnumerator();
		}

		_recorderEngine = new FFmpegRecorderEngine();

        _audioEngine = new AudioCaptureEngine();
        _audioEngine.WaveformDataAvailable += OnWaveformDataAvailable;
        _audioEngine.Start(AudioLayers);
        
        // Initialize the native FFmpeg engine
        
        _recorderEngine.ErrorOccurred += (s, ev) => 
        {
            Dispatcher.UIThread.Post(() => 
            {
                // In a real app we would show a MessageBox or Notification
                Console.WriteLine($"[ERROR] Recorder Engine: {ev.Exception?.Message}");
            });
        };
        
        _recorderEngine.FpsUpdated += (s, fps) =>
        {
            foreach (var overlay in _activeOverlays)
            {
                overlay.UpdateRealtimeFps(fps);
            }
        };
        
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "records.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _recordRepository = new RecordRepository(dbPath);
        
        Loaded += async (s, e) => 
        {
#if WINDOWS
            try
            {
                var handle = this.TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                {
                    // WDA_NONE = 0x00000000; ensure it is fully visible initially
                    SetWindowDisplayAffinity(handle.Value, 0x00000000);
                }
            }
            catch { }
#endif
            await _recordRepository.InitializeAsync();
            await LoadHistoryAsync();
            LoadDevices();
        };

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _previewTimer.Tick += PreviewTimer_Tick;
        _previewTimer.Start();
        
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _elapsedTimer.Tick += (s, e) =>
        {
            var elapsed = DateTime.Now - _recordingStartTime;
            ElapsedTimeText.Text = elapsed.ToString(@"hh\:mm\:ss");
        };

        _hardwareMonitor = new Zenith.UI.Utils.HardwareMonitor();
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += StatsTimer_Tick;
        _statsTimer.Start();

        AppVersionText.Text = $"Zenith v1.0.0";
        try {
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avcodec"] = 63;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avdevice"] = 63;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avfilter"] = 12;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avformat"] = 63;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["avutil"] = 61;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["swresample"] = 7;
            FFmpeg.AutoGen.ffmpeg.LibraryVersionMap["swscale"] = 10;
            FFmpeg.AutoGen.ffmpeg.RootPath = AppContext.BaseDirectory;
            FFmpegVersionText.Text = $"FFmpeg v{FFmpeg.AutoGen.ffmpeg.av_version_info()}";
        } catch { }
    }
    
    private void StatsTimer_Tick(object? sender, EventArgs e)
    {
        var (cpu, memMB, gpu) = _hardwareMonitor.GetStats();
        CpuUsageText.Text = $"CPU: {cpu:F1}%";
        MemUsageText.Text = $"Mem: {memMB:F0} MB";
        GpuUsageText.Text = $"GPU: {gpu:F1}%"; // Optional if implemented
    }
    
    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_recorderEngine == null || _recorderEngine.State != RecorderState.Idle)
        {
            if (PreviewImage != null)
            {
                var oldBitmap = PreviewImage.Source as IDisposable;
                PreviewImage.Source = null;
                oldBitmap?.Dispose();
            }
            return;
        }

        var baseLayer = VideoLayers.Count > 0 ? VideoLayers[0] : null;
        if (baseLayer == null || baseLayer.Type != LayerType.Screen || string.IsNullOrEmpty(baseLayer.SourceId) || baseLayer.Width == 0)
        {
            if (PreviewImage != null)
            {
                var oldBitmap = PreviewImage.Source as IDisposable;
                PreviewImage.Source = null;
                oldBitmap?.Dispose();
            }
            return;
        }

        System.Drawing.Rectangle captureRect = new System.Drawing.Rectangle(baseLayer.X, baseLayer.Y, baseLayer.Width, baseLayer.Height);

#if WINDOWS
        try
        {
            if (_previewWinBmp == null || _previewWinBmp.Width != captureRect.Width || _previewWinBmp.Height != captureRect.Height)
            {
                _previewWinBmp?.Dispose();
                _previewAvaloniaBmp?.Dispose();
                
                _previewWinBmp = new System.Drawing.Bitmap(captureRect.Width, captureRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                _previewAvaloniaBmp = new Avalonia.Media.Imaging.WriteableBitmap(
                    new Avalonia.PixelSize(captureRect.Width, captureRect.Height),
                    new Avalonia.Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888,
                    Avalonia.Platform.AlphaFormat.Premul);
            }

            if (PreviewImage != null && PreviewImage.Source != _previewAvaloniaBmp)
            {
                var old = PreviewImage.Source as IDisposable;
                PreviewImage.Source = _previewAvaloniaBmp;
                if (old != _previewAvaloniaBmp && old != null) old.Dispose();
            }

            using (var g = System.Drawing.Graphics.FromImage(_previewWinBmp))
            {
                g.CopyFromScreen(captureRect.X, captureRect.Y, 0, 0, _previewWinBmp.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var bmpData = _previewWinBmp.LockBits(new System.Drawing.Rectangle(0, 0, _previewWinBmp.Width, _previewWinBmp.Height), 
                                                  System.Drawing.Imaging.ImageLockMode.ReadOnly, 
                                                  _previewWinBmp.PixelFormat);
            using (var fb = _previewAvaloniaBmp?.Lock())
            {
                var fbSize = (fb?.RowBytes ?? 0) * (fb?.Size.Height ?? 0);
                var size = Math.Min(Math.Abs(bmpData.Stride) * _previewWinBmp.Height, fbSize);
                unsafe 
                {
                    Buffer.MemoryCopy((void*)bmpData.Scan0, (void*)(fb?.Address ?? nint.MinValue), size, size);
                }
            }
            _previewWinBmp.UnlockBits(bmpData);

            if (PreviewImage != null)
                PreviewImage.InvalidateVisual();
        }
        catch
        {
            // Ignore preview errors silently
        }
#endif
    }
    
    private void LoadDevices()
    {
        _gpuDevices = new List<GPUDevice>
        {
            new() { Name = "Auto", Id = "Auto" }
        };
        _gpuDevices.AddRange(_deviceEnumerator.GetGPUDevices());

        var audioDevices = new List<AudioSource>
        {
            new AudioSource { Name = Application.Current?.TryGetResource("Auto_SystemAudioDefault", out var sysAudio) == true ? sysAudio?.ToString() : "System Audio (Default)", Id = "default_system" },
            new AudioSource { Name = Application.Current?.TryGetResource("Auto_MicrophoneDefault", out var micAudio) == true ? micAudio?.ToString() : "Microphone (Default)", Id = "default_mic" }
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    audioDevices.Add(new AudioSource { Name = "[Output] " + endpoint.FriendlyName, Id = "sys|" + endpoint.ID });
                }
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
                {
                    audioDevices.Add(new AudioSource { Name = "[Input] " + endpoint.FriendlyName, Id = "mic|" + endpoint.ID });
                }
            }
            catch { }
        }

        AddAudioLayerTypeComboBox.ItemsSource = audioDevices;
        AddAudioLayerTypeComboBox.SelectedIndex = 0;
        
        foreach (var device in audioDevices)
        {
            var menuItem = new MenuItem { Header = device.Name, Tag = device };
            menuItem.Click += AddAudioLayerFromMenu_Click;
            MenuAddAudioSource.Items.Add(menuItem);
        }
        
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
    
    private async Task LoadHistoryAsync()
    {
        var records = await _recordRepository.GetAllAsync();
        var recordList = new List<Record>(records);
        HistoryListBox.ItemsSource = recordList;
        
        var hasHistory = recordList.Count > 0;
        HistoryListBox.IsVisible = hasHistory;
        EmptyHistoryPlaceholder.IsVisible = !hasHistory;
    }

    private void OnWaveformDataAvailable(object? sender, float maxAmplitude)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var rand = new Random();
            foreach (var layer in AudioLayers)
            {
                // Jitter the amplitude slightly so multiple sources don't look perfectly identical
                // In a real app with per-source capture, each layer would have its own event/amplitude.
                var jitter = (float)(rand.NextDouble() * 0.2 - 0.1); 
                var val = Math.Clamp(maxAmplitude + jitter, 0f, 1f);
                
                // Apply the volume slider setting
                layer.CurrentPeak = val * layer.Volume;
            }
        });
    }


    private void AddVideoLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (AddVideoLayerTypeComboBox.SelectedItem is ComboBoxItem item && item.Content != null)
        {
            var typeString = item.Content.ToString();
            LayerType type = typeString switch
            {
                "Screen" => LayerType.Screen,
                "Image" => LayerType.Image,
                "Video File" => LayerType.VideoFile,
                "Text" => LayerType.Text,
                "Camera" => LayerType.Camera,
                "FPS Counter" => LayerType.FpsCounter,
                _ => LayerType.Screen
            };
            
            var newLayer = new VideoLayer
            {
                Name = $"New {typeString}",
                Type = type
            };
            
            // Give a default text for text layers
            if (newLayer.Type == LayerType.Text)
            {
                newLayer.TextContent = "Hello World";
                var typeface = new Avalonia.Media.Typeface(newLayer.FontFamily ?? "Arial");
                var formattedText = new Avalonia.Media.FormattedText(
                    newLayer.TextContent,
                    System.Globalization.CultureInfo.CurrentCulture,
                    Avalonia.Media.FlowDirection.LeftToRight,
                    typeface,
                    newLayer.FontSize > 0 ? newLayer.FontSize : 48,
                    null);
                
                newLayer.Width = (int)Math.Ceiling(formattedText.Width) + 10;
                newLayer.Height = (int)Math.Ceiling(formattedText.Height) + 10;
                newLayer.X = 1920 / 2 - newLayer.Width / 2;
                newLayer.Y = 1080 / 2 - newLayer.Height / 2;
            }
            else if (newLayer.Type == LayerType.Screen)
            {
                newLayer.Width = 1920;
                newLayer.Height = 1080;
                newLayer.X = 0;
                newLayer.Y = 0;
            }
            else
            {
                // Default size for Camera, Image, Video File, FPS Counter: 1/3 of screen, centered
                newLayer.Width = 640;
                newLayer.Height = 360;
                newLayer.X = 1920 / 2 - newLayer.Width / 2;
                newLayer.Y = 1080 / 2 - newLayer.Height / 2;
            }
            
            AddVideoLayerSafely(newLayer);
        }
    }

    private void AddAudioLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (AddAudioLayerTypeComboBox.SelectedItem is AudioSource src)
        {
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
    }

    private void AddVideoLayerSafely(VideoLayer layer)
    {
        if (VideoLayers.Count >= 10)
        {
            // Optional: show a message box, but silent return is ok for now.
            return;
        }
        VideoLayers.Add(layer);
    }
    
    private void AddAudioLayerSafely(AudioLayer layer)
    {
        if (AudioLayers.Count >= 10)
        {
            return;
        }
        AudioLayers.Add(layer);
    }

    private void Menu_AddScreen_Click(object? sender, RoutedEventArgs e)
    {
        var screens = new List<VideoSource>(_deviceEnumerator.GetVideoSources());
        if (screens.Count > 0)
        {
            AddVideoLayerSafely(new VideoLayer { Name = screens[0].Name, Type = LayerType.Screen, SourceId = screens[0].Id });
        }
    }

    private void Menu_AddImage_Click(object? sender, RoutedEventArgs e) { AddVideoLayerSafely(new VideoLayer { Name = "Image Overlay", Type = LayerType.Image }); }
    private void Menu_AddVideoFile_Click(object? sender, RoutedEventArgs e) { AddVideoLayerSafely(new VideoLayer { Name = "Video Overlay", Type = LayerType.VideoFile }); }
    private void Menu_AddText_Click(object? sender, RoutedEventArgs e) { AddVideoLayerSafely(new VideoLayer { Name = "Text Overlay", Type = LayerType.Text }); }
    private void Menu_AddCamera_Click(object? sender, RoutedEventArgs e) { AddVideoLayerSafely(new VideoLayer { Name = "Webcam", Type = LayerType.Camera }); }
    private void Menu_AddFps_Click(object? sender, RoutedEventArgs e) { AddVideoLayerSafely(new VideoLayer { Name = "FPS Counter", Type = LayerType.FpsCounter }); }

    private void AddAudioLayerFromMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is AudioSource src)
        {
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
    }

    private void RemoveVideoLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (VideoLayersListBox.SelectedItem is VideoLayer selectedLayer)
        {
            VideoLayers.Remove(selectedLayer);
        }
    }

    private void RemoveAudioLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (AudioLayersListBox.SelectedItem is AudioLayer selectedLayer)
        {
            AudioLayers.Remove(selectedLayer);
        }
    }

    private void MoveUpVideoLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedIndex = VideoLayersListBox.SelectedIndex;
        if (selectedIndex > 0)
        {
            var item = VideoLayers[selectedIndex];
            VideoLayers.RemoveAt(selectedIndex);
            VideoLayers.Insert(selectedIndex - 1, item);
            VideoLayersListBox.SelectedIndex = selectedIndex - 1;
        }
    }

    private void MoveDownVideoLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedIndex = VideoLayersListBox.SelectedIndex;
        if (selectedIndex >= 0 && selectedIndex < VideoLayers.Count - 1)
        {
            var item = VideoLayers[selectedIndex];
            VideoLayers.RemoveAt(selectedIndex);
            VideoLayers.Insert(selectedIndex + 1, item);
            VideoLayersListBox.SelectedIndex = selectedIndex + 1;
        }
    }

    private void ToggleVisibilityButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VideoLayer layer)
        {
            layer.IsVisible = !layer.IsVisible;
            if (btn.Content is TextBlock tb)
            {
                tb.Text = layer.IsVisible ? "\uf06e" : "\uf070";
                tb.Foreground = new Avalonia.Media.SolidColorBrush(layer.IsVisible ? Avalonia.Media.Color.Parse("#AAAAAA") : Avalonia.Media.Color.Parse("#555555"));
            }
        }
    }

    private void ToggleLockButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VideoLayer layer)
        {
            layer.IsLocked = !layer.IsLocked;
            if (btn.Content is TextBlock tb)
            {
                tb.Text = layer.IsLocked ? "\uf023" : "\uf09c"; // lock : unlock
                tb.Foreground = new Avalonia.Media.SolidColorBrush(layer.IsLocked ? Avalonia.Media.Color.Parse("#AAAAAA") : Avalonia.Media.Color.Parse("#555555"));
            }
        }
    }

    private void EditVideoLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VideoLayer layer)
        {
            var propsWindow = new LayerPropertiesWindow(layer, _deviceEnumerator);
            propsWindow.ShowDialog(this);
        }
    }

    private async void RecordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_recorderEngine.State == RecorderState.Idle)
        {
#if WINDOWS
            try
            {
                var handle = this.TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                {
                    SetWindowDisplayAffinity(handle.Value, 0x00000001); // WDA_EXCLUDEFROMCAPTURE
                }
            }
            catch { }
#endif
            var dir = AppSaveLocation;
            if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            Directory.CreateDirectory(dir);
            var filename = Path.Combine(dir, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
            
            var config = new RecordingConfig
            {
                OutputPath = filename,
                Framerate = 60,
                UseHardwareAcceleration = AppUseHardwareAcceleration,
                SelectedGpuId = AppSelectedGpuId
            };
            
            // Copy current layers into config
            foreach(var vl in VideoLayers) config.VideoLayers.Add(vl);
            foreach(var al in AudioLayers) config.AudioLayers.Add(al);

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
            
            // Show countdown overlay before recording
            var countdownRegion = new System.Drawing.Rectangle(0, 0, config.Width, config.Height);
            var countdown = new CountdownOverlay(countdownRegion);
            countdown.Show();
            await countdown.RunCountdownAsync();
            
            await _recorderEngine.InitializeAsync(config);
            await _recorderEngine.StartAsync();

            _currentConfig = config;
            _recordingStartTime = DateTime.Now;
            _elapsedTimer.Start();

            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            // Spawn overlays for Camera and FPS Counter
            foreach (var layer in config.VideoLayers)
            {
                if (layer.Type == LayerType.Camera || layer.Type == LayerType.FpsCounter)
                {
                    if (layer.IsVisible)
                    {
                        var overlay = new LayerOverlayWindow(layer);
                        overlay.Show();
                        _activeOverlays.Add(overlay);
                    }
                }
            }

            ShowWidget_Click(null, null);
        }
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = StopRecordingAsync();
    }

    public async Task StopRecordingAsync()
    {
        if (_recorderEngine == null || _recorderEngine.State == RecorderState.Idle) return;

        StopButton.IsEnabled = false;

        _elapsedTimer.Stop();
        ElapsedTimeText.Text = "00:00:00";
        await _recorderEngine.StopAsync();
#if WINDOWS
        try
        {
            var handle = this.TryGetPlatformHandle()?.Handle;
            if (handle.HasValue && handle.Value != IntPtr.Zero)
            {
                SetWindowDisplayAffinity(handle.Value, 0x00000000); // WDA_NONE
            }
        }
        catch { }
#endif
        
        // Close and cleanup active overlays
        foreach (var overlay in _activeOverlays)
        {
            overlay.Close();
        }
        _activeOverlays.Clear();
        
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

        RecordButton.IsEnabled = true;
        
        await LoadHistoryAsync(); // Refresh history
    }

    private async Task<string> GenerateThumbnailAsync(string videoPath)
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

    private void ShowWidget_Click(object? sender, RoutedEventArgs e)
    {
        if (_widget == null)
        {
            _widget = new RecordingWidget(_recorderEngine, _recordingStartTime);
            _widget.Closed += (s, ev) =>
            {
                _widget = null;
                this.Show();
                if (_recorderEngine != null && _recorderEngine.State != RecorderState.Idle)
                {
                    _ = StopRecordingAsync();
                }
            };
        }

        _widget.Show();
        this.Hide();
    }

    private void AudioSourceComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
    }

    private async void BtnRefreshHistory_Click(object? sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void BtnClearHistory_Click(object? sender, RoutedEventArgs e)
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

    private async void Menu_Settings_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(AppSaveLocation, AppUseHardwareAcceleration, AppSelectedGpuId, _gpuDevices);
        var result = await settingsWindow.ShowDialog<bool>(this);
        if (result)
        {
            // Changes are applied inside SettingsWindow and saved to config
            AppSaveLocation = Zenith.UI.Utils.ConfigManager.CurrentConfig.SaveLocation;
            AppUseHardwareAcceleration = Zenith.UI.Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration;
            AppSelectedGpuId = Zenith.UI.Utils.ConfigManager.CurrentConfig.SelectedGpuId;
        }
    }

    private void Menu_ShowRecordings_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(AppSaveLocation))
        {
            try { Process.Start(new ProcessStartInfo { FileName = AppSaveLocation, UseShellExecute = true }); } catch { }
        }
    }

    private void Menu_Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Menu_CopyLayer_Click(object? sender, RoutedEventArgs e)
    {
        var editor = this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault();
        editor?.CopySelectedLayer();
    }

    private void Menu_CutLayer_Click(object? sender, RoutedEventArgs e)
    {
        var editor = this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault();
        editor?.CutSelectedLayer();
    }

    private void Menu_PasteLayer_Click(object? sender, RoutedEventArgs e)
    {
        var editor = this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault();
        editor?.PasteLayer();
    }

    private void Menu_DeleteLayer_Click(object? sender, RoutedEventArgs e)
    {
        var editor = this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault();
        editor?.DeleteSelectedLayer();
    }

    private async void Menu_About_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About Zenith",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var panel = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(20) };
        panel.Children.Add(new Avalonia.Controls.TextBlock { Text = "Zenith Screen Recorder", FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 16, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 0, 10) });
        panel.Children.Add(new Avalonia.Controls.TextBlock { Text = "Version 1.0.0", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        panel.Children.Add(new Avalonia.Controls.TextBlock { Text = "Powered by FFmpeg & Avalonia UI", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 10, 0, 0) });
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

	protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        foreach (var overlay in _activeOverlays)
        {
            overlay.Close();
        }
        _activeOverlays.Clear();
        
        _widget?.Close();
        _widget = null;

        _previewTimer?.Stop();
        
        _audioEngine.Stop();
        _audioEngine.Dispose();
        _recorderEngine.Dispose();
    }
}
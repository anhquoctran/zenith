using Avalonia;
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
using Path = System.IO.Path;

namespace Zenith.UI;

public class PathToBitmapConverter : Avalonia.Data.Converters.IValueConverter
{
    private static readonly System.Collections.Generic.Dictionary<string, Avalonia.Media.Imaging.Bitmap> _cache = new();
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
    private readonly Polyline _waveformLine;
    private readonly List<double> _amplitudes = [];
    private const int MaxSamples = 100;
    private readonly RecordRepository _recordRepository;
    private readonly IDeviceEnumerator _deviceEnumerator;
    private readonly DispatcherTimer _previewTimer;
    private System.Drawing.Rectangle? _selectedRegion;
    private readonly DispatcherTimer _elapsedTimer;
    private DispatcherTimer _statsTimer;
    private Zenith.UI.Utils.HardwareMonitor _hardwareMonitor;
    private DateTime _recordingStartTime;
    private CameraOverlayWindow? _cameraOverlay;
    private WebcamCaptureEngine? _sharedWebcamEngine;
    private Avalonia.Media.Imaging.WriteableBitmap? _webcamBitmap;
#if WINDOWS
    private System.Drawing.Bitmap? _previewWinBmp;
#endif
    private Avalonia.Media.Imaging.WriteableBitmap? _previewAvaloniaBmp;
    private RecordingConfig? _currentConfig;

    public MainWindow()
    {
        InitializeComponent();

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

		_waveformLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#007ACC")),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.Parse("#40007ACC")) // Add semi-transparent fill
        };
        WaveformCanvas.Children.Add(_waveformLine);

        _audioEngine = new AudioCaptureEngine();
        _audioEngine.WaveformDataAvailable += OnWaveformDataAvailable;
        _audioEngine.Start();
        
        // Initialize the native FFmpeg engine
        
        _recorderEngine.ErrorOccurred += (s, ev) => 
        {
            Dispatcher.UIThread.Post(() => 
            {
                // In a real app we would show a MessageBox or Notification
                Console.WriteLine($"[ERROR] Recorder Engine: {ev.Exception?.Message}");
            });
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
                    SetWindowDisplayAffinity(handle.Value, WDA_EXCLUDEFROMCAPTURE);
                }
            }
            catch { }
#endif
            SaveLocationTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Constants.OUTPUT_PREFIX_PATH);
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
        var stats = _hardwareMonitor.GetStats();
        CpuUsageText.Text = $"CPU: {stats.cpu:F1}%";
        MemUsageText.Text = $"Mem: {stats.memMB:F0} MB";
        // GpuUsageText.Text = $"GPU: {stats.gpu:F1}%"; // Optional if implemented
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
            return; // Paused preview to save memory
        }

		if (VideoSourceComboBox == null || VideoSourceComboBox.SelectedItem is not VideoSource selectedVideo || selectedVideo.Id == "None" || (selectedVideo.Id != "Region" && selectedVideo.Width == 0))
		{
			if (PreviewImage != null)
            {
                var oldBitmap = PreviewImage.Source as IDisposable;
			    PreviewImage.Source = null;
                oldBitmap?.Dispose();
            }
			return;
		}

		System.Drawing.Rectangle captureRect;
        if (selectedVideo.Id == "Region")
        {
            if (!_selectedRegion.HasValue || _selectedRegion.Value.Width <= 0 || _selectedRegion.Value.Height <= 0) return;
            captureRect = _selectedRegion.Value;
        }
        else
        {
            captureRect = new System.Drawing.Rectangle(selectedVideo.X, selectedVideo.Y, selectedVideo.Width, selectedVideo.Height);
        }

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
                    
                if (PreviewImage != null)
                {
                    var old = PreviewImage.Source as IDisposable;
                    PreviewImage.Source = _previewAvaloniaBmp;
                    if (old != _previewAvaloniaBmp) old?.Dispose();
                }
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
                var size = Math.Min(Math.Abs(bmpData.Stride) * _previewWinBmp.Height, fb?.RowBytes ?? 0 * fb?.Size.Height ?? 0);
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
        var gpus = new List<GPUDevice>
		{
			new() { Name = "Auto", Id = "Auto" }
		};
        gpus.AddRange(_deviceEnumerator.GetGPUDevices());
        GpuComboBox.ItemsSource = gpus;
        GpuComboBox.SelectedIndex = 0;
        var videoSources = new List<VideoSource>();
        videoSources.AddRange(_deviceEnumerator.GetVideoSources());
        if (videoSources.Count == 0) videoSources.Add(new VideoSource { Name = "No Displays Found", Id = "None" });
        
        videoSources.Add(new VideoSource { Name = "Region Select", Id = "Region" });
        
        VideoSourceComboBox.ItemsSource = videoSources;
        VideoSourceComboBox.SelectedIndex = 0;
        
        var cameras = new List<WebcamSource>(_deviceEnumerator.GetWebcams());
        if (cameras.Count == 0) cameras.Add(new WebcamSource { Name = "No Cameras Found" });
        CameraComboBox.ItemsSource = cameras;
        CameraComboBox.SelectedIndex = 0;
        
        var audio = new List<AudioSource>(_deviceEnumerator.GetAudioSources());
        if (audio.Count == 0) audio.Add(new AudioSource { Name = "No Audio Devices Found" });
        AudioSourceComboBox.ItemsSource = audio;
        AudioSourceComboBox.SelectedIndex = 0;
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
            _amplitudes.Add(maxAmplitude);
            if (_amplitudes.Count > MaxSamples)
            {
                _amplitudes.RemoveAt(0);
            }
            DrawWaveform();
        });
    }

    private void DrawWaveform()
    {
        var points = new List<Point>();
        var width = WaveformCanvas.Bounds.Width;
        var height = WaveformCanvas.Bounds.Height;

        // Handle initial zero bounds
        if (width <= 0 || height <= 0) return;

        var stepX = width / MaxSamples;
        var midY = height / 2;

        for (var i = 0; i < _amplitudes.Count; i++)
        {
            var x = i * stepX;
            var yOffset = _amplitudes[i] * midY;
            // Cap at midY so it doesn't overflow
            if (yOffset > midY) yOffset = midY;
            points.Add(new Point(x, midY - yOffset));
        }

        for (var i = _amplitudes.Count - 1; i >= 0; i--)
        {
            var x = i * stepX;
            var yOffset = _amplitudes[i] * midY;
            if (yOffset > midY) yOffset = midY;
            points.Add(new Point(x, midY + yOffset));
        }

        _waveformLine.Points = points;
    }

    private async void RecordButton_Click(object? sender, RoutedEventArgs e)
    {
        VideoSourceComboBox.IsEnabled = false;
        CameraComboBox.IsEnabled = false;
        AudioSourceComboBox.IsEnabled = false;
        HardwareAccelerationCheckBox.IsEnabled = false;
        GpuComboBox.IsEnabled = false;
        SelectFolderButton?.IsEnabled = false;

        var dir = SaveLocationTextBox.Text;
        if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        Directory.CreateDirectory(dir);
        var filename = Path.Combine(dir, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        
        var selectedVideo = VideoSourceComboBox.SelectedItem as VideoSource;
        var config = new RecordingConfig
        {
            OutputPath = filename,
            Framerate = 60,
            UseHardwareAcceleration = HardwareAccelerationCheckBox.IsChecked == true,
            SelectedGpuId = (GpuComboBox.SelectedItem as GPUDevice)?.Id ?? "Auto"
        };

        if (selectedVideo?.Id == "Region" && _selectedRegion.HasValue)
        {
            config.Width = _selectedRegion.Value.Width;
            config.Height = _selectedRegion.Value.Height;
            config.CaptureRegion = _selectedRegion;
        }
        else if (selectedVideo != null)
        {
            config.Width = selectedVideo.Width;
            config.Height = selectedVideo.Height;
            config.CaptureRegion = new System.Drawing.Rectangle(selectedVideo.X, selectedVideo.Y, selectedVideo.Width, selectedVideo.Height);
            
#if WINDOWS
            // Get the correct HMONITOR for the selected screen
            var centerPoint = new System.Drawing.Point(
                selectedVideo.X + selectedVideo.Width / 2,
                selectedVideo.Y + selectedVideo.Height / 2);
            config.MonitorHandle = MonitorFromPoint(centerPoint, 2 /* MONITOR_DEFAULTTONEAREST */);
#endif
        }
        else
        {
            config.Width = 1920;
            config.Height = 1080;
        }
        
        // Show countdown overlay before recording
        var countdownRegion = config.CaptureRegion ?? new System.Drawing.Rectangle(0, 0, config.Width, config.Height);
        var countdown = new CountdownOverlay(countdownRegion);
        countdown.Show();
        await countdown.RunCountdownAsync();
        
        await _recorderEngine.InitializeAsync(config);
        await _recorderEngine.StartAsync();

        _currentConfig = config;
        _recordingStartTime = DateTime.Now;
        _elapsedTimer.Start();
        ElapsedTimeText.IsVisible = true;
        
        // Hide preview camera overlay, show actual window corner overlay
        if (CameraPreviewOverlay != null)
            CameraPreviewOverlay.IsVisible = false;
        var selectedCamera = CameraComboBox.SelectedItem as WebcamSource;
        if (selectedCamera != null && selectedCamera.Name != "No Cameras Found" && selectedCamera.Id != "None")
        {
            if (_cameraOverlay == null)
            {
                _cameraOverlay = new CameraOverlayWindow();
                if (_webcamBitmap != null)
                {
                    _cameraOverlay.SetImageSource(_webcamBitmap);
                }
            }
            _cameraOverlay.Show();
        }

        RecordButton.IsEnabled = false;
        StopButton.IsEnabled = true;

        // Hide main window and show widget to act as system tray/floating mode
        ShowWidget_Click(null, null);
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = StopRecordingAsync();
    }

    public async Task StopRecordingAsync()
    {
        if (_recorderEngine == null || _recorderEngine.State == RecorderState.Idle) return;

        StopButton.IsEnabled = false;
        VideoSourceComboBox.IsEnabled = true;
        CameraComboBox.IsEnabled = true;
        AudioSourceComboBox.IsEnabled = true;
        HardwareAccelerationCheckBox.IsEnabled = true;
        GpuComboBox.IsEnabled = true;
        SelectFolderButton?.IsEnabled = true;

        _elapsedTimer.Stop();
        ElapsedTimeText.IsVisible = false;
        ElapsedTimeText.Text = "00:00:00";
        await _recorderEngine.StopAsync();
        
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

		// Hide window corner overlay, restore preview overlay
		_cameraOverlay?.Close();
		_cameraOverlay = null;
		if (CameraComboBox.SelectedItem is WebcamSource selectedCamera && selectedCamera.Name != "No Cameras Found" && selectedCamera.Id != "None")
		{
			CameraPreviewOverlay?.IsVisible = true;
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

    private async void SelectFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select Save Location",
            AllowMultiple = false
        });
        
        if (folders != null && folders.Count > 0)
        {
            var saveFolder = Path.Combine(folders[0].Path.LocalPath, Constants.OUTPUT_PREFIX_PATH);
            SaveLocationTextBox.Text = saveFolder;
        }
    }

    private void AudioSourceComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
    }

    private void OnWebcamFrameArrived(object? sender, FrameArrivedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_webcamBitmap == null || _webcamBitmap.PixelSize.Width != e.Width || _webcamBitmap.PixelSize.Height != e.Height)
                {
                    _webcamBitmap?.Dispose();
                    _webcamBitmap = new Avalonia.Media.Imaging.WriteableBitmap(
                        new PixelSize(e.Width, e.Height),
                        new Vector(96, 96),
                        Avalonia.Platform.PixelFormat.Bgra8888,
                        Avalonia.Platform.AlphaFormat.Premul);
                }

                using (var fb = _webcamBitmap.Lock())
                {
                    var size = Math.Min(e.Stride * e.Height, e.DataArray.Length);
					Marshal.Copy(e.DataArray, 0, fb.Address, size);
                }

                // Always ensure the source is bound (it may have been null if overlay was hidden during creation)
                if (CameraPreviewImage != null && CameraPreviewImage.Source != _webcamBitmap)
                    CameraPreviewImage.Source = _webcamBitmap;
                
                if (_cameraOverlay != null)
                    _cameraOverlay.SetImageSource(_webcamBitmap);

                // Force redraw
                CameraPreviewImage?.InvalidateVisual();
                _cameraOverlay?.InvalidateImage();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rendering webcam frame: {ex.Message}");
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(e.DataArray);
            }
        });
    }

    private async void VideoSourceComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var comboBox = sender as ComboBox;
        var selectedVideo = comboBox?.SelectedItem as VideoSource;
        if (selectedVideo?.Id == "Region") // Region
        {
            var overlay = new RegionSelectOverlay();
            overlay.Show();
            var region = await overlay.GetRegionAsync();
            if (region != null)
            {
                _selectedRegion = region;
                Console.WriteLine($"Selected Region: {region.Value}");
            }
            else
            {
                // Revert if canceled
                _selectedRegion = null;
                comboBox?.SelectedIndex = 0;
            }
        }
        else
        {
            _selectedRegion = null;
        }
        UpdateGpuWarning();
        PreviewTimer_Tick(null, EventArgs.Empty);
    }

    private void CameraComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var comboBox = sender as ComboBox;
        var selectedCamera = comboBox?.SelectedItem as WebcamSource;

        var hasCamera = selectedCamera != null && selectedCamera.Name != "No Cameras Found" && selectedCamera.Id != "None";
        var isRecording = _recorderEngine != null && _recorderEngine.State != RecorderState.Idle;

        if (hasCamera)
        {
            if (isRecording)
            {
                CameraPreviewOverlay?.IsVisible = false;
                if (_cameraOverlay == null)
                {
                    _cameraOverlay = new CameraOverlayWindow();
                    if (_webcamBitmap != null)
                    {
                        _cameraOverlay.SetImageSource(_webcamBitmap);
                    }
                }
                _cameraOverlay.Show();
            }
            else
            {
                CameraPreviewOverlay?.IsVisible = true;
				_cameraOverlay?.Close();
				_cameraOverlay = null;
			}

            if (_sharedWebcamEngine == null)
            {
                _sharedWebcamEngine = new WebcamCaptureEngine();
                _sharedWebcamEngine.FrameArrived += OnWebcamFrameArrived;
                var devName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "video=" + selectedCamera.Name : selectedCamera.Id;
                
                Task.Run(() => 
                {
                    try
                    {
                        _sharedWebcamEngine.Start(devName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MainWindow] Error starting webcam: {ex.Message}\n{ex.StackTrace}");
                    }
                });
            }
        }
        else
        {
            if (_sharedWebcamEngine != null)
            {
                _sharedWebcamEngine.FrameArrived -= OnWebcamFrameArrived;
                _sharedWebcamEngine.Dispose();
                _sharedWebcamEngine = null;
                
                _webcamBitmap?.Dispose();
                _webcamBitmap = null;
            }

            if (CameraPreviewOverlay != null)
                CameraPreviewOverlay.IsVisible = false;
            if (_cameraOverlay != null)
            {
                _cameraOverlay.Close();
                _cameraOverlay = null;
            }
        }
    }

    private void UpdateGpuWarning()
    {
        if (GpuComboBox == null || GpuWarningPanel == null) return;
        
        if (GpuComboBox.SelectedItem is GPUDevice selectedGpu &&
            VideoSourceComboBox.SelectedItem is VideoSource selectedVideo)
        {
            if (selectedGpu.Id != "Auto" && 
                !string.IsNullOrEmpty(selectedVideo.OwningGpuId) && 
                selectedGpu.Id != selectedVideo.OwningGpuId)
            {
                GpuWarningPanel.IsVisible = true;
            }
            else
            {
                GpuWarningPanel.IsVisible = false;
            }
        }
        else
        {
            GpuWarningPanel.IsVisible = false;
        }
    }

    private void GpuComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateGpuWarning();
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


	protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _previewTimer?.Stop();
        
        if (_sharedWebcamEngine != null)
        {
            _sharedWebcamEngine.FrameArrived -= OnWebcamFrameArrived;
            _sharedWebcamEngine.Dispose();
            _sharedWebcamEngine = null;
        }

        if (_cameraOverlay != null)
        {
            _cameraOverlay.Close();
            _cameraOverlay = null;
        }
        _audioEngine.Stop();
        _audioEngine.Dispose();
        _recorderEngine.Dispose();
    }
}
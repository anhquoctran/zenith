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
using Zenith.Core;
using Zenith.Data;
using Zenith.Interop;

namespace Zenith.UI;

public partial class MainWindow : Window
{
#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);
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
    private DateTime _recordingStartTime;

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
        _recorderEngine = new FFmpegRecorderEngine();
        _recorderEngine.ErrorOccurred += (s, ev) => 
        {
            Dispatcher.UIThread.Post(() => 
            {
                // In a real app we would show a MessageBox or Notification
                Console.WriteLine($"[ERROR] Recorder Engine: {ev.Exception?.Message}");
            });
        };
        
        var dbPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "records.db");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _recordRepository = new RecordRepository(dbPath);
        
        Loaded += async (s, e) => 
        {
            SaveLocationTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
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
    }
    
    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_recorderEngine.State != RecorderState.Idle)
            return; // Don't steal CPU during active recording

		if (VideoSourceComboBox.SelectedItem is not VideoSource selectedVideo || selectedVideo.Id == "None" || (selectedVideo.Id != "Region" && selectedVideo.Width == 0))
		{
			PreviewImage.Source = null;
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
            using var bmp = new System.Drawing.Bitmap(captureRect.Width, captureRect.Height);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(captureRect.X, captureRect.Y, 0, 0, bmp.Size, System.Drawing.CopyPixelOperation.SourceCopy);
            
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            ms.Position = 0;
            PreviewImage.Source = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch
        {
            // Ignore preview errors silently
        }
#endif
    }
    
    private void LoadDevices()
    {
        var gpus = new List<GPUDevice>();
        gpus.Add(new GPUDevice { Name = "Auto (Zero-Copy)", Id = "Auto" });
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
    
    private async System.Threading.Tasks.Task LoadHistoryAsync()
    {
        var records = await _recordRepository.GetAllAsync();
        HistoryListBox.ItemsSource = records;
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
        var dir = SaveLocationTextBox.Text;
        if (string.IsNullOrEmpty(dir)) dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var filename = System.IO.Path.Combine(dir, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        
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

        _recordingStartTime = DateTime.Now;
        _elapsedTimer.Start();
        ElapsedTimeText.IsVisible = true;
        
        RecordButton.IsEnabled = false;
        StopButton.IsEnabled = true;
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        _elapsedTimer.Stop();
        ElapsedTimeText.IsVisible = false;
        ElapsedTimeText.Text = "00:00:00";
        await _recorderEngine.StopAsync();
        RecordButton.IsEnabled = true;
        
        await LoadHistoryAsync(); // Refresh history
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
            SaveLocationTextBox.Text = folders[0].Path.LocalPath;
        }
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
                comboBox.SelectedIndex = 0;
            }
        }
        else
        {
            _selectedRegion = null;
        }
        UpdateGpuWarning();
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

    protected override void OnClosed(System.EventArgs e)
    {
        _audioEngine.Stop();
        _audioEngine.Dispose();
        _recorderEngine.Dispose();
        base.OnClosed(e);
    }
}
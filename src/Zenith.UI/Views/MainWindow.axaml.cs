using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Zenith.Core;
using Zenith.UI.Controls;
using Zenith.UI.ViewModels;

namespace Zenith.UI.Views;

public class PathToBitmapConverter : Avalonia.Data.Converters.IValueConverter
{
    private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _cache = [];
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        if (_cache.TryGetValue(path, out var bmp)) return bmp;
        try
        {
            bmp = new Avalonia.Media.Imaging.Bitmap(path);
            _cache[path] = bmp;
            return bmp;
        }
        catch { return null; }
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
    private readonly DispatcherTimer _previewTimer;
    private readonly List<LayerOverlayWindow> _activeOverlays = [];
#if WINDOWS
    private System.Drawing.Bitmap? _previewWinBmp;
#endif
    private Avalonia.Media.Imaging.WriteableBitmap? _previewAvaloniaBmp;

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        var vm = new MainWindowViewModel();
        DataContext = vm;
        
        InitializeComponent();

        LayerEditor.Setup(vm.VideoLayers);

        VideoLayersListBox.ItemsSource = vm.VideoLayers;
        VideoLayersListBox.SelectionChanged += (s, e) =>
        {
            if (VideoLayersListBox.SelectedItem is VideoLayer layer)
            {
                vm.SelectedVideoLayer = layer;
            }
        };

        AudioLayersListBox.ItemsSource = vm.AudioLayers;

        vm.RecorderEngine.FpsUpdated += (s, fps) =>
        {
            foreach (var overlay in _activeOverlays)
                overlay.UpdateRealtimeFps(fps);
        };

        Loaded += async (s, e) =>
        {
#if WINDOWS
            try
            {
                var handle = this.TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                    SetWindowDisplayAffinity(handle.Value, 0x00000000);
            }
            catch { }
#endif
            await vm.InitializeAsync();
            
            // Setup audio source combo box and menus after devices are loaded
            AddAudioLayerTypeComboBox.ItemsSource = vm.AudioDevices;
            AddAudioLayerTypeComboBox.SelectedIndex = 0;
            
            foreach (var device in vm.AudioDevices)
            {
                var menuItem = new MenuItem { Header = device.Name, Tag = device };
                menuItem.Click += AddAudioLayerFromMenu_Click;
                MenuAddAudioSource.Items.Add(menuItem);
            }
        };

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _previewTimer.Tick += PreviewTimer_Tick;
        _previewTimer.Start();

        // Bind status text updates from ViewModel
        vm.PropertyChanged += (s, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(vm.ElapsedTime):
                    ElapsedTimeText.Text = vm.ElapsedTime;
                    break;
                case nameof(vm.CpuUsage):
                    CpuUsageText.Text = vm.CpuUsage;
                    break;
                case nameof(vm.MemUsage):
                    MemUsageText.Text = vm.MemUsage;
                    break;
                case nameof(vm.GpuUsage):
                    GpuUsageText.Text = vm.GpuUsage;
                    break;
                case nameof(vm.AppVersion):
                    AppVersionText.Text = vm.AppVersion;
                    break;
                case nameof(vm.FFmpegVersion):
                    FFmpegVersionText.Text = vm.FFmpegVersion;
                    break;
                case nameof(vm.IsRecording):
                    RecordButton.IsEnabled = vm.CanRecord;
                    StopButton.IsEnabled = vm.CanStop;
                    break;
                case nameof(vm.Records):
                    HistoryListBox.ItemsSource = vm.Records;
                    HistoryListBox.IsVisible = vm.HasHistory;
                    EmptyHistoryPlaceholder.IsVisible = !vm.HasHistory;
                    break;
                case nameof(vm.HasHistory):
                    HistoryListBox.IsVisible = vm.HasHistory;
                    EmptyHistoryPlaceholder.IsVisible = !vm.HasHistory;
                    break;
            }
        };

        AppVersionText.Text = vm.AppVersion;
        FFmpegVersionText.Text = vm.FFmpegVersion;
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (ViewModel.RecorderEngine.State != RecorderState.Idle)
        {
            if (PreviewImage != null)
            {
                var oldBitmap = PreviewImage.Source as IDisposable;
                PreviewImage.Source = null;
                oldBitmap?.Dispose();
            }
            return;
        }

        var baseLayer = ViewModel.VideoLayers.Count > 0 ? ViewModel.VideoLayers[0] : null;
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
                // Do NOT manually dispose _previewAvaloniaBmp here because the Avalonia render thread might be actively drawing it.
                // Let the garbage collector handle it safely.
                
                _previewWinBmp = new System.Drawing.Bitmap(captureRect.Width, captureRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                _previewAvaloniaBmp = new Avalonia.Media.Imaging.WriteableBitmap(
                    new PixelSize(captureRect.Width, captureRect.Height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888,
                    Avalonia.Platform.AlphaFormat.Premul);
            }

            if (PreviewImage != null && PreviewImage.Source != _previewAvaloniaBmp)
            {
                PreviewImage.Source = _previewAvaloniaBmp;
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

    // Button click handlers that delegate to ViewModel
    private void AddVideoLayerButton_Click(object? sender, RoutedEventArgs e) => ViewModel.AddVideoLayerCommand.Execute(null);
    private void RemoveVideoLayerButton_Click(object? sender, RoutedEventArgs e) => ViewModel.RemoveVideoLayerCommand.Execute(null);
    private void MoveUpVideoLayerButton_Click(object? sender, RoutedEventArgs e) => ViewModel.MoveUpVideoLayerCommand.Execute(null);
    private void MoveDownVideoLayerButton_Click(object? sender, RoutedEventArgs e) => ViewModel.MoveDownVideoLayerCommand.Execute(null);
    
    private void AddAudioLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (AddAudioLayerTypeComboBox.SelectedItem is AudioSource src)
        {
            ViewModel.AddAudioLayerFromSource(src);
        }
    }
    
    private void RemoveAudioLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (AudioLayersListBox.SelectedItem is AudioLayer al)
        {
            ViewModel.SelectedAudioLayer = al;
            ViewModel.RemoveAudioLayerCommand.Execute(null);
        }
    }

    private void AddAudioLayerFromMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is AudioSource src)
            ViewModel.AddAudioLayerFromSource(src);
    }

    // Layer UI handlers
    private void ToggleVisibilityButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VideoLayer layer)
        {
            ViewModel.ToggleLayerVisibility(layer);
            if (btn.Content is TextBlock tb)
            {
                tb.Text = layer.IsVisible ? "\uf06e" : "\uf070";
                tb.Foreground = new SolidColorBrush(layer.IsVisible ? Color.Parse("#AAAAAA") : Color.Parse("#555555"));
            }
        }
    }

    private void ToggleLockButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VideoLayer layer)
        {
            ViewModel.ToggleLayerLock(layer);
            if (btn.Content is TextBlock tb)
            {
                tb.Text = layer.IsLocked ? "\uf023" : "\uf09c";
                tb.Foreground = new SolidColorBrush(layer.IsLocked ? Color.Parse("#AAAAAA") : Color.Parse("#555555"));
            }
        }
    }

    private void EditVideoLayerButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VideoLayer layer)
        {
            var propsWindow = new LayerPropertiesWindow(layer, ViewModel.DeviceEnumerator);
            propsWindow.ShowDialog(this);
        }
    }

    // Menu handlers
    private void Menu_AddScreen_Click(object? sender, RoutedEventArgs e) => ViewModel.AddScreenLayer();
    private void Menu_AddImage_Click(object? sender, RoutedEventArgs e) => ViewModel.AddImageLayer();
    private void Menu_AddVideoFile_Click(object? sender, RoutedEventArgs e) => ViewModel.AddVideoFileLayer();
    private void Menu_AddText_Click(object? sender, RoutedEventArgs e) => ViewModel.AddTextLayer();
    private void Menu_AddCamera_Click(object? sender, RoutedEventArgs e) => ViewModel.AddCameraLayer();
    private void Menu_AddFps_Click(object? sender, RoutedEventArgs e) => ViewModel.AddFpsLayer();

    // Recording (platform-specific)
    private async void RecordButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.RecorderEngine.State == RecorderState.Idle)
        {
#if WINDOWS
            try
            {
                var handle = this.TryGetPlatformHandle()?.Handle;
                if (handle.HasValue && handle.Value != IntPtr.Zero)
                    SetWindowDisplayAffinity(handle.Value, 0x00000001);
            }
            catch { }
#endif
            var config = ViewModel.PrepareRecordingConfig();

            var countdownRegion = new System.Drawing.Rectangle(0, 0, config.Width, config.Height);
            var countdown = new CountdownOverlay(countdownRegion);
            countdown.Show();
            await countdown.RunCountdownAsync();

            await ViewModel.RecorderEngine.InitializeAsync(config);
            await ViewModel.RecorderEngine.StartAsync();
            
            ViewModel.OnRecordingStarted();

            // Spawn overlays
            foreach (var layer in config.VideoLayers)
            {
                if ((layer.Type == LayerType.Camera || layer.Type == LayerType.FpsCounter || layer.Type == LayerType.Image || layer.Type == LayerType.Text) && layer.IsVisible)
                {
                    var overlay = new LayerOverlayWindow(layer);
                    overlay.Show();
                    _activeOverlays.Add(overlay);
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
        if (ViewModel.RecorderEngine.State == RecorderState.Idle) return;

#if WINDOWS
        try
        {
            var handle = this.TryGetPlatformHandle()?.Handle;
            if (handle.HasValue && handle.Value != IntPtr.Zero)
                SetWindowDisplayAffinity(handle.Value, 0x00000000);
        }
        catch { }
#endif

        foreach (var overlay in _activeOverlays)
            overlay.Close();
        _activeOverlays.Clear();

        await ViewModel.OnRecordingStoppedAsync();
    }

    private void ShowWidget_Click(object? sender, RoutedEventArgs e)
    {
        if (_widget == null)
        {
            _widget = new RecordingWidget(ViewModel.RecorderEngine, ViewModel.RecordingStartTime);
            _widget.Closed += (s, ev) =>
            {
                _widget = null;
                this.Show();
                if (ViewModel.RecorderEngine.State != RecorderState.Idle)
                    _ = StopRecordingAsync();
            };
        }

        _widget.Show();
        this.Hide();
    }

    private void AudioSourceComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }

    private async void BtnRefreshHistory_Click(object? sender, RoutedEventArgs e) => await ViewModel.LoadHistoryAsync();
    private async void BtnClearHistory_Click(object? sender, RoutedEventArgs e) => ViewModel.ClearHistoryCommand.Execute(null);

    private async void Menu_Settings_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(
            ViewModel.AppSaveLocation, ViewModel.AppUseHardwareAcceleration,
            ViewModel.AppSelectedGpuId, ViewModel.GpuDevices);
        var result = await settingsWindow.ShowDialog<bool>(this);
        if (result)
        {
            ViewModel.AppSaveLocation = Utils.ConfigManager.CurrentConfig.SaveLocation;
            ViewModel.AppUseHardwareAcceleration = Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration;
            ViewModel.AppSelectedGpuId = Utils.ConfigManager.CurrentConfig.SelectedGpuId;
        }
    }

    private void Menu_ShowRecordings_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.AppSaveLocation))
        {
            try { Process.Start(new ProcessStartInfo { FileName = ViewModel.AppSaveLocation, UseShellExecute = true }); } catch { }
        }
    }

    private void Menu_Exit_Click(object? sender, RoutedEventArgs e) => Close();

    private void Menu_CopyLayer_Click(object? sender, RoutedEventArgs e) => this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault()?.CopySelectedLayer();
    private void Menu_CutLayer_Click(object? sender, RoutedEventArgs e) => this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault()?.CutSelectedLayer();
    private void Menu_PasteLayer_Click(object? sender, RoutedEventArgs e) => this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault()?.PasteLayer();
    private void Menu_DeleteLayer_Click(object? sender, RoutedEventArgs e) => this.GetVisualDescendants().OfType<VisualLayerEditor>().FirstOrDefault()?.DeleteSelectedLayer();

    private async void Menu_About_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About Zenith",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            CanMaximize = false,
            CanMinimize = false,
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Zenith Screen Recorder", FontWeight = FontWeight.Bold, FontSize = 16, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "Version 1.0.0", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Powered by FFmpeg & Avalonia UI", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    protected override async void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (ViewModel.RecorderEngine.State != RecorderState.Idle && ViewModel.RecorderEngine.State != RecorderState.Stopped)
        {
            // Cancel immediate close and properly stop recording first
            e.Cancel = true;
            
            // Disable window interaction while stopping
            this.IsEnabled = false;
            
            await StopRecordingAsync();
            
            // Trigger close again now that recording is stopped
            Close();
            return;
        }
        base.OnClosing(e);
    }

	protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        foreach (var overlay in _activeOverlays)
            overlay.Close();
        _activeOverlays.Clear();
        
        _widget?.Close();
        _widget = null;
        _previewTimer?.Stop();
        
        ViewModel.Dispose();
    }
}
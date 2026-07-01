using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using Zenith.Core;
using Zenith.Interop;

namespace Zenith.UI.Views;

public partial class LayerOverlayWindow : Window
{
    private readonly VideoLayer _layer;
    private WebcamCaptureEngine? _webcam;
    private WriteableBitmap? _bitmap;

    public LayerOverlayWindow()
    {
        InitializeComponent();
        _layer = new VideoLayer();
    }

    public LayerOverlayWindow(VideoLayer layer)
    {
        InitializeComponent();
        _layer = layer;

        Width = layer.Width > 0 ? layer.Width : 256;
        Height = layer.Height > 0 ? layer.Height : 144;
        
        if (layer is { X: >= 0, Y: >= 0 })
        {
            Position = new PixelPoint(layer.X, layer.Y);
        }
        else
        {
            // Position at bottom right as default
            var screen = this.Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                var x = workingArea.Right - this.Width - 20;
                var y = workingArea.Bottom - this.Height - 20;
                this.Position = new PixelPoint((int)x, (int)y);
            }
        }

        if (_layer.Type == LayerType.Camera)
        {
            CameraBorder.IsVisible = true;
            _webcam = new WebcamCaptureEngine();
            _webcam.FrameArrived += OnWebcamFrame;
            
            try
            {
                // Try to start webcam
                var deviceId = string.IsNullOrEmpty(_layer.SourceId) ? "video=Integrated Webcam" : _layer.SourceId;
                _webcam.Start(deviceId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start webcam overlay: {ex.Message}");
            }
        }
        else if (_layer.Type == LayerType.FpsCounter)
        {
            FpsText.IsVisible = true;
            FpsText.FontFamily = new FontFamily(_layer.FontFamily ?? "Arial");
            FpsText.FontSize = _layer.FontSize > 0 ? _layer.FontSize : 24;
            
            try
            {
                if (!string.IsNullOrEmpty(_layer.FontColor))
                {
                    FpsText.Foreground = new SolidColorBrush(Color.Parse(_layer.FontColor));
                }
            }
            catch { FpsText.Foreground = Brushes.LimeGreen; }

            FpsText.Text = "FPS: 0";
        }
        else if (_layer.Type == LayerType.Text)
        {
            TextLayerContent.IsVisible = true;
            TextLayerContent.Text = _layer.TextContent;
            TextLayerContent.FontFamily = new FontFamily(_layer.FontFamily ?? "Arial");
            TextLayerContent.FontSize = _layer.FontSize > 0 ? _layer.FontSize : 48;
            
            try
            {
                if (!string.IsNullOrEmpty(_layer.FontColor))
                {
                    TextLayerContent.Foreground = new SolidColorBrush(Color.Parse(_layer.FontColor));
                }
            }
            catch { TextLayerContent.Foreground = Brushes.White; }
            
            switch (_layer.TextAlignment?.ToLowerInvariant())
            {
                case "center": TextLayerContent.TextAlignment = TextAlignment.Center; break;
                case "right": TextLayerContent.TextAlignment = TextAlignment.Right; break;
                default: TextLayerContent.TextAlignment = TextAlignment.Left; break;
            }

            if (_layer.FontStyle?.ToLowerInvariant() == "italic")
                TextLayerContent.FontStyle = FontStyle.Italic;
                
            if (_layer.FontWeight?.ToLowerInvariant() == "bold")
                TextLayerContent.FontWeight = FontWeight.Bold;
        }
        else if (_layer.Type == LayerType.Image && !string.IsNullOrEmpty(_layer.FilePath) && System.IO.File.Exists(_layer.FilePath))
        {
            ImageLayerContent.IsVisible = true;
            try
            {
                ImageLayerContent.Source = new Avalonia.Media.Imaging.Bitmap(_layer.FilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load image layer: {ex.Message}");
            }
        }
    }
    
    public void UpdateRealtimeFps(int fps)
    {
        if (_layer.Type == LayerType.FpsCounter)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FpsText.Text = $"FPS: {fps}";
            });
        }
    }

    private void OnWebcamFrame(object? sender, FrameArrivedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_bitmap == null || _bitmap.PixelSize.Width != e.Width || _bitmap.PixelSize.Height != e.Height)
            {
                _bitmap = new WriteableBitmap(
                    new PixelSize(e.Width, e.Height),
                    new Vector(96, 96),
                    Avalonia.Platform.PixelFormat.Bgra8888,
                    Avalonia.Platform.AlphaFormat.Premul);
                
                CameraImage.Source = _bitmap;
            }

            using (var buf = _bitmap.Lock())
            {
                Marshal.Copy(e.DataArray, 0, buf.Address, e.DataArray.Length);
            }
            CameraImage.InvalidateVisual();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_webcam == null) return;
        _webcam.FrameArrived -= OnWebcamFrame;
        _webcam.Dispose();
        _webcam = null;
    }
}

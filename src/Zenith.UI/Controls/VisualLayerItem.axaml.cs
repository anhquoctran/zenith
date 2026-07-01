using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Zenith.Core;
using System.ComponentModel;

namespace Zenith.UI.Controls;

public partial class VisualLayerItem : UserControl
{
    private VideoLayer? _layer;

    public VisualLayerItem()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_layer != null)
        {
            _layer.PropertyChanged -= Layer_PropertyChanged;
        }

        _layer = DataContext as VideoLayer;

        if (_layer != null)
        {
            _layer.PropertyChanged += Layer_PropertyChanged;
            UpdateVisuals();
        }
    }

    private void Layer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateVisuals();
    }

    public void UpdateVisuals()
    {
        if (_layer == null) return;

        // Apply Transforms
        var transformGroup = new TransformGroup();
        
        if (_layer.FlipHorizontal || _layer.FlipVertical)
        {
            transformGroup.Children.Add(new ScaleTransform(
                _layer.FlipHorizontal ? -1 : 1,
                _layer.FlipVertical ? -1 : 1
            ));
        }

        if (_layer.RotationAngle != 0)
        {
            transformGroup.Children.Add(new RotateTransform(_layer.RotationAngle));
        }

        TransformBorder.RenderTransform = transformGroup;

        // Reset visibility
        ScreenImage.IsVisible = false;
        MediaImage.IsVisible = false;
        TextDisplay.IsVisible = false;
        CameraPlaceholder.IsVisible = false;
        if (!TextEditor.IsVisible) TextEditor.IsVisible = false;

        switch (_layer.Type)
        {
            case LayerType.Screen:
                ScreenImage.IsVisible = true;
                // The parent editor will feed the preview bitmap to ScreenImage.
                break;

            case LayerType.Image:
                MediaImage.IsVisible = true;
                try
                {
                    if (!string.IsNullOrEmpty(_layer.FilePath) && System.IO.File.Exists(_layer.FilePath))
                    {
                        MediaImage.Source = new Bitmap(_layer.FilePath);
                    }
                }
                catch { }
                break;

            case LayerType.Camera:
                CameraPlaceholder.IsVisible = true;
                CameraPlaceholderText.Text = string.IsNullOrEmpty(_layer.Name) ? "Camera" : _layer.Name;
                break;

            case LayerType.FpsCounter:
                TextDisplay.IsVisible = true;
                TextDisplay.Text = "FPS: 60";
                TextDisplay.Foreground = Brushes.LimeGreen;
                TextDisplay.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
                ApplyTextFormatting();
                break;

            case LayerType.Text:
                if (!TextEditor.IsVisible)
                {
                    TextDisplay.IsVisible = true;
                }
                ApplyTextFormatting();
                break;
        }
    }

    private void ApplyTextFormatting()
    {
        if (_layer == null) return;

        TextDisplay.FontFamily = new FontFamily(_layer.FontFamily ?? "Arial");
        TextEditor.FontFamily = TextDisplay.FontFamily;

        if (!string.IsNullOrEmpty(_layer.FontColor))
        {
            try
            {
                var color = Color.Parse(_layer.FontColor);
                TextDisplay.Foreground = new SolidColorBrush(color);
                TextEditor.Foreground = new SolidColorBrush(color);
            }
            catch { }
        }

        if (Enum.TryParse<Avalonia.Media.FontStyle>(_layer.FontStyle, out var style))
        {
            TextDisplay.FontStyle = style;
            TextEditor.FontStyle = style;
        }

        if (Enum.TryParse<Avalonia.Media.FontWeight>(_layer.FontWeight, out var weight))
        {
            TextDisplay.FontWeight = weight;
            TextEditor.FontWeight = weight;
        }

        if (Enum.TryParse<Avalonia.Media.TextAlignment>(_layer.TextAlignment, out var align))
        {
            TextDisplay.TextAlignment = align;
            TextEditor.TextAlignment = align;
        }
    }

    public void BeginInlineEdit()
    {
        if (_layer != null && _layer.Type == LayerType.Text)
        {
            TextDisplay.IsVisible = false;
            TextEditor.IsVisible = true;
            TextEditor.Focus();
            // Select all text to make it easy to type over
            TextEditor.SelectAll();
        }
    }

    private void TextDisplay_DoubleTapped(object? sender, TappedEventArgs e)
    {
        BeginInlineEdit();
        e.Handled = true;
    }

    private void TextEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        EndTextEditing();
    }

    private void TextEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
        {
            EndTextEditing();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Revert changes
            if (_layer != null)
                TextEditor.Text = _layer.TextContent;
            EndTextEditing();
            e.Handled = true;
        }
    }

    private void EndTextEditing()
    {
        if (_layer != null && TextEditor.IsVisible)
        {
            _layer.TextContent = TextEditor.Text ?? "";
        }
        TextEditor.IsVisible = false;
        TextDisplay.IsVisible = true;
    }

    // Context Menu Handlers (These delegate back to the parent editor or manipulate the layer directly)

    private void BringToFront_Click(object? sender, RoutedEventArgs e)
    {
        if (_layer == null) return;
        var parentEditor = this.FindAncestorOfType<VisualLayerEditor>();
        if (parentEditor?.VideoLayers != null)
        {
            parentEditor.VideoLayers.Remove(_layer);
            parentEditor.VideoLayers.Add(_layer); // Adds to end (top)
        }
    }

    private void SendToBack_Click(object? sender, RoutedEventArgs e)
    {
        if (_layer == null) return;
        var parentEditor = this.FindAncestorOfType<VisualLayerEditor>();
        if (parentEditor?.VideoLayers != null)
        {
            // Index 0 is the screen layer, usually. We should probably place it at index 1.
            parentEditor.VideoLayers.Remove(_layer);
            parentEditor.VideoLayers.Insert(Math.Min(1, parentEditor.VideoLayers.Count), _layer);
        }
    }

    private void Rotate90_Click(object? sender, RoutedEventArgs e)
    {
        if (_layer != null)
            _layer.RotationAngle = (_layer.RotationAngle + 90) % 360;
    }

    private void FlipHorizontal_Click(object? sender, RoutedEventArgs e)
    {
        if (_layer != null)
            _layer.FlipHorizontal = !_layer.FlipHorizontal;
    }

    private void FlipVertical_Click(object? sender, RoutedEventArgs e)
    {
        if (_layer != null)
            _layer.FlipVertical = !_layer.FlipVertical;
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (_layer == null) return;
        var parentEditor = this.FindAncestorOfType<VisualLayerEditor>();
        if (parentEditor?.VideoLayers != null)
        {
            parentEditor.VideoLayers.Remove(_layer);
        }
    }

    private T? FindAncestorOfType<T>() where T : Control
    {
        Control? current = this.Parent as Control;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = current.Parent as Control;
        }
        return null;
    }
}

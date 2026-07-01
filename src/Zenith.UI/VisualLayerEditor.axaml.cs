using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text.Json;
using Zenith.Core;

namespace Zenith.UI;

public partial class VisualLayerEditor : UserControl
{
    public ObservableCollection<VideoLayer>? VideoLayers { get; set; }
    public int RecordingWidth { get; set; } = 1920;
    public int RecordingHeight { get; set; } = 1080;

    private VideoLayer? _selectedLayer;
    private bool _isDragging;
    private bool _isResizing;
    private string _resizeHandle = "";
    private Point _lastMousePosition;
    
    private static string? _clipboardJson;
    
    // Adorner visuals
    private Rectangle? _selectionBox;
    private Rectangle[] _resizeHandles = new Rectangle[8];

    public VisualLayerEditor()
    {
        InitializeComponent();
        
        // Setup Adorner Visuals
        _selectionBox = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.Parse("#007ACC")),
            StrokeThickness = 2,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 },
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 122, 204)),
            IsHitTestVisible = true,
            Cursor = new Cursor(StandardCursorType.SizeAll)
        };
        _selectionBox.PointerPressed += SelectionBox_PointerPressed;
        
        for (int i = 0; i < 8; i++)
        {
            _resizeHandles[i] = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(Color.Parse("#007ACC")),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                IsHitTestVisible = true
            };
            
            // Assign cursors based on position
            StandardCursorType cursorType = StandardCursorType.Arrow;
            switch(i)
            {
                case 0: cursorType = StandardCursorType.TopLeftCorner; break; // TL
                case 1: cursorType = StandardCursorType.TopSide; break; // T
                case 2: cursorType = StandardCursorType.TopRightCorner; break; // TR
                case 3: cursorType = StandardCursorType.RightSide; break; // R
                case 4: cursorType = StandardCursorType.BottomRightCorner; break; // BR
                case 5: cursorType = StandardCursorType.BottomSide; break; // B
                case 6: cursorType = StandardCursorType.BottomLeftCorner; break; // BL
                case 7: cursorType = StandardCursorType.LeftSide; break; // L
            }
            _resizeHandles[i].Cursor = new Cursor(cursorType);
            
            int handleIndex = i;
            _resizeHandles[i].PointerPressed += (s, e) => Handle_PointerPressed(s, e, handleIndex);
        }

        EditorCanvas.SizeChanged += (s, e) => UpdateAdorners();
        
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (s, e) => UpdateAdorners();
        timer.Start();
    }

    public void Setup(ObservableCollection<VideoLayer> layers)
    {
        VideoLayers = layers;
        VideoLayers.CollectionChanged += VideoLayers_CollectionChanged;
        AttachPropertyListeners();
    }

    private void VideoLayers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachPropertyListeners();
    }

    private void AttachPropertyListeners()
    {
        if (VideoLayers == null) return;
        foreach (var layer in VideoLayers)
        {
            layer.PropertyChanged -= Layer_PropertyChanged;
            layer.PropertyChanged += Layer_PropertyChanged;
        }
    }

    private void Layer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoLayer.IsSelected))
        {
            var layer = sender as VideoLayer;
            if (layer != null && layer.IsSelected)
            {
                _selectedLayer = layer;
                // Deselect others
                foreach (var l in VideoLayers!)
                {
                    if (l != layer) l.IsSelected = false;
                }
            }
            else if (layer == _selectedLayer && !layer!.IsSelected)
            {
                _selectedLayer = null;
            }
        }
    }

    private double ScaleX => EditorCanvas.Bounds.Width / RecordingWidth;
    private double ScaleY => EditorCanvas.Bounds.Height / RecordingHeight;

    private void UpdateAdorners()
    {
        if (_selectedLayer == null || EditorCanvas.Bounds.Width == 0)
        {
            if (EditorCanvas.Children.Contains(_selectionBox!))
            {
                EditorCanvas.Children.Remove(_selectionBox!);
                for (int i = 0; i < 8; i++) EditorCanvas.Children.Remove(_resizeHandles[i]);
            }
            return;
        }

        if (!EditorCanvas.Children.Contains(_selectionBox!))
        {
            EditorCanvas.Children.Add(_selectionBox!);
            for (int i = 0; i < 8; i++) EditorCanvas.Children.Add(_resizeHandles[i]);
        }

        double scaledX = _selectedLayer.X * ScaleX;
        double scaledY = _selectedLayer.Y * ScaleY;
        double scaledW = _selectedLayer.Width * ScaleX;
        double scaledH = _selectedLayer.Height * ScaleY;

        Canvas.SetLeft(_selectionBox!, scaledX);
        Canvas.SetTop(_selectionBox!, scaledY);
        _selectionBox!.Width = Math.Max(1, scaledW);
        _selectionBox!.Height = Math.Max(1, scaledH);

        // Position handles (TL, T, TR, R, BR, B, BL, L)
        PositionHandle(0, scaledX, scaledY);
        PositionHandle(1, scaledX + scaledW / 2, scaledY);
        PositionHandle(2, scaledX + scaledW, scaledY);
        PositionHandle(3, scaledX + scaledW, scaledY + scaledH / 2);
        PositionHandle(4, scaledX + scaledW, scaledY + scaledH);
        PositionHandle(5, scaledX + scaledW / 2, scaledY + scaledH);
        PositionHandle(6, scaledX, scaledY + scaledH);
        PositionHandle(7, scaledX, scaledY + scaledH / 2);
    }

    private void PositionHandle(int index, double x, double y)
    {
        Canvas.SetLeft(_resizeHandles[index], x - 5);
        Canvas.SetTop(_resizeHandles[index], y - 5);
    }

    private void SelectionBox_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_selectedLayer == null) return;
        _isDragging = true;
        _lastMousePosition = e.GetPosition(EditorCanvas);
        e.Handled = true;
    }

    private void Handle_PointerPressed(object? sender, PointerPressedEventArgs e, int handleIndex)
    {
        if (_selectedLayer == null) return;
        _isResizing = true;
        _resizeHandle = handleIndex.ToString();
        _lastMousePosition = e.GetPosition(EditorCanvas);
        e.Handled = true;
    }

    private void EditorCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VideoLayers == null) return;
        
        var pos = e.GetPosition(EditorCanvas);
        double unscaledX = pos.X / ScaleX;
        double unscaledY = pos.Y / ScaleY;

        // Find topmost layer that intersects
        VideoLayer? clickedLayer = null;
        for (int i = VideoLayers.Count - 1; i >= 0; i--)
        {
            var layer = VideoLayers[i];
            if (!layer.IsVisible) continue;
            
            if (unscaledX >= layer.X && unscaledX <= layer.X + layer.Width &&
                unscaledY >= layer.Y && unscaledY <= layer.Y + layer.Height)
            {
                clickedLayer = layer;
                break;
            }
        }

        if (clickedLayer != null)
        {
            clickedLayer.IsSelected = true;
        }
        else
        {
            if (_selectedLayer != null) _selectedLayer.IsSelected = false;
        }
    }

    private void EditorCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_selectedLayer == null) return;

        var currentPos = e.GetPosition(EditorCanvas);
        double dx = (currentPos.X - _lastMousePosition.X) / ScaleX;
        double dy = (currentPos.Y - _lastMousePosition.Y) / ScaleY;

        if (_isDragging)
        {
            _selectedLayer.X += (int)dx;
            _selectedLayer.Y += (int)dy;
            _lastMousePosition = currentPos;
        }
        else if (_isResizing)
        {
            int dxInt = (int)dx;
            int dyInt = (int)dy;
            
            switch (_resizeHandle)
            {
                case "0": // TL
                    _selectedLayer.X += dxInt; _selectedLayer.Y += dyInt;
                    _selectedLayer.Width -= dxInt; _selectedLayer.Height -= dyInt;
                    break;
                case "1": // T
                    _selectedLayer.Y += dyInt; _selectedLayer.Height -= dyInt;
                    break;
                case "2": // TR
                    _selectedLayer.Y += dyInt;
                    _selectedLayer.Width += dxInt; _selectedLayer.Height -= dyInt;
                    break;
                case "3": // R
                    _selectedLayer.Width += dxInt;
                    break;
                case "4": // BR
                    _selectedLayer.Width += dxInt; _selectedLayer.Height += dyInt;
                    break;
                case "5": // B
                    _selectedLayer.Height += dyInt;
                    break;
                case "6": // BL
                    _selectedLayer.X += dxInt;
                    _selectedLayer.Width -= dxInt; _selectedLayer.Height += dyInt;
                    break;
                case "7": // L
                    _selectedLayer.X += dxInt; _selectedLayer.Width -= dxInt;
                    break;
            }
            
            // Constrain minimum size
            if (_selectedLayer.Width < 10) _selectedLayer.Width = 10;
            if (_selectedLayer.Height < 10) _selectedLayer.Height = 10;
            
            _lastMousePosition = currentPos;
        }
    }

    private void EditorCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        _isResizing = false;
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (VideoLayers == null) return;

        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_selectedLayer != null)
            {
                VideoLayers.Remove(_selectedLayer);
                _selectedLayer = null;
                UpdateAdorners();
            }
        }
        else if (ctrl && e.Key == Key.C)
        {
            if (_selectedLayer != null)
            {
                _clipboardJson = JsonSerializer.Serialize(_selectedLayer);
            }
        }
        else if (ctrl && e.Key == Key.X)
        {
            if (_selectedLayer != null)
            {
                _clipboardJson = JsonSerializer.Serialize(_selectedLayer);
                VideoLayers.Remove(_selectedLayer);
                _selectedLayer = null;
                UpdateAdorners();
            }
        }
        else if (ctrl && e.Key == Key.V)
        {
            if (!string.IsNullOrEmpty(_clipboardJson))
            {
                try
                {
                    var newLayer = JsonSerializer.Deserialize<VideoLayer>(_clipboardJson);
                    if (newLayer != null)
                    {
                        newLayer.Id = Guid.NewGuid().ToString();
                        newLayer.Name = newLayer.Name + " (Copy)";
                        newLayer.X += 20;
                        newLayer.Y += 20;
                        newLayer.IsSelected = true;
                        
                        // Deselect others
                        foreach (var l in VideoLayers) l.IsSelected = false;
                        
                        VideoLayers.Add(newLayer);
                    }
                }
                catch { }
            }
        }
    }
}

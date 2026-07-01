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
using Avalonia.VisualTree;
using System.Text.Json;
using Zenith.Core;

namespace Zenith.UI.Controls;

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
    private readonly Rectangle? _selectionBox;
    private readonly Rectangle[] _resizeHandles = new Rectangle[8];

    public VisualLayerEditor()
    {
        InitializeComponent();
        
        // Setup Adorner Visuals
        _selectionBox = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.Parse("#007ACC")),
            StrokeThickness = 2,
            StrokeDashArray = [4, 4],
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 122, 204)),
            IsHitTestVisible = true,
            Cursor = new Cursor(StandardCursorType.SizeAll)
        };
        _selectionBox.PointerPressed += SelectionBox_PointerPressed;
        _selectionBox.DoubleTapped += SelectionBox_DoubleTapped;
        
        for (var i = 0; i < 8; i++)
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
            var cursorType = StandardCursorType.Arrow;
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
            
            var handleIndex = i;
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
        LayersItemsControl.ItemsSource = VideoLayers;
        VideoLayers.CollectionChanged += VideoLayers_CollectionChanged;
        AttachPropertyListeners();
        UpdateZOrder();
    }

    private void VideoLayers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachPropertyListeners();
        UpdateZOrder();
    }

    private void UpdateZOrder()
    {
        if (VideoLayers == null) return;
        for (var i = 0; i < VideoLayers.Count; i++)
        {
            VideoLayers[i].ZOrder = i;
        }
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
                for (var i = 0; i < 8; i++) EditorCanvas.Children.Remove(_resizeHandles[i]);
            }
            return;
        }

        if (!EditorCanvas.Children.Contains(_selectionBox!))
        {
            EditorCanvas.Children.Add(_selectionBox!);
            for (var i = 0; i < 8; i++) EditorCanvas.Children.Add(_resizeHandles[i]);
        }

        // Ensure adorners always render above all layers
        var topZ = (VideoLayers?.Count ?? 0) + 10;
        _selectionBox!.ZIndex = topZ;
        for (var i = 0; i < 8; i++) _resizeHandles[i].ZIndex = topZ + 1;

        // Hide resize handles and change cursor if locked
        if (_selectedLayer.IsLocked)
        {
            for (var i = 0; i < 8; i++) _resizeHandles[i].IsVisible = false;
            _selectionBox!.Cursor = new Cursor(StandardCursorType.Arrow);
            _selectionBox.Fill = new SolidColorBrush(Color.FromArgb(30, 204, 0, 0)); // Red tint for locked
            _selectionBox.Stroke = new SolidColorBrush(Color.Parse("#CC0000"));
        }
        else
        {
            for (var i = 0; i < 8; i++) _resizeHandles[i].IsVisible = true;
            _selectionBox!.Cursor = new Cursor(StandardCursorType.SizeAll);
            _selectionBox.Fill = new SolidColorBrush(Color.FromArgb(30, 0, 122, 204));
            _selectionBox.Stroke = new SolidColorBrush(Color.Parse("#007ACC"));
        }

        var scaledX = _selectedLayer.X * ScaleX;
        var scaledY = _selectedLayer.Y * ScaleY;
        var scaledW = _selectedLayer.Width * ScaleX;
        var scaledH = _selectedLayer.Height * ScaleY;

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
        if (_selectedLayer == null || _selectedLayer.IsLocked) return;
        _isDragging = true;
        _lastMousePosition = e.GetPosition(EditorCanvas);
        e.Handled = true;
    }

    private void SelectionBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_selectedLayer != null && _selectedLayer.Type == LayerType.Text)
        {
            // Find the VisualLayerItem container
            var container = LayersItemsControl.ContainerFromIndex(VideoLayers!.IndexOf(_selectedLayer));
            if (container != null)
            {
                var visualItem = container.GetVisualDescendants().OfType<VisualLayerItem>().FirstOrDefault();
                if (visualItem != null)
                {
                    visualItem.BeginInlineEdit();
                }
            }
            e.Handled = true;
        }
    }

    private void Handle_PointerPressed(object? sender, PointerPressedEventArgs e, int handleIndex)
    {
        if (_selectedLayer == null || _selectedLayer.IsLocked) return;
        _isResizing = true;
        _resizeHandle = handleIndex.ToString();
        _lastMousePosition = e.GetPosition(EditorCanvas);
        e.Handled = true;
    }

    private void EditorCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VideoLayers == null) return;
        
        var pos = e.GetPosition(EditorCanvas);
        var unscaledX = pos.X / ScaleX;
        var unscaledY = pos.Y / ScaleY;

        // Find topmost layer that intersects
        VideoLayer? clickedLayer = null;
        for (var i = VideoLayers.Count - 1; i >= 0; i--)
        {
            var layer = VideoLayers[i];
            if (!layer.IsVisible || layer.IsLocked) continue;
            
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
            _selectedLayer?.IsSelected = false;
        }
    }

    private void EditorCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_selectedLayer == null) return;

        var currentPos = e.GetPosition(EditorCanvas);
        var dx = (currentPos.X - _lastMousePosition.X) / ScaleX;
        var dy = (currentPos.Y - _lastMousePosition.Y) / ScaleY;

        if (_isDragging)
        {
            _selectedLayer.X += (int)dx;
            _selectedLayer.Y += (int)dy;
            _lastMousePosition = currentPos;
        }
        else if (_isResizing)
        {
            var dxInt = (int)dx;
            var dyInt = (int)dy;
            
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

    public void DeleteSelectedLayer()
    {
        if (_selectedLayer != null && VideoLayers != null)
        {
            VideoLayers.Remove(_selectedLayer);
            _selectedLayer = null;
            UpdateAdorners();
        }
    }

    public void CopySelectedLayer()
    {
        if (_selectedLayer != null)
        {
            _clipboardJson = JsonSerializer.Serialize(_selectedLayer);
        }
    }

    public void CutSelectedLayer()
    {
        if (_selectedLayer != null && VideoLayers != null)
        {
            _clipboardJson = JsonSerializer.Serialize(_selectedLayer);
            VideoLayers.Remove(_selectedLayer);
            _selectedLayer = null;
            UpdateAdorners();
        }
    }

    public void PasteLayer()
    {
        if (!string.IsNullOrEmpty(_clipboardJson) && VideoLayers != null)
        {
            try
            {
                var newLayer = JsonSerializer.Deserialize<VideoLayer>(_clipboardJson);
                if (newLayer != null)
                {
                    newLayer.Id = Guid.NewGuid().ToString();
                    newLayer.Name = newLayer.Name + " (Copy)";
                    newLayer.X += 20; // Offset slightly
                    newLayer.Y += 20;
                    newLayer.IsSelected = true;
                    VideoLayers.Add(newLayer);
                }
            }
            catch { }
        }
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (VideoLayers == null) return;

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelectedLayer();
        }
        else if (ctrl && e.Key == Key.C)
        {
            CopySelectedLayer();
        }
        else if (ctrl && e.Key == Key.X)
        {
            CutSelectedLayer();
        }
        else if (ctrl && e.Key == Key.V)
        {
            PasteLayer();
        }
    }
}

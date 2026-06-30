using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Zenith.UI;

public partial class RegionSelectOverlay : Window
{
    private Point _startPoint;
    private bool _isDrawing;
    private TaskCompletionSource<System.Drawing.Rectangle?> _tcs = new();

    public RegionSelectOverlay()
    {
        InitializeComponent();
        
        Opened += (s, e) =>
        {
            var screens = this.Screens.All;
            if (screens.Count > 0)
            {
                var minX = int.MaxValue;
                var minY = int.MaxValue;
                var maxX = int.MinValue;
                var maxY = int.MinValue;

                foreach (var screen in screens)
                {
                    minX = Math.Min(minX, screen.Bounds.X);
                    minY = Math.Min(minY, screen.Bounds.Y);
                    maxX = Math.Max(maxX, screen.Bounds.Right);
                    maxY = Math.Max(maxY, screen.Bounds.Bottom);
                }

                this.Position = new PixelPoint(minX, minY);
                this.Width = maxX - minX;
                this.Height = maxY - minY;
            }
        };
    }
    
    public Task<System.Drawing.Rectangle?> GetRegionAsync()
    {
        return _tcs.Task;
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDrawing = true;
            _startPoint = e.GetPosition(OverlayCanvas);

            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            Canvas.SetLeft(SelectionRectangle, _startPoint.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Y);
            SelectionRectangle.IsVisible = true;
        }
    }

    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDrawing) return;

        var currentPoint = e.GetPosition(OverlayCanvas);
        var x = Math.Min(currentPoint.X, _startPoint.X);
        var y = Math.Min(currentPoint.Y, _startPoint.Y);
        var width = Math.Abs(currentPoint.X - _startPoint.X);
        var height = Math.Abs(currentPoint.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private void Window_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _isDrawing)
        {
            _isDrawing = false;
            
            var x = Canvas.GetLeft(SelectionRectangle);
            var y = Canvas.GetTop(SelectionRectangle);
            var w = SelectionRectangle.Width;
            var h = SelectionRectangle.Height;

            _tcs.TrySetResult(new System.Drawing.Rectangle((int)x, (int)y, (int)w, (int)h));
            this.Close();
        }
        else if (e.InitialPressMouseButton == MouseButton.Right)
        {
            _tcs.TrySetResult(null);
            this.Close();
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _tcs.TrySetResult(null);
        base.OnClosed(e);
    }
}

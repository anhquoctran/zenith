using Avalonia;
using Avalonia.Controls;
using System;

namespace Zenith.UI;

public partial class CameraOverlayWindow : Window
{
    public CameraOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Position at bottom right
        var screen = this.Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            // Subtract window size and add some margin
            var x = workingArea.Right - this.Width - 20;
            var y = workingArea.Bottom - this.Height - 20;
            this.Position = new PixelPoint((int)x, (int)y);
        }
    }

    public void SetImageSource(Avalonia.Media.Imaging.WriteableBitmap bitmap)
    {
        CameraImage.Source = bitmap;
    }

    public void InvalidateImage()
    {
        CameraImage.InvalidateVisual();
    }
}

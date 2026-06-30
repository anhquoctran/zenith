using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Zenith.UI;

public partial class CountdownOverlay : Window
{
    private readonly System.Drawing.Rectangle _region;
    private TaskCompletionSource _tcs = new();

    public CountdownOverlay() : this(new System.Drawing.Rectangle(0, 0, 1920, 1080)) { }

    public CountdownOverlay(System.Drawing.Rectangle region)
    {
        InitializeComponent();
        _region = region;

        Width = 200;
        Height = 200;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Position at center of the capture region
        var centerX = _region.X + (_region.Width / 2) - ((int)Width / 2);
        var centerY = _region.Y + (_region.Height / 2) - ((int)Height / 2);
        Position = new PixelPoint(centerX, centerY);
    }

    public async Task RunCountdownAsync()
    {
        for (var i = 3; i >= 1; i--)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CountdownText.Text = i.ToString();
                CountdownBorder.Opacity = 1.0;
            });
            await Task.Delay(1000);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Close();
        });
    }
}

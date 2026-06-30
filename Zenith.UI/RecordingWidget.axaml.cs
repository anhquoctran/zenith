using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using Zenith.Core;

namespace Zenith.UI;

public partial class RecordingWidget : Window
{
    private readonly IRecorderEngine? _engine;
    private readonly DispatcherTimer _timer;
    private DateTime _startTime;

    public RecordingWidget() : this(null) { }

    public RecordingWidget(IRecorderEngine? engine)
    {
        InitializeComponent();
        _engine = engine;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) =>
        {
            var elapsed = DateTime.Now - _startTime;
            TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _startTime = DateTime.Now;
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void Border_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.BeginMoveDrag(e);
        }
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_engine != null)
        {
            await _engine.StopAsync();
        }
        this.Close();
    }
}

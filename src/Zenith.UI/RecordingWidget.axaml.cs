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

    public RecordingWidget() : this(null, DateTime.Now) { }

    public RecordingWidget(IRecorderEngine? engine, DateTime? recordingStartTime = null)
    {
        InitializeComponent();
        _engine = engine;
        _startTime = recordingStartTime ?? DateTime.Now;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
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
        // Update immediately on open, then let timer continue
        var elapsed = DateTime.Now - _startTime;
        TimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
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

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}

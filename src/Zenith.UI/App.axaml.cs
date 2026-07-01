using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Zenith.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Zenith.UI.Utils.ConfigManager.LoadConfig();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Zenith.UI.Views.MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void NativeMenuItem_Show_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Show();
            desktop.MainWindow?.Activate();
        }
    }

    private void NativeMenuItem_Stop_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is Zenith.UI.Views.MainWindow mainWindow)
        {
            _ = mainWindow.StopRecordingAsync();
        }
    }

    private void NativeMenuItem_Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
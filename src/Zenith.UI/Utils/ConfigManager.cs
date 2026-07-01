using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace Zenith.UI.Utils;

public class AppConfig
{
    public string Language { get; set; } = "en-US";
    public string SaveLocation { get; set; } = "";
    public bool UseHardwareAcceleration { get; set; } = true;
    public string SelectedGpuId { get; set; } = "Auto";
}

public static class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zenith",
        "appsettings.json"
    );

    public static AppConfig CurrentConfig { get; private set; } = new AppConfig();

    public static void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    CurrentConfig = config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
        }

        if (string.IsNullOrEmpty(CurrentConfig.SaveLocation))
        {
            CurrentConfig.SaveLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Zenith.Core.Constants.OUTPUT_PREFIX_PATH);
        }

        ChangeLanguage(CurrentConfig.Language);
    }

    public static void ChangeLanguage(string langCode)
    {
        CurrentConfig.Language = langCode;
        SaveConfig();

        var uri = new Uri($"avares://Zenith.UI/Assets/Langs/{langCode}.axaml");
        var resource = new ResourceInclude(uri) { Source = uri };

        // Replace or add the language dictionary in MergedDictionaries
        // The first merged dictionary is the language dictionary (by convention).
        var appResources = Application.Current?.Resources?.MergedDictionaries;
        if (appResources != null)
        {
            appResources.Clear();
            appResources.Add(resource);
        }
    }

    public static void SaveConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            var json = JsonSerializer.Serialize(CurrentConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}

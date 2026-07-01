using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using Zenith.Core;
using Avalonia.Platform.Storage;

namespace Zenith.UI;

public partial class LayerPropertiesWindow : Window
{
    private readonly VideoLayer _layer;

    public LayerPropertiesWindow()
    {
        InitializeComponent();
        _layer = new VideoLayer(); // Default for designer
    }

    public LayerPropertiesWindow(VideoLayer layer) : this()
    {
        _layer = layer;
        
        NameTextBox.Text = _layer.Name;
        
        if (_layer.Type == LayerType.Text)
        {
            TextPropertiesPanel.IsVisible = true;
            TextContentTextBox.Text = _layer.TextContent;
            FontFamilyTextBox.Text = _layer.FontFamily;
            FontSizeNumeric.Value = _layer.FontSize;
            FontColorTextBox.Text = _layer.FontColor;
        }
        else if (_layer.Type == LayerType.Image || _layer.Type == LayerType.VideoFile)
        {
            FilePropertiesPanel.IsVisible = true;
            FilePathTextBox.Text = _layer.FilePath;
        }
    }

    private async void BrowseFileButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select File",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            FilePathTextBox.Text = files[0].Path.LocalPath;
        }
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        _layer.Name = NameTextBox.Text ?? "New Layer";
        
        if (_layer.Type == LayerType.Text)
        {
            _layer.TextContent = TextContentTextBox.Text ?? "";
            _layer.FontFamily = FontFamilyTextBox.Text ?? "Arial";
            _layer.FontSize = (int)(FontSizeNumeric.Value ?? 48);
            _layer.FontColor = FontColorTextBox.Text ?? "#FFFFFF";
        }
        else if (_layer.Type == LayerType.Image || _layer.Type == LayerType.VideoFile)
        {
            _layer.FilePath = FilePathTextBox.Text ?? "";
        }

        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

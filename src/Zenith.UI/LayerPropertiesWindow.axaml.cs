using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;
using Zenith.Core;
using Avalonia.Platform.Storage;

namespace Zenith.UI;

public partial class LayerPropertiesWindow : Window
{
    private readonly VideoLayer _layer;
    private readonly IDeviceEnumerator? _deviceEnumerator;

    public LayerPropertiesWindow()
    {
        InitializeComponent();
        _layer = new VideoLayer(); // Default for designer
    }

    public LayerPropertiesWindow(VideoLayer layer, IDeviceEnumerator? deviceEnumerator = null) : this()
    {
        _layer = layer;
        _deviceEnumerator = deviceEnumerator;
        
        NameTextBox.Text = _layer.Name;
        
        if (_layer.Type == LayerType.Text || _layer.Type == LayerType.FpsCounter)
        {
            TextPropertiesPanel.IsVisible = true;
            TextContentTextBox.Text = _layer.TextContent;
            FontFamilyTextBox.Text = _layer.FontFamily;
            FontSizeNumeric.Value = _layer.FontSize;
            FontColorTextBox.Text = _layer.FontColor;
            
            SetComboBoxSelection(FontStyleComboBox, _layer.FontStyle);
            SetComboBoxSelection(FontWeightComboBox, _layer.FontWeight);
            SetComboBoxSelection(TextAlignmentComboBox, _layer.TextAlignment);
        }
        else if (_layer.Type == LayerType.Image || _layer.Type == LayerType.VideoFile)
        {
            FilePropertiesPanel.IsVisible = true;
            FilePathTextBox.Text = _layer.FilePath;
        }
        else if (_layer.Type == LayerType.Camera)
        {
            CameraPropertiesPanel.IsVisible = true;
            if (_deviceEnumerator != null)
            {
                var webcams = _deviceEnumerator.GetWebcams().ToList();
                WebcamComboBox.ItemsSource = webcams;
                WebcamComboBox.SelectedItem = webcams.FirstOrDefault(w => w.Id == _layer.SourceId);
                if (WebcamComboBox.SelectedItem == null && webcams.Count > 0)
                {
                    WebcamComboBox.SelectedIndex = 0;
                }
            }
        }
    }

    private async void BrowseFileButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Application.Current?.TryGetResource("Auto_SelectFile", out var titleRes) == true ? titleRes?.ToString() : "Select File",
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
        
        if (_layer.Type == LayerType.Text || _layer.Type == LayerType.FpsCounter)
        {
            _layer.TextContent = TextContentTextBox.Text ?? "";
            _layer.FontFamily = FontFamilyTextBox.Text ?? "Arial";
            _layer.FontSize = (int)(FontSizeNumeric.Value ?? 48);
            _layer.FontColor = FontColorTextBox.Text ?? "#FFFFFF";
            
            _layer.FontStyle = (FontStyleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
            _layer.FontWeight = (FontWeightComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
            _layer.TextAlignment = (TextAlignmentComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left";
        }
        else if (_layer.Type == LayerType.Image || _layer.Type == LayerType.VideoFile)
        {
            _layer.FilePath = FilePathTextBox.Text ?? "";
        }
        else if (_layer.Type == LayerType.Camera)
        {
            if (WebcamComboBox.SelectedItem is WebcamSource webcam)
            {
                _layer.SourceId = webcam.Id;
            }
        }

        Close();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetComboBoxSelection(ComboBox comboBox, string value)
    {
        if (string.IsNullOrEmpty(value)) value = "Normal";
        
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (item.Content?.ToString() == value)
            {
                comboBox.SelectedItem = item;
                break;
            }
        }
    }
}

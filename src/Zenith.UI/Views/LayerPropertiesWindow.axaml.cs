using Avalonia;
using Zenith.UI.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Linq;
using Zenith.Core;
using Zenith.UI.ViewModels;
using Avalonia.Platform.Storage;

namespace Zenith.UI.Views;

public partial class LayerPropertiesWindow : Window
{
    private readonly LayerPropertiesViewModel _viewModel;

    public LayerPropertiesWindow()
    {
        InitializeComponent();
        _viewModel = new LayerPropertiesViewModel(new VideoLayer());
    }

    public LayerPropertiesWindow(VideoLayer layer, IDeviceEnumerator? deviceEnumerator = null) : this()
    {
        _viewModel = new LayerPropertiesViewModel(layer, deviceEnumerator);
        
        NameTextBox.Text = _viewModel.Name;
        
        if (_viewModel.IsTextLayer)
        {
            TextPropertiesPanel.IsVisible = true;
            TextContentTextBox.Text = _viewModel.TextContent;
            FontFamilyTextBox.Text = _viewModel.FontFamily;
            FontSizeNumeric.Value = _viewModel.FontSize;
            FontColorTextBox.Text = _viewModel.FontColor;
            
            SetComboBoxSelection(FontStyleComboBox, _viewModel.FontStyle);
            SetComboBoxSelection(FontWeightComboBox, _viewModel.FontWeight);
            SetComboBoxSelection(TextAlignmentComboBox, _viewModel.TextAlignment);
        }
        else if (_viewModel.IsFileLayer)
        {
            FilePropertiesPanel.IsVisible = true;
            FilePathTextBox.Text = _viewModel.FilePath;
        }
        else if (_viewModel.IsCameraLayer)
        {
            CameraPropertiesPanel.IsVisible = true;
            WebcamComboBox.ItemsSource = _viewModel.Webcams;
            WebcamComboBox.SelectedItem = _viewModel.SelectedWebcam;
            if (WebcamComboBox.SelectedItem == null && _viewModel.Webcams.Count > 0)
                WebcamComboBox.SelectedIndex = 0;
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
        // Read values from UI into ViewModel
        _viewModel.Name = NameTextBox.Text ?? "New Layer";
        
        if (_viewModel.IsTextLayer)
        {
            _viewModel.TextContent = TextContentTextBox.Text ?? "";
            _viewModel.FontFamily = FontFamilyTextBox.Text ?? "Arial";
            _viewModel.FontSize = (int)(FontSizeNumeric.Value ?? 48);
            _viewModel.FontColor = FontColorTextBox.Text ?? "#FFFFFF";
            _viewModel.FontStyle = (FontStyleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
            _viewModel.FontWeight = (FontWeightComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Normal";
            _viewModel.TextAlignment = (TextAlignmentComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Left";
        }
        else if (_viewModel.IsFileLayer)
        {
            _viewModel.FilePath = FilePathTextBox.Text ?? "";
        }
        else if (_viewModel.IsCameraLayer)
        {
            if (WebcamComboBox.SelectedItem is WebcamSource webcam)
                _viewModel.SelectedWebcam = webcam;
        }

        _viewModel.Save();
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

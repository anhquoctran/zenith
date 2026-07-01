using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Zenith.Core;
using Zenith.Interop;
using Zenith.Data;
using System.IO;

namespace Zenith.UI;

public partial class SettingsWindow : Window
{
    private List<GPUDevice>? _devices;
    
    // Properties that MainWindow will read/write
    public string SaveLocation { get; private set; } = string.Empty;
    public bool UseHardwareAcceleration { get; private set; } = true;
    public GPUDevice? SelectedGpu { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
    }
    
    public SettingsWindow(string initialSaveLocation, bool initialUseHwAccel, string initialGpuId, List<GPUDevice> devices) : this()
    {
        SaveLocation = initialSaveLocation;
        UseHardwareAcceleration = initialUseHwAccel;
        _devices = devices;
        
        var saveLocationTextBox = this.FindControl<TextBox>("SaveLocationTextBox");
        var hwAccelCheckBox = this.FindControl<CheckBox>("HardwareAccelerationCheckBox");
        var gpuComboBox = this.FindControl<ComboBox>("GpuComboBox");
        
        if (saveLocationTextBox != null)
            saveLocationTextBox.Text = SaveLocation;
            
        if (hwAccelCheckBox != null)
            hwAccelCheckBox.IsChecked = UseHardwareAcceleration;
            
        if (gpuComboBox != null && _devices != null)
        {
            gpuComboBox.ItemsSource = _devices;
            var selected = _devices.FirstOrDefault(d => d.Id == initialGpuId) ?? _devices.FirstOrDefault();
            gpuComboBox.SelectedItem = selected;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void SelectFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Save Location",
            AllowMultiple = false
        });
        
        if (folders != null && folders.Count > 0)
        {
            var saveLocationTextBox = this.FindControl<TextBox>("SaveLocationTextBox");
            if (saveLocationTextBox != null)
            {
                saveLocationTextBox.Text = Path.Combine(folders[0].Path.LocalPath, Constants.OUTPUT_PREFIX_PATH);
            }
        }
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var saveLocationTextBox = this.FindControl<TextBox>("SaveLocationTextBox");
        var hwAccelCheckBox = this.FindControl<CheckBox>("HardwareAccelerationCheckBox");
        var gpuComboBox = this.FindControl<ComboBox>("GpuComboBox");
        
        if (saveLocationTextBox != null)
            SaveLocation = saveLocationTextBox.Text ?? "";
            
        if (hwAccelCheckBox != null)
            UseHardwareAcceleration = hwAccelCheckBox.IsChecked == true;
            
        if (gpuComboBox != null)
            SelectedGpu = gpuComboBox.SelectedItem as GPUDevice;
            
        Close(true); // Return true indicating OK was clicked
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false); // Return false indicating Cancel
    }
}

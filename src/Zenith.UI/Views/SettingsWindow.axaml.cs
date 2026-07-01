using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Zenith.UI.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Zenith.Core;
using Zenith.Interop;
using Zenith.Data;
using Zenith.UI.ViewModels;
using System.IO;

namespace Zenith.UI.Views;

public partial class SettingsWindow : Window
{
    private SettingsWindowViewModel _viewModel;

    public SettingsWindow()
    {
        _viewModel = new SettingsWindowViewModel();
        InitializeComponent();
    }
    
    public SettingsWindow(string initialSaveLocation, bool initialUseHwAccel, string initialGpuId, List<GPUDevice> devices) : this()
    {
        _viewModel = new SettingsWindowViewModel(initialSaveLocation, initialUseHwAccel, initialGpuId, devices);
        
        var saveLocationTextBox = this.FindControl<TextBox>("SaveLocationTextBox");
        var hwAccelCheckBox = this.FindControl<CheckBox>("HardwareAccelerationCheckBox");
        var gpuComboBox = this.FindControl<ComboBox>("GpuComboBox");
        
        if (saveLocationTextBox != null)
            saveLocationTextBox.Text = _viewModel.SaveLocation;
            
        if (hwAccelCheckBox != null)
            hwAccelCheckBox.IsChecked = _viewModel.UseHardwareAcceleration;
            
        if (gpuComboBox != null)
        {
            gpuComboBox.ItemsSource = _viewModel.GpuDevices;
            gpuComboBox.SelectedItem = _viewModel.SelectedGpu;
        }

        var langComboBox = this.FindControl<ComboBox>("LanguageComboBox");
        if (langComboBox != null)
        {
            foreach (ComboBoxItem item in langComboBox.Items)
            {
                if (item.Tag is string tag && tag == _viewModel.Language)
                {
                    langComboBox.SelectedItem = item;
                    break;
                }
            }
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
            Title = Application.Current?.TryGetResource("Auto_SelectSaveLocation", out var titleRes) == true ? titleRes?.ToString() : "Select Save Location",
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
        // Read values from UI into ViewModel
        var saveLocationTextBox = this.FindControl<TextBox>("SaveLocationTextBox");
        var hwAccelCheckBox = this.FindControl<CheckBox>("HardwareAccelerationCheckBox");
        var gpuComboBox = this.FindControl<ComboBox>("GpuComboBox");
        var langComboBox = this.FindControl<ComboBox>("LanguageComboBox");
        
        if (saveLocationTextBox != null)
            _viewModel.SaveLocation = saveLocationTextBox.Text ?? "";
        if (hwAccelCheckBox != null)
            _viewModel.UseHardwareAcceleration = hwAccelCheckBox.IsChecked == true;
        if (gpuComboBox?.SelectedItem is GPUDevice gpu)
            _viewModel.SelectedGpu = gpu;
        if (langComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            _viewModel.Language = langCode;
        
        _viewModel.ApplySettings();
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void CategoryListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox categoryListBox) return;

        try
        {
            var index = categoryListBox.SelectedIndex;
            
            var general = this.FindControl<StackPanel>("GeneralPanel");
            var output = this.FindControl<StackPanel>("OutputPanel");
            var video = this.FindControl<StackPanel>("VideoPanel");
            var audio = this.FindControl<StackPanel>("AudioPanel");
            var hotkeys = this.FindControl<StackPanel>("HotkeysPanel");
            var advanced = this.FindControl<StackPanel>("AdvancedPanel");

            if (general != null) general.IsVisible = index == 0;
            if (output != null) output.IsVisible = index == 1;
            if (video != null) video.IsVisible = index == 2;
            if (audio != null) audio.IsVisible = index == 3;
            if (hotkeys != null) hotkeys.IsVisible = index == 4;
            if (advanced != null) advanced.IsVisible = index == 5;
        }
        catch (InvalidOperationException)
        {
            // Ignore during initialization when name scope is not ready yet
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Zenith.Core;

namespace Zenith.UI.ViewModels;

public class SettingsWindowViewModel : ViewModelBase
{
    private string _saveLocation;
    public string SaveLocation
    {
        get => _saveLocation;
        set => SetProperty(ref _saveLocation, value);
    }

    private bool _useHardwareAcceleration;
    public bool UseHardwareAcceleration
    {
        get => _useHardwareAcceleration;
        set => SetProperty(ref _useHardwareAcceleration, value);
    }

    private string _selectedGpuId;
    public string SelectedGpuId
    {
        get => _selectedGpuId;
        set => SetProperty(ref _selectedGpuId, value);
    }

    private string _language;
    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    private List<GPUDevice> _gpuDevices = [];
    public List<GPUDevice> GpuDevices
    {
        get => _gpuDevices;
        set => SetProperty(ref _gpuDevices, value);
    }

    private GPUDevice? _selectedGpu;
    public GPUDevice? SelectedGpu
    {
        get => _selectedGpu;
        set => SetProperty(ref _selectedGpu, value);
    }

    public SettingsWindowViewModel()
    {
        _saveLocation = string.Empty;
        _selectedGpuId = "Auto";
        _language = "en-US";
    }

    public SettingsWindowViewModel(string initialSaveLocation, bool initialUseHwAccel, string initialGpuId, List<GPUDevice> devices)
    {
        _saveLocation = initialSaveLocation;
        _useHardwareAcceleration = initialUseHwAccel;
        _selectedGpuId = initialGpuId;
        _language = Utils.ConfigManager.CurrentConfig.Language;
        GpuDevices = devices;
        SelectedGpu = devices.FirstOrDefault(d => d.Id == initialGpuId) ?? devices.FirstOrDefault();
    }

    public void ApplySettings()
    {
        Utils.ConfigManager.CurrentConfig.SaveLocation = SaveLocation;
        Utils.ConfigManager.CurrentConfig.UseHardwareAcceleration = UseHardwareAcceleration;
        if (SelectedGpu != null)
            Utils.ConfigManager.CurrentConfig.SelectedGpuId = SelectedGpu.Id;
        
        if (!string.IsNullOrEmpty(Language))
            Utils.ConfigManager.ChangeLanguage(Language);
        else
            Utils.ConfigManager.SaveConfig();
    }
}

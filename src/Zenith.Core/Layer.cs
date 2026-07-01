using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zenith.Core;

public enum LayerType
{
    Screen,
    VideoFile,
    Image,
    Text,
    Camera,
    FpsCounter
}

public enum AudioLayerType
{
    Microphone,
    SystemAudio,
    AudioFile
}

public abstract class Layer : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    private string _name = "New Layer";
    public string Name 
    { 
        get => _name; 
        set => SetField(ref _name, value); 
    }
    
    private int _zOrder;
    public int ZOrder 
    { 
        get => _zOrder; 
        set => SetField(ref _zOrder, value); 
    }
    
    private bool _isVisible = true;
    public bool IsVisible 
    { 
        get => _isVisible; 
        set => SetField(ref _isVisible, value); 
    }

    private bool _isLocked = false;
    public bool IsLocked
    {
        get => _isLocked;
        set => SetField(ref _isLocked, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class VideoLayer : Layer
{
    public LayerType Type { get; set; }
    
    // Position and Dimensions
    private int _x;
    public int X { get => _x; set => SetField(ref _x, value); }
    
    private int _y;
    public int Y { get => _y; set => SetField(ref _y, value); }
    
    private int _width;
    public int Width { get => _width; set => SetField(ref _width, value); }
    
    private int _height;
    public int Height { get => _height; set => SetField(ref _height, value); }

    public string SourceId { get; set; } = string.Empty;
    public IntPtr MonitorHandle { get; set; } = IntPtr.Zero;
    
    // Transform
    private double _rotationAngle;
    public double RotationAngle { get => _rotationAngle; set => SetField(ref _rotationAngle, value); }
    
    private bool _flipHorizontal;
    public bool FlipHorizontal { get => _flipHorizontal; set => SetField(ref _flipHorizontal, value); }
    
    private bool _flipVertical;
    public bool FlipVertical { get => _flipVertical; set => SetField(ref _flipVertical, value); }
    
    // Used for Image/Video
    private string _filePath = string.Empty;
    public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }

    // Used for Text
    private string _textContent = string.Empty;
    public string TextContent { get => _textContent; set => SetField(ref _textContent, value); }
    
    private string _fontFamily = "Arial";
    public string FontFamily { get => _fontFamily; set => SetField(ref _fontFamily, value); }
    
    private int _fontSize = 48;
    public int FontSize { get => _fontSize; set => SetField(ref _fontSize, value); }
    
    private string _fontColor = "#FFFFFF";
    public string FontColor { get => _fontColor; set => SetField(ref _fontColor, value); }
    
    private string _fontStyle = "Normal";
    public string FontStyle { get => _fontStyle; set => SetField(ref _fontStyle, value); }
    
    private string _fontWeight = "Normal";
    public string FontWeight { get => _fontWeight; set => SetField(ref _fontWeight, value); }
    
    private string _textAlignment = "Left";
    public string TextAlignment { get => _textAlignment; set => SetField(ref _textAlignment, value); }
}

public class AudioLayer : Layer
{
    public AudioLayerType Type { get; set; }
    
    public string SourceId { get; set; } = string.Empty;

    private float _volume = 1.0f;
    public float Volume 
    { 
        get => _volume; 
        set => SetField(ref _volume, value); 
    }

    private float _currentPeak = 0.0f;
    public float CurrentPeak
    {
        get => _currentPeak;
        set => SetField(ref _currentPeak, value);
    }
}

using System.Collections.Generic;
using System.Linq;
using Zenith.Core;

namespace Zenith.UI.ViewModels;

public class LayerPropertiesViewModel : ViewModelBase
{
    private readonly VideoLayer _layer;
    private readonly IDeviceEnumerator? _deviceEnumerator;

    // Editable properties
    private string _name;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _textContent = string.Empty;
    public string TextContent
    {
        get => _textContent;
        set => SetProperty(ref _textContent, value);
    }

    private string _fontFamily = "Arial";
    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    private int _fontSize = 48;
    public int FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    private string _fontColor = "#FFFFFF";
    public string FontColor
    {
        get => _fontColor;
        set => SetProperty(ref _fontColor, value);
    }

    private string _fontStyle = "Normal";
    public string FontStyle
    {
        get => _fontStyle;
        set => SetProperty(ref _fontStyle, value);
    }

    private string _fontWeight = "Normal";
    public string FontWeight
    {
        get => _fontWeight;
        set => SetProperty(ref _fontWeight, value);
    }

    private string _textAlignment = "Left";
    public string TextAlignment
    {
        get => _textAlignment;
        set => SetProperty(ref _textAlignment, value);
    }

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    // Camera
    private List<WebcamSource> _webcams = [];
    public List<WebcamSource> Webcams
    {
        get => _webcams;
        set => SetProperty(ref _webcams, value);
    }

    private WebcamSource? _selectedWebcam;
    public WebcamSource? SelectedWebcam
    {
        get => _selectedWebcam;
        set => SetProperty(ref _selectedWebcam, value);
    }

    // Visibility for type-specific panels
    public bool IsTextLayer => _layer.Type == LayerType.Text || _layer.Type == LayerType.FpsCounter;
    public bool IsFileLayer => _layer.Type == LayerType.Image || _layer.Type == LayerType.VideoFile;
    public bool IsCameraLayer => _layer.Type == LayerType.Camera;

    public LayerPropertiesViewModel(VideoLayer layer, IDeviceEnumerator? deviceEnumerator = null)
    {
        _layer = layer;
        _deviceEnumerator = deviceEnumerator;

        _name = layer.Name;
        _textContent = layer.TextContent;
        _fontFamily = layer.FontFamily;
        _fontSize = layer.FontSize;
        _fontColor = layer.FontColor;
        _fontStyle = layer.FontStyle;
        _fontWeight = layer.FontWeight;
        _textAlignment = layer.TextAlignment;
        _filePath = layer.FilePath;

        if (IsCameraLayer && deviceEnumerator != null)
        {
            Webcams = deviceEnumerator.GetWebcams().ToList();
            SelectedWebcam = Webcams.FirstOrDefault(w => w.Id == layer.SourceId) ?? Webcams.FirstOrDefault();
        }
    }

    public void Save()
    {
        _layer.Name = Name;

        if (IsTextLayer)
        {
            _layer.TextContent = TextContent;
            _layer.FontFamily = FontFamily;
            _layer.FontSize = FontSize;
            _layer.FontColor = FontColor;
            _layer.FontStyle = FontStyle;
            _layer.FontWeight = FontWeight;
            _layer.TextAlignment = TextAlignment;
        }
        else if (IsFileLayer)
        {
            _layer.FilePath = FilePath;
        }
        else if (IsCameraLayer && SelectedWebcam != null)
        {
            _layer.SourceId = SelectedWebcam.Id;
        }
    }
}

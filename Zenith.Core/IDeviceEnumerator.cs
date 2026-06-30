using System.Collections.Generic;

namespace Zenith.Core;

public class VideoSource
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public class AudioSource
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public class WebcamSource
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public interface IDeviceEnumerator
{
    IEnumerable<VideoSource> GetVideoSources();
    IEnumerable<AudioSource> GetAudioSources();
    IEnumerable<WebcamSource> GetWebcams();
}

public class FallbackDeviceEnumerator : IDeviceEnumerator
{
    public IEnumerable<VideoSource> GetVideoSources() => Array.Empty<VideoSource>();
    public IEnumerable<AudioSource> GetAudioSources() => Array.Empty<AudioSource>();
    public IEnumerable<WebcamSource> GetWebcams() => Array.Empty<WebcamSource>();
}

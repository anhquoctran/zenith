using System.Linq;
using Xunit;
using Zenith.Interop;

namespace Zenith.Test;

public class WindowsDeviceEnumeratorTests
{
    [Fact]
    public void GetVideoSources_ReturnsAtLeastOneSource()
    {
        var enumerator = new WindowsDeviceEnumerator();
        
        var videoSources = enumerator.GetVideoSources();
        
        // Even if no native sources are found, it adds a default "Display 1" fallback
        Assert.NotNull(videoSources);
        Assert.True(videoSources.Any());
    }

    [Fact]
    public void GetAudioSources_ReturnsList()
    {
        var enumerator = new WindowsDeviceEnumerator();
        
        var audioSources = enumerator.GetAudioSources();
        
        Assert.NotNull(audioSources);
    }

    [Fact]
    public void GetWebcams_ReturnsAtLeastNone()
    {
        var enumerator = new WindowsDeviceEnumerator();
        
        var webcams = enumerator.GetWebcams();
        
        Assert.NotNull(webcams);
        Assert.Contains(webcams, w => w.Name == "None");
    }
}

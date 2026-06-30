using System;
using System.Collections.Generic;
using Zenith.Core;
using NAudio.Wave;
using NAudio.CoreAudioApi;

#if WINDOWS
using System.Management;
using Vortice.DXGI;
#endif

namespace Zenith.Interop;

public class WindowsDeviceEnumerator : IDeviceEnumerator
{
    public IEnumerable<VideoSource> GetVideoSources()
    {
        var sources = new List<VideoSource>();
#if WINDOWS
        try
        {
            DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            if (factory != null)
            {
                int adapterIndex = 0;
                while (factory.EnumAdapters1(adapterIndex, out var adapter).Success)
                {
                    int outputIndex = 0;
                    while (adapter.EnumOutputs(outputIndex, out var output).Success)
                    {
                        var desc = output.Description;
                        var width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                        var height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;
                        sources.Add(new VideoSource
                        {
                            Name = $"Screen {sources.Count + 1} ({width}x{height})",
                            Id = desc.DeviceName,
                            Width = width,
                            Height = height,
                            X = desc.DesktopCoordinates.Left,
                            Y = desc.DesktopCoordinates.Top
                        });
                        output.Dispose();
                        outputIndex++;
                    }
                    adapter.Dispose();
                    adapterIndex++;
                }
                factory.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating displays: {ex.Message}");
            if (sources.Count == 0)
                sources.Add(new VideoSource { Name = "Display 1", Id = "Display1", Width = 1920, Height = 1080 });
        }
#else
        if (sources.Count == 0)
            sources.Add(new VideoSource { Name = "No Devices Found", Id = "None", Width = 0, Height = 0 });
#endif
        return sources;
    }

    public IEnumerable<AudioSource> GetAudioSources()
    {
        var sources = new List<AudioSource>();
#if WINDOWS
        try
        {
            var enumerator = new MMDeviceEnumerator();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                sources.Add(new AudioSource
                {
                    Name = endpoint.FriendlyName,
                    Id = endpoint.ID
                });
            }
            
            // Add loopback devices
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                sources.Add(new AudioSource
                {
                    Name = $"System Audio: {endpoint.FriendlyName}",
                    Id = endpoint.ID
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Audio enumeration error: {ex.Message}");
        }
#endif
        return sources;
    }

    public IEnumerable<WebcamSource> GetWebcams()
    {
        var sources = new List<WebcamSource>();
        sources.Add(new WebcamSource { Name = "None", Id = "None" });
        
#if WINDOWS
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE (PNPClass = 'Image' OR PNPClass = 'Camera')");
            foreach (var device in searcher.Get())
            {
                var name = device["Caption"]?.ToString() ?? "Unknown Camera";
                var id = device["DeviceID"]?.ToString() ?? Guid.NewGuid().ToString();
                sources.Add(new WebcamSource { Name = name, Id = id });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating webcams: {ex.Message}");
        }
#endif
        return sources;
    }
}

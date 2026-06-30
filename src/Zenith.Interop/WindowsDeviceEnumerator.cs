using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Zenith.Core;

#if WINDOWS
using System.Management;
using Vortice.DXGI;
#endif

namespace Zenith.Interop;

public class WindowsDeviceEnumerator : IDeviceEnumerator
{
    [DllImport("zenith_native", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr GetAvailableSources();

    private class NativeSource
    {
        public string SourceID { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
    }

    private static List<NativeSource> LoadNativeSources()
    {
        try
        {
            IntPtr ptr = GetAvailableSources();
            if (ptr != IntPtr.Zero)
            {
                string? json = Marshal.PtrToStringUTF8(ptr);
                if (!string.IsNullOrEmpty(json))
                {
                    return JsonSerializer.Deserialize<List<NativeSource>>(json) ?? new();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling native GetAvailableSources: {ex.Message}");
        }
        return new();
    }

    public IEnumerable<VideoSource> GetVideoSources()
    {
        var sources = new List<VideoSource>();
        var nativeSources = LoadNativeSources();
        
        foreach (var ns in nativeSources)
        {
            if (ns.SourceType == "Screen" || ns.SourceType == "Window")
            {
                int width = 1920;
                int height = 1080;
                if (!string.IsNullOrEmpty(ns.Resolution))
                {
                    var parts = ns.Resolution.Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                    {
                        width = w;
                        height = h;
                    }
                }

                sources.Add(new VideoSource
                {
                    Name = ns.DisplayName,
                    Id = ns.SourceID,
                    Width = width,
                    Height = height,
                    X = ns.X,
                    Y = ns.Y,
                    OwningGpuId = "Auto"
                });
            }
        }

        if (sources.Count == 0)
        {
            sources.Add(new VideoSource { Name = "Display 1", Id = "Display1", Width = 1920, Height = 1080 });
        }
        return sources;
    }

    public IEnumerable<GPUDevice> GetGPUDevices()
    {
        var gpus = new List<GPUDevice>();
#if WINDOWS
        try
        {
            DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            if (factory != null)
            {
                var adapterIndex = 0;
                while (factory.EnumAdapters1(adapterIndex, out var adapter).Success)
                {
                    var desc = adapter.Description1;
                    gpus.Add(new GPUDevice
                    {
                        Name = desc.Description,
                        Id = desc.Luid.ToString()
                    });
                    adapter.Dispose();
                    adapterIndex++;
                }
                factory.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating GPUs: {ex.Message}");
        }
#endif
        return gpus;
    }

    public IEnumerable<AudioSource> GetAudioSources()
    {
        var sources = new List<AudioSource>();
        var nativeSources = LoadNativeSources();

        foreach (var ns in nativeSources)
        {
            if (ns.SourceType == "AudioInput" || ns.SourceType == "AudioOutput")
            {
                sources.Add(new AudioSource
                {
                    Name = ns.DisplayName,
                    Id = ns.SourceID
                });
            }
        }

        return sources;
    }

    public IEnumerable<WebcamSource> GetWebcams()
    {
        var sources = new List<WebcamSource>();
        sources.Add(new WebcamSource { Name = "None", Id = "None" });

        var nativeSources = LoadNativeSources();
        foreach (var ns in nativeSources)
        {
            if (ns.SourceType == "Webcam")
            {
                sources.Add(new WebcamSource
                {
                    Name = ns.DisplayName,
                    Id = ns.SourceID
                });
            }
        }

        return sources;
    }
}

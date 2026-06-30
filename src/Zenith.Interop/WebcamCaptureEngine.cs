using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace Zenith.Interop;

public unsafe class WebcamCaptureEngine : IDisposable
{
    private AVFormatContext* _formatContext = null;

    public void Start(string deviceName = "video=Integrated Webcam")
    {
        ffmpeg.avdevice_register_all();

        var inputFormatName = string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            inputFormatName = "dshow"; // DirectShow
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            inputFormatName = "video4linux2"; // V4L2
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            inputFormatName = "avfoundation"; // AVFoundation
        }

        var inputFormat = ffmpeg.av_find_input_format(inputFormatName);
        if (inputFormat == null)
        {
            Console.WriteLine($"Failed to find webcam input format: {inputFormatName}");
            return;
        }

        var formatContext = ffmpeg.avformat_alloc_context();
        var options = (AVDictionary*)null;
        
        // This is a structural scaffold for Phase 8.
        // It demonstrates how FFmpeg abstracts per-OS webcam acquisition.
        var ret = ffmpeg.avformat_open_input(&formatContext, deviceName, inputFormat, &options);
        if (ret < 0)
        {
            Console.WriteLine($"Failed to open webcam {deviceName} via {inputFormatName}. Error: {ret}");
        }
        else
        {
            _formatContext = formatContext;
            Console.WriteLine($"Successfully opened webcam: {deviceName}");
        }
    }

    public void Stop()
    {
        if (_formatContext != null)
        {
            fixed (AVFormatContext** formatContext = &_formatContext)
            {
                ffmpeg.avformat_close_input(formatContext);
            }
            _formatContext = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

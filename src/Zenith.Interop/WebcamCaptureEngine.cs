using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace Zenith.Interop;

public class FrameArrivedEventArgs : EventArgs
{
    public byte[] DataArray { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
}

public unsafe class WebcamCaptureEngine : IDisposable
{
    private AVFormatContext* _formatContext = null;
    private AVCodecContext* _codecContext = null;
    private SwsContext* _swsContext = null;
    private int _videoStreamIndex = -1;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    public event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    public void Start(string deviceName = "video=Integrated Webcam")
    {
        Console.WriteLine($"[WebcamCaptureEngine] Starting webcam: {deviceName}");
        
        ffmpeg.LibraryVersionMap["avcodec"] = 63;
        ffmpeg.LibraryVersionMap["avdevice"] = 63;
        ffmpeg.LibraryVersionMap["avfilter"] = 12;
        ffmpeg.LibraryVersionMap["avformat"] = 63;
        ffmpeg.LibraryVersionMap["avutil"] = 61;
        ffmpeg.LibraryVersionMap["swresample"] = 7;
        ffmpeg.LibraryVersionMap["swscale"] = 10;
        ffmpeg.RootPath = AppContext.BaseDirectory;
        
        // Initialize libavdevice so device formats (dshow, v4l2, avfoundation) get registered.
        // It threw NotSupportedException before only because avdevice-63.dll was missing from disk!
        ffmpeg.avdevice_register_all();
        Console.WriteLine($"[WebcamCaptureEngine] avdevice_register_all completed successfully");
        
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

        Console.WriteLine($"[WebcamCaptureEngine] Looking for input format: {inputFormatName}");
        var inputFormat = ffmpeg.av_find_input_format(inputFormatName);
        if (inputFormat == null)
        {
            Console.WriteLine($"[WebcamCaptureEngine] FAILED: av_find_input_format returned null for '{inputFormatName}'");
            return;
        }
        Console.WriteLine($"[WebcamCaptureEngine] Found input format: {inputFormatName}");

        var formatContext = ffmpeg.avformat_alloc_context();
        var options = (AVDictionary*)null;
        
        ffmpeg.av_dict_set(&options, "rtbufsize", "1000000000", 0);
        ffmpeg.av_dict_set(&options, "framerate", "30", 0);
        ffmpeg.av_dict_set(&options, "video_size", "640x480", 0);

        Console.WriteLine($"[WebcamCaptureEngine] Opening device: {deviceName}");
        var ret = ffmpeg.avformat_open_input(&formatContext, deviceName, inputFormat, &options);
        if (ret < 0)
        {
            Console.WriteLine($"[WebcamCaptureEngine] FAILED: avformat_open_input returned {ret} for '{deviceName}'");
            return;
        }
        _formatContext = formatContext;
        Console.WriteLine($"[WebcamCaptureEngine] Device opened successfully");

        if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
        {
            Console.WriteLine("[WebcamCaptureEngine] FAILED: avformat_find_stream_info");
            return;
        }

        AVCodec* codec = null;
        _videoStreamIndex = ffmpeg.av_find_best_stream(_formatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
        if (_videoStreamIndex < 0)
        {
            Console.WriteLine("[WebcamCaptureEngine] FAILED: No video stream found");
            return;
        }

        _codecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(_codecContext, _formatContext->streams[_videoStreamIndex]->codecpar);

        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
        {
            Console.WriteLine("[WebcamCaptureEngine] FAILED: avcodec_open2");
            return;
        }

        Console.WriteLine($"[WebcamCaptureEngine] Webcam ready: {_codecContext->width}x{_codecContext->height}, pix_fmt={_codecContext->pix_fmt}");
        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(_cts.Token));
    }

    private void ReadLoop(CancellationToken token)
    {
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* frame = ffmpeg.av_frame_alloc();
        AVFrame* rgbaFrame = ffmpeg.av_frame_alloc();
        
        int width = _codecContext->width;
        int height = _codecContext->height;

        rgbaFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        rgbaFrame->width = width;
        rgbaFrame->height = height;
        ffmpeg.av_frame_get_buffer(rgbaFrame, 32);

        _swsContext = ffmpeg.sws_getContext(
            width, height, _codecContext->pix_fmt,
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
            2 /* SWS_BILINEAR */, null, null, null);

        while (!token.IsCancellationRequested)
        {
            if (ffmpeg.av_read_frame(_formatContext, packet) >= 0)
            {
                if (packet->stream_index == _videoStreamIndex)
                {
                    if (ffmpeg.avcodec_send_packet(_codecContext, packet) == 0)
                    {
                        while (ffmpeg.avcodec_receive_frame(_codecContext, frame) == 0)
                        {
                            ffmpeg.sws_scale(_swsContext, frame->data, frame->linesize, 0, height, rgbaFrame->data, rgbaFrame->linesize);

                            int size = rgbaFrame->linesize[0] * height;
                            byte[] arr = ArrayPool<byte>.Shared.Rent(size);
                            Marshal.Copy((IntPtr)rgbaFrame->data[0], arr, 0, size);

                            FrameArrived?.Invoke(this, new FrameArrivedEventArgs
                            {
                                DataArray = arr,
                                Width = width,
                                Height = height,
                                Stride = rgbaFrame->linesize[0]
                            });
                        }
                    }
                }
                ffmpeg.av_packet_unref(packet);
            }
            else
            {
                Thread.Sleep(10);
            }
        }

        ffmpeg.av_frame_free(&rgbaFrame);
        ffmpeg.av_frame_free(&frame);
        ffmpeg.av_packet_free(&packet);
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _readTask?.Wait();
            _cts = null;
        }

        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (_codecContext != null)
        {
            fixed (AVCodecContext** codecCtx = &_codecContext)
            {
                ffmpeg.avcodec_free_context(codecCtx);
            }
            _codecContext = null;
        }

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

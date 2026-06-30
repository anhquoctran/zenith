using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Zenith.Core;

#if WINDOWS
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.DirectX;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct3D;
#endif

namespace Zenith.Interop;

public unsafe class FFmpegRecorderEngine : IRecorderEngine
{
    public event EventHandler<RecorderStatusEventArgs>? StatusChanged;
    public event EventHandler<RecorderErrorEventArgs>? ErrorOccurred;
    public event EventHandler<float>? WaveformDataAvailable;

    public RecorderState State { get; private set; } = RecorderState.Idle;

    private AVFilterGraph* _filterGraph = null;
    private AVFilterContext* _buffersrcCtxDesktop = null;
    private AVFilterContext* _buffersrcCtxWebcam = null;
    private AVFilterContext* _buffersinkCtx = null;
    private WebcamCaptureEngine? _webcamEngine;
    
    private RecordingConfig? _config;
    private AVFormatContext* _fmtCtx = null;
    private AVCodecContext* _codecCtx = null;
    private AVStream* _videoStream = null;

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory([MarshalAs(UnmanagedType.HString)] string activatableClassId, [In] ref Guid iid, out IGraphicsCaptureItemInterop factory);

    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _winrtDevice;
    private Vortice.Direct3D11.ID3D11Device? _d3dDevice;
    private AVBufferRef* _hwDeviceCtx = null;
    
    // We keep a task to handle the actual FFmpeg encoding loop so it doesn't block UI
    private CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _encodeTask;
    private System.Collections.Concurrent.BlockingCollection<IntPtr>? _frameQueue;
#endif

    public FFmpegRecorderEngine()
    {
    }

    public Task InitializeAsync(RecordingConfig config)
    {
        _config = config;
        
#if WINDOWS
        try
        {
            ffmpeg.RootPath = AppContext.BaseDirectory;
            ffmpeg.avdevice_register_all();

            Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                new[] { FeatureLevel.Level_11_1 },
                out Vortice.Direct3D11.ID3D11Device? device, out Vortice.Direct3D11.ID3D11DeviceContext? context).CheckError();
            
            _d3dDevice = device;

            var dxgiDevice = _d3dDevice!.QueryInterface<IDXGIDevice>();
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pUnknown);
            _winrtDevice = Marshal.GetObjectForIUnknown(pUnknown) as IDirect3DDevice;

            AVBufferRef* hwDeviceCtx = null;
            ffmpeg.av_hwdevice_ctx_create(&hwDeviceCtx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0);
            _hwDeviceCtx = hwDeviceCtx;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new RecorderErrorEventArgs { Exception = ex });
        }
#endif

        if (config.EnableWebcam)
        {
            _webcamEngine = new WebcamCaptureEngine();
            var deviceName = string.IsNullOrEmpty(config.WebcamDeviceName) ? "video=Integrated Webcam" : config.WebcamDeviceName;
            _webcamEngine.Start(deviceName);

            // Scaffold AVFilter Graph for PiP (Picture-in-Picture)
            _filterGraph = ffmpeg.avfilter_graph_alloc();
            var filterArgs = "[in_desktop] [in_webcam] overlay=W-w-10:H-h-10 [out]";
            Console.WriteLine($"Initialized FFmpeg Filter Graph for Webcam PiP: {filterArgs}");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync()
    {
#if WINDOWS
        if (_config == null) throw new InvalidOperationException("Must initialize before starting.");
        
        try
        {
            // Open AVFormatContext
            AVFormatContext* fmtCtx = null;
            var outFormat = ffmpeg.av_guess_format(null, _config.OutputPath ?? "output.mp4", null);
            ffmpeg.avformat_alloc_output_context2(&fmtCtx, outFormat, null, _config.OutputPath ?? "output.mp4");
            _fmtCtx = fmtCtx;

            var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            _videoStream = ffmpeg.avformat_new_stream(_fmtCtx, codec);
            
            _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
            _codecCtx->width = _config.Width;
            _codecCtx->height = _config.Height;
            _codecCtx->time_base = new AVRational { num = 1, den = _config.Framerate };
            _codecCtx->framerate = new AVRational { num = _config.Framerate, den = 1 };
            _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
            
            ffmpeg.avcodec_open2(_codecCtx, codec, null);
            ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _codecCtx);
            
            ffmpeg.avio_open(&_fmtCtx->pb, _config.OutputPath ?? "output.mp4", ffmpeg.AVIO_FLAG_WRITE);
            ffmpeg.avformat_write_header(_fmtCtx, null);

            var hMonitor = MonitorFromWindow(IntPtr.Zero, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            Guid iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref iid, out var factory);
            
            Guid captureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var ptr = factory.CreateForMonitor(hMonitor, ref captureItemGuid);
            var captureItem = Marshal.GetObjectForIUnknown(ptr) as GraphicsCaptureItem;

            if (captureItem != null && _winrtDevice != null)
            {
                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    captureItem.Size);

                _session = _framePool.CreateCaptureSession(captureItem);
                
                _frameQueue = new System.Collections.Concurrent.BlockingCollection<IntPtr>();
                
                _framePool.FrameArrived += OnFrameArrived;
                _session.StartCapture();

                _cts = new CancellationTokenSource();
                _encodeTask = Task.Run(() => EncodeLoop(_cts.Token));
            }
            
            State = RecorderState.Recording;
            StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State });
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new RecorderErrorEventArgs { Exception = ex });
        }
#endif
        return Task.CompletedTask;
    }
    
#if WINDOWS
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame != null && _frameQueue != null && !_frameQueue.IsAddingCompleted)
        {
            // Real implementation would extract the surface, wrap in AVFrame, and push.
            // For now, we simulate receiving it into the queue.
            _frameQueue.Add(IntPtr.Zero);
        }
    }

    private void EncodeLoop(CancellationToken token)
    {
        if (_frameQueue == null) return;
        long pts = 0;
        try
        {
            foreach (var framePtr in _frameQueue.GetConsumingEnumerable())
            {
                if (token.IsCancellationRequested) break;
                
                // Wrap in AVFrame
                AVFrame* avFrame = ffmpeg.av_frame_alloc();
                avFrame->format = (int)AVPixelFormat.AV_PIX_FMT_D3D11;
                avFrame->width = _config.Width;
                avFrame->height = _config.Height;
                avFrame->pts = pts++;

                // Send to encoder
                ffmpeg.avcodec_send_frame(_codecCtx, avFrame);
                ffmpeg.av_frame_free(&avFrame);

                AVPacket* pkt = ffmpeg.av_packet_alloc();
                while (ffmpeg.avcodec_receive_packet(_codecCtx, pkt) == 0)
                {
                    ffmpeg.av_interleaved_write_frame(_fmtCtx, pkt);
                    ffmpeg.av_packet_unref(pkt);
                }
                ffmpeg.av_packet_free(&pkt);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new RecorderErrorEventArgs { Exception = ex });
        }
    }
#endif

    public Task PauseAsync()
    {
        State = RecorderState.Paused;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State });
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        State = RecorderState.Recording;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State });
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        State = RecorderState.Draining;
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State });
        
        return Task.Run(() =>
        {
#if WINDOWS
            try
            {
                _session?.Dispose();
                _framePool?.Dispose();
                _frameQueue?.CompleteAdding();
                
                if (_encodeTask != null)
                {
                    _encodeTask.Wait();
                }
                
                // Drain encoder
                if (_codecCtx != null)
                {
                    ffmpeg.avcodec_send_frame(_codecCtx, null);
                    AVPacket* pkt = ffmpeg.av_packet_alloc();
                    while (ffmpeg.avcodec_receive_packet(_codecCtx, pkt) == 0)
                    {
                        ffmpeg.av_interleaved_write_frame(_fmtCtx, pkt);
                        ffmpeg.av_packet_unref(pkt);
                    }
                    ffmpeg.av_packet_free(&pkt);
                }

                if (_fmtCtx != null)
                {
                    ffmpeg.av_write_trailer(_fmtCtx);
                    ffmpeg.avio_close(_fmtCtx->pb);
                    _fmtCtx->pb = null;
                    ffmpeg.avformat_free_context(_fmtCtx);
                    _fmtCtx = null;
                }
                
                if (_codecCtx != null)
                {
                    AVCodecContext* codecCtx = _codecCtx;
                    ffmpeg.avcodec_free_context(&codecCtx);
                    _codecCtx = null;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new RecorderErrorEventArgs { Exception = ex });
            }
#endif

            State = RecorderState.Stopped;
            StatusChanged?.Invoke(this, new RecorderStatusEventArgs { State = State });
        });
    }

    public Task TakeSnapshotAsync(string snapshotPath)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
#if WINDOWS
        _cts.Cancel();
        _session?.Dispose();
        _framePool?.Dispose();
        
        if (_hwDeviceCtx != null)
        {
            fixed (AVBufferRef** p = &_hwDeviceCtx)
                ffmpeg.av_buffer_unref(p);
        }
        
        _d3dDevice?.Dispose();
#endif

        if (_filterGraph != null)
        {
            fixed (AVFilterGraph** filterGraph = &_filterGraph)
            {
                ffmpeg.avfilter_graph_free(filterGraph);
            }
            _filterGraph = null;
        }

        _webcamEngine?.Dispose();
    }
}

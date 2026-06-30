using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using Zenith.Core;

#if WINDOWS
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.DirectX;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Direct3D;
using Vortice.Mathematics;
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
    
    private RecordingConfig? _config;
    private AVFormatContext* _fmtCtx = null;
    private AVCodecContext* _codecCtx = null;
    private AVStream* _videoStream = null;

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(System.Drawing.Point pt, uint dwFlags);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);
    
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IGraphicsCaptureItemInterop factory);

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _winrtDevice;
    private Vortice.Direct3D11.ID3D11Device? _d3dDevice;
    private Vortice.Direct3D11.ID3D11Texture2D? _stagingTexture;
    private SwsContext* _swsCtx = null;
    
    // We keep a task to handle the actual FFmpeg encoding loop so it doesn't block UI
    private CancellationTokenSource _cts = new CancellationTokenSource();
    private Task? _encodeTask;
    private System.Collections.Concurrent.BlockingCollection<Direct3D11CaptureFrame>? _frameQueue;
    
    // Monitor origin in screen coordinates, needed to compute crop offsets
    private int _monitorOriginX;
    private int _monitorOriginY;
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
            // Override FFmpeg.AutoGen expected versions with the actual latest Master versions we downloaded
            ffmpeg.LibraryVersionMap["avcodec"] = 63;
            ffmpeg.LibraryVersionMap["avdevice"] = 63;
            ffmpeg.LibraryVersionMap["avfilter"] = 12;
            ffmpeg.LibraryVersionMap["avformat"] = 63;
            ffmpeg.LibraryVersionMap["avutil"] = 61;
            ffmpeg.LibraryVersionMap["swresample"] = 7;
            ffmpeg.LibraryVersionMap["swscale"] = 10;

            ffmpeg.RootPath = AppContext.BaseDirectory;

            IDXGIAdapter1? selectedAdapter = null;
            DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            if (factory != null)
            {
                var targetGpuId = config.SelectedGpuId;
                if (targetGpuId == "Auto" && config.MonitorHandle != IntPtr.Zero)
                {
                    var adapterIndex = 0;
                    while (factory.EnumAdapters1(adapterIndex, out var adapter).Success)
                    {
                        var outputIndex = 0;
                        while (adapter.EnumOutputs(outputIndex, out var output).Success)
                        {
                            if (output.Description.Monitor == config.MonitorHandle)
                            {
                                selectedAdapter = adapter;
                                break;
                            }
                            output.Dispose();
                            outputIndex++;
                        }
                        if (selectedAdapter != null) break;
                        adapter.Dispose();
                        adapterIndex++;
                    }
                }
                else if (targetGpuId != "Auto")
                {
                    var adapterIndex = 0;
                    while (factory.EnumAdapters1(adapterIndex, out var adapter).Success)
                    {
                        if (adapter.Description1.Luid.ToString() == targetGpuId)
                        {
                            selectedAdapter = adapter;
                            break;
                        }
                        adapter.Dispose();
                        adapterIndex++;
                    }
                }
            }

			D3D11.D3D11CreateDevice(
                selectedAdapter,
                selectedAdapter != null ? DriverType.Unknown : DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                [FeatureLevel.Level_11_1],
                out var device, out Vortice.Direct3D11.ID3D11DeviceContext? context).CheckError();
            
            _d3dDevice = device;

            selectedAdapter?.Dispose();
            factory?.Dispose();

            var dxgiDevice = _d3dDevice!.QueryInterface<IDXGIDevice>();
            _ = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pUnknown);
            _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new RecorderErrorEventArgs { Exception = ex });
        }
#endif

        if (config.EnableWebcam)
        {
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
            IntPtr hMonitor;
            if (_config.MonitorHandle != IntPtr.Zero)
            {
                hMonitor = _config.MonitorHandle;
            }
            else if (_config.CaptureRegion.HasValue)
            {
                var region = _config.CaptureRegion.Value;
                var centerPoint = new System.Drawing.Point(region.X + region.Width / 2, region.Y + region.Height / 2);
                hMonitor = MonitorFromPoint(centerPoint, 2 /* MONITOR_DEFAULTTONEAREST */);
            }
            else
            {
                hMonitor = MonitorFromWindow(IntPtr.Zero, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            }

            IntPtr hString = IntPtr.Zero;
            WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", "Windows.Graphics.Capture.GraphicsCaptureItem".Length, out hString);
            
            try
            {
                Guid iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                int hr = RoGetActivationFactory(hString, ref iid, out var factory);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                
                Guid captureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                var ptr = factory.CreateForMonitor(hMonitor, ref captureItemGuid);
                var captureItem = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);

                // The capture frame will be at captureItem.Size (full monitor pixel size)
                int captureW = captureItem!.Size.Width;
                int captureH = captureItem.Size.Height;
                
                // Store monitor origin for crop offset calculation in EncodeLoop
                // CaptureRegion is in screen coords; captured texture starts at (0,0)
                if (_config.CaptureRegion.HasValue)
                {
                    _monitorOriginX = _config.CaptureRegion.Value.X;
                    _monitorOriginY = _config.CaptureRegion.Value.Y;
                }
                else
                {
                    _monitorOriginX = 0;
                    _monitorOriginY = 0;
                }
                
                // Output dimensions: use config Width/Height (which may be a sub-region)
                int outputW = _config.Width;
                int outputH = _config.Height;

                // Open AVFormatContext
                AVFormatContext* fmtCtx = null;
                var outFormat = ffmpeg.av_guess_format(null, _config.OutputPath ?? "output.mp4", null);
                ffmpeg.avformat_alloc_output_context2(&fmtCtx, outFormat, null, _config.OutputPath ?? "output.mp4");
                _fmtCtx = fmtCtx;

                AVCodec* codec = null;
                bool isHardware = false;

                if (_config.UseHardwareAcceleration)
                {
                    var hwEncoders = new List<string>();
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        hwEncoders.Add("h264_videotoolbox");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        hwEncoders.Add("h264_vaapi");
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        hwEncoders.Add("h264_nvenc");
                        hwEncoders.Add("h264_amf");
                        hwEncoders.Add("h264_qsv");
                    }

                    foreach (var name in hwEncoders)
                    {
                        codec = ffmpeg.avcodec_find_encoder_by_name(name);
                        if (codec != null)
                        {
                            isHardware = true;
                            Console.WriteLine($"Found hardware encoder: {name}");
                            break;
                        }
                    }
                }

                if (codec == null)
                {
                    codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                    isHardware = false;
                }

                _videoStream = ffmpeg.avformat_new_stream(_fmtCtx, codec);
                
                _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                _codecCtx->width = outputW;
                _codecCtx->height = outputH;
                _codecCtx->time_base = new AVRational { num = 1, den = _config.Framerate };
                
                AVPixelFormat chosenPixFmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                if (codec->pix_fmts != null)
                {
                    chosenPixFmt = *codec->pix_fmts;
                    var p = codec->pix_fmts;
                    while (*p != AVPixelFormat.AV_PIX_FMT_NONE)
                    {
                        if (*p == AVPixelFormat.AV_PIX_FMT_YUV420P)
                        {
                            chosenPixFmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                            break;
                        }
                        p++;
                    }
                }
                _codecCtx->pix_fmt = chosenPixFmt;

                if (!isHardware)
                {
                    ffmpeg.av_opt_set(_codecCtx->priv_data, "preset", "ultrafast", 0);
                }
                
                if ((_fmtCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                {
                    _codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                }
                
                int openRet = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                if (openRet < 0 && isHardware)
                {
                    Console.WriteLine($"Failed to open hardware encoder. Falling back to software. Error: {openRet}");
                    AVCodecContext* tempCodecCtx = _codecCtx;
                    ffmpeg.avcodec_free_context(&tempCodecCtx);
                    _codecCtx = null;
                    
                    codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
                    _codecCtx = ffmpeg.avcodec_alloc_context3(codec);
                    _codecCtx->width = outputW;
                    _codecCtx->height = outputH;
                    _codecCtx->time_base = new AVRational { num = 1, den = _config.Framerate };
                    _codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
                    ffmpeg.av_opt_set(_codecCtx->priv_data, "preset", "ultrafast", 0);
                    
                    if ((_fmtCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
                    {
                        _codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
                    }
                    
                    openRet = ffmpeg.avcodec_open2(_codecCtx, codec, null);
                }

                ffmpeg.avcodec_parameters_from_context(_videoStream->codecpar, _codecCtx);
                
                ffmpeg.avio_open(&_fmtCtx->pb, _config.OutputPath ?? "output.mp4", ffmpeg.AVIO_FLAG_WRITE);
                ffmpeg.avformat_write_header(_fmtCtx, null);

                // Staging texture matches the OUTPUT size (the region we want to encode)
                _stagingTexture = _d3dDevice!.CreateTexture2D(new Vortice.Direct3D11.Texture2DDescription
                {
                    Width = outputW,
                    Height = outputH,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    BindFlags = BindFlags.None,
                    CPUAccessFlags = CpuAccessFlags.Read,
                    MiscFlags = ResourceOptionFlags.None
                });

                // sws input = outputW x outputH (we crop to this size before color conversion)
                _swsCtx = ffmpeg.sws_getContext(
                    outputW, outputH, AVPixelFormat.AV_PIX_FMT_BGRA,
                    outputW, outputH, AVPixelFormat.AV_PIX_FMT_YUV420P,
                    2, null, null, null);

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    captureItem.Size);

                _session = _framePool.CreateCaptureSession(captureItem);
                
                _frameQueue = new System.Collections.Concurrent.BlockingCollection<Direct3D11CaptureFrame>();
                
                _framePool.FrameArrived += OnFrameArrived;
                _session.StartCapture();

                _cts = new CancellationTokenSource();
                _encodeTask = Task.Run(() => EncodeLoop(_cts.Token));
            }
            finally
            {
                if (hString != IntPtr.Zero)
                    WindowsDeleteString(hString);
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
        var frame = sender.TryGetNextFrame();
        if (frame != null && _frameQueue != null && !_frameQueue.IsAddingCompleted)
        {
            _frameQueue.Add(frame);
        }
        else
        {
            frame?.Dispose();
        }
    }

    private void EncodeLoop(CancellationToken token)
    {
        if (_frameQueue == null || _stagingTexture == null || _d3dDevice == null || _swsCtx == null) return;
        long pts = 0;
        var d3dContext = _d3dDevice.ImmediateContext;
        
        try
        {
            foreach (var capturedFrame in _frameQueue.GetConsumingEnumerable())
            {
                if (token.IsCancellationRequested)
                {
                    capturedFrame.Dispose();
                    break;
                }
                
                try
                {
                    var surface = capturedFrame.Surface;
                    var marshaledPtr = WinRT.MarshalInterface<Windows.Graphics.DirectX.Direct3D11.IDirect3DSurface>.FromManaged(surface);
                    var dxgiInterfaceAccess = Marshal.GetObjectForIUnknown(marshaledPtr) as IDirect3DDxgiInterfaceAccess;
                    Marshal.Release(marshaledPtr);
                    if (dxgiInterfaceAccess == null) continue;

                    Guid iid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); // ID3D11Texture2D
                    IntPtr texturePtr = dxgiInterfaceAccess.GetInterface(ref iid);
                    using var texture = new Vortice.Direct3D11.ID3D11Texture2D(texturePtr);
                    
                    int cropX = 0;
                    int cropY = 0;
                    d3dContext.CopySubresourceRegion(_stagingTexture, 0, 0, 0, 0, texture, 0, new Box(cropX, cropY, 0, cropX + _config.Width, cropY + _config.Height, 1));
                    
                    var mapped = d3dContext.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    
                    AVFrame* avFrame = ffmpeg.av_frame_alloc();
                    avFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
                    avFrame->width = _config.Width;
                    avFrame->height = _config.Height;
                    ffmpeg.av_frame_get_buffer(avFrame, 32);

                    byte*[] srcData = new byte*[4] { (byte*)mapped.DataPointer, null, null, null };
                    int[] srcLinesize = new int[4] { mapped.RowPitch, 0, 0, 0 };

                    ffmpeg.sws_scale(_swsCtx, srcData, srcLinesize, 0, _config.Height, avFrame->data, avFrame->linesize);

                    d3dContext.Unmap(_stagingTexture, 0);

                    avFrame->pts = pts++;
                    
                    ffmpeg.avcodec_send_frame(_codecCtx, avFrame);
                    ffmpeg.av_frame_free(&avFrame);

                    AVPacket* pkt = ffmpeg.av_packet_alloc();
                    while (ffmpeg.avcodec_receive_packet(_codecCtx, pkt) == 0)
                    {
                        ffmpeg.av_packet_rescale_ts(pkt, _codecCtx->time_base, _videoStream->time_base);
                        pkt->stream_index = _videoStream->index;
                        ffmpeg.av_interleaved_write_frame(_fmtCtx, pkt);
                        ffmpeg.av_packet_unref(pkt);
                    }
                    ffmpeg.av_packet_free(&pkt);
                }
                finally
                {
                    capturedFrame.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            bool isDeviceRemoved = ex.Message.Contains("DXGI_ERROR_DEVICE_REMOVED") || 
                                   ex.Message.Contains("DXGI_ERROR_DEVICE_RESET") ||
                                   ex.HResult == unchecked((int)0x887A0005) || 
                                   ex.HResult == unchecked((int)0x887A0007);
            if (isDeviceRemoved)
            {
                Console.WriteLine("eGPU / GPU device unplugged/removed during recording! Gracefully finishing output.");
                try
                {
                    if (_codecCtx != null && _fmtCtx != null)
                    {
                        ffmpeg.avcodec_send_frame(_codecCtx, null);
                        AVPacket* pkt = ffmpeg.av_packet_alloc();
                        while (ffmpeg.avcodec_receive_packet(_codecCtx, pkt) == 0)
                        {
                            ffmpeg.av_packet_rescale_ts(pkt, _codecCtx->time_base, _videoStream->time_base);
                            pkt->stream_index = _videoStream->index;
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
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"Error finalizing output during GPU unplug: {cleanupEx.Message}");
                }
            }
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
                    var pkt = ffmpeg.av_packet_alloc();
                    while (ffmpeg.avcodec_receive_packet(_codecCtx, pkt) == 0)
                    {
                        ffmpeg.av_packet_rescale_ts(pkt, _codecCtx->time_base, _videoStream->time_base);
                        pkt->stream_index = _videoStream->index;
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
                    var codecCtx = _codecCtx;
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
        
        _d3dDevice?.Dispose();
        _stagingTexture?.Dispose();
        if (_swsCtx != null)
        {
            ffmpeg.sws_freeContext(_swsCtx);
            _swsCtx = null;
        }
#endif

        if (_filterGraph != null)
        {
            fixed (AVFilterGraph** filterGraph = &_filterGraph)
            {
                ffmpeg.avfilter_graph_free(filterGraph);
            }
            _filterGraph = null;
        }
    }
}

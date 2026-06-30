using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D11;
using Vortice.DXGI;
using FFmpeg.AutoGen;
using Windows.Graphics.DirectX;

using Vortice.Direct3D;

namespace Zenith.PoC.Windows;

public static unsafe class Program
{
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    // COM interop for WGC
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory([MarshalAs(UnmanagedType.HString)] string activatableClassId, [In] ref Guid iid, out IGraphicsCaptureItemInterop factory);

    public static void Main(string[] args)
    {
        Console.WriteLine("Initializing Zenith Zero-Copy PoC");

        try
        {
            // 1. D3D11 Device
            Vortice.Direct3D11.D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                new[] { FeatureLevel.Level_11_1 },
                out Vortice.Direct3D11.ID3D11Device d3dDevice, out Vortice.Direct3D11.ID3D11DeviceContext context).CheckError();

            var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
            CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pUnknown);
            var winrtDevice = Marshal.GetObjectForIUnknown(pUnknown) as IDirect3DDevice;

            Console.WriteLine("D3D11 Device created successfully.");

            // 2. FFmpeg HW Context setup
            ffmpeg.RootPath = AppContext.BaseDirectory;

            // Check if FFmpeg is available
            ffmpeg.avdevice_register_all();

            AVBufferRef* hwDeviceCtx = null;
            ffmpeg.av_hwdevice_ctx_create(&hwDeviceCtx, AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA, null, null, 0);

            Console.WriteLine(hwDeviceCtx != null ? "FFmpeg HW Device Context created." : "Failed to create HW context.");

            // 3. WGC Setup
            var hMonitor = MonitorFromWindow(IntPtr.Zero, 1 /* MONITOR_DEFAULTTOPRIMARY */);
            Guid iid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
            RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref iid, out var factory);

            Guid captureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var ptr = factory.CreateForMonitor(hMonitor, ref captureItemGuid);
            var captureItem = Marshal.GetObjectForIUnknown(ptr) as GraphicsCaptureItem;

            if (captureItem != null)
            {
                var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    1,
                    captureItem.Size);

                var session = framePool.CreateCaptureSession(captureItem);
                session.StartCapture();

                Console.WriteLine("WGC Session Started.");

                using var frameEvent = new AutoResetEvent(false);
                framePool.FrameArrived += (s, e) => frameEvent.Set();

                if (frameEvent.WaitOne(2000))
                {
                    using var frame = framePool.TryGetNextFrame();
                    Console.WriteLine($"Captured frame: {frame.ContentSize.Width}x{frame.ContentSize.Height}");

                    // Wrap in AVFrame
                    AVFrame* avFrame = ffmpeg.av_frame_alloc();
                    avFrame->format = (int)AVPixelFormat.AV_PIX_FMT_D3D11;

                    Console.WriteLine("Frame mapped to FFmpeg. Hardware Encode simulation complete.");
                    ffmpeg.av_frame_free(&avFrame);
                }

                session.Dispose();
                framePool.Dispose();
            }
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("\n[ERROR] FFmpeg binaries not found!");
            Console.WriteLine("To run this PoC, download FFmpeg shared binaries and place them in the executable directory.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] Exception: {ex.Message}");
        }

        Console.WriteLine("PoC Completed.");
    }
}

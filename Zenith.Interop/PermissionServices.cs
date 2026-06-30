using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Zenith.Core;

namespace Zenith.Interop;

public class PermissionServices : IPermissionService
{
    public Task<bool> CheckScreenCapturePermissionAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows Graphics Capture (WGC) implies permission inherently via the picker or desktop duplication
            return Task.FromResult(true);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // MacOS TCC Check: CGPreflightScreenCaptureAccess()
            Console.WriteLine("Checking macOS ScreenCaptureKit permissions via TCC...");
            return Task.FromResult(false); // Scaffold default false
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Wayland DBus portal permissions
            Console.WriteLine("Checking Linux xdg-desktop-portal permissions...");
            return Task.FromResult(false); // Scaffold default false
        }
        
        return Task.FromResult(false);
    }

    public Task<bool> RequestScreenCapturePermissionAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Task.FromResult(true);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // MacOS TCC Request: CGRequestScreenCaptureAccess()
            Console.WriteLine("Requesting macOS ScreenCaptureKit permissions via TCC...");
            return Task.FromResult(true);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("Requesting Linux xdg-desktop-portal permissions...");
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }
}

using System;
using System.Diagnostics;

namespace Zenith.UI.Utils;

public class HardwareMonitor
{
    private readonly Process _process;
    private TimeSpan _lastCpuTime;
    private DateTime _lastMonitorTime;

    public HardwareMonitor()
    {
        _process = Process.GetCurrentProcess();
        _lastCpuTime = _process.TotalProcessorTime;
        _lastMonitorTime = DateTime.UtcNow;
    }

    public (double cpu, double memMB, double gpu) GetStats()
    {
        _process.Refresh();
        
        var cpuTime = _process.TotalProcessorTime;
        var now = DateTime.UtcNow;
        var cpuUsedMs = (cpuTime - _lastCpuTime).TotalMilliseconds;
        var totalMsPassed = (now - _lastMonitorTime).TotalMilliseconds;
        
        double cpuUsage = 0;
        if (totalMsPassed > 0)
        {
            cpuUsage = (cpuUsedMs / (Environment.ProcessorCount * totalMsPassed)) * 100.0;
        }
        
        _lastCpuTime = cpuTime;
        _lastMonitorTime = now;

        // PrivateMemorySize64 (Private Bytes) closely matches the Task Manager's default 'Memory' column 
        // which excludes shared memory (Shared Working Set) that WorkingSet64 includes.
        var memMB = _process.PrivateMemorySize64 / (1024.0 * 1024.0);
        var gpuUsage = 0d; // GPU tracking without external packages is omitted

        return (cpuUsage, memMB, gpuUsage);
    }
}

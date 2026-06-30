using System;
using System.Threading;
using System.Threading.Tasks;
using Zenith.Core;
using Zenith.Interop;
class Program {
    static async Task Main() {
        var engine = new FFmpegRecorderEngine();
        engine.ErrorOccurred += (s, e) => Console.WriteLine(""ERR: "" + e.Exception);
        var config = new RecordingConfig { OutputPath = ""test.mp4"", Width = 1920, Height = 1080 };
        await engine.InitializeAsync(config);
        await engine.StartAsync();
        await Task.Delay(2000);
        await engine.StopAsync();
        Console.WriteLine(""Done!"");
    }
}

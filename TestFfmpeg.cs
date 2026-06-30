using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
class Program {
    static void Main() {
        try {
            ffmpeg.LibraryVersionMap["avcodec"] = 63;
            ffmpeg.LibraryVersionMap["avdevice"] = 63;
            ffmpeg.LibraryVersionMap["avfilter"] = 12;
            ffmpeg.LibraryVersionMap["avformat"] = 63;
            ffmpeg.LibraryVersionMap["avutil"] = 61;
            ffmpeg.LibraryVersionMap["swresample"] = 7;
            ffmpeg.LibraryVersionMap["swscale"] = 10;
            
            ffmpeg.RootPath = AppContext.BaseDirectory;
            ffmpeg.avdevice_register_all();
            Console.WriteLine("Success: " + ffmpeg.avcodec_version());
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }
}

using System;
using FFmpeg.AutoGen;

class Program {
    static void Main() {
        try {
            ffmpeg.RootPath = AppContext.BaseDirectory;
            Console.WriteLine(""Attempting to load FFmpeg from "" + ffmpeg.RootPath);
            Console.WriteLine(ffmpeg.av_version_info());
        } catch (Exception ex) { 
            Console.WriteLine(ex.ToString()); 
        }
    }
}

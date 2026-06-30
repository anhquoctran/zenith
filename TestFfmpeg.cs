using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
unsafe class Program {
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

            Console.WriteLine("av_guess_format...");
            ffmpeg.av_guess_format(null, "output.mp4", null);
            Console.WriteLine("avformat_alloc_output_context2...");
            AVFormatContext* fmtCtx = null;
            ffmpeg.avformat_alloc_output_context2(&fmtCtx, null, null, "output.mp4");
            Console.WriteLine("avcodec_find_encoder...");
            ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            Console.WriteLine("avformat_new_stream...");
            if (fmtCtx != null) ffmpeg.avformat_new_stream(fmtCtx, null);
            Console.WriteLine("avcodec_alloc_context3...");
            ffmpeg.avcodec_alloc_context3(null);
            Console.WriteLine("av_opt_set...");
            ffmpeg.av_opt_set(null, "preset", "ultrafast", 0);
            Console.WriteLine("avcodec_open2...");
            // ffmpeg.avcodec_open2(null, null, null); // Might crash if null
            Console.WriteLine("avcodec_parameters_from_context...");
            Console.WriteLine("avio_open...");
            Console.WriteLine("avformat_write_header...");
            Console.WriteLine("sws_getContext...");
            ffmpeg.sws_getContext(1920, 1080, AVPixelFormat.AV_PIX_FMT_BGRA, 1920, 1080, AVPixelFormat.AV_PIX_FMT_YUV420P, 2, null, null, null);
            Console.WriteLine("av_frame_alloc...");
            ffmpeg.av_frame_alloc();
            Console.WriteLine("av_frame_get_buffer...");
            // ffmpeg.av_frame_get_buffer(null, 32); // Crashes
            Console.WriteLine("sws_scale...");
            Console.WriteLine("avcodec_send_frame...");
            Console.WriteLine("av_frame_free...");
            Console.WriteLine("av_packet_alloc...");
            ffmpeg.av_packet_alloc();
            Console.WriteLine("avcodec_receive_packet...");
            Console.WriteLine("av_interleaved_write_frame...");
            Console.WriteLine("av_packet_unref...");
            Console.WriteLine("av_packet_free...");
            
            Console.WriteLine("Done.");
        } catch (Exception ex) {
            Console.WriteLine("ERROR: " + ex.ToString());
        }
    }
}

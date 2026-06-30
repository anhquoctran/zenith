# FFmpeg License Compliance

Zenith Screen Recorder leverages **FFmpeg** via the `FFmpeg.AutoGen` bindings for hardware-accelerated video encoding and multiplexing.

## LGPL v2.1/v3.0 Adherence

Zenith uses the **LGPL** (Lesser General Public License) version of FFmpeg. To remain legally compliant with the LGPL when distributing the Zenith binary:

1. **Dynamic Linking:** Zenith does NOT statically compile FFmpeg into its executable. It uses `FFmpeg.AutoGen` to dynamically load `avcodec`, `avformat`, `avutil`, `avdevice`, and `avfilter` shared libraries (`.dll`, `.so`, `.dylib`) at runtime.
2. **Open Source:** The Zenith wrapper logic (in `Zenith.Interop`) that interfaces with FFmpeg is provided in this repository.
3. **User Modification:** Users can swap out the distributed FFmpeg dynamic libraries in the application directory with their own custom-built LGPL-compatible FFmpeg binaries, and Zenith will load them.
4. **No GPL-Only Features:** Zenith's build and configuration specifically avoids GPL-tainted flags (such as `--enable-gpl` with `libx264`) unless the end-user manually provides a GPL-compiled FFmpeg binary themselves.

## Attribution
This software uses code of <a href="http://ffmpeg.org">FFmpeg</a> licensed under the <a href="http://www.gnu.org/licenses/old-licenses/lgpl-2.1.html">LGPLv2.1</a> and its source can be downloaded from the FFmpeg website.

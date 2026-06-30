# 🎯 MASTER PROJECT REQUIREMENTS DOCUMENT (PRD)
*v2 — revised to close gaps in audio, hardware encoding, the C#↔C++ contract, webcam PiP, and platform permissions*

You are an expert Cross-Platform Software Architect specializing in C# (.NET 8), Avalonia UI, and Native C++ Multimedia Development.
Your objective is to build a high-performance Screen & Audio Recorder application for Windows, macOS, and Linux. The application uses a hybrid architecture: C# for the UI, logic, and data layers, and custom C++ Native Wrappers for hardware-accelerated, zero-copy video/audio capture and FFmpeg encoding.

> **What changed from v1:** Audio capture/mixing, the hardware-accelerated FFmpeg integration, webcam PiP, and platform permissions now have explicit architecture instead of being implied by a single "Platform-Specific Implementation" bullet. The C#↔C++ contract (callbacks, threading, stop/pause sequencing, error reporting) is specified. Execution Phase 4 is split into separate, individually-testable phases. Items that depend on decisions only you can make are called out under **Open Decisions** rather than assumed silently.

---

# 🏗️ SOLUTION ARCHITECTURE (HYBRID C# + C++)
The solution consists of a .NET 8 Workspace and a CMake C++ Project. You must strictly respect the boundaries of each layer.

## 1. Native C++ Project (`recorder_native` - CMake)
- **Role:** High-performance C++ backend. Exposes a flat C-API (`extern "C"`) for C# P/Invoke.
- **Dependencies:** FFmpeg C-API (libavformat, libavcodec, libavutil).
- **Platform-Specific Capture/Encode:**
  - **Windows:** Direct3D 11 + Windows.Graphics.Capture + D3D11VA HW Encoding.
  - **Linux:** PipeWire (via `xdg-desktop-portal`, Wayland) + VAAPI HW Encoding.
  - **macOS:** ScreenCaptureKit + VideoToolbox.
- **Core Responsibilities:**
  - VRAM texture grabbing and zero-copy hardware compositing (overlaying Webcam PiP — see §1c).
  - Pushing frames to the FFmpeg HW Encoder context without copying raw pixel data to system RAM — see §1a for how this actually works per platform.
  - Dumping raw frames to disk as PNG (Snapshot feature) — an explicit, scoped exception to the zero-copy rule; see **Zero-Copy Native Pipeline Rules** below.

### 1a. Hardware-Accelerated Encoding Pipeline *(new)*
"Zero-copy" means handing FFmpeg a reference into GPU memory it doesn't own, via `AVHWFramesContext`. This is the highest-risk part of the project and should be proven in isolation — a throwaway app that captures and hardware-encodes one frame — before anything else in Phases 4, 6, or 7 is built on top of it.
- **Windows:** Wrap the `ID3D11Texture2D` from the WGC frame pool as an `AVFrame` with pixel format `AV_PIX_FMT_D3D11`, under `AV_HWDEVICE_TYPE_D3D11VA`.
- **macOS:** The `CVPixelBuffer` ScreenCaptureKit delivers is normally already IOSurface-backed, so it typically maps straight to `AV_PIX_FMT_VIDEOTOOLBOX` under `AV_HWDEVICE_TYPE_VIDEOTOOLBOX` with no copy.
- **Linux:** The hardest of the three — there's no OS-level framework gluing this together. PipeWire negotiates buffers as DMA-BUF; import the DMA-BUF as a `VASurfaceID` (DRM-PRIME import), then wrap it as `AV_PIX_FMT_VAAPI` under `AV_HWDEVICE_TYPE_VAAPI`.
- **Acceptance criteria:** validate hardware encoder availability (especially AV1) against actual target GPU generations rather than assuming it — coverage is far less consistent than for H.264/HEVC.

### 1b. Audio Capture & Mixing *(new)*
v1 only referenced audio as an "Audio Source" config field and a waveform UI feature; neither was connected to an actual capture mechanism.
- **Windows:** WASAPI loopback mode for system/desktop audio; a separate WASAPI capture stream for the microphone.
- **Linux:** PipeWire's per-sink monitor source for system audio, negotiated through the same portal session as video.
- **macOS:** ScreenCaptureKit's `capturesAudio` stream option (no third-party driver needed) if the minimum target OS supports it, otherwise a virtual audio driver (BlackHole/Soundflower-style) — see **Open Decisions**.
- **Mixing:** system audio and microphone need to be sample-rate-matched and summed before muxing — this doesn't fall out of the video pipeline automatically.
- **UI feed:** waveform amplitude data needs its own callback channel, separate from status/snapshot callbacks, batched at roughly 30–60Hz rather than per-sample, so `InvalidateVisual()` isn't driven at audio sample rates.

### 1c. Webcam Capture & PiP Compositing *(new)*
v1 listed this only as a compositing responsibility; acquisition is actually a fourth, separate capture path per OS.
- **Windows:** Media Foundation or DirectShow. **macOS:** AVFoundation. **Linux:** V4L2.
- Webcam frames typically arrive as NV12/YUY2 while desktop capture textures are BGRA, so compositing is a format-conversion-plus-blend shader pass, not a plain texture overlay. Build this once the single-source pipeline is proven on at least one platform — not in parallel with it.

## 2. `.Interop` (C# Managed Class Library)
- **Role:** The P/Invoke Bridge.
- **Responsibilities:**
  - `[LibraryImport]` definitions to call exported functions from the C++ shared libraries (`.dll`, `.so`, `.dylib`).
  - Translates configuration structs and manages unmanaged memory pointers using `SafeHandle`.
  - Native→managed callbacks as `[UnmanagedCallersOnly]` static methods with function pointers — not `Marshal.GetFunctionPointerForDelegate`, which has GC-lifetime pitfalls. *(new)*
  - Every callback fires on a native capture/encode thread and must marshal onto the UI thread via `Dispatcher.UIThread.Post` before touching any Avalonia object. *(new)*
  - Implements `IRecorderEngine` (defined in `.Core`) against the native library. *(new)*
  - Owns the Stop sequence as an explicit state machine — **Idle → Recording → Draining → Stopped** — so the encoder gets an EOF signal, flushes buffered frames (B-frame reordering, etc.), and the muxer writes its trailer before any handle is released. Skipping this produces intermittently truncated or unplayable files. *(new)*
  - Owns Pause/Resume PTS re-basing: hardware encoders expect continuous timestamps, so resuming means shifting PTS by the paused duration, not just withholding frames. *(new)*
  - Exposes an error/event channel distinct from polling `GetStatus` — GPU context loss on sleep/resume, a disconnected webcam, a full disk. For a recorder, silently losing footage is the worst failure mode in the category and needs to surface immediately. *(new)*

## 3. `.Data` (C# Managed Class Library)
- **Role:** Data Access Layer.
- **Dependencies:** `Microsoft.Data.Sqlite`, `Dapper`.
- **Responsibilities:**
  - SQLite database initialization, versioned via `PRAGMA user_version` from v1 of the schema so future changes don't need ad-hoc migration scripts. *(new)*
  - CRUD operations using raw SQL strings via Dapper for the `Records` table: `Id, FileName, FilePath, Duration, FileSize, CreatedAt, Resolution, Codec, ThumbnailPath`. *(`Resolution`, `Codec`, `ThumbnailPath` added — the history list can't show a useful entry without them.)*

## 4. `.Core` (C# Managed Class Library)
- **Role:** Business Logic & State Management.
- **Responsibilities:**
  - Manages application state.
  - Handles A/V sync orchestration (calculating PTS via high-resolution `Stopwatch`).
  - Calls `.Data` to save records upon recording completion.
  - Feeds configurations (e.g., Monitor ID, Crop Region, Audio Source) down to `.Interop`.
  - Defines `IRecorderEngine` (Start/Stop/Pause/Resume/Snapshot + error/status events), implemented by `.Interop`. *(new)* This lets `.UI` and `.Core` logic be built and tested against a mock implementation starting in Phase 2/3, before the real native API shape is finalized — otherwise Phase 2 (UI) runs ahead of Phase 4+ (native) with nothing stable to build against, and platform-driven signature changes later would invalidate UI work.
  - Owns multi-monitor/region coordinate translation. *(new)* WGC reports physical pixels per output, ScreenCaptureKit mixes points and pixels under Retina scaling, and the Linux portal path reports logical compositor coordinates — v1 had nothing owning the reconciliation between these and on-screen mouse/overlay coordinates. `.Core` is the right home for it, since `.Interop` should stay a thin bridge.

## 5. `.UI` (C# Avalonia UI Application)
- **Role:** The Cross-Platform Frontend.
- **Dependencies:** `Avalonia.Desktop`, `SkiaSharp.Views.Avalonia`.
- **Responsibilities:**
  - Fluent/Material-like UI with a Two-Pane layout.
  - **Main Window:** Left pane for Source Selection (Screen, Region, Camera, Audio) and a `SkiaSharp` Canvas for real-time audio waveform rendering, driven by a timer or batched-callback data via `InvalidateVisual()` — Avalonia has no built-in render loop, so this needs an explicit strategy. Right pane for the SQLite Recording History (`ListView`).
  - **Mini Widget:** A floating, draggable, always-on-top borderless widget (Topmost=True, SystemDecorations=None) with a Timer, Pause/Stop, and a "Snapshot" button. Hides the Main Window when active.
  - **Region Select Overlay** *(new)*: A transparent per-monitor overlay window for drag-to-select region recording — "Region" was a listed source in v1 with no corresponding UI design. Can be built against placeholder coordinates in Phase 2 and wired to real geometry once `.Core`'s coordinate translation exists.
  - **Global Hotkeys** *(new)*: Start/Stop/Snapshot bindings — platform-fragmented: `RegisterHotKey` (Windows), Carbon/Quartz event taps (macOS), portal- or X11-dependent (Linux).
  - **System Tray:** Avalonia native `TrayIcon` for background execution. *Linux note:* depends on the desktop environment supporting StatusNotifierItem/AppIndicator — vanilla GNOME needs an extension for the icon to appear at all.

---

# 🔐 PERMISSIONS & PLATFORM PORTALS *(new)*
Capture can't start without these, and they shape onboarding UX, not just backend plumbing.
- **macOS:** Explicit TCC grants for Screen Recording and Microphone, with `NSScreenCaptureUsageDescription` / `NSMicrophoneUsageDescription` in Info.plist. Common quirk: the app often needs a restart after Screen Recording permission is granted before capture actually works — design the first-run flow around this rather than treating it as a bug to fix later.
- **Linux:** PipeWire capture flows through `xdg-desktop-portal`'s `ScreenCast` interface, which only applies under **Wayland** and behaves slightly differently across GNOME, KDE, and wlroots portal implementations. X11 needs an entirely separate path (XShm/XComposite) — whether it's in scope is an open decision below, since "Linux: PipeWire" as written quietly assumes Wayland-only.

---

# 🚀 ZERO-COPY NATIVE PIPELINE RULES
The C++ layer MUST be strictly optimized.
1. **No Staging Textures — scoped to the continuous recording pipeline.** Do not map VRAM to System RAM for the video path. *Explicit exception:* the Snapshot feature requires a one-time CPU-readable copy of a single frame — there's no realistic GPU-native PNG encode path. This is an intentional, isolated readback, not a violation of rule #1. *(clarified — v1 stated this as an unscoped absolute, which directly contradicted the Snapshot feature)*
2. The C# layer passes a configuration struct (e.g., `StartRecording(config)`).
3. The C++ layer completely takes over the heavy lifting. The C# UI remains highly responsive, only querying the C++ layer for status updates or receiving asynchronous callbacks (see `.Interop` §2 for the callback/threading contract).

---

# ❓ OPEN DECISIONS *(new — confirm before Phase 4+)*
These were implicit assumptions in v1; each changes scope depending on the answer.
1. **8K capture** — hard requirement or aspirational ceiling? Hardware 8K encode support is inconsistent across GPU generations even where 4K is universal, and this determines the test matrix for Phases 4, 6, and 7.
2. **Target codec/container** — not currently specified (H.264/HEVC/AV1 in MP4/MKV?). Affects which hardware encoders need validating.
3. **FFmpeg build configuration** — which encoders/filters are enabled in the linked build matters, because depending on configure flags the build can end up GPL-encumbered rather than LGPL, which matters for closed-source distribution.
4. **X11 support on Linux** — in scope, or Wayland-only? Determines whether a second Linux capture path is needed alongside the portal/PipeWire path.
5. **macOS minimum OS version** — determines whether `ScreenCaptureKit.capturesAudio` (13+) is usable or a virtual audio driver fallback is required.

---

# 🛠️ EXECUTION PHASES FOR CLAUDE CODE
Do NOT generate the entire codebase at once. Execute step-by-step and ask for my approval after completing each phase. v1's Phase 4 covered all native platform work, audio, and webcam in one bullet; that's split below into separately-testable phases.

### Phase 1: Solution Scaffold & Data Layer
- Scaffold the 4 C# projects (`.UI`, `.Core`, `.Data`, `.Interop`) and link references.
- Implement SQLite setup (with `PRAGMA user_version` versioning) and the `RecordRepository` in `.Data` using **Dapper**.

### Phase 2: Avalonia UI Shell
- Build `MainWindow.axaml` (Two-Pane layout, SkiaSharp Canvas) and `RecordingWidget.axaml` (Mini Floating Widget).
- Build the Region Select Overlay against placeholder coordinates.
- Implement the Avalonia System Tray integration.

### Phase 3: C++ CMake Scaffold, Interop & Engine Contract
- Create the CMake project `recorder_native`.
- Define `extern "C"` structs and function signatures (Init, Start, Stop, Pause, Resume, GetStatus, TakeSnapshot).
- Map these in `.Interop` using `[LibraryImport]`, plus `[UnmanagedCallersOnly]` callback definitions.
- Define `IRecorderEngine` in `.Core` and a mock implementation, so `.UI`/`.Core` logic can be tested independent of native code.

### Phase 4: Windows Native Capture & Encode *(proof of concept)*
- WGC + D3D11 capture → `AVHWFramesContext`/`AV_PIX_FMT_D3D11` zero-copy encode → FFmpeg mux to disk.
- Exit criterion: one complete, valid recording end-to-end on Windows before any other platform or feature proceeds.

### Phase 5: Audio Capture & Mixing
- WASAPI loopback (system audio) + WASAPI capture (microphone), sample-rate-matched mixing.
- Waveform amplitude callback channel into `.UI`, wired to the SkiaSharp canvas.

### Phase 6: Linux Native Capture & Encode
- `xdg-desktop-portal` ScreenCast session → PipeWire frames → DMA-BUF → VAAPI import → `AV_PIX_FMT_VAAPI` encode.
- PipeWire monitor-source audio capture.

### Phase 7: macOS Native Capture & Encode
- ScreenCaptureKit capture → VideoToolbox zero-copy encode.
- System audio via `capturesAudio` (or driver fallback, per Open Decisions).

### Phase 8: Webcam Capture & PiP Compositing
- Per-OS webcam acquisition (Media Foundation/DirectShow, AVFoundation, V4L2).
- Format-conversion-plus-blend compositing pass against the desktop capture texture.

### Phase 9: Permissions, Coordinate Translation, Hotkeys, Error Events
- macOS TCC flow (including restart-after-grant UX) and Linux portal/Wayland permission flow.
- Multi-monitor/region coordinate translation in `.Core`; wire the Region Select Overlay to real geometry.
- Global hotkeys per platform.
- Error/status event channel surfaced into `.UI`.

### Phase 10: Packaging & Distribution
- CMake cross-build matrix for all three platforms.
- FFmpeg license compliance check against the final configure flags.
- macOS notarization; Linux distribution format TBD (AppImage/Flatpak/deb).

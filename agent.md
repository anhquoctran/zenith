# 🌟 ARCHITECTURE UPDATE: MULTI-GPU & eGPU TOPOLOGY HANDLING

The application must gracefully handle multi-GPU environments (Hybrid Graphics/Optimus) and external GPUs (eGPU) across Windows, macOS, and Linux without breaking the zero-copy pipeline unless explicitly requested by the user.

## Native Layer Responsibilities (`recorder_native` C++)
1. **GPU Enumeration:** Expose a C-API function to return a list of all available physical GPUs to the C# UI.
2. **Display-GPU Binding (Zero-Copy):** When a capture target is selected, dynamically query which GPU adapter owns that target. Initialize the D3D11 Device (Windows), Metal Device (macOS), or DRM Render Node (Linux) **strictly on that owning GPU**. Bind the FFmpeg Hardware Encoder (`AVHWDeviceContext`) to this exact same adapter.
3. **Cross-Adapter Support (Fallback):** If the user forces an encoder on GPU B (eGPU) while capturing a screen on GPU A (iGPU), implement hardware-accelerated cross-adapter texture sharing (e.g., `D3D11_RESOURCE_MISC_SHARED` NT Handles on Windows) instead of CPU staging.
4. **Hot-Unplug Resilience:** Capture device removal errors (e.g., `DXGI_ERROR_DEVICE_REMOVED` due to eGPU cable disconnect). Gracefully finalize the `av_interleaved_write_frame` buffer, close the `.mp4` file to prevent corruption, and fire a callback to the C# layer to halt the UI timer.

## UI/UX Updates (`.UI` Avalonia)
- Add an "Encoder Setup" section in the Advanced Settings.
- Provide a Dropdown: "Hardware Encoder: [ Auto (Zero-Copy) | Intel UHD Graphics (iGPU) | NVIDIA RTX 4090 (eGPU) ]".
- If the user selects a GPU different from the capture source, show a subtle warning icon indicating a potential PCIe bandwidth hit.

using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Zenith.Interop;

public class AudioCaptureEngine : IDisposable
{
    private WasapiLoopbackCapture? _systemAudioCapture;
    private WasapiCapture? _micCapture;

    public event EventHandler<float>? WaveformDataAvailable;

    public void Start()
    {
        try
        {
            _systemAudioCapture = new WasapiLoopbackCapture();
            _systemAudioCapture.DataAvailable += OnDataAvailable;
            _systemAudioCapture.StartRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"System Audio Capture Error: {ex.Message}");
        }

        try
        {
            _micCapture = new WasapiCapture();
            _micCapture.DataAvailable += OnDataAvailable;
            _micCapture.StartRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Microphone Capture Error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _systemAudioCapture?.StopRecording();
        _micCapture?.StopRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        var capture = sender as WasapiCapture;
        if (capture == null) return;

        float maxAmplitude = 0;

        if (capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (var i = 0; i < e.BytesRecorded; i += 4)
            {
                var sample = BitConverter.ToSingle(e.Buffer, i);
                var abs = Math.Abs(sample);
                if (abs > maxAmplitude) maxAmplitude = abs;
            }
        }
        else if (capture.WaveFormat.Encoding == WaveFormatEncoding.Pcm && capture.WaveFormat.BitsPerSample == 16)
        {
            for (var i = 0; i < e.BytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i);
                var abs = Math.Abs((float)sample / 32768f);
                if (abs > maxAmplitude) maxAmplitude = abs;
            }
        }

        // Note: NAudio fires DataAvailable often enough. We fire this event to push to UI.
        // We will throttle UI updates in the UI layer.
        WaveformDataAvailable?.Invoke(this, maxAmplitude);
    }

    public void Dispose()
    {
        Stop();

        if (_systemAudioCapture != null)
        {
            _systemAudioCapture.DataAvailable -= OnDataAvailable;
            _systemAudioCapture.Dispose();
        }

        if (_micCapture != null)
        {
            _micCapture.DataAvailable -= OnDataAvailable;
            _micCapture.Dispose();
        }
    }
}

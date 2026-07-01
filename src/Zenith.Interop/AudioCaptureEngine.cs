using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Zenith.Core;

namespace Zenith.Interop;

public class AudioCaptureEngine : IDisposable
{
    public event EventHandler<float>? WaveformDataAvailable;
    
    // We will raise this event when a mixed chunk of PCM float data is ready
    public event EventHandler<float[]>? AudioBufferReady;

    private readonly List<WasapiCapture> _captures = new();
    private MixingSampleProvider? _mixer;
    private readonly WaveFormat _targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private readonly Dictionary<WasapiCapture, ISampleProvider> _resamplers = new();

    public void Start(IEnumerable<AudioLayer> layers)
    {
        Stop();
        
        _mixer = new MixingSampleProvider(_targetFormat);

        foreach (var layer in layers)
        {
            try
            {
                WasapiCapture capture;
                if (layer.Type == AudioLayerType.SystemAudio)
                {
                    if (string.IsNullOrEmpty(layer.SourceId)) 
                    {
                        capture = new WasapiLoopbackCapture();
                    }
                    else 
                    {
                        var enumerator = new MMDeviceEnumerator();
                        var device = enumerator.GetDevice(layer.SourceId);
                        capture = new WasapiLoopbackCapture(device);
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(layer.SourceId))
                    {
                        capture = new WasapiCapture();
                    }
                    else
                    {
                        var enumerator = new MMDeviceEnumerator();
                        var device = enumerator.GetDevice(layer.SourceId);
                        capture = new WasapiCapture(device);
                    }
                }

                var bufferedProvider = new BufferedWaveProvider(capture.WaveFormat)
                {
                    ReadFully = false,
                    DiscardOnBufferOverflow = true
                };

                // Convert to SampleProvider
                ISampleProvider sampleProvider = capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat 
                    ? new WaveToSampleProvider(bufferedProvider) 
                    : new Pcm16BitToSampleProvider(bufferedProvider);
                
                // Resample to target format if needed
                if (capture.WaveFormat.SampleRate != _targetFormat.SampleRate || capture.WaveFormat.Channels != _targetFormat.Channels)
                {
                    var resampler = new MediaFoundationResampler(bufferedProvider, _targetFormat);
                    sampleProvider = new WaveToSampleProvider(resampler);
                }

                // Add volume control
                var volumeProvider = new VolumeSampleProvider(sampleProvider) { Volume = layer.Volume };

                _captures.Add(capture);
                _resamplers[capture] = volumeProvider;
                _mixer.AddMixerInput(volumeProvider);

                capture.DataAvailable += (s, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    }
                };

                capture.StartRecording();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start audio layer {layer.Name}: {ex.Message}");
            }
        }
        
        // Start a background thread to pull from mixer and emit events
        System.Threading.Tasks.Task.Run(MixerLoop);
    }

    private void MixerLoop()
    {
        var buffer = new float[4096];
        while (_mixer != null)
        {
            try
            {
                int read = _mixer.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    float maxAmp = 0;
                    for (int i = 0; i < read; i++)
                    {
                        var abs = Math.Abs(buffer[i]);
                        if (abs > maxAmp) maxAmp = abs;
                    }

                    WaveformDataAvailable?.Invoke(this, maxAmp);
                    
                    var outBuffer = new float[read];
                    Array.Copy(buffer, outBuffer, read);
                    AudioBufferReady?.Invoke(this, outBuffer);
                }
                else
                {
                    System.Threading.Thread.Sleep(10);
                }
            }
            catch
            {
                break;
            }
        }
    }

    public void Stop()
    {
        _mixer = null; // Stops the loop

        foreach (var capture in _captures)
        {
            capture.StopRecording();
            capture.Dispose();
        }
        _captures.Clear();
        _resamplers.Clear();
    }

    public void Dispose()
    {
        Stop();
    }
}

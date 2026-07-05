using NAudio.Wave;

namespace LLamaVoz.Audio;

/// <summary>
/// Captures the default microphone as 16 kHz / 16-bit / mono PCM — the format the local ASR
/// consumes directly. Raises <see cref="LevelChanged"/> (RMS 0..1) for the overlay meter.
/// Audio lives only in memory and is discarded after each session (NFR-09: never persisted).
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    private readonly object _sync = new();
    private readonly List<byte> _buffer = new();
    private WaveInEvent? _waveIn;

    public event Action<float>? LevelChanged;

    /// <summary>Raw PCM chunks (~50 ms) as they arrive, for streaming consumers.</summary>
    public event Action<byte[]>? DataAvailable;

    public bool IsRecording { get; private set; }

    public const int SampleRate = 16000;
    public const int BytesPerSecond = SampleRate * 2;

    public void Start(int deviceNumber = -1)
    {
        lock (_sync)
        {
            if (IsRecording)
            {
                return;
            }
            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException("No hay micrófonos disponibles.");
            }

            _buffer.Clear();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = 50,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsRecording = true;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            for (var i = 0; i < e.BytesRecorded; i++)
            {
                _buffer.Add(e.Buffer[i]);
            }
        }

        if (e.BytesRecorded > 0 && DataAvailable is { } handler)
        {
            var chunk = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, chunk, e.BytesRecorded);
            handler(chunk);
        }

        var samples = e.BytesRecorded / 2;
        if (samples == 0)
        {
            return;
        }
        double sumSquares = 0;
        for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            sumSquares += (double)sample * sample;
        }
        var rms = Math.Sqrt(sumSquares / samples) / short.MaxValue;
        LevelChanged?.Invoke((float)Math.Min(1.0, rms * 4)); // gain so normal speech fills the meter
    }

    /// <summary>Stops capture and returns the recorded PCM (16 kHz / 16-bit / mono).</summary>
    public byte[] Stop()
    {
        lock (_sync)
        {
            if (!IsRecording || _waveIn is null)
            {
                return Array.Empty<byte>();
            }

            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
            IsRecording = false;

            var pcm = _buffer.ToArray();
            _buffer.Clear();
            return pcm;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

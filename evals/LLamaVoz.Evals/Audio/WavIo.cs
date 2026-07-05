using System.IO;

namespace LLamaVoz.Evals.Audio;

/// <summary>Minimal WAV reader/composer for the 16 kHz / 16-bit / mono files the evals use.</summary>
public static class WavIo
{
    /// <summary>Reads the PCM data chunk of a 16 kHz mono 16-bit WAV.</summary>
    public static byte[] ReadPcm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Find the "data" chunk (the fmt chunk is not always 16 bytes — TTS adds extras).
        for (var i = 12; i + 8 <= bytes.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            var size = BitConverter.ToInt32(bytes, i + 4);
            if (id == "data")
            {
                var pcm = new byte[Math.Min(size, bytes.Length - i - 8)];
                Array.Copy(bytes, i + 8, pcm, 0, pcm.Length);
                return pcm;
            }
            i += 8 + size + (size % 2); // chunks are word-aligned
        }
        throw new InvalidDataException($"Sin chunk 'data': {path}");
    }

    public static byte[] Silence(double seconds) => new byte[(int)(seconds * 32000) & ~1];

    public static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }

    public static double Seconds(byte[] pcm) => pcm.Length / 32000.0;

    /// <summary>Splits PCM into ~50 ms chunks like MicrophoneRecorder produces.</summary>
    public static IEnumerable<byte[]> Chunks(byte[] pcm, int chunkMs = 50)
    {
        var chunkBytes = 32000 * chunkMs / 1000;
        for (var offset = 0; offset < pcm.Length; offset += chunkBytes)
        {
            var size = Math.Min(chunkBytes, pcm.Length - offset);
            var chunk = new byte[size];
            Array.Copy(pcm, offset, chunk, 0, size);
            yield return chunk;
        }
    }
}

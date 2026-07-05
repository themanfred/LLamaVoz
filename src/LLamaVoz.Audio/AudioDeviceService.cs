using NAudio.CoreAudioApi;

namespace LLamaVoz.Audio;

public sealed record AudioDeviceInfo(string Id, string FriendlyName, bool IsDefault);

/// <summary>
/// Enumerates active capture devices (microphones) and identifies the default one (FR-004).
/// When the selected device disappears, callers should fall back to the default and warn.
/// </summary>
public sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        string? defaultId = null;
        try
        {
            defaultId = _enumerator
                .GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                .ID;
        }
        catch (Exception)
        {
            // No capture device present at all — an empty list is the honest answer.
        }

        var devices = new List<AudioDeviceInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
        }
        return devices;
    }

    public AudioDeviceInfo? GetDefaultCaptureDevice() =>
        GetCaptureDevices().FirstOrDefault(d => d.IsDefault);

    public void Dispose() => _enumerator.Dispose();
}

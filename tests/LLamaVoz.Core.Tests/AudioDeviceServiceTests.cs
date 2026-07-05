using LLamaVoz.Audio;

namespace LLamaVoz.Core.Tests;

public class AudioDeviceServiceTests
{
    [Fact]
    public void GetCaptureDevices_ReturnsListWithoutThrowing()
    {
        using var service = new AudioDeviceService();
        var devices = service.GetCaptureDevices();

        Assert.NotNull(devices);
        // At most one device may be flagged as default.
        Assert.True(devices.Count(d => d.IsDefault) <= 1);
    }

    [Fact]
    public void GetDefaultCaptureDevice_MatchesFlaggedDevice()
    {
        using var service = new AudioDeviceService();
        var devices = service.GetCaptureDevices();
        var defaultDevice = service.GetDefaultCaptureDevice();

        if (defaultDevice is not null)
        {
            Assert.Contains(devices, d => d.Id == defaultDevice.Id && d.IsDefault);
        }
        else
        {
            Assert.DoesNotContain(devices, d => d.IsDefault);
        }
    }
}

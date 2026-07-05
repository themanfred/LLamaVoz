using System.IO;
using LLamaVoz.DesktopApp.Services;

namespace LLamaVoz.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llamavoz-tests", Guid.NewGuid().ToString("N"));

    private SettingsService Create() => new(_dir);

    [Fact]
    public void Defaults_AreSafe()
    {
        var s = Create();
        Assert.Equal("auto", s.TranscriptionLanguage);
        Assert.Equal("accurate", s.QualityMode);
        Assert.Equal("ctrl+alt", s.PttHotkey);
        Assert.Equal("win+alt", s.ToggleHotkey);
    }

    [Fact]
    public void UnknownLanguage_NormalizesToAuto()
    {
        var s = Create();
        s.TranscriptionLanguage = "es";
        s.TranscriptionLanguage = "klingon";
        Assert.Equal("auto", s.TranscriptionLanguage);
    }

    [Fact]
    public void Language_PersistsAcrossInstances()
    {
        Create().TranscriptionLanguage = "es";
        Assert.Equal("es", Create().TranscriptionLanguage);
    }

    [Fact]
    public void Hotkey_CollidingWithOther_IsRejected()
    {
        var s = Create();
        s.PttHotkey = "win+alt"; // same as toggle default → must be rejected
        Assert.Equal("ctrl+alt", s.PttHotkey);
        s.ToggleHotkey = "ctrl+alt"; // same as ptt → rejected
        Assert.Equal("win+alt", s.ToggleHotkey);
    }

    [Fact]
    public void Hotkey_UnknownCode_IsRejected()
    {
        var s = Create();
        s.PttHotkey = "ctrl+alt+supr";
        Assert.Equal("ctrl+alt", s.PttHotkey);
    }

    [Fact]
    public void Hotkey_ValidChange_PersistsAndFiresEvent()
    {
        var s = Create();
        var fired = false;
        s.HotkeysChanged += () => fired = true;
        s.PttHotkey = "ctrl+shift";
        Assert.True(fired);
        Assert.Equal("ctrl+shift", Create().PttHotkey);
    }

    [Fact]
    public void MigrationV1_BalancedResetsToAccurate()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"Version":1,"TranscriptionLanguage":"en","QualityMode":"balanced","PttHotkey":"ctrl+alt","ToggleHotkey":"win+alt"}""");
        var s = Create();
        Assert.Equal("accurate", s.QualityMode);
        Assert.Equal("en", s.TranscriptionLanguage); // migration only touches quality
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{not json!!");
        var s = Create();
        Assert.Equal("auto", s.TranscriptionLanguage);
        Assert.Equal("accurate", s.QualityMode);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

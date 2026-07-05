using LLamaVoz.DesktopApp.Services;

namespace LLamaVoz.Core.Tests;

public class HotkeyParsingTests
{
    [Theory]
    [InlineData("ctrl+alt", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)]
    [InlineData("ctrl+shift", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift)]
    [InlineData("ctrl+win", HotkeyModifiers.Ctrl | HotkeyModifiers.Win)]
    [InlineData("alt+shift", HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    [InlineData("win+alt", HotkeyModifiers.Win | HotkeyModifiers.Alt)]
    [InlineData("win+shift", HotkeyModifiers.Win | HotkeyModifiers.Shift)]
    public void AllOfferedCodes_ParseToExpectedFlags(string code, HotkeyModifiers expected)
    {
        Assert.Equal(expected, KeyboardHookService.ParseHotkey(code));
    }

    [Fact]
    public void EveryHotkeyInSettingsCatalog_ParsesToTwoModifiers()
    {
        foreach (var (code, _) in SettingsService.Hotkeys)
        {
            var flags = KeyboardHookService.ParseHotkey(code);
            var bits = System.Numerics.BitOperations.PopCount((uint)flags);
            Assert.True(bits == 2, $"'{code}' parsed to {flags} ({bits} modifiers, expected 2)");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo+bar")]
    [InlineData("+++")]
    public void GarbageInput_ParsesToNone(string code)
    {
        Assert.Equal(HotkeyModifiers.None, KeyboardHookService.ParseHotkey(code));
    }

    [Fact]
    public void Parsing_IsCaseAndWhitespaceInsensitive()
    {
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, KeyboardHookService.ParseHotkey(" CTRL + Alt "));
    }
}

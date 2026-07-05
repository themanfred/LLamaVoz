using System.IO;
using System.Text.Json;

namespace LLamaVoz.DesktopApp.Services;

/// <summary>
/// User preferences persisted to %APPDATA%\LLamaVoz\settings.json (settings only,
/// never dictated content). Currently: the transcription language lock.
/// "auto" = whisper detects per dictation; any other code (es/en/fr/de) is forced
/// on the engine and auto-detection is disabled for that recording.
/// </summary>
public sealed class SettingsService
{
    public static readonly (string Code, string Label)[] Languages =
    {
        ("auto", "AUTO — Detección automática"),
        ("es", "ES — Español"),
        ("en", "EN — English"),
        ("fr", "FR — Français"),
        ("de", "DE — Deutsch"),
    };

    public static readonly (string Code, string Label)[] QualityModes =
    {
        ("accurate", "Preciso — solo modelo base (recomendado)"),
        ("balanced", "Equilibrado — tiny + verificación con base"),
        ("fast", "Rápido — solo modelo tiny"),
    };

    /// <summary>Modifier combos offered for both hotkeys (two modifiers each, so no
    /// combo can be a subset of another — pressing one can never half-trigger the other).</summary>
    public static readonly (string Code, string Label)[] Hotkeys =
    {
        ("ctrl+alt", "Ctrl + Alt"),
        ("ctrl+shift", "Ctrl + Mayús"),
        ("ctrl+win", "Ctrl + Win"),
        ("alt+shift", "Alt + Mayús"),
        ("win+alt", "Win + Alt"),
        ("win+shift", "Win + Mayús"),
    };

    private sealed class SettingsData
    {
        // Defaults to 1 so files written before versioning existed migrate on first load.
        public int Version { get; set; } = 1;
        public string TranscriptionLanguage { get; set; } = "auto";
        public string QualityMode { get; set; } = "accurate";
        public string PttHotkey { get; set; } = "ctrl+alt";
        public string ToggleHotkey { get; set; } = "win+alt";
    }

    private readonly string _path;
    private readonly object _sync = new();
    private readonly SettingsData _data;

    /// <summary>Raised (on the caller's thread) when the language selection changes.</summary>
    public event Action<string>? LanguageChanged;

    /// <summary>Raised (on the caller's thread) when either hotkey changes.</summary>
    public event Action? HotkeysChanged;

    public SettingsService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LLamaVoz"))
    {
    }

    /// <summary>Test seam: store settings under an arbitrary directory.</summary>
    internal SettingsService(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
        _data = Load();
        // Migration v1→v2: "balanced" was briefly the shipped default before calibration;
        // the draft model's quality isn't acceptable yet, so reset once to the safe default.
        if (_data.Version < 2)
        {
            if (_data.QualityMode == "balanced")
            {
                _data.QualityMode = "accurate";
            }
            _data.Version = 2;
            Save();
        }
    }

    public string TranscriptionLanguage
    {
        get { lock (_sync) return _data.TranscriptionLanguage; }
        set
        {
            var normalized = Languages.Any(l => l.Code == value) ? value : "auto";
            lock (_sync)
            {
                if (_data.TranscriptionLanguage == normalized)
                {
                    return;
                }
                _data.TranscriptionLanguage = normalized;
                Save();
            }
            LanguageChanged?.Invoke(normalized);
        }
    }

    /// <summary>"fast" (tiny only), "balanced" (draft→verify), "accurate" (base only).</summary>
    public string QualityMode
    {
        get { lock (_sync) return _data.QualityMode; }
        set
        {
            var normalized = QualityModes.Any(m => m.Code == value) ? value : "accurate";
            lock (_sync)
            {
                if (_data.QualityMode == normalized)
                {
                    return;
                }
                _data.QualityMode = normalized;
                Save();
            }
        }
    }

    /// <summary>Hold-to-talk combo code (e.g. "ctrl+alt"). Rejects values equal to the toggle combo.</summary>
    public string PttHotkey
    {
        get { lock (_sync) return _data.PttHotkey; }
        set => SetHotkey(value, ptt: true);
    }

    /// <summary>Tap-to-toggle combo code (e.g. "win+alt"). Rejects values equal to the PTT combo.</summary>
    public string ToggleHotkey
    {
        get { lock (_sync) return _data.ToggleHotkey; }
        set => SetHotkey(value, ptt: false);
    }

    private void SetHotkey(string value, bool ptt)
    {
        lock (_sync)
        {
            if (!Hotkeys.Any(h => h.Code == value)
                || value == (ptt ? _data.ToggleHotkey : _data.PttHotkey)
                || value == (ptt ? _data.PttHotkey : _data.ToggleHotkey))
            {
                return; // unknown, unchanged, or would collide with the other hotkey
            }
            if (ptt)
            {
                _data.PttHotkey = value;
            }
            else
            {
                _data.ToggleHotkey = value;
            }
            Save();
        }
        HotkeysChanged?.Invoke();
    }

    /// <summary>Human label for a hotkey code, e.g. "ctrl+alt" → "Ctrl + Alt".</summary>
    public static string HotkeyLabel(string code) =>
        Hotkeys.FirstOrDefault(h => h.Code == code).Label ?? code;

    /// <summary>Short chip label for the overlay, e.g. "ES", "EN", "AUTO".</summary>
    public static string ChipLabel(string code) => code == "auto" ? "AUTO" : code.ToUpperInvariant();

    private SettingsData Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path)) ?? new SettingsData();
            }
        }
        catch
        {
            // Corrupt settings: fall back to defaults rather than failing the app.
        }
        return new SettingsData();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch
        {
            // Best-effort; never break dictation over settings persistence.
        }
    }
}

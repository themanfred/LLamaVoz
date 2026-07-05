using System.Windows.Threading;
using LLamaVoz.Audio;
using LLamaVoz.Core.Insertion;
using LLamaVoz.Insertion;

namespace LLamaVoz.DesktopApp.Services;

public sealed record DictationRecord(
    bool Success,
    string Text,
    string Language,
    string MethodLabel,
    int Words,
    double AudioSeconds,
    string Message);

/// <summary>
/// Dictation state machine with two activation modes (FR-006):
///  - PushToTalk: hold Ctrl+Alt, release to finish.
///  - Toggle: tap Win+Alt to start, tap again to finish (Esc cancels).
/// The target window is captured when listening starts; the insertion engine re-verifies
/// it right before inserting (FR-028).
/// </summary>
public sealed class DictationController : IDisposable
{
    private enum State { Standby, Listening, Processing }

    private enum SessionMode { PushToTalk, Toggle }

    private static readonly TimeSpan MaxPttRecording = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxToggleRecording = TimeSpan.FromSeconds(120);
    private const int MinPcmBytes = MicrophoneRecorder.BytesPerSecond * 2 / 5; // ~0.4 s

    private readonly KeyboardHookService _hook;
    private readonly MicrophoneRecorder _recorder;
    private readonly TranscriptionService _transcription;
    private readonly SettingsService _settings;
    private readonly InsertionEngine _engine = new();
    private readonly OverlayWindow _overlay;
    private readonly DispatcherTimer _safetyTimer;

    private State _state = State.Standby;
    private SessionMode _mode = SessionMode.PushToTalk;
    private TargetWindowGuard? _target;
    private StreamingTranscriptionSession? _session;

    /// <summary>UI state label: "En espera" / "Escuchando…" / "Procesando…".</summary>
    public event Action<string>? StateChanged;

    /// <summary>Raised after each completed (or failed) dictation for the dashboard.</summary>
    public event Action<DictationRecord>? Completed;

    public DictationController(
        KeyboardHookService hook,
        MicrophoneRecorder recorder,
        TranscriptionService transcription,
        SettingsService settings,
        OverlayWindow overlay)
    {
        _hook = hook;
        _recorder = recorder;
        _transcription = transcription;
        _settings = settings;
        _overlay = overlay;

        _safetyTimer = new DispatcherTimer();
        _safetyTimer.Tick += (_, _) => StopAndProcess();

        _hook.PttPressed += OnPttPressed;
        _hook.PttReleased += OnPttReleased;
        _hook.ToggleTapped += OnToggleTapped;
        _hook.PttInterrupted += CancelListening;
        _hook.EscapePressed += CancelListening;
        _recorder.LevelChanged += OnLevelChanged;
    }

    private void OnPttPressed()
    {
        StartListening(SessionMode.PushToTalk);
    }

    private void OnPttReleased()
    {
        if (_state == State.Listening && _mode == SessionMode.PushToTalk)
        {
            StopAndProcess();
        }
    }

    /// <summary>UI entry point (overlay mic button): behaves like a toggle-hotkey tap.
    /// The overlay never takes focus, so the foreground target window is preserved.</summary>
    public void ToggleDictation() => OnToggleTapped();

    private void OnToggleTapped()
    {
        switch (_state)
        {
            case State.Standby:
                StartListening(SessionMode.Toggle);
                break;
            case State.Listening when _mode == SessionMode.Toggle:
                StopAndProcess();
                break;
        }
    }

    private void StartListening(SessionMode mode)
    {
        if (_state != State.Standby)
        {
            return;
        }

        var target = TargetWindowGuard.CaptureForeground();
        if (target.ProcessId == (uint)Environment.ProcessId)
        {
            return; // Never dictate into our own windows.
        }

        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            _overlay.ShowResult($"Micrófono no disponible: {ex.Message}", success: false, autoHideMs: 3500);
            return;
        }

        // Language is read once here and stays locked for the whole recording;
        // a manual selection disables auto-detection (FR: selección manual manda).
        var language = _settings.TranscriptionLanguage;

        // Transcribe segments in the background while the user is still speaking,
        // so only the trailing segment remains at key-release.
        _session = new StreamingTranscriptionSession(_transcription, language, _settings.QualityMode);
        _recorder.DataAvailable += _session.AddChunk;

        _target = target;
        _mode = mode;
        _state = State.Listening;
        StateChanged?.Invoke("Escuchando…");

        var hint = mode == SessionMode.Toggle
            ? $"Escuchando… ({SettingsService.HotkeyLabel(_settings.ToggleHotkey)} para terminar, Esc cancela)"
            : $"Escuchando… (suelta {SettingsService.HotkeyLabel(_settings.PttHotkey)} para insertar, Esc cancela)";
        _overlay.ShowListening(SettingsService.ChipLabel(language), hint);

        _safetyTimer.Interval = mode == SessionMode.Toggle ? MaxToggleRecording : MaxPttRecording;
        _safetyTimer.Start();
    }

    private void OnLevelChanged(float level)
    {
        if (_state == State.Listening)
        {
            _overlay.Dispatcher.BeginInvoke(() => _overlay.UpdateLevel(level));
        }
    }

    private void CancelListening()
    {
        if (_state != State.Listening)
        {
            return;
        }
        _safetyTimer.Stop();
        DetachSession()?.Cancel();
        _recorder.Stop(); // audio discarded, never persisted
        _state = State.Standby;
        StateChanged?.Invoke("En espera");
        _overlay.HideOverlay();
    }

    private void StopAndProcess()
    {
        if (_state != State.Listening)
        {
            return;
        }
        _safetyTimer.Stop();

        var session = DetachSession();
        var pcm = _recorder.Stop();
        if (pcm.Length < MinPcmBytes || session is null)
        {
            // Too short to be intentional dictation (probably a stray key combo).
            session?.Cancel();
            _state = State.Standby;
            StateChanged?.Invoke("En espera");
            _overlay.HideOverlay();
            return;
        }

        var audioSeconds = pcm.Length / (double)MicrophoneRecorder.BytesPerSecond;
        _state = State.Processing;
        StateChanged?.Invoke("Procesando…");
        _overlay.ShowProcessing();
        var target = _target!;

        _ = Task.Run(async () =>
        {
            string text;
            string language;
            try
            {
                (text, language) = await session.FinishAsync();
            }
            catch (Exception ex)
            {
                Finish(new DictationRecord(false, "", "?", "—", 0, audioSeconds,
                    $"Error de transcripción: {ex.Message}"));
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                Finish(new DictationRecord(false, "", language, "—", 0, audioSeconds,
                    "No se detectó voz. Intenta de nuevo."));
                return;
            }

            var words = StatsService.CountWords(text);
            var result = _engine.Insert(text, target);

            if (result.Success)
            {
                Finish(new DictationRecord(true, text, language, MethodLabel(result.MethodUsed),
                    words, audioSeconds,
                    $"Insertado ✔ [{language}] · {words} palabras · {MethodLabel(result.MethodUsed)}"));
            }
            else if (result.FailureReason == InsertionFailureReason.TargetWindowChanged)
            {
                CopyToClipboard(text);
                Finish(new DictationRecord(false, text, language, "portapapeles", words, audioSeconds,
                    "La ventana cambió — texto copiado, pégalo con Ctrl+V."));
            }
            else if (result.FailureReason == InsertionFailureReason.SensitiveFieldBlocked)
            {
                Finish(new DictationRecord(false, "", language, "—", 0, audioSeconds,
                    "Campo protegido (contraseña): no se inserta."));
            }
            else
            {
                // Engine already left the text in the clipboard (manual fallback tier).
                Finish(new DictationRecord(false, text, language, "manual", words, audioSeconds,
                    "No se pudo insertar — texto en el portapapeles, pégalo con Ctrl+V."));
            }
        });
    }

    private void Finish(DictationRecord record)
    {
        _overlay.Dispatcher.BeginInvoke(() =>
        {
            _overlay.ShowResult(record.Message, record.Success, record.Success ? 2200 : 4000);
            _state = State.Standby;
            StateChanged?.Invoke("En espera");
            Completed?.Invoke(record);
        });
    }

    private StreamingTranscriptionSession? DetachSession()
    {
        var session = _session;
        if (session is not null)
        {
            _recorder.DataAvailable -= session.AddChunk;
            _session = null;
        }
        return session;
    }

    private void CopyToClipboard(string text)
    {
        _overlay.Dispatcher.Invoke(() =>
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, true);
            }
            catch
            {
                // Clipboard occupied by another app; nothing more we can do here.
            }
        });
    }

    public void Dispose()
    {
        _hook.PttPressed -= OnPttPressed;
        _hook.PttReleased -= OnPttReleased;
        _hook.ToggleTapped -= OnToggleTapped;
        _hook.PttInterrupted -= CancelListening;
        _hook.EscapePressed -= CancelListening;
        _recorder.LevelChanged -= OnLevelChanged;
        _engine.Dispose();
    }

    private static string MethodLabel(InsertionMethod method) => method switch
    {
        InsertionMethod.UiaValuePattern => "UIA",
        InsertionMethod.UnicodeKeyboardInput => "teclado",
        InsertionMethod.ClipboardPaste => "portapapeles",
        InsertionMethod.ManualFallback => "manual",
        _ => method.ToString(),
    };
}

using LLamaVoz.Audio;
using LLamaVoz.DesktopApp.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;
using WinForms = System.Windows.Forms;

namespace LLamaVoz.DesktopApp;

/// <summary>
/// Tray-resident dictation app (FR-040). Hold Ctrl+Alt (push-to-talk) or tap Win+Alt
/// (toggle) anywhere, speak, and the text is transcribed locally and inserted into the
/// app you were using. Esc cancels. The panel window shows word counters.
/// </summary>
public partial class App : System.Windows.Application
{
    private KeyboardHookService? _hook;
    private MicrophoneRecorder? _recorder;
    private TranscriptionService? _transcription;
    private DictationController? _controller;
    private OverlayWindow? _overlay;
    private MainWindow? _panel;
    private StatsService? _stats;
    private SettingsService? _settings;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _hotkeyHintItem;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a stale second instance keeps its own keyboard hook and
        // settings snapshot, so GUI changes appear to have no effect (and both fight
        // over the microphone). Launching a second copy just brings the running one's
        // panel to the front and exits silently — no dialog, no task manager needed.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\LLamaVoz.App", out var isNew);
        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LLamaVoz.App.ShowPanel");
        if (!isNew)
        {
            _activationSignal.Set();
            Shutdown(0);
            return;
        }
        var activationListener = new Thread(() =>
        {
            while (_activationSignal.WaitOne())
            {
                Dispatcher.BeginInvoke(ShowPanel);
            }
        })
        { IsBackground = true, Name = "LLamaVoz.Activation" };
        activationListener.Start();

        try
        {
            _transcription = new TranscriptionService();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"{ex.Message}\n\nDescarga un modelo, por ejemplo:\n" +
                "Invoke-WebRequest https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin " +
                "-OutFile models/ggml-base.bin",
                "LLamaVoz — modelo no encontrado", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _overlay = new OverlayWindow();
        _recorder = new MicrophoneRecorder();
        _stats = new StatsService();
        _settings = new SettingsService();

        // Warm the models in the background so the first dictation doesn't pay the load
        // cost. Only the tiers the current quality mode uses are loaded (RAM).
        var mode = _settings.QualityMode;
        _ = Task.Run(() =>
        {
            if (mode != "accurate")
            {
                _transcription.Preload(ModelTier.Draft);
            }
            if (mode != "fast")
            {
                _transcription.Preload(ModelTier.Verify);
            }
        });

        try
        {
            _hook = new KeyboardHookService();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "LLamaVoz — error de atajo global",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ApplyHotkeys();
        _settings.HotkeysChanged += () => Dispatcher.BeginInvoke(ApplyHotkeys);

        _controller = new DictationController(_hook, _recorder, _transcription, _settings, _overlay);
        _overlay.AttachSettings(_settings);
        _overlay.MicClicked += () => _controller.ToggleDictation();

        _panel = new MainWindow(_stats, _settings);
        _panel.AttachController(_controller);
        _panel.ExitRequested += ExitApp;
        _panel.Show();

        SetupTrayIcon();

        _overlay.ShowResult(
            $"LLamaVoz listo · {SettingsService.HotkeyLabel(_settings.PttHotkey)} (mantener) o " +
            $"{SettingsService.HotkeyLabel(_settings.ToggleHotkey)} (toque) · " +
            $"modelo {System.IO.Path.GetFileName(_transcription.ModelPath)}",
            success: true, autoHideMs: 3500);
    }

    private void ApplyHotkeys()
    {
        if (_hook is null || _settings is null)
        {
            return;
        }
        _hook.PttCombo = KeyboardHookService.ParseHotkey(_settings.PttHotkey);
        _hook.ToggleCombo = KeyboardHookService.ParseHotkey(_settings.ToggleHotkey);
        if (_hotkeyHintItem is not null)
        {
            _hotkeyHintItem.Text = HotkeyHint();
        }
    }

    private string HotkeyHint() =>
        $"{SettingsService.HotkeyLabel(_settings!.PttHotkey)}: mantener · " +
        $"{SettingsService.HotkeyLabel(_settings.ToggleHotkey)}: toque · Esc: cancelar";

    private void SetupTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Abrir panel", null, (_, _) => ShowPanel());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        // Quick language switch — takes effect on the very next dictation.
        var langMenu = new WinForms.ToolStripMenuItem("Idioma de transcripción");
        foreach (var (code, label) in SettingsService.Languages)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Tag = code };
            item.Click += (_, _) => _settings!.TranscriptionLanguage = code;
            langMenu.DropDownItems.Add(item);
        }
        langMenu.DropDownOpening += (_, _) =>
        {
            foreach (WinForms.ToolStripMenuItem item in langMenu.DropDownItems)
            {
                item.Checked = (string)item.Tag! == _settings!.TranscriptionLanguage;
            }
        };
        menu.Items.Add(langMenu);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        _hotkeyHintItem = new WinForms.ToolStripMenuItem(HotkeyHint()) { Enabled = false };
        menu.Items.Add(_hotkeyHintItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "LLamaVoz — dictado local",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowPanel();
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "llamavoz.ico");
        return System.IO.File.Exists(path)
            ? new System.Drawing.Icon(path)
            : System.Drawing.SystemIcons.Application;
    }

    private void ShowPanel()
    {
        if (_panel is null)
        {
            return;
        }
        _panel.Show();
        _panel.WindowState = System.Windows.WindowState.Normal;
        _panel.Activate();
    }

    private void ExitApp()
    {
        _controller?.Dispose();
        _hook?.Dispose();
        _recorder?.Dispose();
        _transcription?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}

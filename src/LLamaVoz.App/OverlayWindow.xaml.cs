using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LLamaVoz.DesktopApp.Services;
using Media = System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace LLamaVoz.DesktopApp;

/// <summary>
/// Persistent floating mini-pill (Wispr-Flow style): idle it shows a compact language
/// chip + mic button just above the taskbar; while dictating it expands with a live
/// equalizer and status text, then collapses back to idle. Clicking the chip cycles
/// the transcription language; clicking the mic starts/stops a toggle dictation.
/// Never takes focus (WS_EX_NOACTIVATE) so the target application keeps the caret —
/// clicks still arrive, they just don't activate the window.
/// The equalizer is driven by a single ~33 ms timer that eases each bar toward its
/// target height (mic level while listening, an idle sine wave while processing).
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int BarCount = 13;
    private const double BarMinHeight = 4;
    private const double BarMaxSwing = 24;

    private enum AnimMode { Off, Listening, Processing }

    private readonly DispatcherTimer _autoHide = new();
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _barFactors = new double[BarCount];
    private readonly double[] _targets = new double[BarCount];
    private readonly double[] _heights = new double[BarCount];
    private AnimMode _mode = AnimMode.Off;
    private double _phase;
    private SettingsService? _settings;

    /// <summary>Raised when the mic button is clicked (App routes it to the controller's toggle).</summary>
    public event Action? MicClicked;

    public OverlayWindow()
    {
        InitializeComponent();
        BuildBars();
        _autoHide.Tick += (_, _) => { _autoHide.Stop(); ShowIdle(); };
        _collapse.Tick += (_, _) =>
        {
            _collapse.Stop();
            if (_mode == AnimMode.Off && !_autoHide.IsEnabled && !IsMouseOver)
            {
                SetDormant();
            }
        };
        _anim.Tick += (_, _) => AnimateFrame();
        SizeChanged += (_, _) => PositionBottomCenter();
        // Dormant ↔ expanded on hover: barely-there dot until the mouse touches it.
        MouseEnter += (_, _) =>
        {
            _collapse.Stop();
            if (_mode == AnimMode.Off && !_autoHide.IsEnabled)
            {
                SetExpandedIdle();
            }
        };
        MouseLeave += (_, _) => _collapse.Start();
        ShowIdle();
    }

    /// <summary>Resting look: a tiny dark sliver no bigger than a cursor — content hidden.</summary>
    private void SetDormant()
    {
        PillContent.Visibility = Visibility.Collapsed;
        Pill.Height = 12;
        Pill.MinWidth = 40;
        Pill.CornerRadius = new CornerRadius(6);
        Pill.Background = new Media.SolidColorBrush(Media.Color.FromArgb(0xE6, 0x10, 0x10, 0x18));
        Pill.BorderBrush = new Media.SolidColorBrush(Media.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>Interactive look: language chip + mic (the size it grows to on hover/use).</summary>
    private void SetExpandedIdle()
    {
        PillContent.Visibility = Visibility.Visible;
        RestorePillChrome();
        RecDot.Visibility = Visibility.Collapsed;
        BarsPanel.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        MicButton.Visibility = Visibility.Visible;
        if (_settings is not null)
        {
            LangText.Text = SettingsService.ChipLabel(_settings.TranscriptionLanguage);
        }
    }

    private void RestorePillChrome()
    {
        Pill.Height = 46;
        Pill.CornerRadius = new CornerRadius(21);
        Pill.BorderBrush = new Media.SolidColorBrush(Media.Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        var gradient = new Media.LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1),
        };
        gradient.GradientStops.Add(new Media.GradientStop(Media.Color.FromArgb(0xF2, 0x26, 0x26, 0x38), 0));
        gradient.GradientStops.Add(new Media.GradientStop(Media.Color.FromArgb(0xF2, 0x15, 0x15, 0x1F), 1));
        Pill.Background = gradient;
    }

    /// <summary>Wires the pill to settings: chip shows/changes the language, mic tooltip
    /// reflects the configured hotkeys.</summary>
    public void AttachSettings(SettingsService settings)
    {
        _settings = settings;
        settings.LanguageChanged += code => Dispatcher.BeginInvoke(() =>
        {
            if (_mode == AnimMode.Off)
            {
                LangText.Text = SettingsService.ChipLabel(code);
            }
        });
        settings.HotkeysChanged += () => Dispatcher.BeginInvoke(RefreshMicTooltip);
        LangText.Text = SettingsService.ChipLabel(settings.TranscriptionLanguage);
        RefreshMicTooltip();
    }

    private void RefreshMicTooltip()
    {
        if (_settings is not null)
        {
            MicButton.ToolTip = $"Dictar — {SettingsService.HotkeyLabel(_settings.PttHotkey)} (mantener) · " +
                                $"{SettingsService.HotkeyLabel(_settings.ToggleHotkey)} (toque) · o clic aquí";
        }
    }

    private void OnLangChipClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }
        var index = Array.FindIndex(SettingsService.Languages, l => l.Code == _settings.TranscriptionLanguage);
        var next = SettingsService.Languages[(index + 1) % SettingsService.Languages.Length].Code;
        _settings.TranscriptionLanguage = next;
        LangText.Text = SettingsService.ChipLabel(next);
    }

    private void OnMicClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        MicClicked?.Invoke();
    }

    private void BuildBars()
    {
        // Symmetric envelope (tall center, short edges) with a cyan→purple→pink sweep.
        var from = Media.Color.FromRgb(0x8A, 0xE0, 0xFF);
        var mid = Media.Color.FromRgb(0xB1, 0x8A, 0xFF);
        var to = Media.Color.FromRgb(0xFF, 0x8A, 0xD4);
        for (var i = 0; i < BarCount; i++)
        {
            _barFactors[i] = 0.35 + 0.65 * Math.Sin(Math.PI * (i + 0.5) / BarCount);
            var t = i / (double)(BarCount - 1);
            var color = t < 0.5 ? Lerp(from, mid, t * 2) : Lerp(mid, to, (t - 0.5) * 2);
            var bar = new Rectangle
            {
                Width = 3.5,
                Height = BarMinHeight,
                RadiusX = 1.75,
                RadiusY = 1.75,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                Fill = new Media.SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _bars[i] = bar;
            _heights[i] = BarMinHeight;
            _targets[i] = BarMinHeight;
            BarsPanel.Children.Add(bar);
        }
    }

    private static Media.Color Lerp(Media.Color a, Media.Color b, double t) => Media.Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        Left = area.Left + (area.Width - width) / 2;
        Top = area.Bottom - Height + 2; // hugging the taskbar (window has a 6px inner margin)
    }

    /// <summary>Resting state: a tiny dormant sliver (or the compact chip+mic while hovered).</summary>
    public void ShowIdle()
    {
        _autoHide.Stop();
        StopAnimation();
        MicIcon.Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(0x8A, 0xE0, 0xFF));
        if (IsMouseOver)
        {
            SetExpandedIdle();
        }
        else
        {
            SetDormant();
        }
        EnsureVisible();
    }

    public void ShowListening(string languageLabel, string hint)
    {
        _autoHide.Stop();
        _collapse.Stop();
        PillContent.Visibility = Visibility.Visible;
        RestorePillChrome();
        LangText.Text = languageLabel;
        StatusText.Text = hint;
        StatusText.Foreground = Media.Brushes.White;
        StatusText.Visibility = Visibility.Visible;
        BarsPanel.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Visible;
        MicIcon.Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(0xFF, 0x5A, 0x6E));
        StartAnimation(AnimMode.Listening);
        EnsureVisible();
    }

    public void ShowProcessing()
    {
        _autoHide.Stop();
        _collapse.Stop();
        PillContent.Visibility = Visibility.Visible;
        RestorePillChrome();
        StatusText.Text = "Procesando…";
        StatusText.Visibility = Visibility.Visible;
        BarsPanel.Visibility = Visibility.Visible;
        RecDot.Visibility = Visibility.Collapsed;
        MicIcon.Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(0x8A, 0xE0, 0xFF));
        StartAnimation(AnimMode.Processing);
        EnsureVisible();
    }

    public void ShowResult(string message, bool success, int autoHideMs = 2200)
    {
        _autoHide.Stop();
        _collapse.Stop();
        StopAnimation();
        PillContent.Visibility = Visibility.Visible;
        RestorePillChrome();
        StatusText.Text = message;
        StatusText.Foreground = success
            ? new Media.SolidColorBrush(Media.Color.FromRgb(0xC9, 0xF2, 0xD9))
            : new Media.SolidColorBrush(Media.Color.FromRgb(0xFF, 0xB3, 0xA1));
        StatusText.Visibility = Visibility.Visible;
        BarsPanel.Visibility = Visibility.Collapsed;
        RecDot.Visibility = Visibility.Collapsed;
        MicIcon.Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(0x8A, 0xE0, 0xFF));
        EnsureVisible();
        _autoHide.Interval = TimeSpan.FromMilliseconds(autoHideMs);
        _autoHide.Start();
    }

    /// <summary>Kept for the controller's cancel path: collapse back to the idle pill.</summary>
    public void HideOverlay() => ShowIdle();

    public void UpdateLevel(float level)
    {
        if (_mode != AnimMode.Listening)
        {
            return;
        }
        for (var i = 0; i < BarCount; i++)
        {
            _targets[i] = BarMinHeight + level * BarMaxSwing * _barFactors[i];
        }
    }

    private void EnsureVisible()
    {
        if (!IsVisible)
        {
            Show();
        }
        PositionBottomCenter();
    }

    private void StartAnimation(AnimMode mode)
    {
        _mode = mode;
        _phase = 0;
        for (var i = 0; i < BarCount; i++)
        {
            _targets[i] = BarMinHeight;
        }
        _anim.Start();
    }

    private void StopAnimation()
    {
        _mode = AnimMode.Off;
        _anim.Stop();
    }

    private void AnimateFrame()
    {
        _phase += 0.22;

        if (_mode == AnimMode.Processing)
        {
            // Idle traveling wave so the pill still feels alive while whisper runs.
            for (var i = 0; i < BarCount; i++)
            {
                _targets[i] = BarMinHeight + 6 * (0.5 + 0.5 * Math.Sin(_phase + i * 0.55));
            }
        }
        else if (_mode == AnimMode.Listening)
        {
            // Pulse the REC dot (~1 Hz).
            RecDot.Opacity = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(_phase * 0.9));
        }

        for (var i = 0; i < BarCount; i++)
        {
            _heights[i] += (_targets[i] - _heights[i]) * 0.35; // ease toward target
            _bars[i].Height = Math.Max(BarMinHeight, _heights[i]);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

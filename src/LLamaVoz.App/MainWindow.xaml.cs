using System.ComponentModel;
using System.Windows;
using LLamaVoz.DesktopApp.Services;

namespace LLamaVoz.DesktopApp;

/// <summary>
/// Dashboard: live state, word counters (today/total) and the last transcript.
/// The transcript is shown from memory only — it is never written to disk.
/// Closing the window hides it; the app lives in the tray (FR-040).
/// </summary>
public partial class MainWindow : Window
{
    private readonly StatsService _stats;
    private readonly SettingsService _settings;
    public event Action? ExitRequested;

    public MainWindow(StatsService stats, SettingsService settings)
    {
        InitializeComponent();
        _stats = stats;
        _settings = settings;
        LoadLogo();
        RefreshStats();
        InitLanguageSelector();
        InitHotkeySelectors();
    }

    private void InitHotkeySelectors()
    {
        foreach (var (code, label) in SettingsService.Hotkeys)
        {
            PttSelector.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = code });
            ToggleSelector.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = code });
        }
        SyncHotkeyUi();
    }

    /// <summary>Re-selects both combos from settings and regenerates the hotkey note.</summary>
    private void SyncHotkeyUi()
    {
        PttSelector.SelectedIndex = Array.FindIndex(SettingsService.Hotkeys, h => h.Code == _settings.PttHotkey);
        ToggleSelector.SelectedIndex = Array.FindIndex(SettingsService.Hotkeys, h => h.Code == _settings.ToggleHotkey);
        var ptt = SettingsService.HotkeyLabel(_settings.PttHotkey);
        var toggle = SettingsService.HotkeyLabel(_settings.ToggleHotkey);
        HotkeysText.Text =
            $"{ptt} (mantener) — habla y suelta para insertar\n" +
            $"{toggle} (un toque) — empieza a escuchar; otro toque inserta\n" +
            "Esc — cancelar y descartar el audio";
        if (LastText.Text.StartsWith("Aún no has dictado"))
        {
            LastText.Text = $"Aún no has dictado nada. Mantén {ptt} y habla, o toca {toggle}.";
        }
    }

    private void OnPttHotkeyChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PttSelector.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string code })
        {
            _settings.PttHotkey = code; // rejected silently if it collides with the toggle
            SyncHotkeyUi();             // snaps the combo back on rejection
        }
    }

    private void OnToggleHotkeyChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ToggleSelector.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string code })
        {
            _settings.ToggleHotkey = code;
            SyncHotkeyUi();
        }
    }

    private void InitLanguageSelector()
    {
        foreach (var (code, label) in SettingsService.Languages)
        {
            LanguageSelector.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = code });
        }
        var current = _settings.TranscriptionLanguage;
        LanguageSelector.SelectedIndex = Math.Max(0,
            Array.FindIndex(SettingsService.Languages, l => l.Code == current));

        // Keep the panel in sync when the language is changed elsewhere (tray menu).
        _settings.LanguageChanged += code => Dispatcher.BeginInvoke(() =>
            LanguageSelector.SelectedIndex = Math.Max(0,
                Array.FindIndex(SettingsService.Languages, l => l.Code == code)));

        foreach (var (code, label) in SettingsService.QualityModes)
        {
            QualitySelector.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = label, Tag = code });
        }
        var mode = _settings.QualityMode;
        QualitySelector.SelectedIndex = Math.Max(0,
            Array.FindIndex(SettingsService.QualityModes, m => m.Code == mode));
    }

    private void OnQualityChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (QualitySelector.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string code })
        {
            _settings.QualityMode = code;
        }
    }

    private void OnLanguageChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LanguageSelector.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string code })
        {
            _settings.TranscriptionLanguage = code;
        }
    }

    private void LoadLogo()
    {
        var png = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
        var ico = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "llamavoz.ico");
        try
        {
            if (System.IO.File.Exists(png))
            {
                var image = new System.Windows.Media.Imaging.BitmapImage(new Uri(png));
                LogoBrush.ImageSource = image;
            }
            if (System.IO.File.Exists(ico))
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(ico));
            }
        }
        catch
        {
            // Cosmetic only — never fail startup over a logo.
        }
    }

    public void AttachController(DictationController controller)
    {
        controller.StateChanged += state =>
            Dispatcher.BeginInvoke(() => StateChip.Text = state);

        controller.Completed += record =>
            Dispatcher.BeginInvoke(() =>
            {
                if (record.Words > 0)
                {
                    _stats.RecordDictation(record.Words);
                }
                if (!string.IsNullOrEmpty(record.Text))
                {
                    LastText.Text = record.Text;
                    LastMeta.Text =
                        $"{record.Words} palabras · idioma {record.Language} · {record.AudioSeconds:F1} s de audio · " +
                        $"{(record.Success ? $"insertado vía {record.MethodLabel}" : record.Message)}";
                }
                else
                {
                    LastMeta.Text = record.Message;
                }
                RefreshStats();
            });
    }

    private void RefreshStats()
    {
        TodayWords.Text = _stats.TodayWords.ToString("N0");
        TodayDictations.Text = _stats.TodayDictations.ToString("N0");
        TotalWords.Text = _stats.TotalWords.ToString("N0");
        AvgWords.Text = _stats.AverageWordsPerDictation.ToString("F0");
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    protected override void OnClosing(CancelEventArgs e)
    {
        // The app lives in the tray; closing the panel just hides it.
        e.Cancel = true;
        Hide();
    }
}

using System.IO;
using System.Text.Json;

namespace LLamaVoz.DesktopApp.Services;

/// <summary>
/// Word/dictation counters for the dashboard. Persists ONLY numbers to
/// %APPDATA%\LLamaVoz\stats.json — never dictated text or audio (NFR-10 spirit:
/// content and metrics stay strictly separated).
/// </summary>
public sealed class StatsService
{
    private sealed class StatsData
    {
        public long TotalWords { get; set; }
        public long TotalDictations { get; set; }
        public string TodayDate { get; set; } = "";
        public long TodayWords { get; set; }
        public long TodayDictations { get; set; }
    }

    private readonly string _path;
    private readonly object _sync = new();
    private readonly StatsData _data;
    private readonly Func<DateTime> _clock;

    public StatsService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LLamaVoz"))
    {
    }

    /// <summary>Test seam: arbitrary storage directory and injectable clock (day rollover).</summary>
    internal StatsService(string directory, Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "stats.json");
        _data = Load();
        RollDayIfNeeded();
    }

    public long TotalWords { get { lock (_sync) return _data.TotalWords; } }
    public long TotalDictations { get { lock (_sync) return _data.TotalDictations; } }
    public long TodayWords { get { lock (_sync) { RollDayIfNeeded(); return _data.TodayWords; } } }
    public long TodayDictations { get { lock (_sync) { RollDayIfNeeded(); return _data.TodayDictations; } } }

    public double AverageWordsPerDictation
    {
        get { lock (_sync) return _data.TotalDictations == 0 ? 0 : (double)_data.TotalWords / _data.TotalDictations; }
    }

    public void RecordDictation(int words)
    {
        lock (_sync)
        {
            RollDayIfNeeded();
            _data.TotalWords += words;
            _data.TotalDictations++;
            _data.TodayWords += words;
            _data.TodayDictations++;
            Save();
        }
    }

    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private void RollDayIfNeeded()
    {
        var today = _clock().ToString("yyyy-MM-dd");
        if (_data.TodayDate != today)
        {
            _data.TodayDate = today;
            _data.TodayWords = 0;
            _data.TodayDictations = 0;
        }
    }

    private StatsData Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<StatsData>(File.ReadAllText(_path)) ?? new StatsData();
            }
        }
        catch
        {
            // Corrupt stats file: start fresh rather than failing the app.
        }
        return new StatsData();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_data));
        }
        catch
        {
            // Stats are best-effort; never break dictation over them.
        }
    }
}

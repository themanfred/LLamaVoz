using System.IO;
using LLamaVoz.DesktopApp.Services;

namespace LLamaVoz.Core.Tests;

public class StatsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "llamavoz-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("hola", 1)]
    [InlineData("hola mundo", 2)]
    [InlineData("hola   mundo\r\n\tqué tal", 4)]
    public void CountWords_HandlesWhitespaceVariants(string text, int expected)
    {
        Assert.Equal(expected, StatsService.CountWords(text));
    }

    [Fact]
    public void RecordDictation_Accumulates_AndPersists()
    {
        var s = new StatsService(_dir);
        s.RecordDictation(10);
        s.RecordDictation(5);
        Assert.Equal(15, s.TotalWords);
        Assert.Equal(2, s.TotalDictations);
        Assert.Equal(7.5, s.AverageWordsPerDictation);

        var reloaded = new StatsService(_dir);
        Assert.Equal(15, reloaded.TotalWords);
        Assert.Equal(2, reloaded.TotalDictations);
    }

    [Fact]
    public void DayRollover_ResetsTodayButKeepsTotals()
    {
        var now = new DateTime(2026, 7, 5, 23, 0, 0);
        var s = new StatsService(_dir, () => now);
        s.RecordDictation(20);
        Assert.Equal(20, s.TodayWords);

        now = now.AddDays(1); // midnight passed
        Assert.Equal(0, s.TodayWords);
        Assert.Equal(0, s.TodayDictations);
        Assert.Equal(20, s.TotalWords);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

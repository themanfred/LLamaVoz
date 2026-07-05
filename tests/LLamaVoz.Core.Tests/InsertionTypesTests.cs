using LLamaVoz.Core.Insertion;

namespace LLamaVoz.Core.Tests;

public class InsertionTypesTests
{
    [Fact]
    public void Blocked_ProducesFailedResultWithReason()
    {
        var result = InsertionResult.Blocked(
            InsertionFailureReason.TargetWindowChanged, "window changed");

        Assert.False(result.Success);
        Assert.Equal(InsertionMethod.None, result.MethodUsed);
        Assert.Equal(InsertionFailureReason.TargetWindowChanged, result.FailureReason);
        Assert.Equal(0, result.CharactersInserted);
    }

    [Fact]
    public void DefaultOptions_EnableAllTiersAndTargetVerification()
    {
        var options = new InsertionOptions();

        Assert.True(options.AllowUia);
        Assert.True(options.AllowKeyboardInput);
        Assert.True(options.AllowClipboard);
        Assert.False(options.SkipTargetWindowVerification);
    }
}

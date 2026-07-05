namespace LLamaVoz.Insertion;

/// <summary>
/// Clipboard APIs require an STA thread. Runs the action inline when the caller is already
/// STA; otherwise hops to a short-lived STA thread.
/// </summary>
internal static class StaRunner
{
    public static void Run(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new InvalidOperationException("STA operation failed.", captured);
        }
    }
}

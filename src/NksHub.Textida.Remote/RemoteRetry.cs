namespace NksHub.Textida.Remote;

/// <param name="Attempt">The repeat that is about to start, counted from 1.</param>
/// <param name="Max">How many repeats are allowed.</param>
/// <param name="Delay">How long to wait before it.</param>
/// <param name="Error">What went wrong in the attempt before.</param>
public readonly record struct RetryInfo(int Attempt, int Max, TimeSpan Delay, RemoteException Error);

/// <summary>
/// Repeats a transfer after a lost connection, waiting twice as long before each repeat. Only a lost connection is repeated; a refusal, a missing file or a
/// version conflict is final. A repeat is a new call, so it checks the server version again and continues a partial file. The callbacks run on the context of the caller
/// (the user interface thread, when it has one), so they may show messages and ask questions.
/// </summary>
public static class RemoteRetry
{
    /// <summary>The wait before repeat number <paramref name="attempt"/> (from 1): the base wait doubled for every repeat before it.</summary>
    public static TimeSpan DelayBefore(int attempt, int baseDelayMilliseconds) =>
        TimeSpan.FromMilliseconds((double)baseDelayMilliseconds * Math.Pow(2, Math.Max(attempt, 1) - 1));

    /// <param name="attempt">One try of the whole transfer.</param>
    /// <param name="retries">How many repeats follow the first try.</param>
    /// <param name="beforeRepeat">Reports the failure, waits for <see cref="RetryInfo.Delay"/> and makes the connection again; false stops the repeating.</param>
    /// <returns>The result of the first try that worked, or null when <paramref name="beforeRepeat"/> stopped it.</returns>
    public static async Task<T?> RunAsync<T>(Func<Task<T>> attempt, int retries, int baseDelayMilliseconds, Func<RetryInfo, Task<bool>> beforeRepeat) where T : class
    {
        for (int n = 1; ; n++)
        {
            RetryInfo info;
            try
            {
                return await attempt();
            }
            catch (RemoteException e) when (e.Error == RemoteError.ConnectionLost && n <= retries)
            {
                info = new RetryInfo(n, retries, DelayBefore(n, baseDelayMilliseconds), e);
            }
            if (!await beforeRepeat(info)) return null;
        }
    }
}

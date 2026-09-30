namespace MinorShift.Emuera.Web.Runtime;

public sealed record BrowserInfiniteLoopEvent(
    bool DiagnosticContinuation,
    int MaxWarnings,
    long MaxElapsedMilliseconds,
    int WarningCount,
    long ElapsedMilliseconds,
    string ErbPosition,
    bool Continued);

public sealed class BrowserInfiniteLoopContinuation(Func<long>? clock = null)
{
    public const int WarningLimit = 12;
    public const long ElapsedLimitMilliseconds = 60_000;
    readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    readonly long started = (clock ?? (() => Environment.TickCount64))();
    readonly List<BrowserInfiniteLoopEvent> events = [];

    public IReadOnlyList<BrowserInfiniteLoopEvent> Events => events;
    public int WarningCount => events.Count;
    public long ElapsedMilliseconds => Math.Max(0, now() - started);

    public bool ShouldAbort(string title, string position)
    {
        long elapsed = ElapsedMilliseconds;
        bool continued = events.Count < WarningLimit && elapsed <= ElapsedLimitMilliseconds;
        events.Add(new(true, WarningLimit, ElapsedLimitMilliseconds, events.Count + 1, elapsed, position, continued));
        return !continued;
    }
}

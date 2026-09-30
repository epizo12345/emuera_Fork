using MinorShift.Emuera.GameProc;

namespace MinorShift.Emuera.Web.Runtime;

public sealed record BrowserLongRunningNotice(
    long SessionId,
    long PromptId,
    string Title,
    string File,
    int Line,
    long ElapsedMilliseconds,
    int ExecutedLines);

public sealed record BrowserLongRunningDecision(bool ContinueRequested, string Reason);

public sealed record BrowserLongRunningEvent(
    BrowserLongRunningNotice Notice,
    string Phase,
    bool? ContinueRequested,
    string Reason,
    DateTimeOffset TimestampUtc);

public sealed class BrowserLongRunningPrompt(long sessionId, Func<BrowserLongRunningNotice, BrowserLongRunningDecision> confirm)
{
    readonly List<BrowserLongRunningEvent> events = [];
    long nextPromptId;

    public IReadOnlyList<BrowserLongRunningEvent> Events => events;

    internal bool ShouldAbort(WebInfiniteLoopPromptInfo info)
    {
        var notice = new BrowserLongRunningNotice(
            sessionId,
            Interlocked.Increment(ref nextPromptId),
            info.Title,
            info.File,
            info.Line,
            info.ElapsedMilliseconds,
            info.ExecutedLines);
        events.Add(new(notice, "show", null, "", DateTimeOffset.UtcNow));
        try
        {
            BrowserLongRunningDecision decision = confirm(notice)
                ?? throw new InvalidOperationException("LONG_RUNNING_CONFIRM_RETURNED_NULL");
            string reason = string.IsNullOrWhiteSpace(decision.Reason)
                ? decision.ContinueRequested ? "confirm-approved" : "cancel-or-suppressed"
                : decision.Reason;
            events.Add(new(notice, "answer", decision.ContinueRequested, reason, DateTimeOffset.UtcNow));
            return !decision.ContinueRequested;
        }
        catch (Exception exception)
        {
            events.Add(new(notice, "error", null, $"{exception.GetType().Name}: {exception.Message}", DateTimeOffset.UtcNow));
            throw new InvalidOperationException("LONG_RUNNING_CONFIRM_FAILED", exception);
        }
    }
}

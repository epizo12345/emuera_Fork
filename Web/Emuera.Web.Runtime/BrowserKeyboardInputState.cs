namespace MinorShift.Emuera.Web.Runtime;

public sealed class BrowserKeyboardInputState
{
    sealed record Press(long Session, long Generation, BrowserInputPrompt Prompt);
    sealed record Repeat(string Code, string Key, Press Press, long SourceRequest);
    readonly Dictionary<string, Press> down = new();
    long generation;
    Press? owner;
    Repeat? pending;
    public bool Busy { get; private set; }
    static bool Eligible(BrowserInputPrompt? p) => p is { TimeLimit: <= 0, TimedInputName: null or "" }
        && ((p.OneInput && p.Kind is BrowserInputKind.String or BrowserInputKind.Integer)
            || p.Kind is BrowserInputKind.Enter or BrowserInputKind.AnyKey or BrowserInputKind.MouseKey);
    static bool Same(Press press, BrowserInputPrompt? p, long session) => Eligible(p)
        && press.Session == session && press.Prompt.Kind == p!.Kind && press.Prompt.OneInput == p.OneInput;
    public void KeyDown(string code, bool repeat, BrowserInputPrompt? prompt, long session)
    {
        if (repeat) return; // OS風の独自タイマーを重ねず、ブラウザが発生させた物理repeatだけを長押しの根拠にする。
        down.Remove(code);
        if (Eligible(prompt)) down[code] = new(session, ++generation, prompt!);
    }
    public void KeyUp(string code)
    {
        down.Remove(code);
        if (pending?.Code == code) pending = null;
    }
    public void Clear() { down.Clear(); pending = null; }
    public bool IsHeld(string code, BrowserInputPrompt? prompt, long session) =>
        down.TryGetValue(code, out var held) && Same(held, prompt, session);
    public void Revalidate(BrowserInputPrompt? prompt, long session)
    {
        foreach (string code in down.Where(pair => !Same(pair.Value, prompt, session)).Select(pair => pair.Key).ToArray()) down.Remove(code);
        if (pending is { } item && !Same(item.Press, prompt, session)) pending = null;
    }
    public bool TryBegin(string code, string key, bool repeat, BrowserInputPrompt? prompt, long session, out string reason)
    {
        if (Busy)
        {
            // WAIT・primitive・時間制限付き要求・別の物理タップは遅延実行すると二重入力になるため保留しない。
            if (repeat && owner is { Prompt.OneInput: true } && owner.Session == session
                && down.TryGetValue(code, out var press) && Same(press, owner.Prompt, session))
            {
                pending = new(code, key, press, owner.Prompt.RequestId);
                reason = "coalesced-repeat";
            }
            else reason = "input-gate";
            return false;
        }
        if (repeat && !IsHeld(code, prompt, session))
        { KeyUp(code); reason = "repeat-filter"; return false; }
        if (prompt is null) { reason = "no-pending-input"; return false; }
        Busy = true;
        owner = new(session, generation, prompt);
        reason = "accepted";
        return true;
    }
    public (string Code, string Key)? Complete(BrowserInputPrompt? prompt, long session)
    {
        Busy = false;
        owner = null;
        Revalidate(prompt, session);
        var item = pending;
        pending = null;
        return item is not null && down.TryGetValue(item.Code, out var held)
            && held.Generation == item.Press.Generation && Same(held, prompt, session)
            && prompt!.OneInput && prompt.RequestId > item.SourceRequest
            ? (item.Code, item.Key) : null;
    }
}

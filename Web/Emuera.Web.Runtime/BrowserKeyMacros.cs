namespace MinorShift.Emuera.Web.Runtime;

public sealed class BrowserKeyMacros
{
    public int Group { get; private set; }
    public string[] Slots { get; private set; } = Enumerable.Repeat(string.Empty, 120).ToArray();
    public string[] GroupNames { get; private set; } = BrowserMacroFile.DefaultGroupNames();
    public string Get(int slot) => Slots[Group * 12 + CheckedSlot(slot)];
    public bool Register(int slot, string text)
    {
        int index = Group * 12 + CheckedSlot(slot);
        if (text.Length == 0) return false; // Native Shift+F does not erase a registered slot.
        Slots[index] = text;
        return true;
    }
    public void SelectGroup(int group)
    {
        if (group is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(group));
        Group = group;
    }
    public void Restore(int group, string[] slots, string[]? groupNames = null)
    {
        if (group is < 0 or > 9 || slots.Length != 120 || slots.Any(s => s is null)
            || groupNames is not null && (groupNames.Length != 10 || groupNames.Any(s => s is null)))
            throw new ArgumentException("Invalid key macro settings");
        Slots = (string[])slots.Clone();
        GroupNames = (string[])(groupNames ?? BrowserMacroFile.DefaultGroupNames()).Clone();
        Group = group;
    }
    static int CheckedSlot(int slot) => slot is >= 0 and < 12 ? slot : throw new ArgumentOutOfRangeException(nameof(slot));
    public static bool SubmitOnRecall(string before, string after, BrowserInputPrompt? prompt) =>
        after.Length > before.Length && (prompt?.OneInput == true || prompt?.Kind == BrowserInputKind.AnyKey);
    public static string RecallInput(string before, string after, BrowserInputPrompt prompt) =>
        prompt.Kind == BrowserInputKind.AnyKey ? string.Empty : prompt.OneInput ? after[before.Length..] : after;
}

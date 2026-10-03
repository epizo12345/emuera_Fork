namespace MinorShift.Emuera.Web.Runtime;

// Ordinary PRINT output is wrapped before publishing. HTML owns its own layout.
internal static class BrowserPhysicalLines
{
    public static IReadOnlyList<IReadOnlyList<BrowserDisplayPart>> Wrap(
        IReadOnlyList<BrowserDisplayPart> parts, double limit,
        Func<BrowserDisplayPart, string, double> measure, bool buttonWrap, bool legacyNonButtonWrap)
    {
        var rows = new List<IReadOnlyList<BrowserDisplayPart>>();
        var row = new List<BrowserDisplayPart>();
        double used = 0;
        void EndRow() { rows.Add(row.ToArray()); row.Clear(); used = 0; }
        foreach (var original in parts)
        {
            var part = original;
            while (true)
            {
                bool text = part.Children is null && part.Kind is BrowserDisplayPartKind.Text or BrowserDisplayPartKind.Button or BrowserDisplayPartKind.NonButton;
                double width = text ? measure(part, part.Text) : part.Width;
                if (part.LockedX is int x) used = x;
                if (used + width <= limit || !text || part.Text.Length == 0)
                { row.Add(part); used += width; break; }
                // Native keeps selectable buttons together unless the button itself is too wide.
                bool split = !buttonWrap || row.Count == 0 || (part.Input is null && !legacyNonButtonWrap);
                if (!split) { EndRow(); continue; }
                int lo = 0, hi = part.Text.Length;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo + 1) / 2;
                    if (measure(part, part.Text[..mid]) <= limit - used) lo = mid; else hi = mid - 1;
                }
                if (lo > 0 && lo < part.Text.Length && char.IsHighSurrogate(part.Text[lo - 1]) && char.IsLowSurrogate(part.Text[lo])) lo--;
                if (lo == 0)
                {
                    if (row.Count > 0) { EndRow(); continue; }
                    // An indivisible glyph wider than the viewport is retained, as in Native.
                    lo = char.IsHighSurrogate(part.Text[0]) && part.Text.Length > 1 && char.IsLowSurrogate(part.Text[1]) ? 2 : 1;
                }
                string head = part.Text[..lo];
                row.Add(part with { Text = head, Width = part.Width > 0 ? (int)measure(part, head) : 0 });
                EndRow();
                part = part with { Text = part.Text[lo..], Width = 0, LockedX = null };
                if (part.Text.Length == 0) break;
            }
        }
        if (row.Count > 0 || rows.Count == 0) rows.Add(row.ToArray());
        return rows;
    }
}

namespace MinorShift.Emuera.Web.Runtime;

// Ordinary PRINT and normal-flow HTML are wrapped before publishing.
// Positioned groups and islands keep their separate layout contract.
internal static class BrowserPhysicalLines
{
    public static IReadOnlyList<IReadOnlyList<BrowserDisplayPart>> Wrap(
        IReadOnlyList<BrowserDisplayPart> parts, double limit,
        Func<BrowserDisplayPart, string, double> measure, bool buttonWrap, bool legacyNonButtonWrap, bool noWrap = false)
    {
        var rows = new List<IReadOnlyList<BrowserDisplayPart>>();
        var row = new List<BrowserDisplayPart>();
        double used = 0;
        void EndRow() { rows.Add(row.ToArray()); row.Clear(); used = 0; }
        foreach (var original in parts)
        {
            if (original.Kind == BrowserDisplayPartKind.Break) { EndRow(); continue; }
            var part = original;
            while (true)
            {
                bool flow = IsFlow(part);
                double width = Width(part, measure);
                if (part.LockedX is int x) used = x;
                if (noWrap || used + width <= limit || !flow)
                { row.Add(part); used += width; break; }
                // Native keeps selectable buttons together unless the button itself is too wide.
                bool split = !buttonWrap || row.Count == 0 || (part.Input is null && !legacyNonButtonWrap);
                if (!split) { EndRow(); part = part with { LockedX = null }; continue; }
                var (head, tail) = Split(part, limit - used, measure, row.Count == 0);
                if (head is null) { EndRow(); part = part with { LockedX = null }; continue; }
                row.Add(head);
                if (tail is null) { used += width; break; }
                EndRow();
                part = tail with { LockedX = null };
            }
        }
        if (row.Count > 0 || rows.Count == 0) rows.Add(row.ToArray());
        return rows;
    }

    static bool IsFlow(BrowserDisplayPart part) => part.Layout?.ExplicitPosition != true
        && (part.Layout is null || part.Layout.Mode == BrowserDisplayMode.Relative)
        && part.Kind is BrowserDisplayPartKind.Text or BrowserDisplayPartKind.Button or BrowserDisplayPartKind.NonButton
        && (part.Children is null || part.Children.All(child => child.Children is null
            && child.Layout?.ExplicitPosition != true
            && child.Kind is BrowserDisplayPartKind.Text or BrowserDisplayPartKind.Image or BrowserDisplayPartKind.Shape));

    static double Width(BrowserDisplayPart part, Func<BrowserDisplayPart, string, double> measure) =>
        part.Children is not null && IsFlow(part) ? part.Children.Sum(child => Width(child, measure))
        : part.Kind is BrowserDisplayPartKind.Text or BrowserDisplayPartKind.Button or BrowserDisplayPartKind.NonButton
            && part.Children is null ? measure(part, part.Text) : part.Width;

    static (BrowserDisplayPart? Head, BrowserDisplayPart? Tail) Split(BrowserDisplayPart part, double available,
        Func<BrowserDisplayPart, string, double> measure, bool first)
    {
        if (part.Children is not null)
        {
            var head = new List<BrowserDisplayPart>();
            var tail = new List<BrowserDisplayPart>();
            foreach (var child in part.Children)
            {
                if (tail.Count > 0) { tail.Add(child); continue; }
                double width = Width(child, measure);
                if (width <= available) { head.Add(child); available -= width; continue; }
                if (child.Kind == BrowserDisplayPartKind.Text)
                {
                    var pieces = Split(child, available, measure, first && head.Count == 0);
                    if (pieces.Head is not null) head.Add(pieces.Head);
                    if (pieces.Tail is not null) tail.Add(pieces.Tail);
                    if (pieces.Tail is null && pieces.Head is not null) available -= width;
                }
                else if (first && head.Count == 0) { head.Add(child); available -= width; }
                else tail.Add(child);
            }
            BrowserDisplayPart Piece(List<BrowserDisplayPart> children) => part with {
                Children = children.ToArray(), Text = string.Concat(children.Where(c => c.Kind == BrowserDisplayPartKind.Text).Select(c => c.Text)) };
            return (head.Count > 0 ? Piece(head) : null, tail.Count > 0 ? Piece(tail) : null);
        }
        if (part.Text.Length == 0) return (part, null);
        int lo = 0, hi = part.Text.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (measure(part, part.Text[..mid]) <= available) lo = mid; else hi = mid - 1;
        }
        if (lo > 0 && lo < part.Text.Length && char.IsHighSurrogate(part.Text[lo - 1]) && char.IsLowSurrogate(part.Text[lo])) lo--;
        if (lo == 0)
        {
            if (!first) return (null, part);
            // Retain a glyph that cannot fit even on an empty line.
            lo = char.IsHighSurrogate(part.Text[0]) && part.Text.Length > 1 && char.IsLowSurrogate(part.Text[1]) ? 2 : 1;
        }
        string text = part.Text[..lo];
        return (part with { Text = text, Width = part.Width > 0 ? (int)measure(part, text) : 0 },
            lo == part.Text.Length ? null : part with { Text = part.Text[lo..], Width = 0, LockedX = null });
    }
}

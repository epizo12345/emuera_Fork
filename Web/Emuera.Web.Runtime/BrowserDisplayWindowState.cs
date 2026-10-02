namespace MinorShift.Emuera.Web.Runtime;

public readonly record struct BrowserDisplayScrollState(
    double ScrollTop,
    double ClientHeight,
    bool NearBottom,
    long AnchorLineId,
    double AnchorOffsetPx);

public readonly record struct BrowserDisplayLineMeasurement(long LineId, double Height);

public sealed record BrowserDisplayWindowSlice(
    int Start,
    int End,
    double TopSpacerPx,
    double BottomSpacerPx,
    bool FollowTail,
    int NativePaintStart = 0,
    int NativePaintEnd = 0)
{
    public bool IsNativeLinePainted(int lineIndex) => lineIndex >= NativePaintStart && lineIndex < NativePaintEnd;
}

public sealed class BrowserDisplayWindowState
{
    public const double InitialEstimatedLineHeight = 18.0;
    public const int MaximumMeasurementCache = 4096;

    readonly Dictionary<long, double> measurements = [];
    BrowserDisplayScrollState scrollState = new(0, 0, true, 0, 0);
    bool followTail = true;
    IReadOnlyList<BrowserDisplayLine>? cachedLines;
    long? cachedStructureGeneration;
    int cachedCount;
    int cachedDisplayLineHeight;
    BrowserDisplayWindowSlice? cachedSlice;

    public int MeasurementCount => measurements.Count;
    public bool FollowTail => followTail;
    public BrowserDisplayScrollState ScrollState => scrollState;
    public bool HasMeasurement(long lineId) => measurements.ContainsKey(lineId);
    public bool RemoveMeasurement(long lineId)
    {
        bool removed = measurements.Remove(lineId);
        if (removed) cachedLines = null;
        return removed;
    }

    public void Reset()
    {
        measurements.Clear();
        scrollState = new(0, 0, true, 0, 0);
        followTail = true;
        cachedLines = null;
    }

    public bool UpdateScroll(BrowserDisplayScrollState state)
    {
        if (!double.IsFinite(state.ScrollTop) || !double.IsFinite(state.ClientHeight) || !double.IsFinite(state.AnchorOffsetPx))
            return false;
        bool changed = scrollState != state || followTail != state.NearBottom;
        scrollState = state;
        followTail = state.NearBottom;
        if (changed) cachedLines = null;
        return changed;
    }

    public bool UpdateScroll(BrowserDisplayScrollState state, long eventDisplayStructureGeneration, long currentDisplayStructureGeneration) =>
        eventDisplayStructureGeneration == currentDisplayStructureGeneration && UpdateScroll(state);

    public bool ResumeFollowTail()
    {
        bool changed = !followTail || !scrollState.NearBottom;
        followTail = true;
        scrollState = scrollState with { NearBottom = true };
        if (changed) cachedLines = null;
        return changed;
    }

    public bool ApplyMeasurements(IReadOnlyList<BrowserDisplayLineMeasurement> values)
    {
        bool changed = false;
        foreach (BrowserDisplayLineMeasurement value in values)
        {
            if (value.LineId <= 0 || !double.IsFinite(value.Height) || value.Height < 1)
                continue;
            if (!measurements.TryGetValue(value.LineId, out double current) || Math.Abs(current - value.Height) > 0.01)
            {
                measurements[value.LineId] = value.Height;
                changed = true;
            }
        }
        while (measurements.Count > MaximumMeasurementCache)
            measurements.Remove(measurements.Keys.Min());
        if (changed) cachedLines = null;
        return changed;
    }

    public BrowserDisplayWindowSlice Calculate(IReadOnlyList<BrowserDisplayLine> lines, int? mainLineCount = null, long? structureGeneration = null, int? displayLineHeight = null)
    {
        int count = mainLineCount ?? lines.Count;
        if (count < 0 || count > lines.Count) throw new ArgumentOutOfRangeException(nameof(mainLineCount));
        int nativeLineHeight = Math.Max(1, displayLineHeight ?? (int)InitialEstimatedLineHeight);
        // Visual image changes do not alter row geometry. Measurements and scroll
        // explicitly invalidate this cache; callers without a revision always recalculate.
        if (structureGeneration.HasValue && ReferenceEquals(cachedLines, lines)
            && cachedStructureGeneration == structureGeneration && cachedCount == count
            && cachedDisplayLineHeight == nativeLineHeight)
            return cachedSlice!;
        if (count == 0)
        {
            measurements.Clear();
            cachedLines = null;
            return new(0, 0, 0, 0, followTail, 0, 0);
        }

        Prune(lines, count);
        double estimate = EstimateHeight();
        double clientHeight = Math.Max(1, scrollState.ClientHeight);
        int visibleCount = Math.Max(1, (int)Math.Ceiling(clientHeight / estimate));
        int overscan = Math.Max(32, visibleCount * 2);
        int start;
        int end;
        int target = count - 1;

        if (followTail)
        {
            end = count;
            start = Math.Max(0, end - visibleCount - overscan);
        }
        else
        {
            target = Math.Clamp((int)Math.Floor(Math.Max(0, scrollState.ScrollTop) / estimate), 0, count - 1);
            for (int attempt = 0; attempt < 3; attempt++)
                target = Locate(lines, count, Math.Max(0, scrollState.ScrollTop), estimate, target);
            start = Math.Max(0, target - overscan);
            end = Math.Min(count, target + visibleCount + overscan);
        }

        (int nativePaintStart, int nativePaintEnd) = NativePaintRange(count, nativeLineHeight, target, scrollState.ClientHeight, followTail);
        double top = Offset(lines, start, estimate);
        double total = Offset(lines, count, estimate);
        double bottom = Math.Max(0, total - Offset(lines, end, estimate));
        var slice = new BrowserDisplayWindowSlice(start, end, Math.Max(0, top), bottom, followTail, nativePaintStart, nativePaintEnd);
        if (structureGeneration.HasValue)
        {
            cachedLines = lines;
            cachedStructureGeneration = structureGeneration;
            cachedCount = count;
            cachedDisplayLineHeight = nativeLineHeight;
            cachedSlice = slice;
        }
        return slice;
    }

    static (int Start, int End) NativePaintRange(int lineCount, int lineHeight, int visibleTop, double clientHeight, bool followTail)
    {
        if (lineCount == 0) return (0, 0);
        if (clientHeight <= 0 || clientHeight >= (double)lineCount * lineHeight) return (0, lineCount);

        int pixelHeight = (int)clientHeight;
        int visibleRows = Math.Max(1, (int)Math.Ceiling(clientHeight / lineHeight));
        int bottomLineNo = followTail ? lineCount - 1 : Math.Min(lineCount - 1, visibleTop + visibleRows - 1);
        // Mirrors EmueraConsole.OnPaint: the first painted owner row may sit
        // above the viewport, but its AbsoluteLeftBottom children still draw.
        int topLineNo = bottomLineNo - ((pixelHeight - lineHeight) / lineHeight + 1);
        return (Math.Max(0, topLineNo), bottomLineNo + 1);
    }

    void Prune(IReadOnlyList<BrowserDisplayLine> lines, int count)
    {
        long first = lines[0].LineId;
        long last = lines[count - 1].LineId;
        foreach (long lineId in measurements.Keys.Where(lineId => lineId < first || lineId > last || !IsRetained(lineId)).ToArray())
            measurements.Remove(lineId);

        bool IsRetained(long lineId)
        {
            int low = 0, high = count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                long candidate = lines[middle].LineId;
                if (candidate == lineId) return true;
                if (candidate < lineId) low = middle + 1;
                else high = middle - 1;
            }
            return false;
        }
    }

    double EstimateHeight()
    {
        if (measurements.Count == 0)
            return InitialEstimatedLineHeight;
        double[] heights = measurements.Values.Order().ToArray();
        double median = heights[heights.Length / 2];
        return Math.Clamp(median, 12, 256);
    }

    int Locate(IReadOnlyList<BrowserDisplayLine> lines, int count, double scrollTop, double estimate, int guess)
    {
        double offset = Offset(lines, guess, estimate);
        if (offset > scrollTop)
        {
            while (guess > 0 && offset > scrollTop)
                offset -= Height(lines[--guess], estimate);
            return guess;
        }
        while (guess < count - 1 && offset + Height(lines[guess], estimate) <= scrollTop)
            offset += Height(lines[guess++], estimate);
        return guess;
    }

    double Offset(IReadOnlyList<BrowserDisplayLine> lines, int end, double estimate)
    {
        double offset = 0;
        for (int index = 0; index < end; index++)
            offset += Height(lines[index], estimate);
        return offset;
    }

    double Height(BrowserDisplayLine line, double estimate) => measurements.GetValueOrDefault(line.LineId, estimate);
}

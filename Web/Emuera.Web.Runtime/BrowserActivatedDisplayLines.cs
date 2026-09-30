using System.Collections;

namespace MinorShift.Emuera.Web.Runtime;

// Retained history is indexed; only accessed input-bearing rows are projected.
internal sealed class BrowserActivatedDisplayLines(
    IReadOnlyList<BrowserDisplayLine> source,
    Func<BrowserDisplayLine, BrowserDisplayLine> project) : IReadOnlyList<BrowserDisplayLine>
{
    readonly Dictionary<long, (BrowserDisplayLine Source, BrowserDisplayLine Result)> cache = [];
    public int Count => source.Count;
    public BrowserDisplayLine this[int index]
    {
        get
        {
            BrowserDisplayLine line = source[index];
            if (cache.TryGetValue(line.LineId, out var value) && ReferenceEquals(value.Source, line)) return value.Result;
            BrowserDisplayLine result = project(line);
            if (!ReferenceEquals(line, result))
            {
                // Diagnostic full-history mode can access more rows than the window.
                if (cache.Count >= BrowserDisplayWindowState.MaximumMeasurementCache) cache.Clear();
                cache[line.LineId] = (line, result);
            }
            return result;
        }
    }
    public IEnumerator<BrowserDisplayLine> GetEnumerator()
    {
        for (int index = 0; index < Count; index++) yield return this[index];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

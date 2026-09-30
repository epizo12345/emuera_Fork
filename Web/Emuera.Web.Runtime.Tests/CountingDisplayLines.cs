using MinorShift.Emuera.Web.Runtime;

sealed class CountingDisplayLines(IReadOnlyList<BrowserDisplayLine> source) : IReadOnlyList<BrowserDisplayLine>
{
    public long Reads { get; set; }
    public int Count => source.Count;
    public BrowserDisplayLine this[int index] { get { Reads++; return source[index]; } }
    public IEnumerator<BrowserDisplayLine> GetEnumerator()
    {
        for (int index = 0; index < Count; index++) yield return this[index];
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

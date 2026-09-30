using System.Collections;

namespace MinorShift.Emuera.Web.Runtime;

internal sealed class BrowserDisplayLineBuffer : IReadOnlyList<BrowserDisplayLine>
{
    BrowserDisplayLine[] items = [];
    int head;

    public int Count { get; private set; }

    public BrowserDisplayLine this[int index]
    {
        get => items[PhysicalIndex(index)];
        set => items[PhysicalIndex(index)] = value;
    }

    public void Add(BrowserDisplayLine item)
    {
        EnsureCapacity();
        items[(head + Count) % items.Length] = item;
        Count++;
    }

    public void RemoveFirst()
    {
        if (Count == 0) return;
        items[head] = null!;
        head = (head + 1) % items.Length;
        Count--;
        if (Count == 0) head = 0;
    }

    public void RemoveLast(int count)
    {
        count = Math.Min(Math.Max(0, count), Count);
        while (count-- > 0)
        {
            Count--;
            items[(head + Count) % items.Length] = null!;
        }
        if (Count == 0) head = 0;
    }

    public void Clear()
    {
        if (Count != 0) Array.Clear(items);
        head = 0;
        Count = 0;
    }

    public IEnumerator<BrowserDisplayLine> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return items[(head + index) % items.Length];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    int PhysicalIndex(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return (head + index) % items.Length;
    }

    void EnsureCapacity()
    {
        if (Count < items.Length) return;
        var expanded = new BrowserDisplayLine[items.Length == 0 ? 4 : items.Length * 2];
        for (int index = 0; index < Count; index++)
            expanded[index] = items[(head + index) % items.Length];
        items = expanded;
        head = 0;
    }
}

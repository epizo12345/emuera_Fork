using System.Collections.Generic;
using System.Reflection;

namespace MinorShift.Emuera.Runtime.Script.Parser;

/// <summary>
/// 字句解析結果の保存場所。Listとその現在位置を結びつけるためのもの。
/// 基本的に全てpublicで
/// </summary>
internal sealed class WordCollection
{
    public WordCollection()
    {
        compactCollection = [];
    }

    private WordCollection(bool lexerPending) { }

    internal static WordCollection CreateLexerResult() => new(true);

    // ERB解析の通常経路では短い字句列をListで保持し、LinkedList/Nodeのallocationを避ける。
    // Insert/Pointer/Collectionなど旧APIが必要になった時だけLinkedListへ昇格し、compactPointerと語順を維持する。
    List<Word> compactCollection;
    LinkedList<Word> linkedCollection;
    LinkedListNode<Word> linkedPointer;
    int compactPointer;
    int index = 0;
    private static Word nullToken = new NullWord();
    public int Count => linkedCollection?.Count ?? compactCollection?.Count ?? 0;

#if PERFORMANCE_METRICS
    // census専用。Collection/Pointerを呼ばず、compactからlinkedへの昇格も行わない。
    internal WordCollectionBenchmarkStorage GetBenchmarkStorage()
    {
        int compactCount = compactCollection == null ? 0 : 1;
        int linkedCount = linkedCollection == null ? 0 : 1;
        int compactWords = 0;
        int compactCapacity = 0;
        int linkedNodes = 0;
        long identifierWords = 0;
        long symbolWords = 0;
        long literalIntegerWords = 0;
        long operatorWords = 0;
        long otherWords = 0;

        if (compactCollection is List<Word> compact)
        {
            compactWords = compact.Count;
            compactCapacity = compact.Capacity;
            foreach (Word word in compact)
                CountWordType(word, ref identifierWords, ref symbolWords, ref literalIntegerWords, ref operatorWords, ref otherWords);
        }
        if (linkedCollection is LinkedList<Word> linked)
        {
            linkedNodes = linked.Count;
            foreach (Word word in linked)
                CountWordType(word, ref identifierWords, ref symbolWords, ref literalIntegerWords, ref operatorWords, ref otherWords);
        }

        return new WordCollectionBenchmarkStorage(
            compactCount,
            linkedCount,
            compactWords,
            compactCapacity,
            linkedNodes,
            identifierWords,
            symbolWords,
            literalIntegerWords,
            operatorWords,
            otherWords);
    }

    private static void CountWordType(
        Word word,
        ref long identifierWords,
        ref long symbolWords,
        ref long literalIntegerWords,
        ref long operatorWords,
        ref long otherWords)
    {
        if (word is IdentifierWord)
            identifierWords++;
        else if (word is SymbolWord)
            symbolWords++;
        else if (word is LiteralIntegerWord)
            literalIntegerWords++;
        else if (word is OperatorWord)
            operatorWords++;
        else
            otherWords++;
    }
#endif

    public LinkedList<Word> Collection
    {
        get
        {
            EnsureLinked();
            return linkedCollection;
        }
    }

    public LinkedListNode<Word> Pointer
    {
        get
        {
            EnsureLinked();
            return linkedPointer;
        }
        set
        {
            EnsureLinked();
            linkedPointer = value;
        }
    }

    public void PointerReset()
    {
        index = 0;
        if (linkedCollection != null)
            linkedPointer = linkedCollection.First;
        else
            compactPointer = 0;
    }

    public void Add(Word token)
    {
        if (linkedCollection != null)
        {
            linkedCollection.AddLast(token);
            linkedPointer ??= linkedCollection.First;
            return;
        }

        List<Word> compact = compactCollection;
        if (compact == null)
        {
            compact = new List<Word>(8);
            compactCollection = compact;
        }
        bool wasEol = compactPointer >= compact.Count;
        compact.Add(token);
        if (wasEol)
            compactPointer = 0;
    }
    public void Add(WordCollection wc)
    {
        if (ReferenceEquals(this, wc))
        {
            EnsureLinked();
            foreach (var word in linkedCollection)
                linkedCollection.AddLast(word);
            linkedPointer ??= linkedCollection.First;
            return;
        }

        if (linkedCollection != null)
        {
            if (wc.linkedCollection != null)
            {
                foreach (var word in wc.linkedCollection)
                    linkedCollection.AddLast(word);
            }
            else if (wc.compactCollection != null)
            {
                foreach (var word in wc.compactCollection)
                    linkedCollection.AddLast(word);
            }
            linkedPointer ??= linkedCollection.First;
            return;
        }

        int addCount = wc.Count;
        if (addCount == 0 && compactCollection == null)
            return;
        List<Word> compact = compactCollection;
        if (compact == null)
        {
            compact = new List<Word>(8);
            compactCollection = compact;
        }
        bool wasEol = compactPointer >= compact.Count;
        if (wc.linkedCollection != null)
            compact.AddRange(wc.linkedCollection);
        else if (wc.compactCollection != null)
            compact.AddRange(wc.compactCollection);
        if (wasEol)
            compactPointer = 0;
    }

    public void Clear()
    {
        EnsureLinked();
        linkedCollection.Clear();
    }

    public void ShiftNext()
    {
        if (linkedCollection != null)
        {
            if (linkedPointer == null)
            {
                if (index == 0)
                    linkedPointer = linkedCollection.First;
            }
            else
                linkedPointer = linkedPointer.Next;
        }
        else
        {
            if (compactPointer >= (compactCollection?.Count ?? 0))
            {
                if (index == 0)
                    compactPointer = 0;
            }
            else
                compactPointer++;
        }

        index++;
    }
    public Word Current
    {
        get
        {
            if (linkedCollection != null)
                return linkedPointer?.Value ?? nullToken;
            if (compactPointer >= (compactCollection?.Count ?? 0))
                return nullToken;
            return compactCollection[compactPointer];
        }
    }
    public bool EOL
    {
        get
        {
            return linkedCollection != null ? linkedPointer == null : compactPointer >= (compactCollection?.Count ?? 0);
        }
    }

    public void Insert(Word w)
    {
        EnsureLinked();
        if (linkedPointer == null)
        {
            if (linkedCollection.Count == 0)
            {
                linkedCollection.AddFirst(w);
                linkedPointer = linkedCollection.First;
            }
            else
            {
                linkedCollection.AddLast(w);
                linkedPointer = linkedCollection.Last;
            }
        }
        else
        {
            linkedCollection.AddAfter(linkedPointer, w);
        }
    }
    public void InsertRange(WordCollection wc)
    {
        EnsureLinked();
        var pointer = linkedPointer?.Previous;
        LinkedListNode<Word> lastPointer = null;
        if (wc.linkedCollection != null)
        {
            foreach (var word in wc.linkedCollection)
            {
                AddAfterPointer(word, ref pointer, ref lastPointer);
            }
        }
        else if (wc.compactCollection != null)
        {
            foreach (var word in wc.compactCollection)
            {
                AddAfterPointer(word, ref pointer, ref lastPointer);
            }
        }

        linkedPointer = lastPointer;
    }
    public void Remove()
    {
        EnsureLinked();
        var next = linkedPointer.Next;
        linkedCollection.Remove(linkedPointer);
        linkedPointer = next;
    }

    public void SetIsMacro()
    {
        if (linkedCollection != null)
        {
            foreach (Word word in linkedCollection)
                word.SetIsMacro();
        }
        else if (compactCollection != null)
        {
            foreach (Word word in compactCollection)
                word.SetIsMacro();
        }
    }

    public WordCollection Clone()
    {
        var ret = new WordCollection();
        ret.Add(this);
        return ret;
    }

    void EnsureLinked()
    {
        if (linkedCollection != null)
            return;

        if (compactCollection == null)
        {
            linkedCollection = new LinkedList<Word>();
            compactCollection = null;
            return;
        }

        linkedCollection = new LinkedList<Word>(compactCollection);
        if (compactPointer < compactCollection.Count)
        {
            linkedPointer = linkedCollection.First;
            for (int i = 0; i < compactPointer; i++)
                linkedPointer = linkedPointer.Next;
        }
        compactCollection = null;
    }

    void AddAfterPointer(Word word, ref LinkedListNode<Word> pointer, ref LinkedListNode<Word> lastPointer)
    {
        if (pointer == null)
        {
            if (index == 0)
                pointer = linkedCollection.AddFirst(word);
            else
                pointer = linkedCollection.AddLast(word);
            linkedPointer = pointer;
        }
        else
            pointer = linkedCollection.AddAfter(pointer, word);

        lastPointer ??= pointer;
    }
    // public WordCollection Clone(int start, int count)
    // {
    //     WordCollection ret = new();
    //     if (start > Collection.Count)
    //         return ret;
    //     int end = start + count;
    //     if (end > Collection.Count)
    //         end = Collection.Count;
    //     for (int i = start; i < end; i++)
    //     {
    //         ret.Collection.Add(Collection[i]);
    //     }
    //     return ret;
    // }

}

#if PERFORMANCE_METRICS
internal readonly record struct WordCollectionBenchmarkStorage(
    int CompactCollectionCount,
    int LinkedCollectionCount,
    int CompactTotalCount,
    int CompactTotalCapacity,
    int LinkedNodeCount,
    long IdentifierWordCount,
    long SymbolWordCount,
    long LiteralIntegerWordCount,
    long OperatorWordCount,
    long OtherWordCount);
#endif



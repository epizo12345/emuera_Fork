using System;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MinorShift.Emuera;

static partial class Rename
{
    [GeneratedRegex(@"\[\[.*?\]\]")]
    private static partial Regex regexRenameIdentifer();

    public static void RenameString(ref string @string)
    {
        var span = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(@string.AsSpan()), @string.Length);
        var matches = regexRenameIdentifer().EnumerateMatches(span);
        foreach (var match in matches)
        {
            var destSpan = span[match.Index..(match.Index + match.Length)];
            if (ParserMediator.RenameDic.TryGetValue(XxHash3.HashToUInt64(MemoryMarshal.AsBytes(destSpan)), out var targetStr))
            {
                targetStr.AsSpan().CopyTo(destSpan);
                span[(match.Index + targetStr.Length)..(match.Index + match.Length)].Fill(' ');
            }
        }
    }
}
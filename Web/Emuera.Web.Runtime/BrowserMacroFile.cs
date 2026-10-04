using System.Text;
using System.Text.RegularExpressions;

namespace MinorShift.Emuera.Web.Runtime;

public sealed record BrowserMacroFileData(string[] GroupNames, string[] Slots, string EncodingName, int DefinedSlots);

// Native KeyMacro file syntax. Import is deliberately stricter than Native's ignored invalid lines.
public static class BrowserMacroFile
{
    public const int MaxBytes = 1024 * 1024;
    public static string[] DefaultGroupNames() => Enumerable.Range(0, 10).Select(g => $"マクログループ{g}に設定").ToArray();
    static readonly Regex GroupLine = new(@"^グループ([0-9]):(.{3,})$", RegexOptions.CultureInvariant);
    static readonly Regex SlotLine = new(@"^(?:G([0-9]):)?マクロキーF([1-9]|1[0-2]):(.*)$", RegexOptions.CultureInvariant);
    static Encoding Cp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
    public static BrowserMacroFileData Decode(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new FormatException("macro.txtは1MiB以下にしてください。");
        Encoding encoding = Cp932(); int skip = 0;
        // File.ReadAllLines in Native also recognizes Unicode BOMs; no-BOM input is CP932.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = new UTF8Encoding(true, true); skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
        string text;
        try { text = encoding.GetString(bytes, skip, bytes.Length - skip); }
        catch (DecoderFallbackException) { throw new FormatException("文字コードが不正です。BOMなしはShift-JIS（CP932）として読み込みます。"); }
        if (text.Contains('\0')) throw new FormatException("NUL文字を含むmacro.txtは読み込めません。");
        var names = DefaultGroupNames(); var slots = Enumerable.Repeat("", 120).ToArray();
        var groupsSeen = new bool[10]; var slotsSeen = new bool[120]; int number = 0, count = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is string line)
        {
            number++;
            if (line.Length == 0 || line[0] == ';') continue;
            var group = GroupLine.Match(line);
            if (group.Success)
            {
                int g = group.Groups[1].Value[0] - '0';
                if (groupsSeen[g]) throw new FormatException($"{number}行目: グループ{g}が重複しています。");
                groupsSeen[g] = true; names[g] = group.Groups[2].Value; continue;
            }
            var slot = SlotLine.Match(line);
            if (!slot.Success) throw new FormatException($"{number}行目: 実機macro.txt形式でない行、または範囲外の番号です。");
            int index = (slot.Groups[1].Success ? slot.Groups[1].Value[0] - '0' : 0) * 12 + int.Parse(slot.Groups[2].Value) - 1;
            if (slotsSeen[index]) throw new FormatException($"{number}行目: G{index / 12}/F{index % 12 + 1}が重複しています。");
            slotsSeen[index] = true; count++; slots[index] = slot.Groups[3].Value;
        }
        return new(names, slots, skip == 0 ? "Shift-JIS（CP932）" : encoding.WebName + " BOM", count);
    }
    public static byte[] Encode(string[] groupNames, string[] slots)
    {
        if (groupNames.Length != 10 || slots.Length != 120) throw new FormatException("10グループ・120スロットが必要です。");
        static bool Invalid(string? s) => s is null || s.IndexOfAny(['\r', '\n', '\0']) >= 0;
        if (groupNames.Any(s => Invalid(s) || s.Length < 3) || slots.Any(Invalid))
            throw new FormatException("実機形式では改行・NUL文字や3文字未満のグループ名を保存できません。");
        var text = new StringBuilder();
        for (int g = 0; g < 10; g++) text.Append($"グループ{g}:{groupNames[g]}\r\n");
        for (int i = 0; i < 120; i++)
        {
            if (i >= 12) text.Append($"G{i / 12}:");
            text.Append($"マクロキーF{i % 12 + 1}:{slots[i]}\r\n");
        }
        var encoding = Cp932(); string value = text.ToString(); byte[] bytes;
        try { bytes = encoding.GetBytes(value); }
        catch (EncoderFallbackException) { throw new FormatException("CP932で表現できない文字があります。文字を変更するまで書き出せません（?への置換はしません）。"); }
        if (encoding.GetString(bytes) != value) throw new FormatException("CP932で往復すると別の文字になる内容があります。内容を変換せず書き出しを中止しました。");
        if (bytes.Length > MaxBytes) throw new FormatException("書き出し内容が1MiBを超えています。");
        return bytes;
    }
}

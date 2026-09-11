using System.Collections.Immutable;
using System.Text;

namespace MinorShift.Emuera.Next.Compiler;

public sealed record StartupSourceValidation(bool Valid, string? Detail,
    ImmutableArray<string> StaticCalls, int LocalLabelCount, int StructuralBlockCount);

// Non-retaining startup syntax pass. It deliberately accepts known Legacy commands that Compact cannot execute yet.
public static class StartupSourceValidator
{
    private static readonly Dictionary<string, string> Openers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IF"] = "ENDIF", ["SELECTCASE"] = "ENDSELECT", ["FOR"] = "NEXT", ["REPEAT"] = "REND",
        ["WHILE"] = "WEND", ["DO"] = "LOOP", ["PRINTDATA"] = "ENDDATA", ["DATALIST"] = "ENDLIST",
    };

    public static StartupSourceValidation Validate(FunctionSource source, CompilerCompatibilityOptions options)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(source.Bytes); }
        catch (DecoderFallbackException ex) { return Fail(ex.Message); }
        var labels = new HashSet<string>(options.NameComparer);
        var gotos = new List<string>();
        var calls = ImmutableArray.CreateBuilder<string>();
        var blocks = new Stack<(string Close, int Line)>();
        var blockCount = 0;
        var lineNumber = source.Function.Span.StartLine;
        var first = true;
        var continuation = false;
        var continuationText = string.Empty;
        foreach (var physical in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = physical.TrimStart(' ', '\t', '　');
            if (first) { first = false; lineNumber++; continue; }
            if (continuation)
            {
                if (line.TrimEnd(' ', '\t', '　') == "}") { continuation = false; line = continuationText.TrimStart(' ', '\t', '　'); }
                else { continuationText += " " + line; lineNumber++; continue; }
            }
            if (line.TrimEnd(' ', '\t', '　') == "{") { continuation = true; continuationText = string.Empty; lineNumber++; continue; }
            if (line.Length == 0 || line[0] is ';' or '#') { lineNumber++; continue; }
            if (line[0] == '$')
            {
                var name = line[1..].Split(';')[0].TrimEnd(' ', '\t', '　');
                if (name.Length == 0 || !labels.Add(name)) return Fail($"invalid/duplicate local label at line {lineNumber}");
                lineNumber++; continue;
            }
            var scan = LegacyIdentifierScanner.ReadFirstIdentifier(line, options);
            var length = scan.StopPosition - scan.StartPosition;
            if (length == 0) { lineNumber++; continue; }
            var token = line[..length];
            var separator = length < line.Length ? line[length] : '\0';
            if (LegacyOpcodeMap.IsLegacyLineHeadIdentifier(token, options) && !LegacyIdentifierScanner.IsCommandSeparator(separator, options))
                return Fail($"invalid command separator after {token} at line {lineNumber}");
            var operandStart = LegacyIdentifierScanner.SkipCommandSeparators(line, length, options);
            var operand = operandStart < line.Length ? line[operandStart..].Split(';')[0].Trim() : string.Empty;
            if (token.StartsWith("PRINTDATA", options.NameComparison) || token.Equals("STRDATA", options.NameComparison))
            { blocks.Push(("ENDDATA", lineNumber)); blockCount++; }
            else if (Openers.TryGetValue(token, out var close)) { blocks.Push((close, lineNumber)); blockCount++; }
            else if (Openers.Values.Contains(token, options.NameComparer))
            {
                if (!blocks.TryPop(out var opened) || !token.Equals(opened.Close, options.NameComparison))
                    return Fail($"unmatched {token} at line {lineNumber}");
            }
            if (token.Equals("GOTO", options.NameComparison) || token.Equals("TRYGOTO", options.NameComparison))
            {
                var target = FixedName(operand);
                if (target is not null) gotos.Add(target);
            }
            if (token is "CALL" or "JUMP" or "TRYCALL" or "TRYCCALL" ||
                options.IgnoreCase && token.Equals("CALL", StringComparison.OrdinalIgnoreCase) ||
                options.IgnoreCase && token.Equals("JUMP", StringComparison.OrdinalIgnoreCase) ||
                options.IgnoreCase && token.Equals("TRYCALL", StringComparison.OrdinalIgnoreCase) ||
                options.IgnoreCase && token.Equals("TRYCCALL", StringComparison.OrdinalIgnoreCase))
            {
                var target = FixedName(operand);
                if (target is not null && !token.StartsWith("TRY", options.NameComparison)) calls.Add(target);
            }
            lineNumber++;
        }
        if (continuation) return Fail("unclosed continuation block");
        if (blocks.TryPeek(out var pending)) return Fail($"unclosed block expecting {pending.Close} from line {pending.Line}");
        var missing = gotos.FirstOrDefault(target => !labels.Contains(target));
        return missing is null ? new(true, null, calls.ToImmutable(), labels.Count, blockCount) : Fail("missing local label: " + missing);

        static string? FixedName(string operand)
        {
            if (operand.Length == 0 || operand[0] is '%' or '{' or '@' or '\\') return null;
            var end = operand.IndexOfAny(['(', ',', ' ', '\t', '　']);
            var value = (end < 0 ? operand : operand[..end]).Trim();
            return value.Length == 0 || value.Contains("[[", StringComparison.Ordinal) ? null : value;
        }
        static StartupSourceValidation Fail(string detail) => new(false, detail, [], 0, 0);
    }
}

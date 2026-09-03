using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Compiler;

public enum RuntimeMetadataValueType : byte { Integer, String }
public readonly record struct FunctionRuntimeParameter(string Name, RuntimeMetadataValueType Type, int Slot, bool HasDefault, long DefaultInteger, string? DefaultString);
public readonly record struct FunctionRuntimePrivate(string Name, RuntimeMetadataValueType Type, ImmutableArray<int> Dimensions, bool IsStatic);

// Compile-time-only representation. It contains no source slice or Legacy parser object.
public sealed record FunctionRuntimeMetadata(
    ImmutableArray<FunctionRuntimeParameter> Parameters,
    int LocalSize,
    int LocalsSize,
    ImmutableArray<FunctionRuntimePrivate> PrivateVariables,
    RuntimeMetadataValueType? ReturnType)
{
    public static FunctionRuntimeMetadata Empty { get; } = new([], 0, 0, [], null);
}

public static class FunctionRuntimeMetadataParser
{
    private const SourceIndexFlags HardFallback = SourceIndexFlags.Preprocessor | SourceIndexFlags.Rename | SourceIndexFlags.LineContinuation | SourceIndexFlags.OtherSemanticFallback;

    public static bool TryParse(FunctionSource source, CompilerCompatibilityOptions options, out FunctionRuntimeMetadata metadata, out string detail)
    {
        metadata = FunctionRuntimeMetadata.Empty;
        detail = string.Empty;
        if ((source.Function.Flags & HardFallback) != 0) { detail = "unresolved function-local fallback flags"; return false; }
        string text;
        try { text = new UTF8Encoding(false, true).GetString(source.Bytes); }
        catch (DecoderFallbackException) { detail = "invalid UTF-8 metadata source"; return false; }
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || !TryParseHeader(lines[0], options, out var parameters, out detail)) return false;
        var locals = 0;
        var localsString = 0;
        RuntimeMetadataValueType? returnType = null;
        var privateVariables = ImmutableArray.CreateBuilder<FunctionRuntimePrivate>();
        for (var line = 1; line < lines.Length; line++)
        {
            var value = lines[line].TrimStart(' ', '\t', '　');
            if (value.Length == 0 || value[0] != '#') continue;
            var body = StripComment(value[1..]).Trim();
            var scan = LegacyIdentifierScanner.ReadFirstIdentifier(body, options);
            if (scan.Identifier.Length == 0) { detail = "empty metadata directive"; return false; }
            var argument = body[scan.StopPosition..].Trim();
            if (scan.Identifier.Equals("LOCALSIZE", options.NameComparison))
            {
                if (!TryParsePositiveInt(argument, out locals)) { detail = "invalid #LOCALSIZE"; return false; }
                continue;
            }
            if (scan.Identifier.Equals("LOCALSSIZE", options.NameComparison))
            {
                if (!TryParsePositiveInt(argument, out localsString)) { detail = "invalid #LOCALSSIZE"; return false; }
                continue;
            }
            if (scan.Identifier.Equals("FUNCTION", options.NameComparison)) { returnType = RuntimeMetadataValueType.Integer; continue; }
            if (scan.Identifier.Equals("FUNCTIONS", options.NameComparison)) { returnType = RuntimeMetadataValueType.String; continue; }
            if (scan.Identifier.Equals("DIM", options.NameComparison) || scan.Identifier.Equals("DIMS", options.NameComparison))
            {
                if (!TryParsePrivate(argument, scan.Identifier.Equals("DIMS", options.NameComparison), options, out var variable)) { detail = "unsupported private #DIM/#DIMS"; return false; }
                privateVariables.Add(variable);
                continue;
            }
            detail = "unsupported metadata directive: #" + scan.Identifier;
            return false;
        }
        metadata = new(parameters, locals, localsString, privateVariables.ToImmutable(), returnType);
        return true;
    }

    // [Emuera改修:NEXT-3D-R1.4F 2026-09-04]
    // 特定function名の例外ではなく、Legacy-valid header grammarを共通に受ける互換修復。
    // comma/parenthesized form、top-level inline default、quoted comma/equals、nested delimiterを
    // lexical contextを保ったまま分解するため、後段がARG/ARGSを取り違えずfallback境界も維持できる。
    private static bool TryParseHeader(string line, CompilerCompatibilityOptions options, out ImmutableArray<FunctionRuntimeParameter> parameters, out string detail)
    {
        parameters = [];
        detail = string.Empty;
        var header = line.TrimStart(' ', '\t', '　');
        if (header.Length == 0 || header[0] != '@') { detail = "function header missing"; return false; }
        var scan = LegacyIdentifierScanner.ReadFirstIdentifier(header[1..], options);
        if (scan.Identifier.Length == 0) { detail = "function name missing"; return false; }
        var rest = header[(scan.StopPosition + 1)..].Trim();
        if (rest.Length == 0) return true;
        string parameterText;
        if (rest[0] == '(' && rest[^1] == ')') parameterText = rest[1..^1];
        else if (rest[0] == ',') parameterText = rest[1..];
        else { detail = "unsupported function header suffix"; return false; }
        var parts = SemanticLexicalTokenStream.SplitTopLevel(parameterText, ',', options);
        if (parts.Count == 0) return true;
        var result = ImmutableArray.CreateBuilder<FunctionRuntimeParameter>();
        for (var i = 0; i < parts.Count;)
        {
            var item = parts[i++].Trim();
            var equals = FindTopLevelEquals(item);
            var inlineDefault = equals < 0 ? string.Empty : item[(equals + 1)..].Trim();
            var inlineDefaultPresent = equals >= 0;
            var name = equals < 0 ? item : item[..equals].Trim();
            if (!TryParseParameter(name, result.Count, out var parameter)) { detail = "unsupported function parameter: " + name; return false; }
            var fallback = inlineDefault;
            var next = i < parts.Count ? parts[i].Trim() : string.Empty;
            var nextEquals = FindTopLevelEquals(next);
            var nextName = nextEquals < 0 ? next : next[..nextEquals].Trim();
            if (i < parts.Count && !TryParseParameter(nextName, result.Count, out _))
            {
                if (inlineDefaultPresent) { detail = "duplicate parameter default"; return false; }
                fallback = parts[i++].Trim();
            }
            if (inlineDefaultPresent && fallback.Length == 0) { detail = "empty parameter default"; return false; }
            long integer = 0;
            string? text = null;
            if (fallback.Length != 0 && !TryParseConstant(fallback, parameter.Type, out integer, out text)) { detail = "non-constant parameter default"; return false; }
            result.Add(parameter with { HasDefault = fallback.Length != 0, DefaultInteger = fallback.Length == 0 ? 0 : integer, DefaultString = fallback.Length == 0 ? null : text });
        }
        parameters = result.ToImmutable();
        return true;
    }

    private static bool TryParseParameter(string value, int slot, out FunctionRuntimeParameter parameter)
    {
        parameter = default;
        var colon = value.IndexOf(':');
        var name = colon < 0 ? value : value[..colon];
        var parsed = slot;
        if (colon >= 0 && (!int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < 0)) return false;
        var type = name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) ? RuntimeMetadataValueType.String : name.Equals("ARG", StringComparison.OrdinalIgnoreCase) ? RuntimeMetadataValueType.Integer : (RuntimeMetadataValueType?)null;
        if (type is null) return false;
        parameter = new(name.ToUpperInvariant(), type.Value, parsed, false, 0, null);
        return true;
    }

    private static bool TryParsePrivate(string value, bool isString, CompilerCompatibilityOptions options, out FunctionRuntimePrivate variable)
    {
        variable = default;
        var beforeDefault = value.Split('=', 2)[0].Trim();
        var parts = SemanticLexicalTokenStream.SplitTopLevel(beforeDefault, ',', options);
        if (parts.Count == 0) return false;
        var declaration = parts[0].Trim().Split([' ', '\t', '　'], StringSplitOptions.RemoveEmptyEntries);
        var offset = declaration.Length > 0 && (declaration[0].Equals("DYNAMIC", options.NameComparison) || declaration[0].Equals("STATIC", options.NameComparison)) ? 1 : 0;
        if (declaration.Length != offset + 1) return false;
        var dimensions = ImmutableArray.CreateBuilder<int>();
        for (var i = 1; i < parts.Count; i++) { if (!TryParsePositiveInt(parts[i].Trim(), out var length)) return false; dimensions.Add(length); }
        if (dimensions.Count == 0) dimensions.Add(1);
        variable = new(declaration[offset], isString ? RuntimeMetadataValueType.String : RuntimeMetadataValueType.Integer, dimensions.ToImmutable(), offset == 0 || declaration[0].Equals("STATIC", options.NameComparison));
        return true;
    }

    private static bool TryParsePositiveInt(string value, out int result) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result is > 0 and < int.MaxValue;
    private static int FindTopLevelEquals(string value)
    {
        var quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            if (quote != '\0') { if (value[i] == '\\') i++; else if (value[i] == quote) quote = '\0'; }
            else if (value[i] is '"' or '\'') quote = value[i];
            else if (value[i] == '=') return i;
        }
        return -1;
    }
    private static bool TryParseConstant(string value, RuntimeMetadataValueType type, out long integer, out string? text)
    {
        integer = 0; text = null;
        if (type == RuntimeMetadataValueType.Integer) return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer);
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return false;
        text = value[1..^1]; return true;
    }
    private static string StripComment(string value)
    {
        var quote = '\0';
        for (var i = 0; i < value.Length; i++) { if (quote != '\0') { if (value[i] == '\\') i++; else if (value[i] == quote) quote = '\0'; } else if (value[i] is '"' or '\'') quote = value[i]; else if (value[i] == ';') return value[..i]; }
        return value;
    }
}

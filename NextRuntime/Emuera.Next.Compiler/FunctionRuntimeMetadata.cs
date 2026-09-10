using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Compiler;

public enum RuntimeMetadataValueType : byte { Integer, String }
public readonly record struct FunctionRuntimeParameter(string Name, RuntimeMetadataValueType Type, int Slot, bool HasDefault, long DefaultInteger, string? DefaultString, bool IsPrivate = false);
public readonly record struct FunctionRuntimePrivate(string Name, RuntimeMetadataValueType Type, ImmutableArray<int> Dimensions, bool IsStatic,
    bool IsConst = false, bool HasInitializer = false, long InitialInteger = 0, string? InitialString = null,
    ImmutableArray<long> InitialIntegers = default, ImmutableArray<string> InitialStrings = default);

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
    public static bool TryParse(FunctionSource source, CompilerCompatibilityOptions options, out FunctionRuntimeMetadata metadata, out string detail,
        MacroCatalog? constants = null, bool allowUnresolvedDynamicInitializer = false)
    {
        metadata = FunctionRuntimeMetadata.Empty;
        detail = string.Empty;
        string text;
        try { text = new UTF8Encoding(false, true).GetString(source.Bytes); }
        catch (DecoderFallbackException) { detail = "invalid UTF-8 metadata source"; return false; }
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0) { detail = "function header missing"; return false; }
        var locals = 0;
        var localsString = 0;
        RuntimeMetadataValueType? returnType = null;
        var privateVariables = ImmutableArray.CreateBuilder<FunctionRuntimePrivate>();
        for (var line = 1; line < lines.Length; line++)
        {
            var value = lines[line].TrimStart(' ', '\t', '　');
            if (value.Length == 0) continue;
            if (value[0] != '#')
            {
                if (!options.UseScopedVariableInstruction) continue;
                var bodyLine = StripComment(value).Trim();
                var bodyScan = LegacyIdentifierScanner.ReadFirstIdentifier(bodyLine, options);
                if (!bodyScan.Identifier.Equals("VARI", options.NameComparison) && !bodyScan.Identifier.Equals("VARS", options.NameComparison)) continue;
                if (!TryParseBodyPrivate(bodyLine[bodyScan.StopPosition..], bodyScan.Identifier.Equals("VARS", options.NameComparison), options, out var bodyVariable))
                { detail = "unsupported body-local " + bodyScan.Identifier; return false; }
                if (privateVariables.Any(existing => existing.Name.Equals(bodyVariable.Name, options.NameComparison)))
                { detail = "duplicate private variable: " + bodyVariable.Name; return false; }
                privateVariables.Add(bodyVariable);
                continue;
            }
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
            if (scan.Identifier.Equals("PRI", options.NameComparison) || scan.Identifier.Equals("LATER", options.NameComparison) ||
                scan.Identifier.Equals("ONLY", options.NameComparison) || scan.Identifier.Equals("SINGLE", options.NameComparison)) continue;
            if (scan.Identifier.Equals("DIM", options.NameComparison) || scan.Identifier.Equals("DIMS", options.NameComparison))
            {
                if (!TryParsePrivate(argument, scan.Identifier.Equals("DIMS", options.NameComparison), options, constants,
                        allowUnresolvedDynamicInitializer, out var variable)) { detail = "unsupported private #DIM/#DIMS"; return false; }
                privateVariables.Add(variable);
                continue;
            }
            detail = "unsupported metadata directive: #" + scan.Identifier;
            return false;
        }
        var privateMetadata = privateVariables.ToImmutable();
        if (!TryParseHeader(lines[0], privateMetadata, options, out var parameters, out detail)) return false;
        metadata = new(parameters, locals, localsString, privateMetadata, returnType);
        return true;
    }

    // [Emuera改修:NEXT-3D-R1.4F 2026-09-04]
    // 特定function名の例外ではなく、Legacy-valid header grammarを共通に受ける互換修復。
    // comma/parenthesized form、top-level inline default、quoted comma/equals、nested delimiterを
    // lexical contextを保ったまま分解するため、後段がARG/ARGSを取り違えずfallback境界も維持できる。
    private static bool TryParseHeader(string line, ImmutableArray<FunctionRuntimePrivate> privateVariables, CompilerCompatibilityOptions options, out ImmutableArray<FunctionRuntimeParameter> parameters, out string detail)
    {
        parameters = [];
        detail = string.Empty;
        var header = StripComment(line).TrimStart(' ', '\t', '　').TrimEnd();
        if (header.Length == 0 || header[0] != '@') { detail = "function header missing"; return false; }
        var scan = LegacyIdentifierScanner.ReadFirstIdentifier(header[1..], options);
        if (scan.Identifier.Length == 0) { detail = "function name missing"; return false; }
        var rest = header[(scan.StopPosition + 1)..].Trim();
        if (rest.Length == 0) return true;
        string parameterText;
        if (rest[0] == '(' && rest[^1] == ')') parameterText = rest[1..^1];
        else if (rest[0] == ',') parameterText = rest[1..];
        else { detail = "unsupported function header suffix"; return false; }
        if (string.IsNullOrWhiteSpace(parameterText)) return true;
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
            if (!TryParseParameter(name, NextParameterSlot(name, result, options), privateVariables, options, out var parameter)) { detail = "unsupported function parameter: " + name; return false; }
            var fallback = inlineDefault;
            var next = i < parts.Count ? parts[i].Trim() : string.Empty;
            var nextEquals = FindTopLevelEquals(next);
            var nextName = nextEquals < 0 ? next : next[..nextEquals].Trim();
            if (i < parts.Count && !TryParseParameter(nextName, NextParameterSlot(nextName, result, options), privateVariables, options, out _))
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

    private static int NextParameterSlot(string value, ImmutableArray<FunctionRuntimeParameter>.Builder parameters, CompilerCompatibilityOptions options)
    {
        var colon = value.IndexOf(':');
        var name = (colon < 0 ? value : value[..colon]).Trim();
        return name.Equals("ARG", options.NameComparison)
            ? parameters.Count(parameter => parameter.Type == RuntimeMetadataValueType.Integer && !parameter.IsPrivate)
            : name.Equals("ARGS", options.NameComparison)
                ? parameters.Count(parameter => parameter.Type == RuntimeMetadataValueType.String && !parameter.IsPrivate)
                : 0;
    }

    private static bool TryParseParameter(string value, int slot, ImmutableArray<FunctionRuntimePrivate> privateVariables,
        CompilerCompatibilityOptions options, out FunctionRuntimeParameter parameter)
    {
        parameter = default;
        var colon = value.IndexOf(':');
        var name = (colon < 0 ? value : value[..colon]).Trim();
        var parsed = slot;
        if (colon >= 0 && (!int.TryParse(value[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < 0)) return false;
        var type = name.Equals("ARGS", options.NameComparison) ? RuntimeMetadataValueType.String : name.Equals("ARG", options.NameComparison) ? RuntimeMetadataValueType.Integer : (RuntimeMetadataValueType?)null;
        if (type is not null)
        {
            parameter = new(name.ToUpperInvariant(), type.Value, parsed, false, 0, null);
            return true;
        }
        var declaration = privateVariables.FirstOrDefault(variable => variable.Name.Equals(name, options.NameComparison));
        var privateIndex = colon < 0 ? 0 : parsed;
        if (declaration.Name is null || declaration.Dimensions.Length != 1 || privateIndex < 0 || privateIndex >= declaration.Dimensions[0]) return false;
        parameter = new(declaration.Name, declaration.Type, privateIndex, false, 0, null, true);
        return true;
    }

    private static bool TryParsePrivate(string value, bool isString, CompilerCompatibilityOptions options, MacroCatalog? constants,
        bool allowUnresolvedDynamicInitializer, out FunctionRuntimePrivate variable)
    {
        variable = default;
        value = StripComment(value).Trim();
        var equals = FindTopLevelEquals(value);
        var beforeDefault = (equals < 0 ? value : value[..equals]).Trim();
        var initializer = equals < 0 ? string.Empty : value[(equals + 1)..].Trim();
        var parts = SemanticLexicalTokenStream.SplitTopLevel(beforeDefault, ',', options);
        if (parts.Count == 0) return false;
        var declaration = parts[0].Trim().Split([' ', '\t', '　'], StringSplitOptions.RemoveEmptyEntries);
        if (declaration.Length == 0) return false;
        var dynamic = false;
        var explicitlyStatic = false;
        var isConst = false;
        for (var i = 0; i < declaration.Length - 1; i++)
        {
            if (declaration[i].Equals("DYNAMIC", options.NameComparison)) dynamic = true;
            else if (declaration[i].Equals("STATIC", options.NameComparison)) explicitlyStatic = true;
            else if (declaration[i].Equals("CONST", options.NameComparison)) isConst = true;
            else return false;
        }
        if (dynamic && (explicitlyStatic || isConst)) return false;
        var dimensions = ImmutableArray.CreateBuilder<int>();
        for (var i = 1; i < parts.Count; i++) { if (!TryParsePositiveInt(parts[i].Trim(), out var length)) return false; dimensions.Add(length); }
        var type = isString ? RuntimeMetadataValueType.String : RuntimeMetadataValueType.Integer;
        long initialInteger = 0;
        string? initialString = null;
        var integerInitializers = ImmutableArray.CreateBuilder<long>();
        var stringInitializers = ImmutableArray.CreateBuilder<string>();
        if (initializer.Length != 0)
        {
            foreach (var item in SemanticLexicalTokenStream.SplitTopLevel(initializer, ',', options))
            {
                var itemText = item.Trim();
                if (!TryParseConstant(itemText, type, out var integerValue, out var stringValue) &&
                    (constants is null || !constants.TryGetConstant(itemText, out integerValue, out stringValue) ||
                     type == RuntimeMetadataValueType.Integer && stringValue is not null || type == RuntimeMetadataValueType.String && stringValue is null))
                {
                    if (!allowUnresolvedDynamicInitializer || !dynamic) return false;
                    integerInitializers.Clear(); stringInitializers.Clear(); initializer = string.Empty;
                    break;
                }
                if (type == RuntimeMetadataValueType.Integer) integerInitializers.Add(integerValue); else stringInitializers.Add(stringValue ?? string.Empty);
            }
            initialInteger = integerInitializers.Count == 0 ? 0 : integerInitializers[0];
            initialString = stringInitializers.Count == 0 ? null : stringInitializers[0];
        }
        if (dimensions.Count == 0) dimensions.Add(Math.Max(1, type == RuntimeMetadataValueType.Integer ? integerInitializers.Count : stringInitializers.Count));
        var elementCount = dimensions.Aggregate(1, static (count, dimension) => checked(count * dimension));
        if (integerInitializers.Count > elementCount || stringInitializers.Count > elementCount) return false;
        variable = new(declaration[^1], type, dimensions.ToImmutable(), !dynamic, isConst, initializer.Length != 0, initialInteger, initialString,
            integerInitializers.ToImmutable(), stringInitializers.ToImmutable());
        return true;
    }

    private static bool TryParseBodyPrivate(string value, bool isString, CompilerCompatibilityOptions options, out FunctionRuntimePrivate variable)
    {
        variable = default;
        value = StripComment(value).Trim();
        var equals = FindTopLevelEquals(value);
        var declaration = (equals < 0 ? value : value[..equals]).Trim();
        var parts = SemanticLexicalTokenStream.SplitTopLevel(declaration, ',', options);
        if (parts.Count == 0 || string.IsNullOrWhiteSpace(parts[0])) return false;
        var name = parts[0].Trim();
        if (LegacyIdentifierScanner.ReadFirstIdentifier(name, options).Identifier.Length != name.Length) return false;
        var dimensions = ImmutableArray.CreateBuilder<int>();
        for (var index = 1; index < parts.Count; index++)
        {
            if (!TryParsePositiveInt(parts[index].Trim(), out var length)) return false;
            dimensions.Add(length);
        }
        if (dimensions.Count == 0) dimensions.Add(1);
        variable = new(name, isString ? RuntimeMetadataValueType.String : RuntimeMetadataValueType.Integer, dimensions.ToImmutable(), false);
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
        if (type == RuntimeMetadataValueType.Integer)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return true;
            try
            {
                if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) integer = Convert.ToInt64(value[2..], 16);
                else if (value.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) integer = Convert.ToInt64(value[2..], 2);
                else return false;
                return true;
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
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

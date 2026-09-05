using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Compiler;

public enum SemanticNodeKind : byte { IntegerLiteral, StringLiteral, Symbol, Variable, VariableSubkey, Call, Unary, Binary, Ternary, Format, ConditionalFormat, TripleLiteral, RenameTemplate, Case, FormattedSequence, CountedLoop, MissingArgument }
public enum SemanticOperator : byte { None, Plus, Minus, Multiply, Divide, Modulo, Equal, Greater, Less, GreaterEqual, LessEqual, NotEqual, BitAnd, BitOr, BitXor, LogicalAnd, LogicalOr, LogicalXor, Nand, Nor, ShiftLeft, ShiftRight, Not, BitNot, PrefixIncrement, PrefixDecrement, PostfixIncrement, PostfixDecrement, CountedFor, CountedRepeat }
public enum SemanticFormatKind : byte { Percent, Curly }
[Flags] public enum SemanticNodeFlags : int { None = 0, RightFirstZeroCheck = 1, ShortCircuit = 2, SelectedBranchOnly = 4, RightPresent = 8, WarningParityDeferred = 16 }
public enum SemanticOperandKind : byte { Expression, Format, Case, CountedLoop }

[StructLayout(LayoutKind.Sequential, Pack = 4)] public readonly record struct SemanticSlice(int Offset, int Length);
[StructLayout(LayoutKind.Sequential, Pack = 4)] public readonly record struct SemanticNode(SemanticNodeKind Kind, SemanticOperator Operator, int A, int B, int C, int D);
[StructLayout(LayoutKind.Sequential, Pack = 4)] public readonly record struct SemanticEdge(int To);
[StructLayout(LayoutKind.Sequential, Pack = 4)] public readonly record struct SemanticCaseArm(int ValueNode, int ToNode);
[StructLayout(LayoutKind.Sequential, Pack = 4)] public readonly record struct SemanticRecord(int InstructionIndex, int RootNodeIndex, int NodeCount);
public enum SemanticHostIdentityKind : byte { Variable, Call }
// Stable IDs are compiler output. Names remain diagnostic/binding input only and are never parsed on the VM hot path.
public readonly record struct SemanticHostIdentity(SemanticHostIdentityKind Kind, int NodeIndex, ulong StableId, int NameSymbolIndex, int SubkeySymbolIndex, int IndexArity);

public sealed class SemanticPayload
{
    public ImmutableArray<SemanticNode> Nodes { get; }
    public ImmutableArray<SemanticEdge> Edges { get; }
    public ImmutableArray<SemanticSlice> Symbols { get; }
    public ImmutableArray<SemanticCaseArm> CaseArms { get; }
    public ImmutableArray<SemanticRecord> Records { get; }
    public ImmutableArray<SemanticHostIdentity> HostIdentities { get; }
    public byte[] Utf8 { get; }
    private readonly int[] hostIdentityIndexByNode;
    public bool HasSemanticOperands => !Records.IsDefaultOrEmpty;
    public int SemanticNodeCount => Nodes.Length;
    public int SemanticRecordCount => Records.Length;
    public SemanticPayload(IEnumerable<SemanticNode> nodes, IEnumerable<SemanticEdge> edges, IEnumerable<SemanticSlice> symbols, IEnumerable<SemanticCaseArm> caseArms, IEnumerable<SemanticRecord> records, byte[]? utf8 = null)
    {
        (Nodes, Edges, Symbols, CaseArms, Records, Utf8) = (nodes.ToImmutableArray(), edges.ToImmutableArray(), symbols.ToImmutableArray(), caseArms.ToImmutableArray(), records.ToImmutableArray(), utf8 ?? []);
        HostIdentities = CreateHostIdentities();
        hostIdentityIndexByNode = CreateHostIdentityIndex();
    }
    public string ReadSymbol(SemanticSlice slice) => Encoding.UTF8.GetString(Utf8, slice.Offset, slice.Length);
    public bool TryGetHostIdentity(int nodeIndex, SemanticHostIdentityKind kind, out SemanticHostIdentity identity) =>
        TryGetHostIdentity(nodeIndex, kind, out identity, out _);
    public bool TryGetHostIdentity(int nodeIndex, SemanticHostIdentityKind kind, out SemanticHostIdentity identity, out int scannedElements)
    {
        scannedElements = 0;
        if ((uint)nodeIndex < (uint)hostIdentityIndexByNode.Length)
        {
            var identityIndex = hostIdentityIndexByNode[nodeIndex];
            if ((uint)identityIndex < (uint)HostIdentities.Length)
            {
                var candidate = HostIdentities[identityIndex];
                if (candidate.NodeIndex == nodeIndex && candidate.Kind == kind) { identity = candidate; return true; }
            }
        }
        identity = default; return false;
    }
    public static SemanticPayload Empty { get; } = new([], [], [], [], []);
    public static SemanticPayload Merge(IEnumerable<SemanticPayload> payloads)
    {
        var nodes = new List<SemanticNode>(); var edges = new List<SemanticEdge>(); var symbols = new List<SemanticSlice>(); var arms = new List<SemanticCaseArm>(); var records = new List<SemanticRecord>(); var bytes = new List<byte>();
        foreach (var payload in payloads)
        {
            var nodeBase = nodes.Count; var edgeBase = edges.Count; var symbolBase = symbols.Count; var armBase = arms.Count; var byteBase = bytes.Count;
            bytes.AddRange(payload.Utf8); symbols.AddRange(payload.Symbols.Select(x => new SemanticSlice(byteBase + x.Offset, x.Length)));
            foreach (var node in payload.Nodes)
            {
                var a = node.A; var b = node.B; var c = node.C; var d = node.D;
                switch (node.Kind)
                {
                    case SemanticNodeKind.IntegerLiteral: case SemanticNodeKind.StringLiteral: case SemanticNodeKind.Symbol: case SemanticNodeKind.RenameTemplate: case SemanticNodeKind.TripleLiteral: a = Rebase(a, symbolBase); break;
                    case SemanticNodeKind.VariableSubkey: a = Rebase(a, symbolBase); b = Rebase(b, symbolBase); c = Rebase(c, edgeBase); break;
                    case SemanticNodeKind.Variable: case SemanticNodeKind.Call: a = Rebase(a, symbolBase); b = Rebase(b, edgeBase); break;
                    case SemanticNodeKind.Unary: a = Rebase(a, nodeBase); break;
                    case SemanticNodeKind.Binary: case SemanticNodeKind.Ternary: a = Rebase(a, nodeBase); b = Rebase(b, nodeBase); c = Rebase(c, nodeBase); break;
                    case SemanticNodeKind.Format: a = Rebase(a, nodeBase); b = Rebase(b, nodeBase); c = Rebase(c, symbolBase); break;
                    case SemanticNodeKind.ConditionalFormat: a = Rebase(a, nodeBase); b = Rebase(b, nodeBase); c = Rebase(c, nodeBase); break;
                    case SemanticNodeKind.Case: a = Rebase(a, armBase); break;
                    case SemanticNodeKind.FormattedSequence: a = Rebase(a, edgeBase); break;
                    case SemanticNodeKind.CountedLoop: a = Rebase(a, nodeBase); b = Rebase(b, nodeBase); c = Rebase(c, nodeBase); d = Rebase(d, nodeBase); break;
                }
                nodes.Add(new(node.Kind, node.Operator, a, b, c, d));
            }
            edges.AddRange(payload.Edges.Select(x => new SemanticEdge(Rebase(x.To, nodeBase))));
            arms.AddRange(payload.CaseArms.Select(x => new SemanticCaseArm(Rebase(x.ValueNode, nodeBase), Rebase(x.ToNode, nodeBase))));
            records.AddRange(payload.Records.Select(x => new SemanticRecord(x.InstructionIndex, Rebase(x.RootNodeIndex, nodeBase), x.NodeCount)));
        }
        return new(nodes, edges, symbols, arms, records, bytes.ToArray());
        static int Rebase(int value, int @base) => value < 0 ? value : value + @base;
    }

    private ImmutableArray<SemanticHostIdentity> CreateHostIdentities()
    {
        var identities = ImmutableArray.CreateBuilder<SemanticHostIdentity>();
        for (var index = 0; index < Nodes.Length; index++)
        {
            var node = Nodes[index];
            var kind = node.Kind == SemanticNodeKind.Call ? SemanticHostIdentityKind.Call : SemanticHostIdentityKind.Variable;
            var isHost = node.Kind is SemanticNodeKind.Symbol or SemanticNodeKind.Variable or SemanticNodeKind.VariableSubkey or SemanticNodeKind.Call;
            if (!isHost || (uint)node.A >= (uint)Symbols.Length) continue;
            var subkey = node.Kind == SemanticNodeKind.VariableSubkey ? node.B : -1;
            if (subkey >= Symbols.Length) continue;
            var arity = node.Kind switch { SemanticNodeKind.Variable => node.C, SemanticNodeKind.VariableSubkey => node.D, SemanticNodeKind.Call => node.C, _ => 0 };
            var name = ReadSymbol(Symbols[node.A]);
            var subkeyText = subkey < 0 ? string.Empty : ReadSymbol(Symbols[subkey]);
            identities.Add(new(kind, index, StableId(kind, name, subkeyText, arity), node.A, subkey, arity));
        }
        return identities.ToImmutable();
    }

    private int[] CreateHostIdentityIndex()
    {
        var index = new int[Nodes.Length];
        Array.Fill(index, -1);
        for (var identityIndex = 0; identityIndex < HostIdentities.Length; identityIndex++)
        {
            var nodeIndex = HostIdentities[identityIndex].NodeIndex;
            if ((uint)nodeIndex < (uint)index.Length && index[nodeIndex] < 0) index[nodeIndex] = identityIndex;
        }
        return index;
    }

    private static ulong StableId(SemanticHostIdentityKind kind, string name, string subkey, int arity)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes($"{(byte)kind}\0{name.ToUpperInvariant()}\0{subkey.ToUpperInvariant()}\0{arity}")) { hash ^= b; hash *= 1099511628211UL; }
        return hash;
    }
}

public sealed class SemanticRenameResolver
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public SemanticRenameResolver(IEnumerable<KeyValuePair<string, string>> mappings) { foreach (var mapping in mappings) values[mapping.Key] = mapping.Value; }
    public bool TryResolve(string key, out string value) => values.TryGetValue(key, out value!);
}

public sealed class StructuralSemanticEnvironment
{
    public CompilerCompatibilityOptions Compatibility { get; }
    public MacroCatalog Macros { get; }
    public SemanticRenameResolver? RenameResolver { get; }
    public bool SystemIgnoreTripleSymbol { get; }
    public string RenameStrategy => RenameResolver is null ? "NONE" : "A";
    public bool RenameResolutionDeferred => RenameResolver is null;
    public StructuralSemanticEnvironment(CompilerCompatibilityOptions compatibility, MacroCatalog? macros = null, bool systemIgnoreTripleSymbol = true, SemanticRenameResolver? renameResolver = null)
        => (Compatibility, Macros, SystemIgnoreTripleSymbol, RenameResolver) = (compatibility, macros ?? new MacroCatalog(compatibility), systemIgnoreTripleSymbol, renameResolver);
}

public enum MacroReplacementKind : byte { Empty, Integer, Variable, FunctionCall, Text, RenameTemplate }
public sealed record MacroDefinition(string Name, string Replacement, MacroReplacementKind Kind = MacroReplacementKind.Text, bool RenameTemplate = false);

public sealed class MacroCatalog
{
    private readonly Dictionary<string, MacroDefinition> definitions;
    private readonly CompilerCompatibilityOptions? canonicalOptions;
    public MacroCatalog(StringComparer? comparer = null) => definitions = new(comparer ?? StringComparer.OrdinalIgnoreCase);
    public MacroCatalog(CompilerCompatibilityOptions options) => (canonicalOptions, definitions) = (options, new(options.NameComparer));
    public int Count => definitions.Count;
    public IReadOnlyCollection<MacroDefinition> Definitions => definitions.Values;
    public void Add(MacroDefinition definition) => definitions[definition.Name] = definition;
    public bool TryGet(string name, out MacroDefinition definition) => definitions.TryGetValue(name, out definition!);
    public static MacroCatalog FromHeaderSources(IEnumerable<string> orderedHeaderSources, CompilerCompatibilityOptions options, SemanticRenameResolver? renameResolver = null)
    {
        var catalog = new MacroCatalog(options);
        foreach (var source in orderedHeaderSources)
        foreach (var rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = ApplyRename(rawLine, renameResolver); var index = SkipHeaderPrefix(line, options);
            if (index < 0 || index >= line.Length || line[index] != '#') continue;
            index++; var directiveLength = LegacyIdentifierScanner.ReadIdentifierLength(line.AsSpan(index));
            if (directiveLength == 0) throw new SemanticParseException("# directive requires immediate identifier");
            var directive = line.Substring(index, directiveLength); if (!directive.Equals("DEFINE", options.NameComparison)) continue;
            index += directiveLength; index = SkipLegacySpace(line, index, options); var nameLength = LegacyIdentifierScanner.ReadIdentifierLength(line.AsSpan(index)); if (nameLength == 0) throw new SemanticParseException("#DEFINE name missing");
            var name = line.Substring(index, nameLength); index += nameLength; if (index < line.Length && line[index] == '(') throw new SemanticParseException("function-like #DEFINE declaration detected immediately after macro name");
            var replacement = SemanticLexicalTokenStream.ApplyRename(index < line.Length ? line[index..] : string.Empty, renameResolver); var effective = catalog.Expand(replacement, options, out _);
            catalog.Add(new(name, effective, effective.Length == 0 ? MacroReplacementKind.Empty : MacroReplacementKind.Text));
        }
        return catalog;
    }
    public static MacroCatalog FromHeaderText(string source, CompilerCompatibilityOptions options, SemanticRenameResolver? renameResolver = null) => FromHeaderSources([source], options, renameResolver);
    public string Expand(string source, CompilerCompatibilityOptions options, out int substitutions)
    {
        if (canonicalOptions is { } canonical && canonical != options) throw new SemanticParseException("macro catalog compatibility mismatch");
        substitutions = 0;
        var tokens = SemanticLexicalTokenStream.Tokenize(source, options);
        var hasRootMacroMatch = false;
        foreach (var token in tokens)
        {
            if (token.Kind == SemanticTokenKind.Identifier && TryGet(token.Raw, out _))
            {
                hasRootMacroMatch = true;
                break;
            }
        }
        if (!hasRootMacroMatch) return SemanticLexicalTokenStream.Serialize(tokens);
        var output = new List<SemanticToken>(); ExpandTokens(tokens, options, ref substitutions, output); return SemanticLexicalTokenStream.Serialize(output);
    }
    private void ExpandTokens(IEnumerable<SemanticToken> tokens, CompilerCompatibilityOptions options, ref int substitutions, List<SemanticToken> output)
    {
        foreach (var token in tokens)
        {
            if (token.Kind == SemanticTokenKind.Identifier && TryGet(token.Raw, out var definition))
            {
                if (++substitutions > 100) throw new SemanticParseException("macro expansion limit exceeded");
                if (output.Count > 0) output.Add(new(SemanticTokenKind.Boundary, string.Empty));
                ExpandTokens(SemanticLexicalTokenStream.Tokenize(definition.Replacement, options), options, ref substitutions, output);
                output.Add(new(SemanticTokenKind.Boundary, string.Empty));
            }
            else output.Add(token);
        }
    }
    private static int SkipHeaderPrefix(string line, CompilerCompatibilityOptions options)
    {
        var index = 0; while (true) { index = SkipLegacySpace(line, index, options); if (line.AsSpan(index).StartsWith(";!;", StringComparison.Ordinal)) { index += 3; continue; } if (options.DebugMode && line.AsSpan(index).StartsWith(";#;", StringComparison.Ordinal)) { index += 3; continue; } return index < line.Length && line[index] == ';' ? -1 : index; }
    }
    private static int SkipLegacySpace(string value, int index, CompilerCompatibilityOptions options) { while (index < value.Length && (value[index] is ' ' or '\t' || options.SystemAllowFullSpace && value[index] == '　')) index++; return index; }
    private static string ApplyRename(string source, SemanticRenameResolver? resolver)
    {
        if (resolver is null) return source; var result = new StringBuilder(source.Length); var index = 0;
        while (index < source.Length) { var start = source.IndexOf("[[", index, StringComparison.Ordinal); if (start < 0) { result.Append(source[index..]); break; } result.Append(source[index..start]); var end = source.IndexOf("]]", start + 2, StringComparison.Ordinal); if (end < 0) { result.Append(source[start..]); break; } var key = source[start..(end + 2)]; result.Append(resolver.TryResolve(key, out var value) ? value : key); index = end + 2; }
        return result.ToString();
    }
}

public enum SemanticTokenKind : byte { Identifier, Number, String, Formatted, Operator, Punctuation, Other, Boundary }
public readonly record struct SemanticToken(SemanticTokenKind Kind, string Raw)
{ public bool IsIdentifier(string value, CompilerCompatibilityOptions options) => Kind == SemanticTokenKind.Identifier && Raw.Equals(value, options.NameComparison); }

internal static class FormattedDelimiterScanner
{
    internal static int ReadFormattedAtomEnd(string source, int start, CompilerCompatibilityOptions options) => source.AsSpan(start).StartsWith("@\"", StringComparison.Ordinal) ? ReadFormattedQuotedEnd(source, start, options) : source.AsSpan(start).StartsWith("\\@", StringComparison.Ordinal) ? ReadBareConditionalEnd(source, start, options) : -1;
    internal static int ReadFormattedQuotedEnd(string source, int start, CompilerCompatibilityOptions options)
    {
        for (var i = start + 2; i < source.Length;)
        {
            if (source.AsSpan(i).StartsWith("\\@", StringComparison.Ordinal)) { i = ReadBareConditionalEnd(source, i, options); continue; }
            if (source[i] == '\\') { i += Math.Min(2, source.Length - i); continue; }
            if (source[i] == '%') { var end = FindEmbeddedEnd(source, i + 1, '%', options); if (end < 0) throw new SemanticParseException("unterminated formatted segment"); i = end + 1; continue; }
            if (source[i] == '{') { var end = FindEmbeddedEnd(source, i + 1, '}', options); if (end < 0) throw new SemanticParseException("unterminated formatted segment"); i = end + 1; continue; }
            if (source[i++] == '"') return i;
        }
        throw new SemanticParseException("unterminated formatted string");
    }
    internal static int ReadBareConditionalEnd(string source, int start, CompilerCompatibilityOptions options)
    {
        var question = FindConditionalQuestion(source, start + 2, options); if (question < 0) throw new SemanticParseException("conditional format requires ?");
        var delimiter = FindConditionalBranchDelimiter(source, question + 1, true, options); if (delimiter < 0) throw new SemanticParseException("unterminated conditional format");
        if (source[delimiter] != '#') return delimiter + 2;
        var end = FindConditionalBranchDelimiter(source, delimiter + 1, false, options); if (end < 0) throw new SemanticParseException("unterminated conditional format"); return end + 2;
    }
    internal static int FindConditionalQuestion(string source, int start, CompilerCompatibilityOptions options)
    {
        var depth = 0; var quote = false;
        for (var i = start; i < source.Length; i++)
        {
            if (quote) { if (source[i] == '\\') i++; else if (source[i] == '"') quote = false; continue; }
            if (source[i] == '"') { quote = true; continue; }
            if (source[i] == ';') { if (SkipCommentMarker(source, ref i, options)) continue; return -1; }
            if (source[i] is '(' or '[' or '{') depth++; else if (source[i] is ')' or ']' or '}') depth--; else if (source[i] == '?' && depth == 0) return i;
        }
        return -1;
    }
    internal static int FindConditionalBranchDelimiter(string source, int start, bool allowHash, CompilerCompatibilityOptions options)
    {
        for (var i = start; i < source.Length; i++)
        {
            if (source.AsSpan(i).StartsWith("\\@", StringComparison.Ordinal)) return i;
            if (source[i] == '\\') { i++; continue; }
            if (source[i] == '%') { var end = FindEmbeddedEnd(source, i + 1, '%', options); if (end < 0) return -1; i = end; continue; }
            if (source[i] == '{') { var end = FindEmbeddedEnd(source, i + 1, '}', options); if (end < 0) return -1; i = end; continue; }
            if (allowHash && source[i] == '#') return i;
        }
        return -1;
    }
    internal static int FindEmbeddedEnd(string source, int start, char closing, CompilerCompatibilityOptions options)
    {
        var depth = 0; var quote = false;
        for (var i = start; i < source.Length; i++)
        {
            var c = source[i]; if (quote) { if (c == '\\') i++; else if (c == '"') quote = false; continue; }
            if (c == '"') { quote = true; continue; }
            if (c == ';') { if (SkipCommentMarker(source, ref i, options)) continue; return -1; }
            if (c is '(' or '[' or '{') depth++; else if (c is ')' or ']') depth--; else if (c == closing && depth == 0) return i;
        }
        return -1;
    }
    private static bool SkipCommentMarker(string source, ref int index, CompilerCompatibilityOptions options)
    {
        if (source.AsSpan(index).StartsWith(";!;", StringComparison.Ordinal) || options.DebugMode && source.AsSpan(index).StartsWith(";#;", StringComparison.Ordinal)) { index += 2; return true; }
        return false;
    }
}

public static class SemanticLexicalTokenStream
{
    public static IReadOnlyList<SemanticToken> Tokenize(string source, CompilerCompatibilityOptions options)
    {
        var result = new List<SemanticToken>();
        for (var i = 0; i < source.Length;)
        {
            if (source[i] is ' ' or '\t' || options.SystemAllowFullSpace && source[i] == '　') { var start = i++; while (i < source.Length && (source[i] is ' ' or '\t' || options.SystemAllowFullSpace && source[i] == '　')) i++; result.Add(new(SemanticTokenKind.Other, source[start..i])); continue; }
            if (source[i] == ';') { if (source.AsSpan(i).StartsWith(";!;", StringComparison.Ordinal) || options.DebugMode && source.AsSpan(i).StartsWith(";#;", StringComparison.Ordinal)) { result.Add(new(SemanticTokenKind.Boundary, string.Empty)); i += 3; continue; } break; }
            if (source.AsSpan(i).StartsWith("@\"", StringComparison.Ordinal)) { var end = FormattedDelimiterScanner.ReadFormattedQuotedEnd(source, i, options); result.Add(new(SemanticTokenKind.Formatted, source[i..end])); i = end; continue; }
            if (source.AsSpan(i).StartsWith("\\@", StringComparison.Ordinal)) { var end = FormattedDelimiterScanner.ReadBareConditionalEnd(source, i, options); result.Add(new(SemanticTokenKind.Formatted, source[i..end])); i = end; continue; }
            if (source.AsSpan(i).StartsWith("[[", StringComparison.Ordinal)) { var end = source.IndexOf("]]", i + 2, StringComparison.Ordinal); if (end >= 0) { end += 2; result.Add(new(SemanticTokenKind.Punctuation, source[i..end])); i = end; continue; } }
            if (source[i] == '"') { var start = i++; while (i < source.Length) { if (source[i] == '\\') i += Math.Min(2, source.Length - i); else if (source[i++] == '"') break; } result.Add(new(SemanticTokenKind.String, source[start..i])); continue; }
            if (source[i] is >= '0' and <= '9') { var start = i; i = ReadNumberEnd(source, i); result.Add(new(SemanticTokenKind.Number, source[start..i])); continue; }
            var length = LegacyIdentifierScanner.ReadIdentifierLength(source.AsSpan(i)); if (length > 0) { result.Add(new(SemanticTokenKind.Identifier, source.Substring(i, length))); i += length; continue; }
            var op = new[] { "||", "&&", "^^", "!&", "!|", ">=", "<=", "!=", ">>", "<<", "++", "--" }.FirstOrDefault(x => source.AsSpan(i).StartsWith(x, StringComparison.Ordinal)); if (op is not null) { result.Add(new(SemanticTokenKind.Operator, op)); i += op.Length; continue; }
            result.Add(new("()[]{},?:#@".Contains(source[i]) ? SemanticTokenKind.Punctuation : SemanticTokenKind.Operator, source[i++].ToString()));
        }
        return result;
    }
    public static string Serialize(IEnumerable<SemanticToken> tokens)
    {
        var output = new StringBuilder(); SemanticToken? previous = null; var boundary = false;
        foreach (var token in tokens)
        {
            if (token.Kind == SemanticTokenKind.Boundary) { boundary = true; continue; }
            if (token.Kind == SemanticTokenKind.Other && token.Raw.All(c => c is ' ' or '\t' or '　')) { output.Append(token.Raw); previous = null; boundary = false; continue; }
            if (previous is { } prior && boundary && NeedsSeparator(prior, token)) output.Append(' ');
            output.Append(token.Raw); previous = token; boundary = false;
        }
        return output.ToString();
    }
    public static string ApplyRename(string source, SemanticRenameResolver? resolver)
        => ApplyRenameCore(source, resolver, out _);
#if PERFORMANCE_METRICS
    public static string ApplyRename(string source, SemanticRenameResolver? resolver, out bool markerPresent)
        => ApplyRenameCore(source, resolver, out markerPresent);
#endif
    private static string ApplyRenameCore(string source, SemanticRenameResolver? resolver, out bool markerPresent)
    {
        markerPresent = false;
        if (resolver is null) return source;
        var result = new StringBuilder(source.Length); var index = 0;
        while (index < source.Length)
        {
            var start = source.IndexOf("[[", index, StringComparison.Ordinal); if (start < 0) { result.Append(source[index..]); break; }
            markerPresent = true;
            result.Append(source[index..start]); var end = source.IndexOf("]]", start + 2, StringComparison.Ordinal); if (end < 0) { result.Append(source[start..]); break; }
            var key = source[start..(end + 2)]; result.Append(resolver.TryResolve(key, out var value) ? value : key); index = end + 2;
        }
        return result.ToString();
    }
    private static bool NeedsSeparator(SemanticToken previous, SemanticToken current)
    {
        if ((previous.Kind is SemanticTokenKind.Identifier or SemanticTokenKind.Number) && (current.Kind is SemanticTokenKind.Identifier or SemanticTokenKind.Number)) return true;
        if ((previous.Raw + current.Raw) is "++" or "--" or "&&" or "||" or "^^" or "!&" or "!|" or ">=" or "<=" or "!=" or ">>" or "<<") return true;
        return previous.Raw == "@" && current.Kind == SemanticTokenKind.String || previous.Raw == "[" && current.Raw == "[";
    }
    private static int ReadNumberEnd(string source, int position)
    {
        var fromBase = 10;
        if (position + 1 < source.Length && source[position] == '0' && source[position + 1] is 'x' or 'X') { fromBase = 16; position += 2; }
        else if (position + 1 < source.Length && source[position] == '0' && source[position + 1] is 'b' or 'B') { fromBase = 2; position += 2; }
        while (position < source.Length && (fromBase == 2 ? source[position] is '0' or '1' : fromBase == 16 ? source[position] is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F' : source[position] is >= '0' and <= '9')) position++;
        if (position < source.Length && source[position] is 'p' or 'P' or 'e' or 'E') { position++; if (position < source.Length && source[position] is '+' or '-') position++; while (position < source.Length && (fromBase == 2 ? source[position] is '0' or '1' : fromBase == 16 ? source[position] is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F' : source[position] is >= '0' and <= '9')) position++; }
        return position;
    }
    public static string RemoveOrdinaryComment(string source, CompilerCompatibilityOptions options)
    {
        var quote = false; for (var i = 0; i < source.Length; i++) { if (quote) { if (source[i] == '\\') i++; else if (source[i] == '"') quote = false; continue; } if (source[i] == '"') { quote = true; continue; } if (source[i] != ';') continue; if (source.AsSpan(i).StartsWith(";!;", StringComparison.Ordinal)) { source = source.Remove(i, 3); i--; continue; } if (options.DebugMode && source.AsSpan(i).StartsWith(";#;", StringComparison.Ordinal)) { source = source.Remove(i, 3); i--; continue; } return source[..i]; } return source;
    }
    public static List<string> SplitTopLevel(string value, char separator, CompilerCompatibilityOptions options)
    {
        var result = new List<string>(); var start = 0; var depth = 0; var quote = false; for (var i = 0; i < value.Length; i++) { if (quote) { if (value[i] == '\\') i++; else if (value[i] == '"') quote = false; continue; } var formattedEnd = FormattedDelimiterScanner.ReadFormattedAtomEnd(value, i, options); if (formattedEnd >= 0) { i = formattedEnd - 1; continue; } if (value[i] == '"') { quote = true; continue; } if (value[i] is '(' or '[' or '{') depth++; else if (value[i] is ')' or ']' or '}') depth--; else if (value[i] == separator && depth == 0) { result.Add(value[start..i]); start = i + 1; } } result.Add(value[start..]); return result;
    }
    public static int FindTopLevel(string value, string token, CompilerCompatibilityOptions options)
    {
        var depth = 0; var quote = false;
        for (var i = 0; i <= value.Length - token.Length; i++)
        {
            if (quote) { if (value[i] == '\\') i++; else if (value[i] == '"') quote = false; continue; }
            var formattedEnd = FormattedDelimiterScanner.ReadFormattedAtomEnd(value, i, options); if (formattedEnd >= 0) { i = formattedEnd - 1; continue; }
            if (value[i] == '"') { quote = true; continue; }
            if (value[i] is '(' or '[' or '{') { depth++; continue; }
            if (value[i] is ')' or ']' or '}') { depth--; continue; }
            var keyword = LegacyIdentifierScanner.ReadIdentifierLength(token.AsSpan()) == token.Length;
            if (depth == 0 && value.AsSpan(i).StartsWith(token, options.NameComparison) && (!keyword || (i == 0 || LegacyIdentifierScanner.ReadIdentifierLength(value[(i - 1)..].AsSpan()) == 0) && (i + token.Length == value.Length || LegacyIdentifierScanner.ReadIdentifierLength(value[(i + token.Length)..].AsSpan()) == 0))) return i;
        }
        return -1;
    }
}

public sealed class SemanticParseException(string message) : FormatException(message);

public static class SemanticIrCompiler
{
    private static readonly (string Text, SemanticOperator Operator, int Priority)[] Operators = [("||", SemanticOperator.LogicalOr, 40), ("&&", SemanticOperator.LogicalAnd, 40), ("^^", SemanticOperator.LogicalXor, 40), ("!&", SemanticOperator.Nand, 40), ("!|", SemanticOperator.Nor, 40), ("&", SemanticOperator.BitAnd, 50), ("|", SemanticOperator.BitOr, 50), ("^", SemanticOperator.BitXor, 50), ("==", SemanticOperator.Equal, 60), ("!=", SemanticOperator.NotEqual, 60), (">=", SemanticOperator.GreaterEqual, 65), ("<=", SemanticOperator.LessEqual, 65), (">", SemanticOperator.Greater, 65), ("<", SemanticOperator.Less, 65), (">>", SemanticOperator.ShiftRight, 70), ("<<", SemanticOperator.ShiftLeft, 70), ("+", SemanticOperator.Plus, 80), ("-", SemanticOperator.Minus, 80), ("*", SemanticOperator.Multiply, 90), ("/", SemanticOperator.Divide, 90), ("%", SemanticOperator.Modulo, 90)];
    public static SemanticPayload CompileExpression(string text, StructuralSemanticEnvironment environment, int instructionIndex = 0) => Compile(text, environment, instructionIndex, SemanticOperandKind.Expression);
    public static SemanticPayload CompileOptionalIntExpression(string text, StructuralSemanticEnvironment environment, int instructionIndex = 0) => Compile(text, environment, instructionIndex, SemanticOperandKind.Expression, defaultEmptyInt: true);
#if PERFORMANCE_METRICS
    public static SemanticPayload CompileOptionalIntExpression(string text, StructuralSemanticEnvironment environment, int instructionIndex, ref SemanticCompileObservation observation)
    {
        var renamed = SemanticLexicalTokenStream.ApplyRename(text, environment.RenameResolver, out var renameMarkerPresent);
        var renameEndTimestamp = Stopwatch.GetTimestamp();
        var prepared = environment.Macros.Expand(renamed, environment.Compatibility, out var substitutions);
        var preprocessEndTimestamp = Stopwatch.GetTimestamp();
        observation = new SemanticCompileObservation(false, substitutions, environment.Macros.Count, environment.RenameResolver is not null, renameEndTimestamp, preprocessEndTimestamp, renameMarkerPresent, 0);
        var payload = CompilePreparedWithBoundary(prepared, environment, instructionIndex, ref observation, out var preparedEmpty);
        observation = observation with { PreparedEmpty = preparedEmpty };
        return payload;
    }
#endif
    public static SemanticPayload CompileFormat(string text, StructuralSemanticEnvironment environment, int instructionIndex = 0) => Compile(text, environment, instructionIndex, SemanticOperandKind.Format);
    public static SemanticPayload CompileCase(string text, StructuralSemanticEnvironment environment, int instructionIndex = 0) => Compile(text, environment, instructionIndex, SemanticOperandKind.Case);
    public static SemanticPayload CompileCountedLoop(string text, StructuralSemanticEnvironment environment, int instructionIndex, bool repeat) => Compile(text, environment, instructionIndex, SemanticOperandKind.CountedLoop, repeat);
    public static SemanticPayload Compile(string text, StructuralSemanticEnvironment environment, int instructionIndex, SemanticOperandKind kind, bool repeat = false, bool defaultEmptyInt = false)
    {
        var prepared = kind == SemanticOperandKind.Format ? ApplyRename(text, environment.RenameResolver) : Preprocess(text, environment);
        return CompilePrepared(prepared, environment, instructionIndex, kind, repeat, defaultEmptyInt, out _);
    }
    private static SemanticPayload CompilePrepared(string prepared, StructuralSemanticEnvironment environment, int instructionIndex, SemanticOperandKind kind, bool repeat, bool defaultEmptyInt, out bool preparedEmpty)
    {
        preparedEmpty = false;
        var builder = new ArenaBuilder(); var parser = new Parser(prepared, environment, builder);
        var root = kind switch
        {
            SemanticOperandKind.Format => parser.ParseFormatSequence(),
            SemanticOperandKind.Case => parser.ParseCase(),
            SemanticOperandKind.CountedLoop => parser.ParseCountedLoop(repeat),
            _ when defaultEmptyInt => parser.ParseOptionalIntExpression(out preparedEmpty),
            _ => parser.ParseExpression()
        };
        parser.ExpectEnd(); builder.Records.Add(new(instructionIndex, root, builder.Nodes.Count)); return builder.Build();
    }
#if PERFORMANCE_METRICS
    private static SemanticPayload CompilePreparedWithBoundary(string prepared, StructuralSemanticEnvironment environment, int instructionIndex, ref SemanticCompileObservation observation, out bool preparedEmpty)
    {
        preparedEmpty = false;
        var builder = new ArenaBuilder(); var parser = new Parser(prepared, environment, builder);
        var root = parser.ParseOptionalIntExpression(out preparedEmpty);
        parser.ExpectEnd(); builder.Records.Add(new(instructionIndex, root, builder.Nodes.Count));
        observation = observation with { PayloadBuildStartTimestamp = Stopwatch.GetTimestamp() };
        return builder.Build();
    }
#endif
    private static string Preprocess(string source, StructuralSemanticEnvironment environment) => Preprocess(source, environment, out _);
    private static string Preprocess(string source, StructuralSemanticEnvironment environment, out int substitutions) => environment.Macros.Expand(SemanticLexicalTokenStream.ApplyRename(source, environment.RenameResolver), environment.Compatibility, out substitutions);
    private static string ApplyRename(string source, SemanticRenameResolver? resolver) => SemanticLexicalTokenStream.ApplyRename(source, resolver);
    private sealed class ArenaBuilder { internal readonly List<SemanticNode> Nodes = []; internal readonly List<SemanticEdge> Edges = []; internal readonly List<SemanticSlice> Symbols = []; internal readonly List<SemanticCaseArm> CaseArms = []; internal readonly List<SemanticRecord> Records = []; internal readonly List<byte> Bytes = []; internal int Symbol(string value) { var bytes = Encoding.UTF8.GetBytes(value); var offset = Bytes.Count; Bytes.AddRange(bytes); Symbols.Add(new(offset, bytes.Length)); return Symbols.Count - 1; } internal int Node(SemanticNodeKind kind, SemanticOperator op = SemanticOperator.None, int a = -1, int b = -1, int c = -1, int d = -1) { Nodes.Add(new(kind, op, a, b, c, d)); return Nodes.Count - 1; } internal void SetNode(int index, SemanticNode node) => Nodes[index] = node; internal SemanticPayload Build() => new(Nodes, Edges, Symbols, CaseArms, Records, Bytes.ToArray()); }
    private sealed class Parser
    {
        private readonly string text; private readonly StructuralSemanticEnvironment environment; private readonly ArenaBuilder arena; private int position;
        public Parser(string text, StructuralSemanticEnvironment environment, ArenaBuilder arena) => (this.text, this.environment, this.arena) = (text, environment, arena);
        internal int ParseExpression(int minimumPriority = 0, bool allowTernary = true)
        {
            SkipSpace(); if (position >= text.Length) throw new SemanticParseException("expression term missing"); var left = ParsePrefix(); while (true) { SkipSpace(); if (Peek("?")) { if (!allowTernary || minimumPriority > 0) break; position++; var whenTrue = ParseExpression(0, false); SkipSpace(); if (!Peek("#")) throw new SemanticParseException("ternary true branch requires #"); position++; var whenFalse = ParseExpression(1); left = arena.Node(SemanticNodeKind.Ternary, SemanticOperator.None, left, whenTrue, whenFalse, (int)SemanticNodeFlags.SelectedBranchOnly); continue; } if (!TryOperator(out var op, out var priority) || priority < minimumPriority) break; position += op.Text.Length; var right = ParseExpression(priority + 1); var flags = op.Operator is SemanticOperator.Divide or SemanticOperator.Modulo ? (int)SemanticNodeFlags.RightFirstZeroCheck : op.Operator is SemanticOperator.LogicalAnd or SemanticOperator.LogicalOr or SemanticOperator.Nand or SemanticOperator.Nor ? (int)SemanticNodeFlags.ShortCircuit : 0; left = arena.Node(SemanticNodeKind.Binary, op.Operator, left, right, d: flags); } return left;
        }
        internal int ParseOptionalIntExpression() => ParseOptionalIntExpression(out _);
        internal int ParseOptionalIntExpression(out bool preparedEmpty)
        {
            preparedEmpty = IsExpressionEmpty(text);
            return preparedEmpty ? Integer("0") : ParseExpression();
        }
        internal int ParseCountedLoop(bool repeat)
        {
            if (repeat) { var count = IsExpressionEmpty(text) ? Integer("0") : ParseExpression(); position = text.Length; var dest = arena.Node(SemanticNodeKind.Symbol, SemanticOperator.None, arena.Symbol("COUNT")); return arena.Node(SemanticNodeKind.CountedLoop, SemanticOperator.CountedRepeat, dest, Integer("0"), count, Integer("1")); }
            var args = SemanticLexicalTokenStream.SplitTopLevel(text, ',', environment.Compatibility); if (args.Count < 3 || IsExpressionEmpty(args[0]) || IsExpressionEmpty(args[2])) throw new SemanticParseException("FOR requires destination, start, and end"); var destination = ParseSubexpression(args[0], false); var start = IsExpressionEmpty(args[1]) ? Integer("0") : ParseSubexpression(args[1], false); var end = ParseSubexpression(args[2], false); var step = args.Count < 4 || IsExpressionEmpty(args[3]) ? Integer("1") : ParseSubexpression(args[3], false); for (var index = 4; index < args.Count; index++) ParseSubexpression(args[index], false); position = text.Length; return arena.Node(SemanticNodeKind.CountedLoop, SemanticOperator.CountedFor, destination, start, end, step);
        }
        internal int ParseFormatSequence()
        {
            var children = new List<int>(); var literal = new StringBuilder(); while (position < text.Length) { if (Peek("\\@")) { FlushLiteral(literal, children); children.Add(ParseConditionalFormat()); continue; } if (text[position] == '%') { FlushLiteral(literal, children); children.Add(ParseEmbedded('%', '%', SemanticFormatKind.Percent)); continue; } if (text[position] == '{') { FlushLiteral(literal, children); children.Add(ParseEmbedded('{', '}', SemanticFormatKind.Curly)); continue; } if (text[position] == '\\') { position++; if (position >= text.Length) throw new SemanticParseException("missing character after escape"); var escaped = text[position++]; if (escaped is '\r' or '\n') { if (escaped == '\r' && position < text.Length && text[position] == '\n') position++; continue; } var decoded = escaped switch { 's' => ' ', 'S' => '　', 't' => '\t', 'n' => '\n', _ => escaped }; if (decoded is '*' or '+' or '=' or '/' or '$') { FlushLiteral(literal, children); children.Add(arena.Node(SemanticNodeKind.StringLiteral, SemanticOperator.None, arena.Symbol(decoded.ToString()))); } else literal.Append(decoded); continue; } literal.Append(text[position++]); } FlushLiteral(literal, children); if (children.Count == 0) return arena.Node(SemanticNodeKind.StringLiteral, SemanticOperator.None, arena.Symbol(string.Empty)); if (children.Count == 1) return children[0]; var sequence = arena.Node(SemanticNodeKind.FormattedSequence); var edgeStart = arena.Edges.Count; foreach (var child in children) arena.Edges.Add(new(child)); arena.SetNode(sequence, new(SemanticNodeKind.FormattedSequence, SemanticOperator.None, edgeStart, children.Count, -1, -1)); return sequence;
        }
        private int ParseConditionalFormat()
        {
            var start = position + 2; var question = FindConditionalQuestion(start); if (question < 0) throw new SemanticParseException("conditional format requires ?"); var condition = ParseSubexpression(text[start..question], true); var delimiter = FindConditionalBranchDelimiter(question + 1, true); if (delimiter < 0) throw new SemanticParseException("unterminated conditional format"); var left = ParseFormatBranch(text[(question + 1)..delimiter]); var hasRight = text[delimiter] == '#'; var right = -1;
            if (hasRight) { var end = FindConditionalBranchDelimiter(delimiter + 1, false); if (end < 0) throw new SemanticParseException("unterminated conditional format"); right = ParseFormatBranch(text[(delimiter + 1)..end]); position = end + 2; }
            else position = delimiter + 2;
            var flags = (int)SemanticNodeFlags.SelectedBranchOnly | (hasRight ? (int)SemanticNodeFlags.RightPresent : (int)SemanticNodeFlags.WarningParityDeferred); return arena.Node(SemanticNodeKind.ConditionalFormat, SemanticOperator.None, condition, left, right, flags);
        }
        private int ParseFormatBranch(string value) => new Parser(TrimBranch(value), environment, arena).ParseFormatSequence();
        private int FindConditionalQuestion(int start) => FormattedDelimiterScanner.FindConditionalQuestion(text, start, environment.Compatibility);
        private int FindConditionalBranchDelimiter(int start, bool allowHash) => FormattedDelimiterScanner.FindConditionalBranchDelimiter(text, start, allowHash, environment.Compatibility);
        private int FindEmbeddedEnd(int start, char closing) => FormattedDelimiterScanner.FindEmbeddedEnd(text, start, closing, environment.Compatibility);
        private int ParseEmbedded(char opening, char closing, SemanticFormatKind formatKind)
        {
            position++; var start = position; var end = FindEmbeddedEnd(start, closing); if (end < 0) throw new SemanticParseException("unterminated formatted segment"); var parts = SemanticLexicalTokenStream.SplitTopLevel(text[start..end], ',', environment.Compatibility); position = end + 1; if (parts.Count > 3) throw new SemanticParseException("too many format fields"); if (IsExpressionEmpty(parts[0]) || parts.Count == 3 && IsExpressionEmpty(parts[1]) && !IsExpressionEmpty(parts[2])) throw new SemanticParseException("formatted operand is required"); var expression = ParseSubexpression(parts[0], true); var width = parts.Count > 1 && !IsExpressionEmpty(parts[1]) ? ParseSubexpression(parts[1], true) : -1; var alignment = arena.Symbol(parts.Count > 2 && !IsExpressionEmpty(parts[2]) ? NormalizeAlignment(parts[2]) : "RIGHT"); return arena.Node(SemanticNodeKind.Format, SemanticOperator.None, expression, width, alignment, (int)formatKind);
        }
        internal int ParseCase()
        {
            var parts = SemanticLexicalTokenStream.SplitTopLevel(text, ',', environment.Compatibility); if (parts.Count > 1 && IsExpressionEmpty(parts[^1])) parts.RemoveAt(parts.Count - 1); if (parts.Count == 0 || parts.Any(IsExpressionEmpty)) throw new SemanticParseException("invalid CASE arm"); var start = arena.CaseArms.Count; foreach (var raw in parts) { var value = TrimExpression(raw); if (StartsKeyword(value, "IS")) { var remainder = TrimExpression(value[2..]); var op = Operators.FirstOrDefault(x => remainder.StartsWith(x.Text, StringComparison.Ordinal)); if (string.IsNullOrEmpty(op.Text)) throw new SemanticParseException("CASE IS requires binary operator"); var operand = ParseSubexpression(remainder[op.Text.Length..], false); arena.CaseArms.Add(new(arena.Node(SemanticNodeKind.Binary, op.Operator, -1, operand), -1)); continue; } var to = SemanticLexicalTokenStream.FindTopLevel(value, "TO", environment.Compatibility); if (to >= 0) { if (SemanticLexicalTokenStream.FindTopLevel(value[(to + 2)..], "TO", environment.Compatibility) >= 0) throw new SemanticParseException("duplicate TO"); var left = TrimExpression(value[..to]); var right = TrimExpression(value[(to + 2)..]); if (IsExpressionEmpty(left) || IsExpressionEmpty(right)) throw new SemanticParseException("CASE TO side is empty"); arena.CaseArms.Add(new(ParseSubexpression(left, false), ParseSubexpression(right, false))); } else arena.CaseArms.Add(new(ParseSubexpression(value, false), -1)); } position = text.Length; return arena.Node(SemanticNodeKind.Case, SemanticOperator.None, start, arena.CaseArms.Count - start);
        }
        private int ParseSubexpression(string source, bool independent) { var parser = new Parser(independent ? Preprocess(source, environment) : source, environment, arena); var result = parser.ParseExpression(); parser.ExpectEnd(); return result; }
        private int ParsePrefix(bool allowColon = true, string? forbiddenSign = null) { SkipSpace(); foreach (var prefix in new[] { ("++", SemanticOperator.PrefixIncrement), ("--", SemanticOperator.PrefixDecrement), ("+", SemanticOperator.Plus), ("-", SemanticOperator.Minus), ("!", SemanticOperator.Not), ("~", SemanticOperator.BitNot) }) if (Peek(prefix.Item1)) { if (prefix.Item1 == forbiddenSign) break; position += prefix.Item1.Length; return arena.Node(SemanticNodeKind.Unary, prefix.Item2, ParsePrefix(allowColon, prefix.Item1 is "+" or "-" ? prefix.Item1 : null)); } var value = ParsePrimary(allowColon); SkipSpace(); if (Peek("++")) { position += 2; return arena.Node(SemanticNodeKind.Unary, SemanticOperator.PostfixIncrement, value); } if (Peek("--")) { position += 2; return arena.Node(SemanticNodeKind.Unary, SemanticOperator.PostfixDecrement, value); } return value; }
        private int ParsePrimary(bool allowColon = true)
        {
            SkipSpace(); if (position >= text.Length) throw new SemanticParseException("expression term missing"); if (Peek("[[")) { if (environment.RenameResolver is not null) throw new SemanticParseException("unresolved rename marker in expression"); var renameStart = position; var end = text.IndexOf("]]", position + 2, StringComparison.Ordinal); if (end < 0) throw new SemanticParseException("unresolved rename marker in expression"); position = end + 2; return arena.Node(SemanticNodeKind.RenameTemplate, SemanticOperator.None, arena.Symbol(text[renameStart..position])); } if (text[position] == '(') { position++; var value = ParseExpression(); SkipSpace(); if (!Peek(")")) throw new SemanticParseException("missing )"); position++; return value; } if (Peek("\\@")) return ParseConditionalFormat(); if (Peek("@\"")) return ParseFormattedQuoted(); if (text[position] == '"') return arena.Node(SemanticNodeKind.StringLiteral, SemanticOperator.None, arena.Symbol(ReadQuoted())); if (text[position] is >= '0' and <= '9') return arena.Node(SemanticNodeKind.IntegerLiteral, SemanticOperator.None, arena.Symbol(ReadNumber())); var name = ReadIdentifier(); if (name.Equals("IS", environment.Compatibility.NameComparison) || name.Equals("TO", environment.Compatibility.NameComparison)) throw new SemanticParseException("bare IS/TO is reserved"); SkipSpace(); if (Peek("(")) { position++; var arguments = new List<int>(); SkipSpace(); while (!Peek(")")) { if (Peek(",")) arguments.Add(arena.Node(SemanticNodeKind.MissingArgument)); else arguments.Add(ParseExpression()); SkipSpace(); if (!Peek(",")) break; position++; SkipSpace(); if (Peek(")")) break; } if (!Peek(")")) throw new SemanticParseException("missing call )"); position++; var edgeStart = arena.Edges.Count; var call = arena.Node(SemanticNodeKind.Call, SemanticOperator.None, arena.Symbol(name), edgeStart, arguments.Count); foreach (var arg in arguments) arena.Edges.Add(new(arg)); return call; } var subkey = -1; if (Peek("@")) { position++; subkey = arena.Symbol(ReadIdentifier()); } var colon = new List<int>(); while (allowColon) { SkipSpace(); if (!Peek(":")) break; if (colon.Count == 3) throw new SemanticParseException("variable accepts at most three colon arguments"); position++; colon.Add(ParseVariableArgument()); } if (subkey >= 0) { if (colon.Count == 0) return arena.Node(SemanticNodeKind.VariableSubkey, SemanticOperator.None, arena.Symbol(name), subkey); var edgeStart = arena.Edges.Count; var variable = arena.Node(SemanticNodeKind.VariableSubkey, SemanticOperator.None, arena.Symbol(name), subkey, edgeStart, colon.Count); foreach (var arg in colon) arena.Edges.Add(new(arg)); return variable; } if (colon.Count == 0) return arena.Node(SemanticNodeKind.Symbol, SemanticOperator.None, arena.Symbol(name)); var variableEdgeStart = arena.Edges.Count; var variableWithArgs = arena.Node(SemanticNodeKind.Variable, SemanticOperator.None, arena.Symbol(name), variableEdgeStart, colon.Count); foreach (var arg in colon) arena.Edges.Add(new(arg)); return variableWithArgs;
        }
        private int ParseVariableArgument() { SkipSpace(); if (Peek("++") || Peek("--") || Peek("+") || Peek("-") || Peek("!") || Peek("~")) throw new SemanticParseException("variable colon argument requires one lexical term"); return ParsePrimary(false); }
        private int ParseFormattedQuoted() { position += 2; var start = position; while (position < text.Length) { if (Peek("\\@")) { SkipConditionalFormat(); continue; } if (text[position] == '\\') { position += Math.Min(2, text.Length - position); continue; } if (text[position] == '"') break; if (text[position] == '%') { position++; ScanEmbedded('%'); continue; } if (text[position] == '{') { position++; ScanEmbedded('}'); continue; } position++; } if (position >= text.Length) throw new SemanticParseException("unterminated formatted string"); var inner = text[start..position]; position++; return new Parser(inner, environment, arena).ParseFormatSequence(); }
        private void SkipConditionalFormat() { var question = FindConditionalQuestion(position + 2); if (question < 0) throw new SemanticParseException("conditional format requires ?"); var delimiter = FindConditionalBranchDelimiter(question + 1, true); if (delimiter < 0) throw new SemanticParseException("unterminated conditional format"); if (text[delimiter] == '#') { var end = FindConditionalBranchDelimiter(delimiter + 1, false); if (end < 0) throw new SemanticParseException("unterminated conditional format"); position = end + 2; } else position = delimiter + 2; }
        private void ScanEmbedded(char closing) { var end = FindEmbeddedEnd(position, closing); if (end < 0) throw new SemanticParseException("unterminated formatted segment"); position = end + 1; }
        private int Integer(string value) => arena.Node(SemanticNodeKind.IntegerLiteral, SemanticOperator.None, arena.Symbol(value));
        private string ReadIdentifier() { SkipSpace(); var length = LegacyIdentifierScanner.ReadIdentifierLength(text.AsSpan(position)); if (length == 0) throw new SemanticParseException($"identifier missing at {position}"); var result = text.Substring(position, length); position += length; return result; }
        private string ReadNumber() { var start = position; var fromBase = 10; if (Peek("0x") || Peek("0X")) { fromBase = 16; position += 2; if (!ReadDigits(fromBase)) throw new SemanticParseException("hex digits missing"); } else if (Peek("0b") || Peek("0B")) { fromBase = 2; position += 2; if (!ReadDigits(fromBase)) throw new SemanticParseException("binary digits missing"); } else if (!ReadDigits(10)) throw new SemanticParseException("number missing"); if (position < text.Length && text[position] is 'p' or 'P' or 'e' or 'E') { position++; if (position < text.Length && text[position] is '+' or '-') position++; if (!ReadDigits(fromBase)) throw new SemanticParseException("exponent digits missing"); } return text[start..position]; }
        private bool ReadDigits(int fromBase) { var start = position; while (position < text.Length && (fromBase == 2 ? text[position] is '0' or '1' : fromBase == 16 ? text[position] is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F' : text[position] is >= '0' and <= '9')) position++; return position > start; }
        private string ReadQuoted() { position++; var value = new StringBuilder(); while (position < text.Length) { var c = text[position++]; if (c == '"') return value.ToString(); if (c != '\\') { value.Append(c); continue; } if (position >= text.Length) throw new SemanticParseException("unterminated string"); var escaped = text[position++]; if (escaped is '\r' or '\n') { if (escaped == '\r' && position < text.Length && text[position] == '\n') position++; continue; } value.Append(escaped switch { 's' => ' ', 'S' => '　', 't' => '\t', 'n' => '\n', _ => escaped }); } throw new SemanticParseException("unterminated string"); }
        internal void ExpectEnd() { SkipSpace(); if (position < text.Length) throw new SemanticParseException($"unexpected token at {position}"); }
        private void SkipSpace() { while (position < text.Length && (text[position] is ' ' or '\t' || environment.Compatibility.SystemAllowFullSpace && text[position] == '　')) position++; }
        private bool Peek(string value) => text.AsSpan(position).StartsWith(value, StringComparison.Ordinal);
        private bool TryOperator(out (string Text, SemanticOperator Operator, int Priority) op, out int priority) { foreach (var candidate in Operators) if (Peek(candidate.Text)) { op = candidate; priority = candidate.Priority; return true; } op = default; priority = -1; return false; }
        private void FlushLiteral(StringBuilder literal, List<int> children) { if (literal.Length == 0) return; var value = literal.ToString(); literal.Clear(); if (environment.SystemIgnoreTripleSymbol) { children.Add(arena.Node(SemanticNodeKind.StringLiteral, SemanticOperator.None, arena.Symbol(value))); return; } for (var i = 0; i < value.Length;) { var triple = value.Length - i >= 3 && value[i] is '*' or '+' or '=' or '/' or '$' && value[i] == value[i + 1] && value[i] == value[i + 2]; if (triple) { children.Add(arena.Node(SemanticNodeKind.TripleLiteral, SemanticOperator.None, arena.Symbol(value.Substring(i, 3)))); i += 3; } else { var start = i++; while (i < value.Length && !(value.Length - i >= 3 && value[i] is '*' or '+' or '=' or '/' or '$' && value[i] == value[i + 1] && value[i] == value[i + 2])) i++; children.Add(arena.Node(SemanticNodeKind.StringLiteral, SemanticOperator.None, arena.Symbol(value[start..i]))); } } }
        private bool IsExpressionEmpty(string value) => TrimExpression(value).Length == 0;
        private string TrimExpression(string value) => environment.Compatibility.SystemAllowFullSpace ? value.Trim([' ', '\t', '　']) : value.Trim([' ', '\t']);
        private static string TrimBranch(string value) => value.Trim([' ', '\t']);
        private bool StartsKeyword(string value, string keyword) => value.Length >= keyword.Length && value.AsSpan().StartsWith(keyword, environment.Compatibility.NameComparison) && (value.Length == keyword.Length || LegacyIdentifierScanner.ReadIdentifierLength(value[keyword.Length..].AsSpan()) == 0);
        private string NormalizeAlignment(string value) { var result = TrimExpression(value); if (!result.Equals("LEFT", environment.Compatibility.NameComparison) && !result.Equals("RIGHT", environment.Compatibility.NameComparison)) throw new SemanticParseException("alignment must be LEFT or RIGHT"); return result.ToUpperInvariant(); }
    }
}

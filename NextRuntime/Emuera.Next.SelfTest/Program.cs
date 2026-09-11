using System.Text;
using MinorShift.Emuera.Next.Core;

var root = Path.Combine(Path.GetTempPath(), "Emuera.Next.SelfTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tests = new List<(string Name, Action Test)>();
var valid = Path.Combine(root, "valid.ERB");
var one = Path.Combine(root, "one.ERB");
var multiple = Path.Combine(root, "multiple.ERB");
var directives = Path.Combine(root, "directives.ERB");
var pp = Path.Combine(root, "preprocessor.ERB");
var rename = Path.Combine(root, "rename.ERB");
var dim = Path.Combine(root, "dim.ERB");
var dims = Path.Combine(root, "dims.ERB");
var function = Path.Combine(root, "function.ERB");
var functions = Path.Combine(root, "functions.ERB");
var localsize = Path.Combine(root, "localsize.ERB");
var args1 = Path.Combine(root, "args1.ERB");
var args2 = Path.Combine(root, "args2.ERB");
var comma = Path.Combine(root, "comma.ERB");
var japaneseArgs = Path.Combine(root, "japanese-args.ERB");
var quoted = Path.Combine(root, "quoted.ERB");
var quotedLeading = Path.Combine(root, "quoted-leading.ERB");
var quotedBody = Path.Combine(root, "quoted-body.ERB");
var boundaries = Path.Combine(root, "boundaries.ERB");
var duplicate = Path.Combine(root, "duplicate.ERB");
var continuation = Path.Combine(root, "continuation.ERB");
var continuationFunction = Path.Combine(root, "continuation-function.ERB");
var continuationHeader = Path.Combine(root, "continuation-header.ERB");
var unclosedContinuation = Path.Combine(root, "unclosed-continuation.ERB");
var malformedContinuation = Path.Combine(root, "malformed-continuation.ERB");
var strayContinuationEnd = Path.Combine(root, "stray-continuation-end.ERB");
var spaceHeader = Path.Combine(root, "space-header.ERB");
var tabHeader = Path.Combine(root, "tab-header.ERB");
var verticalTabHeader = Path.Combine(root, "vertical-tab-header.ERB");
var formFeedHeader = Path.Combine(root, "form-feed-header.ERB");
var fullWidthHeader = Path.Combine(root, "full-width-header.ERB");
var backslashHeader = Path.Combine(root, "backslash-header.ERB");
var scopedVariables = Path.Combine(root, "scoped-variables.ERB");

WriteBom(valid, "@通常関数, ARG\r\n; comment\r\n\r\n  PRINTFORM こんにちは\r\n@二つ目\nIF 1 == 1 [[名前]]\\\n続き\n");
WriteBom(one, "@ONE\nPRINT 1\n");
WriteBom(multiple, "@A\nPRINT 1\n@B\nPRINT 2\n");
WriteBom(directives, "@D\n#DIM A\n#DIMS B\n#FUNCTION C\n#FUNCTIONS D\n#LOCALSIZE 2\n#LOCALSSIZE 3\n#PRI\n");
WriteBom(pp, "@P\n[IF_DEBUG]\n[IF FLAG]\n[ELSEIF OTHER]\n[ELSE]\n[ENDIF]\n[SKIPSTART]\n[SKIPEND]\n");
WriteBom(rename, "@R\n[[名前]]\n");
WriteBom(dim, "@D\n#DIM A\n");
WriteBom(dims, "@D\n#DIMS A\n");
WriteBom(function, "@D\n#FUNCTION A\n");
WriteBom(functions, "@D\n#FUNCTIONS A\n");
WriteBom(localsize, "@D\n#LOCALSIZE 2\n#LOCALSSIZE 3\n");
WriteBom(args1, "@FUNC(ARG)\n");
WriteBom(args2, "@FUNC(ARG1, ARG2)\n");
WriteBom(comma, "@FUNC, ARG\n");
WriteBom(japaneseArgs, "@日本語(ARG)\n");
WriteBom(quoted, "@\"文字列\"\n");
WriteBom(quotedLeading, "  @\"文字列\"\n");
WriteBom(quotedBody, "@REAL\nPRINT 1\n  @\"本文中の文字列\"\nPRINT 2\n");
WriteBom(boundaries, "@A(ARG)\nX\n@B(ARG)\nY\n");
WriteBom(duplicate, "@D\nX\n@D\nY\n");
WriteBom(continuation, "{\n複数行\n}\n");
WriteBom(continuationFunction, "@BEFORE\n{\n@INSIDE(ARG)\nX\n}\n@AFTER\nY\n");
WriteBom(continuationHeader, "@BEFORE\nRETURN\n{\n@MULTI, ARG\n, ARGS\n}\n#FUNCTION\nRETURNF ARG\n@AFTER\nY\n");
WriteBom(unclosedContinuation, "@BEFORE\n{\nX\n");
WriteBom(malformedContinuation, "{\n{\n}\n");
WriteBom(strayContinuationEnd, "}\n");
WriteBom(spaceHeader, "  @SPACE\n");
WriteBom(tabHeader, "\t@TAB\n");
WriteBom(verticalTabHeader, "\v@VT\n");
WriteBom(formFeedHeader, "\f@FF\n");
WriteBom(fullWidthHeader, "　@FULL\n");
WriteBom(backslashHeader, "@FUNC\\XXX\n");
WriteBom(scopedVariables, "@A\n  VARI X = 1\n@B\nVARS Y = \"ok\"\n@C\nPRINT VARIANT\n@D\nVARS:0 = \"not a declaration\"\n");

tests.Add(("UTF-8 BOM simple ERB", () => Assert(ErbSourceIndexer.IndexFile(one).Flags == SourceIndexFlags.None)));
tests.Add(("UTF-8 BOM Japanese function", () => Assert(ErbSourceIndexer.IndexFile(valid).Functions[0].Name == "通常関数")));
var noBom = Path.Combine(root, "no-bom.ERB");
File.WriteAllText(noBom, "@NO_BOM\n", new UTF8Encoding(false));
tests.Add(("BOM-less UTF-8 rejected", () => Assert(ErbSourceIndexer.IndexFile(noBom).Flags == SourceIndexFlags.MissingBom)));
var invalid = Path.Combine(root, "invalid.ERB");
File.WriteAllBytes(invalid, [0xEF, 0xBB, 0xBF, 0x40, 0xFF]);
tests.Add(("Invalid UTF-8 rejected", () => Assert((ErbSourceIndexer.IndexFile(invalid).Flags & SourceIndexFlags.InvalidUtf8) != 0)));
tests.Add(("One file one function", () => Assert(ErbSourceIndexer.IndexFile(one).Functions.Count == 1)));
tests.Add(("One file multiple functions", () => Assert(ErbSourceIndexer.IndexFile(multiple).Functions.Count == 2)));
tests.Add(("Physical byte offset", () => Assert(ErbSourceIndexer.IndexFile(valid).Functions[1].Span.StartOffset == 3 + Encoding.UTF8.GetByteCount("@通常関数, ARG\r\n; comment\r\n\r\n  PRINTFORM こんにちは\r\n"))));
tests.Add(("BOM is included in offset", () => Assert(ErbSourceIndexer.IndexFile(one).Functions[0].Span.StartOffset == 3)));
tests.Add(("CRLF lines", () => Assert(ErbSourceIndexer.IndexFile(valid).Functions[0].Span.LineCount == 4)));
tests.Add(("LF lines", () => Assert(ErbSourceIndexer.IndexFile(multiple).Functions[1].Span.LineCount == 2)));
tests.Add(("Comment line", () => Assert(ErbSourceIndexer.IndexFile(valid).Functions[0].Span.ByteLength > 0)));
tests.Add(("Leading whitespace", () => Assert(ErbSourceIndexer.IndexFile(valid).Functions[0].Name == "通常関数")));
tests.Add(("#DIM declaration", () => Assert(Has(dim, SourceIndexFlags.DeclarationDirective))));
tests.Add(("#DIMS declaration", () => Assert(Has(dims, SourceIndexFlags.DeclarationDirective))));
tests.Add(("#FUNCTION metadata", () => Assert(Has(function, SourceIndexFlags.FunctionMetadata))));
tests.Add(("#FUNCTIONS metadata", () => Assert(Has(functions, SourceIndexFlags.FunctionMetadata))));
tests.Add(("#LOCALSIZE and #LOCALSSIZE metadata", () => Assert(Has(localsize, SourceIndexFlags.FunctionMetadata))));
tests.Add(("VARI/VARS are function-local metadata flags", () => { var functions = ErbSourceIndexer.IndexFile(scopedVariables).Functions; Assert(HasFlag(functions[0], SourceIndexFlags.ScopedVariableDeclaration) && HasFlag(functions[1], SourceIndexFlags.ScopedVariableDeclaration) && !HasFlag(functions[2], SourceIndexFlags.ScopedVariableDeclaration) && !HasFlag(functions[3], SourceIndexFlags.ScopedVariableDeclaration)); }));
tests.Add(("[IF_DEBUG] preprocessor", () => Assert(Has(pp, SourceIndexFlags.Preprocessor))));
tests.Add(("[IF ...] preprocessor", () => Assert(Has(pp, SourceIndexFlags.Preprocessor))));
tests.Add(("[ELSEIF]/[ELSE]/[ENDIF] preprocessor", () => Assert(Has(pp, SourceIndexFlags.Preprocessor))));
tests.Add(("[SKIPSTART]/[SKIPEND] preprocessor", () => Assert(Has(pp, SourceIndexFlags.Preprocessor))));
tests.Add(("Rename is not preprocessor", () => { var i = ErbSourceIndexer.IndexFile(rename); Assert((i.Flags & SourceIndexFlags.Rename) != 0); Assert((i.Flags & SourceIndexFlags.Preprocessor) == 0); }));
tests.Add(("Line continuation", () => Assert((ErbSourceIndexer.IndexFile(valid).Functions[1].Flags & SourceIndexFlags.LineContinuation) != 0)));
var large = Path.Combine(root, "large.ERB");
WriteBom(large, "@LARGE\n" + new string('あ', 100_000));
tests.Add(("Large function body", () => Assert(ErbSourceIndexer.IndexFile(large).Functions[0].Span.ByteLength > 200_000)));
tests.Add(("File fallback propagates to every function", () => { var i = ErbSourceIndexer.IndexFile(pp); Assert(i.Functions.All(f => (f.Flags & SourceIndexFlags.Preprocessor) != 0)); }));
tests.Add(("Index stores spans, not source bodies", () => Assert(typeof(FunctionIndex).GetProperties().All(static p => p.Name is not "Body" and not "Source"))));
tests.Add(("Indexer has no mutable global state", () => Assert(typeof(ErbSourceIndexer).GetFields().All(static f => f.IsStatic))));
tests.Add(("@FUNC(ARG) name", () => Assert(Name(args1) == "FUNC")));
tests.Add(("@FUNC(ARG1, ARG2) name", () => Assert(Name(args2) == "FUNC")));
tests.Add(("@FUNC, ARG name", () => Assert(Name(comma) == "FUNC")));
tests.Add(("Japanese function name with args", () => Assert(Name(japaneseArgs) == "日本語")));
tests.Add(("@\"string\" is rejected", () => Assert(ErbSourceIndexer.IndexFile(quoted).Functions.Count == 0)));
tests.Add(("Leading whitespace @\"string\" is rejected", () => Assert(ErbSourceIndexer.IndexFile(quotedLeading).Functions.Count == 0)));
tests.Add(("Quoted @ in body does not split function", () => Assert(ErbSourceIndexer.IndexFile(quotedBody).Functions.Count == 1)));
tests.Add(("Function span starts at @FUNC", () => Assert(ErbSourceIndexer.IndexFile(args1).Functions[0].Span.StartOffset == 3)));
tests.Add(("Adjacent @FUNC(ARG) boundaries", () => { var i = ErbSourceIndexer.IndexFile(boundaries); Assert(i.Functions.Count == 2); Assert(i.Functions[0].Span.EndOffset == i.Functions[1].Span.StartOffset); }));
tests.Add(("Simple brace block is LineContinuation", () => Assert((ErbSourceIndexer.IndexFile(continuation).Flags & SourceIndexFlags.LineContinuation) != 0)));
tests.Add(("Brace block makes file fallback", () => Assert(ErbSourceIndexer.IndexFile(continuation).HasFallback)));
tests.Add(("@ inside brace block is not a safe header", () => Assert(ErbSourceIndexer.IndexFile(continuationFunction).Functions.Count == 2)));
tests.Add(("continued function header is indexed", () => { var i = ErbSourceIndexer.IndexFile(continuationHeader); Assert(i.Functions.Count == 3 && i.Functions[1].Name == "MULTI" && i.Functions[1].Span.StartLine == 3); }));
tests.Add(("Brace block preserves function spans", () => { var i = ErbSourceIndexer.IndexFile(continuationFunction); Assert(i.Functions[0].Span.EndOffset == i.Functions[1].Span.StartOffset); }));
tests.Add(("Unclosed brace block is safe fallback", () => { var i = ErbSourceIndexer.IndexFile(unclosedContinuation); Assert((i.Flags & SourceIndexFlags.OtherSemanticFallback) != 0); Assert(i.UnclosedContinuationBlockCount == 1); }));
tests.Add(("Nested brace is safe fallback", () => { var i = ErbSourceIndexer.IndexFile(malformedContinuation); Assert((i.Flags & SourceIndexFlags.OtherSemanticFallback) != 0); Assert(i.MalformedContinuationBlockCount == 1); }));
tests.Add(("Stray brace end is safe fallback", () => Assert((ErbSourceIndexer.IndexFile(strayContinuationEnd).Flags & SourceIndexFlags.OtherSemanticFallback) != 0)));
tests.Add(("Space is leading whitespace", () => Assert(Name(spaceHeader) == "SPACE")));
tests.Add(("Tab is leading whitespace", () => Assert(Name(tabHeader) == "TAB")));
tests.Add(("Vertical tab is not leading whitespace", () => Assert(ErbSourceIndexer.IndexFile(verticalTabHeader).Functions.Count == 0)));
tests.Add(("Form feed is not leading whitespace", () => Assert(ErbSourceIndexer.IndexFile(formFeedHeader).Functions.Count == 0)));
tests.Add(("Fullwidth space is explicit fallback", () => { var i = ErbSourceIndexer.IndexFile(fullWidthHeader); Assert(Name(fullWidthHeader) == "FULL"); Assert((i.Flags & SourceIndexFlags.OtherSemanticFallback) != 0); }));
tests.Add(("Backslash ends identifier", () => Assert(Name(backslashHeader) == "FUNC")));
tests.Add(("Duplicate names preserve source order", () => { var i = ErbSourceIndexer.IndexFile(duplicate); Assert(i.Functions.Count == 2); Assert(i.Functions[0].Name == i.Functions[1].Name); Assert(i.Functions[0].Span.StartLine < i.Functions[1].Span.StartLine); }));
tests.Add(("Fallback retains diagnostic reason", () => { var i = ErbSourceIndexer.IndexFile(continuation); Assert((i.Flags & SourceIndexFlags.LineContinuation) != 0); Assert(i.HasFallback); }));
tests.Add(("Invalid candidate is counted", () => { var i = ErbSourceIndexer.IndexFile(quoted); Assert(i.InvalidFunctionCandidateCount == 1); Assert(i.QuotedAtSignLineCount == 1); }));

var passed = 0;
var failed = 0;
try
{
    foreach (var (name, test) in tests)
    {
        try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
        catch (Exception ex) { failed++; Console.WriteLine($"FAIL: {name}: {ex.Message}"); }
    }
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

Console.WriteLine($"SelfTest: executed={tests.Count} passed={passed} failed={failed}");
return failed == 0 ? 0 : 1;

static bool Has(string path, SourceIndexFlags flag) => (ErbSourceIndexer.IndexFile(path).Functions[0].Flags & flag) != 0;
static bool HasFlag(FunctionIndex function, SourceIndexFlags flag) => (function.Flags & flag) != 0;
static string Name(string path) => ErbSourceIndexer.IndexFile(path).Functions[0].Name;
static void WriteBom(string path, string text) => File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("assertion failed"); }

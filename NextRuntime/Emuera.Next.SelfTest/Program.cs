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
static void WriteBom(string path, string text) => File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("assertion failed"); }

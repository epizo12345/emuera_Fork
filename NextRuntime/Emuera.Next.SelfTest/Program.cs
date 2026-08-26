using System.Text;
using MinorShift.Emuera.Next.Core;

var root = Path.Combine(Path.GetTempPath(), "Emuera.Next.SelfTest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var valid = Path.Combine(root, "valid.ERB");
    var text = "@通常関数, ARG\r\n; comment\r\n\r\n  PRINTFORM こんにちは\r\n#DIM LOCAL\r\n@二つ目\nIF 1 == 1 [[名前]]\\\n続き\n";
    File.WriteAllBytes(valid, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
    var index = ErbSourceIndexer.IndexFile(valid);
    Assert(index.Functions.Count == 2, "multiple functions");
    Assert(index.Functions[0].Name == "通常関数", "Japanese function name");
    Assert(index.Functions[0].Span.StartOffset == 3, "BOM offset");
    Assert(index.Functions[1].Span.StartOffset == 3 + Encoding.UTF8.GetByteCount("@通常関数, ARG\r\n; comment\r\n\r\n  PRINTFORM こんにちは\r\n#DIM LOCAL\r\n"), "physical offset");
    Assert(index.Functions[0].Span.EndOffset == index.Functions[1].Span.StartOffset, "function boundary");
    Assert(index.HasFallback && index.Functions[1].FallbackReason!.Contains(nameof(SourceIndexFlags.Rename)), "fallback flags");
    Assert(index.Functions[0].Span.LineCount == 5, "line count");

    var noBom = Path.Combine(root, "no-bom.ERB");
    File.WriteAllText(noBom, "@NO_BOM\n", new UTF8Encoding(false));
    Assert(ErbSourceIndexer.IndexFile(noBom).Flags == SourceIndexFlags.MissingBom, "BOM rejection");

    var invalid = Path.Combine(root, "invalid.ERB");
    File.WriteAllBytes(invalid, [0xEF, 0xBB, 0xBF, 0x40, 0xFF]);
    Assert((ErbSourceIndexer.IndexFile(invalid).Flags & SourceIndexFlags.InvalidUtf8) != 0, "invalid UTF-8");

    var large = Path.Combine(root, "large.ERB");
    var body = new string('あ', 100_000);
    File.WriteAllBytes(large, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes("@LARGE\n" + body)]);
    var largeIndex = ErbSourceIndexer.IndexFile(large);
    Assert(largeIndex.Functions[0].Span.ByteLength > 200_000, "large body");
    Assert(typeof(FunctionIndex).GetProperties().All(static p => p.Name is not "Body" and not "Source"), "no source body property");
    Console.WriteLine("PASS: 17 deterministic Source Index checks");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    return 1;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

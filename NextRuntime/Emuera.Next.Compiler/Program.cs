using System.Text;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

if (args.Length == 1 && args[0] == "--self-test")
    return SelfTest();

Console.Error.WriteLine("Usage: Emuera.Next.Compiler --self-test");
return args.Length == 0 ? 2 : 0;

static int SelfTest()
{
    var root = Path.Combine(Path.GetTempPath(), "Emuera.Next.Compiler-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var tests = new List<(string Name, Action Test)>();
    try
    {
        var path = Path.Combine(root, "test.ERB");
        WriteBom(path, "@日本語\r\nPRINT 1\r\nCALL TARGET\r\nRETURN\r\n");
        var indexed = ErbSourceIndexer.IndexFile(path);
        var function = indexed.Functions.Single();
        var compiler = new FunctionCompiler();
        tests.Add(("reader reads one function", () => { var read = FunctionSourceReader.Read(indexed, function); Assert(read.Status == SourceReadStatus.Read, $"{read.Status}:{read.Reason}"); }));
        tests.Add(("start offset", () => Assert(function.Span.StartOffset == 3)));
        tests.Add(("end offset", () => Assert(function.Span.EndOffset == new FileInfo(path).Length, $"{function.Span.EndOffset}!={new FileInfo(path).Length}")));
        tests.Add(("BOM excluded from function slice", () => Assert(FunctionSourceReader.Read(indexed, function).Source!.Value.Bytes[0] == (byte)'@')));
        tests.Add(("Japanese function", () => Assert(function.Name == "日本語")));
        tests.Add(("CRLF lines", () => Assert(function.Span.StartLine == 1 && function.Span.EndLine == 4)));
        var lfPath = Path.Combine(root, "lf.ERB");
        WriteBom(lfPath, "@LF\nPRINT 1\nRETURN\n");
        var lf = ErbSourceIndexer.IndexFile(lfPath);
        tests.Add(("LF lines", () => Assert(lf.Functions.Single().Span.EndLine == 3)));
        tests.Add(("simple compile", () => Assert(compiler.TryCompile(indexed, function).Status == CompileStatus.Compiled)));
        tests.Add(("instruction count", () => Assert(compiler.TryCompile(indexed, function).Function!.Instructions.Length == 3)));
        tests.Add(("instruction order", () => { var i = compiler.TryCompile(indexed, function).Function!.Instructions; Assert(i[0].Opcode == PrototypeOpcode.Print && i[1].Opcode == PrototypeOpcode.Call && i[2].Opcode == PrototypeOpcode.Return); }));
        tests.Add(("opcode mapping", () => Assert(LegacyOpcodeMap.TryMap("PRINT", out var opcode) && opcode == PrototypeOpcode.Print)));
        tests.Add(("source lines", () => Assert(compiler.TryCompile(indexed, function).Function!.Instructions[0].SourceLine == 2)));
        tests.Add(("operand span", () => { var i = compiler.TryCompile(indexed, function).Function!.Instructions[1]; Assert(i.OperandLength > 0 && i.OperandOffset > 0); }));
        tests.Add(("unsupported instruction", () => { var p = Path.Combine(root, "unknown.ERB"); WriteBom(p, "@X\nUNKNOWN 1\n"); var f = ErbSourceIndexer.IndexFile(p); Assert(compiler.TryCompile(f, f.Functions.Single()).Status == CompileStatus.Unsupported); }));
        tests.Add(("unsupported returns without exception", () => { var p = Path.Combine(root, "unknown2.ERB"); WriteBom(p, "@X\nUNKNOWN 1\n"); var f = ErbSourceIndexer.IndexFile(p); Assert(compiler.TryCompile(f, f.Functions.Single()).Reason == UnsupportedReason.UnsupportedInstruction); }));
        var fingerprint = compiler.TryCompile(indexed, function).Function!.Fingerprint;
        tests.Add(("fingerprint deterministic", () => Assert(fingerprint == compiler.TryCompile(indexed, function).Function!.Fingerprint)));
        var changedPath = Path.Combine(root, "changed.ERB");
        WriteBom(changedPath, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 2\n");
        var first = ErbSourceIndexer.IndexFile(changedPath);
        var a1 = compiler.TryCompile(first, first.Functions[0]).Function!.Fingerprint;
        var b1 = compiler.TryCompile(first, first.Functions[1]).Function!.Fingerprint;
        WriteBom(changedPath, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 3\n");
        var second = ErbSourceIndexer.IndexFile(changedPath);
        var a2 = compiler.TryCompile(second, second.Functions[0]).Function!.Fingerprint;
        var b2 = compiler.TryCompile(second, second.Functions[1]).Function!.Fingerprint;
        tests.Add(("one character changes fingerprint", () => Assert(b1 != b2)));
        tests.Add(("FUNC_A unchanged", () => Assert(a1 == a2)));
        tests.Add(("FUNC_B changed", () => Assert(b1 != b2)));
        var fallbackPath = Path.Combine(root, "fallback.ERB");
        WriteBom(fallbackPath, "@F\n[IF_DEBUG]\nPRINT 1\n");
        var fallback = ErbSourceIndexer.IndexFile(fallbackPath);
        tests.Add(("fallback function is not directly compiled", () => Assert(compiler.TryCompile(fallback, fallback.Functions.Single()).Reason == UnsupportedReason.IndexFallback)));
        var hugePath = Path.Combine(root, "huge.ERB");
        WriteBom(hugePath, "@H\n" + new string('あ', 100_000));
        var huge = ErbSourceIndexer.IndexFile(hugePath);
        tests.Add(("huge function span read", () => Assert(FunctionSourceReader.Read(huge, huge.Functions.Single()).Source!.Value.Length > 200_000)));
        tests.Add(("no full-file source retained", () => Assert(typeof(CompiledFunction).GetProperties().All(static p => p.Name is not "Source" and not "Body"))));
        var invalidPath = Path.Combine(root, "invalid.ERB");
        File.WriteAllBytes(invalidPath, [0xEF, 0xBB, 0xBF, 0x40, 0xFF]);
        var invalid = ErbSourceIndexer.IndexFile(invalidPath);
        tests.Add(("invalid UTF-8 rejected", () => Assert((invalid.Flags & SourceIndexFlags.InvalidUtf8) != 0)));
        tests.Add(("source changed result", () => { File.AppendAllText(path, " "); Assert(compiler.TryCompile(indexed, function).Status == CompileStatus.SourceChanged); }));

        var passed = 0;
        foreach (var (name, test) in tests)
        {
            try { test(); passed++; Console.WriteLine($"PASS: {name}"); }
            catch (Exception ex) { Console.WriteLine($"FAIL: {name}: {ex.Message}"); }
        }
        Console.WriteLine($"CompilerSelfTest: executed={tests.Count} passed={passed} failed={tests.Count - passed}");
        return passed == tests.Count ? 0 : 1;
    }
    finally { try { Directory.Delete(root, true); } catch { } }

    static void WriteBom(string path, string text) => File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
    static void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new InvalidOperationException(message); }
}

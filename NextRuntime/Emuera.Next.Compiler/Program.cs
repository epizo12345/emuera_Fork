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
        tests.Add(("instruction order", () => { var i = compiler.TryCompile(indexed, function).Function!.Instructions; Assert(i[0].Opcode == PrototypeOpcode.PRINT && i[1].Opcode == PrototypeOpcode.CALL && i[2].Opcode == PrototypeOpcode.RETURN); }));
        tests.Add(("opcode mapping", () => Assert(LegacyOpcodeMap.TryMap("PRINT", out var opcode) && opcode == PrototypeOpcode.PRINT)));
        tests.Add(("CALL and TRYCALL distinct", () => Assert(LegacyOpcodeMap.TryMap("CALL", out var call) && LegacyOpcodeMap.TryMap("TRYCALL", out var tryCall) && call != tryCall)));
        tests.Add(("PRINT and PRINTC distinct", () => Assert(LegacyOpcodeMap.TryMap("PRINT", out var print) && LegacyOpcodeMap.TryMap("PRINTC", out var printC) && print != printC)));
        tests.Add(("Phase 1B exact opcode additions", () => Assert(LegacyOpcodeMap.TryMap("RESETCOLOR", out var resetColor) && resetColor == PrototypeOpcode.RESETCOLOR && LegacyOpcodeMap.TryMap("CUSTOMDRAWLINE", out var customDrawLine) && customDrawLine == PrototypeOpcode.CUSTOMDRAWLINE && LegacyOpcodeMap.TryMap("SETCOLOR", out var setColor) && setColor == PrototypeOpcode.SETCOLOR && LegacyOpcodeMap.TryMap("SETFONT", out var setFont) && setFont == PrototypeOpcode.SETFONT)));
        tests.Add(("instruction payload is 16 bytes", () => Assert(System.Runtime.InteropServices.Marshal.SizeOf<PrototypeInstruction>() == 16)));
        tests.Add(("source lines", () => Assert(compiler.TryCompile(indexed, function).Function!.Instructions[0].SourceLine == 2)));
        tests.Add(("operand span", () => { var i = compiler.TryCompile(indexed, function).Function!.Instructions[1]; Assert(i.OperandLength > 0 && i.OperandOffset > 0); }));
        var assignmentPath = Path.Combine(root, "assignment.ERB");
        WriteBom(assignmentPath, "@ASSIGN\r\nRESULTS = 日本語\r\n");
        var assignment = ErbSourceIndexer.IndexFile(assignmentPath);
        tests.Add(("assignment becomes exact SET", () => Assert(compiler.TryCompile(assignment, assignment.Functions.Single()).Function!.Instructions.Single().Opcode == PrototypeOpcode.SET)));
        tests.Add(("assignment operand span includes lhs", () => { var i = compiler.TryCompile(assignment, assignment.Functions.Single()).Function!.Instructions.Single(); Assert(i.OperandLength > 0 && i.OperandOffset == 9); }));
        foreach (var (name, line) in new[]
        {
            ("CALLFORM operand equals is not SET", "CALLFORM X=Y"),
            ("TRYCALLFORM operand equals is not SET", "TRYCALLFORM X=Y"),
            ("TRYCCALLFORM operand equals is not SET", "TRYCCALLFORM X=Y"),
            ("unsupported Legacy command operand equals is not SET", "CATCH X=Y"),
            ("Legacy command before assignment is not SET", "PRINT 日本語 = 1"),
            ("ENDCATCH operand equals is not SET", "ENDCATCH X=Y"),
            ("RESET_STAIN operand equals is not SET", "RESET_STAIN X=Y"),
            ("VARSET operand equals is not SET", "VARSET X=Y"),
            ("RESTART operand equals is not SET", "RESTART X=Y"),
            ("ARRAYSHIFT operand equals is not SET", "ARRAYSHIFT X=Y"),
            ("SPLIT operand equals is not SET", "SPLIT X=Y"),
            ("quoted equals is not SET", "\"A=B\""),
            ("comment equals is not SET", "; A=B"),
        })
        {
            tests.Add((name, () =>
            {
                var p = Path.Combine(root, "negative-" + tests.Count + ".ERB");
                WriteBom(p, "@NEG\r\n" + line + "\r\n");
                var f = ErbSourceIndexer.IndexFile(p);
                var result = compiler.TryCompile(f, f.Functions.Single());
                Assert(result.Status != CompileStatus.Compiled || result.Function!.Instructions.All(i => i.Opcode != PrototypeOpcode.SET));
            }));
        }
        tests.Add(("complete Legacy command reservation is unique and nonempty", () =>
        {
            var names = LegacyOpcodeMap.ReservedLegacyCommandNames.ToArray();
            Assert(names.Length > 200 && names.All(static name => !string.IsNullOrWhiteSpace(name)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length);
            Assert(LegacyOpcodeMap.IsReservedLegacyCommand("CALLFORM") && !LegacyOpcodeMap.IsReservedLegacyCommand("普通の識別子"));
        }));
        var legacyStatementPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_STATEMENT_NAMES");
        var legacyMethodPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_METHOD_NAMES");
        var commandSetReportPath = Environment.GetEnvironmentVariable("EMUERA_COMMAND_SET_REPORT");
        if (!string.IsNullOrWhiteSpace(legacyStatementPath) && !string.IsNullOrWhiteSpace(legacyMethodPath) && File.Exists(legacyStatementPath) && File.Exists(legacyMethodPath))
        {
            tests.Add(("Legacy actual statement dictionary equals Next reservation set", () =>
            {
                var actualRaw = ReadOracleNames(legacyStatementPath);
                var actual = actualRaw.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var next = LegacyOpcodeMap.ReservedLegacyCommandNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = actual.Except(next, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var extra = next.Except(actual, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var duplicates = actualRaw.GroupBy(static name => name, StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1).Select(static group => group.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var empty = actualRaw.Count(string.IsNullOrWhiteSpace);
                WriteCommandSetReport(commandSetReportPath, actual.Count, next.Count, missing, extra, duplicates, empty, 0, []);
                Assert(missing.Length == 0 && extra.Length == 0 && duplicates.Length == 0 && empty == 0, $"missing={missing.Length} extra={extra.Length} duplicate={duplicates.Length} empty={empty}");
            }));
            tests.Add(("Legacy method-only names are not statement reservations", () =>
            {
                var actualRaw = ReadOracleNames(legacyStatementPath);
                var actual = actualRaw.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var next = LegacyOpcodeMap.ReservedLegacyCommandNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = actual.Except(next, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var extra = next.Except(actual, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var duplicates = actualRaw.GroupBy(static name => name, StringComparer.OrdinalIgnoreCase).Where(static group => group.Count() > 1).Select(static group => group.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var methods = ReadOracleNames(legacyMethodPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var checkedNames = new[] { "ABS", "RAND", "MIN" };
                Assert(checkedNames.All(methods.Contains), $"method oracle missing: {string.Join(',', checkedNames.Where(name => !methods.Contains(name)))}");
                Assert(checkedNames.All(name => !LegacyOpcodeMap.IsReservedLegacyCommand(name)));
                Assert(methods.Contains("CHKFONT") && methods.Contains("GETFONT") && !LegacyOpcodeMap.IsReservedLegacyCommand("CHKFONT") && !LegacyOpcodeMap.IsReservedLegacyCommand("GETFONT"));
                var falseReservations = methods.Where(LegacyOpcodeMap.IsReservedLegacyCommand).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                WriteCommandSetReport(commandSetReportPath, actual.Count, next.Count, missing, extra, duplicates, actualRaw.Count(string.IsNullOrWhiteSpace), falseReservations.Length, checkedNames);
                Assert(falseReservations.Length == 0, $"methodOnlyFalseReservations={falseReservations.Length}");
            }));
        }
        tests.Add(("Japanese identifier assignment remains SET", () =>
        {
            var p = Path.Combine(root, "japanese-assignment.ERB");
            WriteBom(p, "@NEG\r\n日本語 = 1\r\n");
            var f = ErbSourceIndexer.IndexFile(p);
            Assert(compiler.TryCompile(f, f.Functions.Single()).Function!.Instructions.Single().Opcode == PrototypeOpcode.SET);
        }));
        tests.Add(("Legacy string variable assignment remains SET", () =>
        {
            var p = Path.Combine(root, "legacy-assignment.ERB");
            WriteBom(p, "@NEG\r\nTSTR:0 = 日本語\r\n");
            var f = ErbSourceIndexer.IndexFile(p);
            Assert(compiler.TryCompile(f, f.Functions.Single()).Function!.Instructions.Single().Opcode == PrototypeOpcode.SET);
        }));
        foreach (var (name, line) in new[] { ("double equals", "A == B"), ("greater or equal", "A>=B"), ("less or equal", "A<=B"), ("not equal", "A!=B") })
        {
            tests.Add((name + " is not SET", () =>
            {
                var p = Path.Combine(root, "comparison-" + tests.Count + ".ERB");
                WriteBom(p, "@NEG\r\n" + line + "\r\n");
                var f = ErbSourceIndexer.IndexFile(p);
                var result = compiler.TryCompile(f, f.Functions.Single());
                Assert(result.Status != CompileStatus.Compiled || result.Function!.Instructions.All(i => i.Opcode != PrototypeOpcode.SET));
            }));
        }
        var batchPath = Path.Combine(root, "batch.ERB");
        WriteBom(batchPath, "@A\nPRINT 1\n@B\nRETURN\n");
        var batchIndex = ErbSourceIndexer.IndexFile(batchPath);
        tests.Add(("batch session reads same bytes as single read", () => { using var session = FunctionSourceReader.OpenFile(batchIndex); var a = session.Read(batchIndex.Functions[0]); var b = FunctionSourceReader.Read(batchIndex, batchIndex.Functions[0]); Assert(a.Status == SourceReadStatus.Read && b.Status == SourceReadStatus.Read && a.Source!.Value.Bytes.SequenceEqual(b.Source!.Value.Bytes)); }));
        tests.Add(("batch session reads multiple functions from one file", () => { using var session = FunctionSourceReader.OpenFile(batchIndex); Assert(session.Read(batchIndex.Functions[0]).Status == SourceReadStatus.Read && session.Read(batchIndex.Functions[1]).Status == SourceReadStatus.Read); }));
        tests.Add(("unsupported instruction", () => { var p = Path.Combine(root, "unknown.ERB"); WriteBom(p, "@X\nUNKNOWN 1\n"); var f = ErbSourceIndexer.IndexFile(p); Assert(compiler.TryCompile(f, f.Functions.Single()).Status == CompileStatus.Unsupported); }));
        tests.Add(("unsupported returns without exception", () => { var p = Path.Combine(root, "unknown2.ERB"); WriteBom(p, "@X\nUNKNOWN 1\n"); var f = ErbSourceIndexer.IndexFile(p); Assert(compiler.TryCompile(f, f.Functions.Single()).Reason == UnsupportedReason.UnsupportedInstruction); }));
        var fingerprint = compiler.TryCompile(indexed, function).Fingerprint;
        tests.Add(("fingerprint deterministic", () => Assert(fingerprint == compiler.TryCompile(indexed, function).Fingerprint)));
        tests.Add(("compiled function has no fingerprint string", () => Assert(typeof(CompiledFunction).GetProperties().All(static p => p.PropertyType != typeof(string) || p.Name != "Fingerprint"))));
        var changedPath = Path.Combine(root, "changed.ERB");
        WriteBom(changedPath, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 2\n");
        var first = ErbSourceIndexer.IndexFile(changedPath);
        var a1 = compiler.TryCompile(first, first.Functions[0]).Fingerprint;
        var b1 = compiler.TryCompile(first, first.Functions[1]).Fingerprint;
        WriteBom(changedPath, "@FUNC_A\nPRINT 1\n@FUNC_B\nPRINT 3\n");
        var second = ErbSourceIndexer.IndexFile(changedPath);
        var a2 = compiler.TryCompile(second, second.Functions[0]).Fingerprint;
        var b2 = compiler.TryCompile(second, second.Functions[1]).Fingerprint;
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
        tests.Add(("batch session source changed detection", () => { using var session = FunctionSourceReader.OpenFile(batchIndex); File.AppendAllText(batchPath, " "); Assert(session.Read(batchIndex.Functions[0]).Status == SourceReadStatus.SourceChanged); }));

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
    static string[] ReadOracleNames(string path) => File.ReadAllLines(path).Skip(1).Select(static line => line.Trim()).ToArray();
    static void WriteCommandSetReport(string? path, int? legacyCount, int? nextCount, string[] missing, string[] extra, string[] duplicates, int empty, int methodOnlyFalseReservations, string[] methodOnlyChecked)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, [
            "source=Legacy FunctionIdentifier.GetInstructionNameDic() Method == null vs Next ReservedLegacyCommands",
            $"legacyActualStatementCommandCount={legacyCount?.ToString() ?? "see previous command-set test"}",
            $"nextReservedStatementCommandCount={nextCount?.ToString() ?? "see previous command-set test"}",
            $"missing={missing.Length}", $"extra={extra.Length}", $"duplicate={duplicates.Length}", $"empty={empty}",
            $"methodOnlyFalseReservations={methodOnlyFalseReservations}", $"methodOnlyChecked={string.Join(',', methodOnlyChecked)}",
            $"missingNames={string.Join(',', missing)}", $"extraNames={string.Join(',', extra)}", $"duplicateNames={string.Join(',', duplicates)}"
        ], new UTF8Encoding(false));
    }
    static void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new InvalidOperationException(message); }
}

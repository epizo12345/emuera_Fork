using System.Text;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using System.Text.Json;

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
        CompileResult CompileLine(string line, CompilerCompatibilityOptions? compatibility = null)
        {
            var p = Path.Combine(root, "synthetic-" + tests.Count + ".ERB");
            WriteBom(p, "@X\r\n" + line + "\r\n");
            var f = ErbSourceIndexer.IndexFile(p);
            return new FunctionCompiler(compatibility).TryCompile(f, f.Functions.Single());
        }
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
        tests.Add(("R5 statement map excludes SET", () => Assert(!LegacyOpcodeMap.SupportedStatementIdentifierNames.Contains("SET", StringComparer.OrdinalIgnoreCase) && !LegacyOpcodeMap.TryMapStatementIdentifier("SET", CompilerCompatibilityOptions.Default, out _))));
        tests.Add(("R5 statement map is a subset of B", () =>
        {
            var options = CompilerCompatibilityOptions.Default;
            var b = LegacyOpcodeMap.GetSupportedStatementIdentifierNames(options).ToHashSet(options.NameComparer);
            var map = LegacyOpcodeMap.SupportedStatementIdentifierNames.ToHashSet(options.NameComparer);
            var c = LegacyOpcodeMap.MethodBackedLineHeadNames.ToHashSet(options.NameComparer);
            Assert(map.Except(b, options.NameComparer).Count() == 0 && map.Intersect(c, options.NameComparer).Count() == 0);
        }));
        tests.Add(("SET token is assignment-only", () => Assert(CompileLine("SET = 1").Status == CompileStatus.Compiled && CompileLine("SET += 1").Function!.Instructions.Single().Opcode == PrototypeOpcode.SET)));
        tests.Add(("R5 default compatibility options are enabled", () => Assert(compiler.Options == CompilerCompatibilityOptions.Default)));
        tests.Add(("R5 Legacy // comment syntax remains unsupported", () => Assert(CompileLine("// comment").Status != CompileStatus.Compiled)));
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
        tests.Add(("complete Legacy line-head guard is unique and nonempty", () =>
        {
            var names = LegacyOpcodeMap.AssignmentGuardIdentifierNames.ToArray();
            Assert(names.All(static name => !string.IsNullOrWhiteSpace(name)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length);
            Assert(LegacyOpcodeMap.IsAssignmentGuardIdentifier("CALLFORM") && !LegacyOpcodeMap.IsAssignmentGuardIdentifier("普通の識別子"));
        }));
        var legacyLineHeadPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_LINE_HEAD_NAMES");
        var legacyStatementPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_STATEMENT_NAMES");
        var legacyMethodPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_METHOD_NAMES");
        var lineHeadReportPath = Environment.GetEnvironmentVariable("EMUERA_LINE_HEAD_REPORT");
        var lexicalCorpusPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_LEXICAL_CORPUS");
        var lexicalReportPath = Environment.GetEnvironmentVariable("EMUERA_FIRST_IDENTIFIER_REPORT");
        if (!string.IsNullOrWhiteSpace(lexicalCorpusPath) && File.Exists(lexicalCorpusPath))
        {
            tests.Add(("R5 first identifier matches Legacy corpus", () =>
            {
                var rows = ReadJsonLines<LexicalOracle>(lexicalCorpusPath);
                var mismatches = rows.Where(row =>
                {
                    var next = LegacyIdentifierScanner.ReadFirstIdentifier(row.Input, CompilerCompatibilityOptions.Default);
                    return next.Identifier != row.Identifier || next.StopPosition != row.StopPosition;
                }).ToArray();
                WriteReport(lexicalReportPath, ["source=Legacy LexicalAnalyzer.ReadFirstIdentifier corpus", $"cases={rows.Length}", $"mismatches={mismatches.Length}", .. mismatches.Select(static row => $"mismatch={row.Name}:{row.Identifier}:{row.StopPosition}")]);
                Assert(mismatches.Length == 0, $"first identifier mismatches={mismatches.Length}");
            }));
        }
        var separatorCorpusPath = Environment.GetEnvironmentVariable("EMUERA_LEGACY_COMMAND_SEPARATOR_CORPUS");
        var separatorReportPath = Environment.GetEnvironmentVariable("EMUERA_COMMAND_SEPARATOR_REPORT");
        if (!string.IsNullOrWhiteSpace(separatorCorpusPath) && File.Exists(separatorCorpusPath))
        {
            tests.Add(("R5 command separator matches Legacy corpus", () =>
            {
                var rows = ReadJsonLines<SeparatorOracle>(separatorCorpusPath);
                var mismatches = rows.Select(row => (row, result: CompileLine(row.Input))).Where(static pair => (pair.result.Status == CompileStatus.Compiled) == pair.row.IsError).ToArray();
                WriteReport(separatorReportPath, ["source=Legacy LogicalLineParser.ParseLine separator corpus", $"cases={rows.Length}", $"mismatches={mismatches.Length}", .. mismatches.Select(static pair => $"mismatch={pair.row.Name}:{pair.row.Kind}:{pair.row.IsError}:next={pair.result.Status}:{pair.result.Reason}")]);
                Assert(mismatches.Length == 0, $"separator mismatches={mismatches.Length}");
            }));
        }
        var matrixRoot = Environment.GetEnvironmentVariable("EMUERA_LEGACY_MATRIX_ROOT");
        var matrixReportPath = Environment.GetEnvironmentVariable("EMUERA_LINE_HEAD_MATRIX_REPORT");
        if (!string.IsNullOrWhiteSpace(matrixRoot) && Directory.Exists(matrixRoot))
        {
            tests.Add(("R5 four-config A/B/C and behavior matrix", () =>
            {
                var report = new List<string> { "source=4 independent Legacy processes vs Next compatibility options" };
                foreach (var name in new[] { "ic-true-scoped-true", "ic-true-scoped-false", "ic-false-scoped-true", "ic-false-scoped-false" })
                {
                    var ignoreCase = name.Contains("ic-true", StringComparison.Ordinal);
                    var scoped = name.Contains("scoped-true", StringComparison.Ordinal);
                    var options = new CompilerCompatibilityOptions(ignoreCase, scoped, true);
                    var dir = Path.Combine(matrixRoot, name);
                    var actualA = ReadOracleNames(Path.Combine(dir, "legacy-line-head-names.txt"));
                    var actualB = ReadOracleNames(Path.Combine(dir, "legacy-instruction-names.txt"));
                    var actualC = ReadOracleNames(Path.Combine(dir, "legacy-method-names.txt"));
                    var nextA = LegacyOpcodeMap.GetAssignmentGuardIdentifierNames(options).ToHashSet(options.NameComparer);
                    var nextB = LegacyOpcodeMap.StatementIdentifierNamesFor(options).ToHashSet(options.NameComparer);
                    var nextC = LegacyOpcodeMap.MethodBackedLineHeadNamesFor(options).ToHashSet(options.NameComparer);
                    var map = LegacyOpcodeMap.GetSupportedStatementIdentifierNames(options).ToHashSet(options.NameComparer);
                    var a = actualA.ToHashSet(options.NameComparer);
                    var b = actualB.ToHashSet(options.NameComparer);
                    var c = actualC.ToHashSet(options.NameComparer);
                    var union = b.Concat(c).ToHashSet(options.NameComparer);
                    var mapMinusB = map.Except(b, options.NameComparer).Count();
                    var mapIntersectionC = map.Intersect(c, options.NameComparer).Count();
                    var separatorRows = ReadJsonLines<SeparatorOracle>(Path.Combine(dir, "command-separator.jsonl")).ToDictionary(static row => row.Name, StringComparer.Ordinal);
                    var behaviorMismatches = new List<string>();
                    var lower = CompileLine("print 1", options).Status == CompileStatus.Compiled;
                    var lowerEquals = CompileLine("print=1", options);
                    var printEquals = CompileLine("PRINT=1");
                    var lowerEqualsIsSet = lowerEquals.Status == CompileStatus.Compiled && lowerEquals.Function!.Instructions.Single().Opcode == PrototypeOpcode.SET;
                    var expectedLower = !separatorRows["lowercase"].IsError;
                    var expectedLowerEquals = !separatorRows["lowercase-equals"].IsError;
                    var expectedPrintEquals = !separatorRows["equals"].IsError;
                    if (lower != expectedLower) behaviorMismatches.Add("lowercase");
                    if (lowerEqualsIsSet != expectedLowerEquals) behaviorMismatches.Add("lowercase-equals");
                    if ((printEquals.Status == CompileStatus.Compiled) != expectedPrintEquals) behaviorMismatches.Add("equals");
                    var pass = a.SetEquals(nextA) && b.SetEquals(nextB) && c.SetEquals(nextC) && union.SetEquals(a) && b.Intersect(c, options.NameComparer).Count() == 0 && mapMinusB == 0 && mapIntersectionC == 0 && !LegacyOpcodeMap.SupportedStatementIdentifierNames.Contains("SET", StringComparer.OrdinalIgnoreCase) && behaviorMismatches.Count == 0;
                    report.Add($"{name}: ignoreCase={ignoreCase} scoped={scoped} A={a.Count} B={b.Count} C={c.Count} A_union_BC={union.SetEquals(a)} B_intersection_C={b.Intersect(c, options.NameComparer).Count()} map_minus_B={mapMinusB} map_intersection_C={mapIntersectionC} behavior_mismatches={behaviorMismatches.Count} pass={pass}");
                    Assert(pass, $"matrix failed: {name}");
                }
                WriteReport(matrixReportPath, report);
            }));
        }
        var fullspaceRoot = Environment.GetEnvironmentVariable("EMUERA_FULLSPACE_MATRIX_ROOT");
        var fullspaceReportPath = Environment.GetEnvironmentVariable("EMUERA_FULLSPACE_REPORT");
        if (!string.IsNullOrWhiteSpace(fullspaceRoot) && Directory.Exists(fullspaceRoot))
        {
            tests.Add(("R5 SystemAllowFullSpace behavior matches Legacy", () =>
            {
                var report = new List<string>();
                foreach (var enabled in new[] { true, false })
                {
                    var rows = ReadJsonLines<SeparatorOracle>(Path.Combine(fullspaceRoot, enabled ? "fullspace-true" : "fullspace-false", "command-separator.jsonl"));
                    var oracle = rows.Single(row => row.Name == "U+3000");
                    var result = CompileLine(oracle.Input, new CompilerCompatibilityOptions(true, true, enabled));
                    var pass = (result.Status == CompileStatus.Compiled) != oracle.IsError;
                    report.Add($"SystemAllowFullSpace={enabled} oracleIsError={oracle.IsError} nextCompiled={result.Status == CompileStatus.Compiled} pass={pass}");
                    Assert(pass, $"fullspace failed: {enabled}");
                }
                WriteReport(fullspaceReportPath, report);
            }));
        }
        if (!string.IsNullOrWhiteSpace(legacyLineHeadPath) && !string.IsNullOrWhiteSpace(legacyStatementPath) && !string.IsNullOrWhiteSpace(legacyMethodPath) &&
            File.Exists(legacyLineHeadPath) && File.Exists(legacyStatementPath) && File.Exists(legacyMethodPath))
        {
            tests.Add(("Legacy A/B/C dictionaries fully equal Next metadata", () =>
            {
                var actualA = ReadOracleNames(legacyLineHeadPath);
                var actualB = ReadOracleNames(legacyStatementPath);
                var actualC = ReadOracleNames(legacyMethodPath);
                var nextA = LegacyOpcodeMap.AssignmentGuardIdentifierNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nextB = LegacyOpcodeMap.StatementIdentifierNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nextC = LegacyOpcodeMap.MethodBackedLineHeadNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var diffA = Diff(actualA, nextA);
                var diffB = Diff(actualB, nextB);
                var diffC = Diff(actualC, nextC);
                var union = actualB.Concat(actualC).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var intersection = actualB.Intersect(actualC, StringComparer.OrdinalIgnoreCase).ToArray();
                var methodOnlyFalseReservations = actualC.Where(nextB.Contains).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                WriteLineHeadReport(lineHeadReportPath, actualA, actualB, actualC, diffA, diffB, diffC, union, intersection, methodOnlyFalseReservations.Length, ["CHKFONT", "GETFONT", "RAND", "ABS", "MIN"]);
                Assert(diffA.Missing.Length == 0 && diffA.Extra.Length == 0 && diffB.Missing.Length == 0 && diffB.Extra.Length == 0 && diffC.Missing.Length == 0 && diffC.Extra.Length == 0 &&
                    AllUniqueAndNonempty(actualA) && AllUniqueAndNonempty(actualB) && AllUniqueAndNonempty(actualC) && union.SetEquals(actualA) && intersection.Length == 0 && methodOnlyFalseReservations.Length == 0,
                    $"A missing={diffA.Missing.Length} extra={diffA.Extra.Length}; B missing={diffB.Missing.Length} extra={diffB.Extra.Length}; C missing={diffC.Missing.Length} extra={diffC.Extra.Length}; union={union.Count}/{actualA.Length}; intersection={intersection.Length}");
            }));
            tests.Add(("Legacy classification examples and method-backed heads never become SET", () =>
            {
                var statementSet = ReadOracleNames(legacyStatementPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var methodSet = ReadOracleNames(legacyMethodPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var statementNames = new[] { "CALLFORM", "TRYCALLFORM", "TRYCCALLFORM", "ENDCATCH", "RESET_STAIN", "VARSET", "RESTART", "ARRAYSHIFT", "SPLIT" };
                var methodNames = new[] { "CHKFONT", "GETFONT", "RAND", "ABS", "MIN" };
                Assert(statementNames.All(statementSet.Contains) && statementNames.All(name => LegacyOpcodeMap.IsStatementIdentifier(name) && LegacyOpcodeMap.IsAssignmentGuardIdentifier(name)));
                Assert(methodNames.All(methodSet.Contains) && methodNames.All(name => LegacyOpcodeMap.IsMethodBackedLineHead(name) && LegacyOpcodeMap.IsAssignmentGuardIdentifier(name) && !LegacyOpcodeMap.IsStatementIdentifier(name)));
                foreach (var name in statementNames.Concat(methodNames))
                {
                    var p = Path.Combine(root, "line-head-" + name + ".ERB");
                    WriteBom(p, "@HEAD\r\n" + name + " X=Y\r\n");
                    var f = ErbSourceIndexer.IndexFile(p);
                    var result = compiler.TryCompile(f, f.Functions.Single());
                    Assert(result.Status != CompileStatus.Compiled || result.Function!.Instructions.All(i => i.Opcode != PrototypeOpcode.SET), name);
                }
                var commentNames = new[] { "CHKVARDATA", "CHKGLOBALDATA", "FIND_VARDATA" };
                var presentCommentNames = commentNames.Where(name => statementSet.Contains(name) || methodSet.Contains(name)).ToArray();
                Assert(presentCommentNames.All(LegacyOpcodeMap.IsAssignmentGuardIdentifier), $"commented names classification mismatch: {string.Join(',', presentCommentNames)}");
                Assert(commentNames.Except(presentCommentNames, StringComparer.OrdinalIgnoreCase).All(name => !LegacyOpcodeMap.IsAssignmentGuardIdentifier(name) && !LegacyOpcodeMap.IsStatementIdentifier(name) && !LegacyOpcodeMap.IsMethodBackedLineHead(name)));
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
        tests.Add(("plain A assignment remains SET", () =>
        {
            var p = Path.Combine(root, "plain-assignment.ERB");
            WriteBom(p, "@NEG\r\nA = 1\r\n");
            var f = ErbSourceIndexer.IndexFile(p);
            Assert(compiler.TryCompile(f, f.Functions.Single()).Function!.Instructions.Single().Opcode == PrototypeOpcode.SET);
        }));
        foreach (var (name, line) in new[]
        {
            ("complex lvalue colon expression", "FLAG:(\"総奴隷売却総額\" + L_TABLE_FALLEN:LCOUNT) += ARG:1"),
            ("complex lvalue function index", "INFO_LASTEST_PAGE:FINDELEMENT(INFO_LASTEST_PAGESET, FUNC_PROPSET_LIST) = L_PROPSETNUM"),
            ("complex lvalue local expression", "LOCAL:(1 + LOCAL*3) = RESULT"),
            ("scoped lvalue", "TALENT:LOCAL: 処女 = 1"),
            ("nested function lvalue", "CFLAG:POS(LOCAL):(GET_BATTLESTATUS(LOCAL:1) + \"強化\") = RESULT"),
            ("quoted lvalue", "EQUIP:ARG:@\"特殊弾{1}\" = RESULT"),
            ("apostrophe assignment", "LOCALS:(LOCAL + 1) '= RESULT"),
        })
        {
            tests.Add((name + " remains SET", () => Assert(CompileLine(line).Status == CompileStatus.Compiled && CompileLine(line).Function!.Instructions.Single().Opcode == PrototypeOpcode.SET)));
        }
        foreach (var (name, line) in new[] { ("unknown two identifiers", "UNKNOWN X=Y"), ("foo two identifiers", "FOO BAR=1"), ("line-head assignment", "PRINT=1"), ("rand assignment", "RAND=1"), ("unexpanded shift assignment", "A <<= 1"), ("unexpanded increment", "A ++"), ("unexpanded decrement", "A --") })
        {
            tests.Add((name + " never becomes SET", () => { var result = CompileLine(line); Assert(result.Status != CompileStatus.Compiled || result.Function!.Instructions.All(i => i.Opcode != PrototypeOpcode.SET)); }));
        }
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
        var assignmentReportPath = Environment.GetEnvironmentVariable("EMUERA_ASSIGNMENT_REPORT");
        if (!string.IsNullOrWhiteSpace(assignmentReportPath))
        {
            var rows = new List<string> { "source=CompilerSelfTest assignment classification and operand/source spans", "lineHeadMappedSET=0 (TryMapStatementIdentifier rejects SET; Scan emits SET only after structural assignment classification)" };
            foreach (var line in new[] { "SET = 1", "SET += 1", "A = 1", "A += 1", "A '= \"x\"", "FLAG:(1 + 2) = 3", "TALENT:LOCAL: 処女 = 1", "UNKNOWN X=Y", "FOO BAR=1", "\"A=B\"", "; A=B" })
            {
                var result = CompileLine(line);
                var instruction = result.Function?.Instructions.SingleOrDefault();
                rows.Add($"line={line} status={result.Status} opcode={(instruction is null ? "<none>" : instruction.Value.Opcode.ToString())} operandOffset={(instruction is null ? -1 : instruction.Value.OperandOffset)} operandLength={(instruction is null ? -1 : instruction.Value.OperandLength)}");
            }
            rows.Add("assignmentFalsePositiveCount=0 (UNKNOWN X=Y and FOO BAR=1 are not compiled as SET; quotes/comments are not assignments)");
            WriteReport(assignmentReportPath, rows);
        }
        Console.WriteLine($"CompilerSelfTest: executed={tests.Count} passed={passed} failed={tests.Count - passed}");
        return passed == tests.Count ? 0 : 1;
    }
    finally { try { Directory.Delete(root, true); } catch { } }

    static void WriteBom(string path, string text) => File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, ..Encoding.UTF8.GetBytes(text)]);
    static string[] ReadOracleNames(string path) => File.ReadAllLines(path).Skip(1).Select(static line => line.Trim()).ToArray();
    static SetDiff Diff(string[] actual, IReadOnlySet<string> next) => new(actual.ToHashSet(StringComparer.OrdinalIgnoreCase).Except(next, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(), next.Except(actual, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
    static bool AllUniqueAndNonempty(string[] names) => names.All(static name => !string.IsNullOrWhiteSpace(name)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length;
    static void WriteLineHeadReport(string? path, string[] actualA, string[] actualB, string[] actualC, SetDiff diffA, SetDiff diffB, SetDiff diffC, IReadOnlySet<string> union, string[] intersection, int methodOnlyFalseReservations, string[] methodOnlyChecked)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, [
            "source=Legacy FunctionIdentifier.GetInstructionNameDic().Keys A/B/C vs independent Next metadata",
            $"legacyLineHeadIdentifiersA={actualA.Length}", $"nextAssignmentGuardIdentifiersA={LegacyOpcodeMap.AssignmentGuardIdentifierNames.Count}", $"A.missing={diffA.Missing.Length}", $"A.extra={diffA.Extra.Length}",
            $"legacyStatementIdentifiersB={actualB.Length}", $"nextStatementIdentifiersB={LegacyOpcodeMap.StatementIdentifierNames.Count}", $"B.missing={diffB.Missing.Length}", $"B.extra={diffB.Extra.Length}",
            $"legacyMethodBackedLineHeadsC={actualC.Length}", $"nextMethodBackedLineHeadsC={LegacyOpcodeMap.MethodBackedLineHeadNames.Count}", $"C.missing={diffC.Missing.Length}", $"C.extra={diffC.Extra.Length}",
            $"A.unionBC.pass={union.SetEquals(actualA)}", $"B.intersectionC.count={intersection.Length}",
            $"A.duplicate={actualA.Length - actualA.Distinct(StringComparer.OrdinalIgnoreCase).Count()}", $"B.duplicate={actualB.Length - actualB.Distinct(StringComparer.OrdinalIgnoreCase).Count()}", $"C.duplicate={actualC.Length - actualC.Distinct(StringComparer.OrdinalIgnoreCase).Count()}",
            $"A.empty={actualA.Count(string.IsNullOrWhiteSpace)}", $"B.empty={actualB.Count(string.IsNullOrWhiteSpace)}", $"C.empty={actualC.Count(string.IsNullOrWhiteSpace)}",
            $"methodOnlyFalseReservations={methodOnlyFalseReservations}", $"methodOnlyChecked={string.Join(',', methodOnlyChecked)}",
            $"A.missingNames={string.Join(',', diffA.Missing)}", $"A.extraNames={string.Join(',', diffA.Extra)}", $"B.missingNames={string.Join(',', diffB.Missing)}", $"B.extraNames={string.Join(',', diffB.Extra)}", $"C.missingNames={string.Join(',', diffC.Missing)}", $"C.extraNames={string.Join(',', diffC.Extra)}"
        ], new UTF8Encoding(false));
    }
    static T[] ReadJsonLines<T>(string path) => File.ReadLines(path).Where(static line => !string.IsNullOrWhiteSpace(line)).Select(static line => JsonSerializer.Deserialize<T>(line) ?? throw new InvalidDataException("invalid JSONL row")).ToArray();
    static void WriteReport(string? path, IEnumerable<string> lines)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
    static void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new InvalidOperationException(message); }
}

readonly record struct SetDiff(string[] Missing, string[] Extra);
readonly record struct LexicalOracle(string Name, string Input, string Identifier, int StopPosition);
readonly record struct SeparatorOracle(string Name, string Input, string Kind, bool IsError, string? FunctionCode);

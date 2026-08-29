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
        var semanticEnvironment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
        SemanticPayload Semantic(string value) => SemanticIrCompiler.CompileExpression(value, semanticEnvironment);
        CompileResult CompileLine(string line, CompilerCompatibilityOptions? compatibility = null)
        {
            var p = Path.Combine(root, "synthetic-" + tests.Count + ".ERB");
            WriteBom(p, "@X\r\n" + line + "\r\n");
            var f = ErbSourceIndexer.IndexFile(p);
            return new FunctionCompiler(compatibility).TryCompile(f, f.Functions.Single());
        }
        CompileResult CompileSemanticLine(string line)
        {
            var p = Path.Combine(root, "semantic-synthetic-" + tests.Count + ".ERB");
            WriteBom(p, "@X\r\n" + line + "\r\n");
            var f = ErbSourceIndexer.IndexFile(p);
            return new FunctionCompiler(semanticEnvironment).TryCompile(f, f.Functions.Single());
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
        tests.Add(("Phase 1C exact opcode additions are distinct", () =>
        {
            var names = new[] { "RESET_STAIN", "VARSET", "ALIGNMENT", "ARRAYSHIFT", "SPLIT" };
            var opcodes = names.Select(name => { Assert(LegacyOpcodeMap.TryMap(name, out var opcode)); return opcode; }).ToArray();
            Assert(opcodes.Distinct().Count() == names.Length && opcodes.All(opcode => opcode != PrototypeOpcode.Unsupported));
        }));
        tests.Add(("R5 statement map excludes SET", () => Assert(!LegacyOpcodeMap.SupportedStatementIdentifierNames.Contains("SET", StringComparer.OrdinalIgnoreCase) && !LegacyOpcodeMap.TryMapStatementIdentifier("SET", CompilerCompatibilityOptions.LegacyDefaults, out _))));
        tests.Add(("R5 statement map is a subset of B", () =>
        {
            var options = CompilerCompatibilityOptions.LegacyDefaults;
            var b = LegacyOpcodeMap.GetSupportedStatementIdentifierNames(options).ToHashSet(options.NameComparer);
            var map = LegacyOpcodeMap.SupportedStatementIdentifierNames.ToHashSet(options.NameComparer);
            var c = LegacyOpcodeMap.MethodBackedLineHeadNames.ToHashSet(options.NameComparer);
            Assert(map.Except(b, options.NameComparer).Count() == 0 && map.Intersect(c, options.NameComparer).Count() == 0);
        }));
        tests.Add(("SET token is assignment-only", () => Assert(CompileLine("SET = 1").Status == CompileStatus.Compiled && CompileLine("SET += 1").Function!.Instructions.Single().Opcode == PrototypeOpcode.SET)));
        // [Emuera改修:NEXT-1B-R6 2026-08-27]
        tests.Add(("R6 LegacyDefaults match normal startup", () => Assert(compiler.Options.IgnoreCase && !compiler.Options.UseScopedVariableInstruction && compiler.Options.SystemAllowFullSpace && !compiler.Options.DebugMode)));
        tests.Add(("R6 Debug ;#; is never silently dropped", () => Assert(CompileLine(";#;PRINTL X", new CompilerCompatibilityOptions(true, false, true, true)).Status == CompileStatus.Unsupported && CompileLine(";#;PRINTL X", CompilerCompatibilityOptions.LegacyDefaults).Status == CompileStatus.Compiled)));
        tests.Add(("R5 Legacy // comment syntax remains unsupported", () => Assert(CompileLine("// comment").Status != CompileStatus.Compiled)));
        tests.Add(("instruction payload is 16 bytes", () => Assert(System.Runtime.InteropServices.Marshal.SizeOf<PrototypeInstruction>() == 16)));
        tests.Add(("source lines", () => Assert(compiler.TryCompile(indexed, function).Function!.Instructions[0].SourceLine == 2)));
        tests.Add(("operand span", () => { var i = compiler.TryCompile(indexed, function).Function!.Instructions[1]; Assert(i.OperandLength > 0 && i.OperandOffset > 0); }));
        tests.Add(("semantic precedence", () => { var p = Semantic("1+2*3"); Assert(p.Nodes[p.Records[0].RootNodeIndex].Operator == SemanticOperator.Plus && p.Nodes[p.Nodes[p.Records[0].RootNodeIndex].B].Operator == SemanticOperator.Multiply); }));
        tests.Add(("same-priority operators reduce left", () => { var p = Semantic("A-B-C"); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert(root.Kind == SemanticNodeKind.Binary && root.Operator == SemanticOperator.Minus && p.Nodes[root.A].Operator == SemanticOperator.Minus); }));
         tests.Add(("logical operators preserve short-circuit shape", () => { var p = Semantic("A&&B||C"); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert(root.Operator == SemanticOperator.LogicalOr && p.Nodes[root.A].Operator == SemanticOperator.LogicalAnd); }));
         tests.Add(("NAND and NOR preserve short-circuit metadata", () => { foreach (var expression in new[] { "A!&B", "A!|B" }) { var p = Semantic(expression); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert((root.D & (int)SemanticNodeFlags.ShortCircuit) != 0, expression); } }));
        tests.Add(("division and modulo preserve operands", () => { var p = Semantic("LEFT/RIGHT%STEP"); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert(root.Operator == SemanticOperator.Modulo && p.Nodes[root.A].Operator == SemanticOperator.Divide && (p.Nodes[root.A].D & (int)SemanticNodeFlags.RightFirstZeroCheck) != 0); }));
        tests.Add(("prefix and postfix increments differ", () => { var prefix = Semantic("++A"); var postfix = Semantic("A++"); Assert(prefix.Nodes[prefix.Records[0].RootNodeIndex].Operator == SemanticOperator.PrefixIncrement && postfix.Nodes[postfix.Records[0].RootNodeIndex].Operator == SemanticOperator.PostfixIncrement); }));
        tests.Add(("ternary follows legacy left reduction", () => { var p = Semantic("A?B#C?D#E"); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert(root.Kind == SemanticNodeKind.Ternary && p.Nodes[root.A].Kind == SemanticNodeKind.Ternary); }));
        tests.Add(("unparenthesized ternary true nesting rejects", new Action(() => { Expect<SemanticParseException>(() => Semantic("A?B?C#D#E")); })));
        tests.Add(("parenthesized ternary nesting accepts", () => Assert(Semantic("A?(B?C#D)#E").Nodes.Length > 0)));
         tests.Add(("variable colon arguments stay lexical", () => { var p = Semantic("A:B:C"); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert(root.Kind == SemanticNodeKind.Variable && root.C == 2); }));
         tests.Add(("FOR and REPEAT retain counted-loop identities", () => { var explicitFor = SemanticIrCompiler.CompileCountedLoop("LOCAL, 0, DUNGEONNUM, 2", semanticEnvironment, 0, false); var defaults = SemanticIrCompiler.CompileCountedLoop("LOCAL,,10", semanticEnvironment, 0, false); var repeat = SemanticIrCompiler.CompileCountedLoop("3", semanticEnvironment, 0, true); var f = explicitFor.Nodes[explicitFor.Records[0].RootNodeIndex]; var d = defaults.Nodes[defaults.Records[0].RootNodeIndex]; var r = repeat.Nodes[repeat.Records[0].RootNodeIndex]; Assert(f.Kind == SemanticNodeKind.CountedLoop && f.Operator == SemanticOperator.CountedFor && d.B != -1 && d.D != -1 && r.Operator == SemanticOperator.CountedRepeat); }));
         tests.Add(("actual FOR and expression regressions parse", () => { foreach (var line in new[] { "LOCAL, 0, DUNGEONNUM", "ループ用0, 1, 4,", "LOCAL,,10" }) Assert(SemanticIrCompiler.CompileCountedLoop(line, semanticEnvironment, 0, false).Records.Length == 1); foreach (var line in new[] { "!EQUIP:ARG:@\"特殊弾{LCOUNT}\"", "CFLAG:L_CHARA:@\"登録装備%GET_EQUIP(LCOUNT)%{LCOUNT:1}\" == L_EQUIPNUM", "FINDELEMENT(NAME_LIST, ESCAPE(TEMPS:1), , , 1) >= 0", "GET_ROLE_PROP(\"CAN_USE_MANTRA\", PALENT, ,\\@IS_TRANS ? 変身形態 # 通常形態\\@)", "!GCREATEFROMFILE(GID, \"90_アイコン\\\\アイコン装飾\\\\\" + ARGS + \".png\")" }) Assert(Semantic(line).Records.Length == 1, line); Assert(SemanticIrCompiler.CompileCase("\"Time to Make History\"", semanticEnvironment).Records.Length == 1); }));
        tests.Add(("variable colon does not consume binary", () => { var p = Semantic("A:B+1"); var root = p.Nodes[p.Records[0].RootNodeIndex]; Assert(root.Operator == SemanticOperator.Plus && p.Nodes[root.A].Kind == SemanticNodeKind.Variable); }));
         tests.Add(("parenthesized and call variable arguments are single args", () => { var a = Semantic("A:(B+1)"); var b = Semantic("A:FUNC(B+1)"); Assert(a.Nodes[a.Records[0].RootNodeIndex].C == 1 && b.Nodes[b.Records[0].RootNodeIndex].C == 1, $"counts={a.Nodes[a.Records[0].RootNodeIndex].C},{b.Nodes[b.Records[0].RootNodeIndex].C}"); }));
         tests.Add(("function trailing comma preserves only real omissions", () => { var p = Semantic("FUNC(,A,,B,)"); var call = p.Nodes[p.Records[0].RootNodeIndex]; var trailingPayload = Semantic("FUNC(A,)"); var trailing = trailingPayload.Nodes[trailingPayload.Records[0].RootNodeIndex]; var onlyMissingPayload = Semantic("FUNC(,)"); var onlyMissing = onlyMissingPayload.Nodes[onlyMissingPayload.Records[0].RootNodeIndex]; Assert(call.Kind == SemanticNodeKind.Call && call.C == 4 && p.Nodes.Count(x => x.Kind == SemanticNodeKind.MissingArgument) == 2 && trailing.C == 1 && onlyMissing.C == 1); }));
        tests.Add(("VAR at subkey keeps two symbols", () => { var p = Semantic("VAR@SUBKEY"); var n = p.Nodes[p.Records[0].RootNodeIndex]; Assert(n.Kind == SemanticNodeKind.VariableSubkey && p.ReadSymbol(p.Symbols[n.A]) == "VAR" && p.ReadSymbol(p.Symbols[n.B]) == "SUBKEY"); }));
        tests.Add(("bare IS and TO reject", new Action(() => { Expect<SemanticParseException>(() => Semantic("IS")); Expect<SemanticParseException>(() => Semantic("TO")); })));
        tests.Add(("CASE expression IS TO and trailing comma", () => { var p = SemanticIrCompiler.CompileCase("1, IS >= 2, 3 TO 4,", semanticEnvironment); Assert(p.CaseArms.Length == 3); }));
        tests.Add(("CASE malformed arms reject", new Action(() => { Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase(",1", semanticEnvironment)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("1,,2", semanticEnvironment)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("1 TO 2 TO 3", semanticEnvironment)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("1 garbage", semanticEnvironment)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("IS >= 2 garbage", semanticEnvironment)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("1 TO 2 garbage", semanticEnvironment)); })));
        tests.Add(("number bases and exponent boundaries", new Action(() => { foreach (var value in new[] { "0x10", "0b10", "1p0", "1p10", "1e+3", "0x10p2", "0b10p10" }) Assert(Semantic(value).Nodes.Any(x => x.Kind == SemanticNodeKind.IntegerLiteral), value); Expect<SemanticParseException>(() => Semantic("0x")); Expect<SemanticParseException>(() => Semantic("1e+")); Expect<SemanticParseException>(() => Semantic("1.5")); var boundary = SemanticLexicalTokenStream.Tokenize("1e3ABC", semanticEnvironment.Compatibility); Assert(boundary.Count == 2 && boundary[0].Kind == SemanticTokenKind.Number && boundary[0].Raw == "1e3" && boundary[1].Kind == SemanticTokenKind.Identifier && boundary[1].Raw == "ABC"); Expect<SemanticParseException>(() => Semantic("1e3ABC")); })));
        tests.Add(("string escapes decode to semantic bytes", () => { var p = Semantic("\"a\\s\\S\\t\\n\\x\""); var n = p.Nodes[p.Records[0].RootNodeIndex]; Assert(p.ReadSymbol(p.Symbols[n.A]) == "a \u3000\t\nx"); }));
        tests.Add(("full-width space option is explicit", new Action(() => { Assert(Semantic("A　+B").Nodes.Length > 0); var disabled = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, false, false)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("A　+B", disabled)); })));
         tests.Add(("VT and FF are not expression whitespace", () => { var vt = Semantic("A\v+B"); var ff = Semantic("A\f+B"); Assert(vt.Nodes.Any(x => x.Kind == SemanticNodeKind.Binary) && ff.Nodes.Any(x => x.Kind == SemanticNodeKind.Binary)); }));
        tests.Add(("comment marker rules are explicit", () => { var ordinary = Semantic("1;comment"); Assert(ordinary.Nodes[ordinary.Records[0].RootNodeIndex].Kind == SemanticNodeKind.IntegerLiteral); var continued = Semantic("1;!; + 2"); Assert(continued.Nodes[continued.Records[0].RootNodeIndex].Operator == SemanticOperator.Plus); var debug = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, true)); var debugTree = SemanticIrCompiler.CompileExpression("1;#; + 2", debug); Assert(debugTree.Nodes[debugTree.Records[0].RootNodeIndex].Operator == SemanticOperator.Plus); var normalTree = Semantic("1;#; + 2"); Assert(normalTree.Nodes[normalTree.Records[0].RootNodeIndex].Kind == SemanticNodeKind.IntegerLiteral); }));
         tests.Add(("formatted width and alignment are compact", () => { var p = SemanticIrCompiler.CompileFormat("%VALUE,5,LEFT%", semanticEnvironment); Assert(p.Nodes.Any(x => x.Kind == SemanticNodeKind.Format) && p.Symbols.Length >= 2); }));
         tests.Add(("formatted mixed sequence keeps percent curly and width IR", () => { var p = SemanticIrCompiler.CompileFormat("pre%VALUE,5,LEFT%mid{OTHER}post", semanticEnvironment); var formats = p.Nodes.Where(x => x.Kind == SemanticNodeKind.Format).ToArray(); var percent = formats.Single(x => x.D == (int)SemanticFormatKind.Percent); var curly = formats.Single(x => x.D == (int)SemanticFormatKind.Curly); Assert(p.Nodes.Any(x => x.Kind == SemanticNodeKind.FormattedSequence) && p.Nodes[percent.B].Kind == SemanticNodeKind.IntegerLiteral && p.ReadSymbol(p.Symbols[percent.C]) == "LEFT" && p.ReadSymbol(p.Symbols[curly.C]) == "RIGHT"); }));
         tests.Add(("formatted literal macro isolation and missing right", () => { var macros = new MacroCatalog(); macros.Add(new("M", "VALUE")); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var p = SemanticIrCompiler.CompileExpression("@\"literal M {M}\"", env); var q = SemanticIrCompiler.CompileFormat("\\@A?B\\@", env); Assert(p.Nodes.Any(x => x.Kind == SemanticNodeKind.FormattedSequence) && q.Nodes.Any(x => x.Kind == SemanticNodeKind.ConditionalFormat && (x.D & (int)SemanticNodeFlags.RightPresent) == 0)); }));
        tests.Add(("conditional format keeps omitted right branch", () => { var p = SemanticIrCompiler.CompileFormat("\\@A?%B%#%C%\\@", semanticEnvironment); Assert(p.Nodes.Any(x => x.Kind == SemanticNodeKind.ConditionalFormat)); }));
        tests.Add(("triple symbol config controls node kind", () => { var ignored = SemanticIrCompiler.CompileFormat("***", semanticEnvironment); var emitted = SemanticIrCompiler.CompileFormat("***", new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, systemIgnoreTripleSymbol: false)); Assert(ignored.Nodes[0].Kind == SemanticNodeKind.StringLiteral && emitted.Nodes[0].Kind == SemanticNodeKind.TripleLiteral); }));
         tests.Add(("macro integer variable and call expand", () => { var macros = new MacroCatalog(); macros.Add(new("I", "1", MacroReplacementKind.Integer)); macros.Add(new("V", "VALUE", MacroReplacementKind.Variable)); macros.Add(new("F", "FUNC(1)", MacroReplacementKind.FunctionCall)); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); Assert(SemanticIrCompiler.CompileExpression("I+V", env).Nodes.Any(x => x.Kind == SemanticNodeKind.IntegerLiteral)); Assert(SemanticIrCompiler.CompileExpression("F", env).Nodes.Any(x => x.Kind == SemanticNodeKind.Call)); }));
         tests.Add(("repeated macro names and exact expansion cap", new Action(() => { var repeated = new MacroCatalog(); repeated.Add(new("M", "1")); Assert(SemanticIrCompiler.CompileExpression("M+M", new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, repeated)).Records.Length == 1); var independent = new MacroCatalog(); var names = Enumerable.Range(0, 101).Select(i => { var name = "M" + i; independent.Add(new(name, "1")); return name; }); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression(string.Join('+', names), new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, independent))); })));
        tests.Add(("empty cyclic and capped macros are explicit", new Action(() => { var empty = new MacroCatalog(); empty.Add(new("EMPTY", "", MacroReplacementKind.Empty)); Assert(empty.Expand("EMPTY", semanticEnvironment.Compatibility, out var emptyCount) == "" && emptyCount == 1); var cyclic = new MacroCatalog(); cyclic.Add(new("A", "B")); cyclic.Add(new("B", "A")); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("A", new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, cyclic))); })));
        tests.Add(("rename macro retains symbolic identity", () => { var macros = new MacroCatalog(); macros.Add(new("R", "[[NAME]]", MacroReplacementKind.RenameTemplate, true)); var p = SemanticIrCompiler.CompileExpression("R", new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros)); Assert(p.Nodes.Any(x => x.Kind == SemanticNodeKind.RenameTemplate)); }));
         tests.Add(("function-like macro declaration is rejected with reason", new Action(() => { try { MacroCatalog.FromHeaderText("#DEFINE F(x) x\n#DEFINE EMPTY\n", semanticEnvironment.Compatibility); throw new InvalidOperationException("expected function-like rejection"); } catch (SemanticParseException ex) { Assert(ex.Message.Contains("function-like", StringComparison.Ordinal), ex.Message); } })));
         tests.Add(("header macro parser follows legacy directive boundaries", new Action(() => { var options = semanticEnvironment.Compatibility; var catalog = MacroCatalog.FromHeaderText("  #DEFINE M 1\n;!; #DEFINE N 2\n;#; #DEFINE OFF 3\n#DEFINE F (x)\n#DEFINE Q \";\"\n", options); Assert(catalog.Count == 4 && catalog.Expand("M+N", options, out _) == " 1+ 2" && catalog.Expand("F", options, out _) == " (x)" && catalog.Expand("Q", options, out _) == " \";\""); Expect<SemanticParseException>(() => MacroCatalog.FromHeaderText("# DEFINE BAD 1", options)); var debug = options with { DebugMode = true }; Assert(MacroCatalog.FromHeaderText(";#; #DEFINE D 4", debug).Count == 1 && MacroCatalog.FromHeaderText(";#; #DEFINE D 4", options).Count == 0); })));
         tests.Add(("header macro definition-time and forward expansion", () => { var options = semanticEnvironment.Compatibility; var defined = MacroCatalog.FromHeaderText("#DEFINE A 1\n#DEFINE B A\n#DEFINE EMPTY\n", options); var definedB = defined.Expand("B", options, out _); var definedEmpty = defined.Expand("EMPTY", options, out _); Assert(definedB.Trim() == "1" && definedEmpty == "", $"definedB={definedB};definedEmpty={definedEmpty}"); var forward = MacroCatalog.FromHeaderText("#DEFINE B A\n#DEFINE A 1\n", options); var forwardB = forward.Expand("B", options, out _); Assert(forwardB.Trim() == "1", $"forwardB={forwardB}"); }));
         tests.Add(("empty macro preserves instruction context defaults", () => { var macros = MacroCatalog.FromHeaderText("#DEFINE EMPTY\n", semanticEnvironment.Compatibility); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var expression = SemanticIrCompiler.CompileExpression("1 EMPTY + 2", env); Assert(expression.Nodes[expression.Records[0].RootNodeIndex].Operator == SemanticOperator.Plus); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("EMPTY", env)); var emptyIf = SemanticIrCompiler.CompileOptionalIntExpression("EMPTY", env); Assert(emptyIf.Nodes[emptyIf.Records[0].RootNodeIndex].Kind == SemanticNodeKind.IntegerLiteral); var repeat = SemanticIrCompiler.CompileCountedLoop("EMPTY", env, 0, true); Assert(repeat.Nodes[repeat.Records[0].RootNodeIndex].C >= 0); var counted = SemanticIrCompiler.CompileCountedLoop("I,EMPTY,10", env, 0, false); Assert(counted.Nodes[counted.Records[0].RootNodeIndex].B >= 0); }));
         tests.Add(("macro expansion is independent per formatted lexical stream", () => { var macros = new MacroCatalog(); macros.Add(new("M", "1")); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var hundred = string.Join('+', Enumerable.Repeat("M", 100)); var p = SemanticIrCompiler.CompileFormat($"%{hundred}%{{{hundred}}}", env); Assert(p.Nodes.Count(x => x.Kind == SemanticNodeKind.FormattedSequence || x.Kind == SemanticNodeKind.Format) > 0); }));
         tests.Add(("rename preprocessing is supplied and case-sensitive", () => { var resolver = new SemanticRenameResolver(new[] { new KeyValuePair<string, string>("[[依頼:TEST]]", "95"), new KeyValuePair<string, string>("[[キャラ:TEST]]", "123"), new KeyValuePair<string, string>("[[スキル:百烈突き]]", "229"), new KeyValuePair<string, string>("[[スキル:千烈突き]]", "230"), new KeyValuePair<string, string>("[[Key]]", "1") }); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, renameResolver: resolver); var variable = SemanticIrCompiler.CompileExpression("依頼フラグ:[[依頼:TEST]]:0", env); Assert(variable.Nodes.Any(x => x.Kind == SemanticNodeKind.Variable) && variable.Symbols.Any(x => variable.ReadSymbol(x) == "95")); var group = SemanticIrCompiler.CompileExpression("GROUPMATCH(L_SKILL, [[スキル:百烈突き]], [[スキル:千烈突き]])", env); Assert(group.Symbols.Any(x => group.ReadSymbol(x) == "229") && group.Symbols.Any(x => group.ReadSymbol(x) == "230")); var quoted = SemanticIrCompiler.CompileExpression("\"[[UNKNOWN]]\"", env); Assert(quoted.Nodes[quoted.Records[0].RootNodeIndex].Kind == SemanticNodeKind.StringLiteral); var formatted = SemanticIrCompiler.CompileExpression("@\"ID=[[UNKNOWN]]\"", env); Assert(formatted.Symbols.Any(x => formatted.ReadSymbol(x) == "ID=[[UNKNOWN]]")); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("CFLAG:[[UNKNOWN]]:1", env)); var sensitive = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, false, true, false), renameResolver: resolver); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("[[key]]", sensitive)); }));
         tests.Add(("rename preprocessing precedes header macro tokenization", () => { var resolver = new SemanticRenameResolver(new[] { new KeyValuePair<string, string>("[[依頼:TEST]]", "95") }); var options = semanticEnvironment.Compatibility; var macros = MacroCatalog.FromHeaderText("#DEFINE FLAG_REQ95_進行度 依頼フラグ:[[依頼:TEST]]:0", options, resolver); var env = new StructuralSemanticEnvironment(options, macros, renameResolver: resolver); var p = SemanticIrCompiler.CompileExpression("FLAG_REQ95_進行度", env); Assert(p.Nodes.Any(x => x.Kind == SemanticNodeKind.Variable) && p.Symbols.Any(x => p.ReadSymbol(x) == "95") && !p.Nodes.Any(x => x.Kind == SemanticNodeKind.RenameTemplate)); }));
         tests.Add(("fullwidth digit remains identifier and colon negatives are exact", () => { var p = Semantic("CFLAG:ARG:１moreフラグ > 0"); Assert(p.Symbols.Any(x => p.ReadSymbol(x) == "１moreフラグ")); Expect<SemanticParseException>(() => Semantic("A:-1")); Assert(Semantic("A:(-1)").Nodes.Any(x => x.Kind == SemanticNodeKind.Variable)); }));
         tests.Add(("formatted empty and explicit empty right are distinct", () => { var empty = SemanticIrCompiler.CompileExpression("@\"\"", semanticEnvironment); var missing = SemanticIrCompiler.CompileFormat("\\@A?B\\@", semanticEnvironment); var explicitRight = SemanticIrCompiler.CompileFormat("\\@A?B#\\@", semanticEnvironment); var emptyRoot = empty.Nodes[empty.Records[0].RootNodeIndex]; var missingRoot = missing.Nodes[missing.Records[0].RootNodeIndex]; var explicitRoot = explicitRight.Nodes[explicitRight.Records[0].RootNodeIndex]; Assert(emptyRoot.Kind == SemanticNodeKind.StringLiteral && empty.ReadSymbol(empty.Symbols[emptyRoot.A]) == "" && (missingRoot.D & (int)SemanticNodeFlags.RightPresent) == 0 && (explicitRoot.D & (int)SemanticNodeFlags.RightPresent) != 0 && explicitRoot.C >= 0); }));
         tests.Add(("formatted scanner handles inner quotes and legacy non-nesting", () => { var inner = SemanticIrCompiler.CompileExpression("@\"%TOSTR(L_ICONNUM, \"000\")%\"", semanticEnvironment); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileFormat("\\@A?left \\@B?x#y\\@ #right\\@", semanticEnvironment)); Assert(inner.Nodes.Any(x => x.Kind == SemanticNodeKind.Format)); }));
         tests.Add(("formatted and quoted comment markers remain literal", () => { var quoted = Semantic("\"X;!;Y\""); var formatted = SemanticIrCompiler.CompileExpression("@\"X;!;Y\"", semanticEnvironment); var debug = SemanticIrCompiler.CompileExpression("@\"X;#;Y\"", semanticEnvironment); Assert(quoted.ReadSymbol(quoted.Symbols[quoted.Nodes[0].A]) == "X;!;Y" && formatted.ReadSymbol(formatted.Symbols[formatted.Nodes[0].A]) == "X;!;Y" && debug.ReadSymbol(debug.Symbols[debug.Nodes[0].A]) == "X;#;Y"); }));
        tests.Add(("FOR positional contract rejects two and parses extras", () => { foreach (var value in new[] { "I,,10", "I,0,10", "I,0,10,", "I,0,10,2", "I,0,10,2,EXTRA" }) Assert(SemanticIrCompiler.CompileCountedLoop(value, semanticEnvironment, 0, false).Records.Length == 1, value); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCountedLoop("I,10", semanticEnvironment, 0, false)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCountedLoop("I,10,", semanticEnvironment, 0, false)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCountedLoop("I,0,10,1,1+", semanticEnvironment, 0, false)); }));
        tests.Add(("variable colon is one term with outer postfix and max three", () => { foreach (var value in new[] { "A:-1", "A:+1", "A:!B", "A:~B" }) Expect<SemanticParseException>(() => Semantic(value)); var postfix = Semantic("TFLAG:key++"); var root = postfix.Nodes[postfix.Records[0].RootNodeIndex]; var variable = postfix.Nodes[root.A]; var three = Semantic("A:B:C:D").Nodes.Last(); Assert(root.Operator == SemanticOperator.PostfixIncrement && variable.Kind == SemanticNodeKind.Variable && variable.C == 1 && postfix.ReadSymbol(postfix.Symbols[postfix.Nodes[postfix.Edges[variable.B].To].A]) == "key" && three.Kind == SemanticNodeKind.Variable && three.C == 3); Expect<SemanticParseException>(() => Semantic("A:B:C:D:E")); }));
        tests.Add(("subkey variables retain colon edges and merge", () => { var p = Semantic("VAR@SUBKEY:A:B"); var n = p.Nodes[p.Records[0].RootNodeIndex]; var merged = SemanticPayload.Merge([Semantic("X"), p]); var m = merged.Nodes.Single(x => x.Kind == SemanticNodeKind.VariableSubkey); Assert(n.C >= 0 && n.D == 2 && p.ReadSymbol(p.Symbols[n.A]) == "VAR" && p.ReadSymbol(p.Symbols[n.B]) == "SUBKEY" && merged.ReadSymbol(merged.Symbols[m.A]) == "VAR" && merged.Edges[m.C].To >= 0 && merged.Edges[m.C + 1].To >= 0); }));
        tests.Add(("empty expressions default only in optional instruction context", () => { Expect<SemanticParseException>(() => Semantic("")); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("", semanticEnvironment)); foreach (var value in new[] { "%%", "{}", "%,5%", "{,5}" }) Expect<SemanticParseException>(() => SemanticIrCompiler.CompileFormat(value, semanticEnvironment)); var defaults = new[] { "SIF", "IF", "ELSEIF", "WHILE", "LOOP", "REPEAT" }.Select(CompileSemanticLine).ToArray(); var select = CompileSemanticLine("SELECTCASE"); var optionalRoots = defaults.Take(5).Select(x => x.Function!.SemanticPayload!.Nodes[x.Function.SemanticPayload.Records[0].RootNodeIndex]); var repeatRoot = defaults[5].Function!.SemanticPayload!.Nodes[defaults[5].Function.SemanticPayload.Records[0].RootNodeIndex]; Assert(defaults.All(x => x.Status == CompileStatus.Compiled) && optionalRoots.All(x => x.Kind == SemanticNodeKind.IntegerLiteral && x.A == 0) && repeatRoot.Kind == SemanticNodeKind.CountedLoop && select.Status == CompileStatus.Unsupported && select.Reason == UnsupportedReason.ExpressionSensitiveSyntax && select.Detail == "expression term missing", $"defaults={string.Join(',', defaults.Select(x => x.Status))};select={select.Status}/{select.Reason}/{select.Detail}"); }));
        tests.Add(("macro expansion respects formatted lexical contexts", () => { var macros = new MacroCatalog(semanticEnvironment.Compatibility); macros.Add(new("M", "1")); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var p = SemanticIrCompiler.CompileExpression("M == @\"literal M {M}\"", env); var root = p.Nodes[p.Records[0].RootNodeIndex]; var format = p.Nodes.Single(x => x.Kind == SemanticNodeKind.Format); Assert(root.Operator == SemanticOperator.Equal && p.Nodes[root.A].Kind == SemanticNodeKind.IntegerLiteral && p.Nodes.Any(x => x.Kind == SemanticNodeKind.StringLiteral && p.ReadSymbol(p.Symbols[x.A]) == "literal M ") && p.Nodes[format.A].Kind == SemanticNodeKind.IntegerLiteral); }));
        tests.Add(("macro catalog has one compatibility authority", () => { var exact = new CompilerCompatibilityOptions(false, true, true, false); var catalog = new MacroCatalog(exact); catalog.Add(new("M", "1")); Assert(catalog.Expand("M", exact, out _) == "1"); Expect<SemanticParseException>(() => catalog.Expand("M", semanticEnvironment.Compatibility, out _)); var noFullSpace = new CompilerCompatibilityOptions(true, true, false, false); var fullSpaceCatalog = new MacroCatalog(noFullSpace); fullSpaceCatalog.Add(new("W", "A　+B")); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("W", new StructuralSemanticEnvironment(noFullSpace, fullSpaceCatalog))); }));
        tests.Add(("conditional comments stream and inner quotes are context-aware", () => { var p = SemanticIrCompiler.CompileFormat("\\@A?left;semi#right;semi\\@", semanticEnvironment); var n = p.Nodes[p.Records[0].RootNodeIndex]; var emptyLeft = SemanticIrCompiler.CompileFormat("\\@A?#C\\@", semanticEnvironment); var emptyLeftNode = emptyLeft.Nodes[emptyLeft.Records[0].RootNodeIndex]; var quotedCondition = SemanticIrCompiler.CompileExpression("@\"\\@ARGS == \"x\"?yes#no\\@\"", semanticEnvironment); Assert(p.ReadSymbol(p.Symbols[p.Nodes[n.B].A]) == "left;semi" && p.ReadSymbol(p.Symbols[p.Nodes[n.C].A]) == "right;semi" && emptyLeft.ReadSymbol(emptyLeft.Symbols[emptyLeft.Nodes[emptyLeftNode.B].A]) == "" && (emptyLeftNode.D & (int)SemanticNodeFlags.RightPresent) != 0 && quotedCondition.Nodes.Any(x => x.Kind == SemanticNodeKind.ConditionalFormat)); }));
        tests.Add(("normal physical line escape and triples preserve source order", () => { var normal = Semantic("\"A\\\r\nB\""); var node = normal.Nodes[normal.Records[0].RootNodeIndex]; var escaped = SemanticIrCompiler.CompileFormat("\\***", new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, systemIgnoreTripleSymbol: false)); var ignored = SemanticIrCompiler.CompileFormat("pre***post", semanticEnvironment); Assert(normal.ReadSymbol(normal.Symbols[node.A]) == "AB" && !escaped.Nodes.Any(x => x.Kind == SemanticNodeKind.TripleLiteral) && ignored.Nodes.Length == 1 && ignored.Nodes[0].Kind == SemanticNodeKind.StringLiteral && ignored.ReadSymbol(ignored.Symbols[ignored.Nodes[0].A]) == "pre***post"); }));
        tests.Add(("format omissions and fullwidth whitespace use expression rules", () => { foreach (var value in new[] { "%A%", "%A,%", "%A,5%", "%A,5,%", "%A,5,LEFT%", "%A,5,RIGHT%", "{A}", "{A,}", "{A,5}", "{A,5,}" }) Assert(SemanticIrCompiler.CompileFormat(value, semanticEnvironment).Records.Length == 1, value); foreach (var value in new[] { "%A,,LEFT%", "{A,,LEFT}" }) Expect<SemanticParseException>(() => SemanticIrCompiler.CompileFormat(value, semanticEnvironment)); var caseFull = SemanticIrCompiler.CompileCase("　IS >= 1", semanticEnvironment); var rangeFull = SemanticIrCompiler.CompileCase("1　TO　2", semanticEnvironment); var formatFull = SemanticIrCompiler.CompileFormat("%A,　% %A,5,　LEFT　%", semanticEnvironment); var disabled = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, false, false)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileFormat("%A,　%", disabled)); var conditional = SemanticIrCompiler.CompileFormat("\\@A?　left #right\\@", semanticEnvironment); var conditionalRoot = conditional.Nodes[conditional.Records[0].RootNodeIndex]; Assert(caseFull.CaseArms.Length == 1 && rangeFull.CaseArms.Length == 1 && formatFull.Nodes.Count(x => x.Kind == SemanticNodeKind.Format) == 2 && conditional.ReadSymbol(conditional.Symbols[conditional.Nodes[conditionalRoot.B].A]).StartsWith("　", StringComparison.Ordinal)); }));
        tests.Add(("actual game colon postfix regressions preserve outer ownership", () => { foreach (var value in new[] { "TFLAG:スキルトリガー用1++ > 0", "TFLAG:スキルトリガー用1++", "ダンジョンフラグ:(FLAG:現ダンジョン):0++ == 0", "(LOCAL:3++ % PRINTCPERLINE()) == PRINTCPERLINE() - 1" }) { var p = Semantic(value); Assert(p.Nodes.Any(x => x.Operator == SemanticOperator.PostfixIncrement), value); } }));
        tests.Add(("bare conditional is opaque outside and lexical inside", () => { var macros = new MacroCatalog(semanticEnvironment.Compatibility); macros.Add(new("M", "1")); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var p = SemanticIrCompiler.CompileExpression("M == \\@M?literal M {M}#M\\@", env); var conditional = p.Nodes.Single(x => x.Kind == SemanticNodeKind.ConditionalFormat); var right = p.Nodes[conditional.C]; var embedded = p.Nodes.Single(x => x.Kind == SemanticNodeKind.Format); Assert(p.Nodes[p.Records[0].RootNodeIndex].Operator == SemanticOperator.Equal && p.Nodes[conditional.A].Kind == SemanticNodeKind.IntegerLiteral && p.Nodes[embedded.A].Kind == SemanticNodeKind.IntegerLiteral && p.Nodes.Any(x => x.Kind == SemanticNodeKind.StringLiteral && p.ReadSymbol(p.Symbols[x.A]) == "literal M ") && p.ReadSymbol(p.Symbols[right.A]) == "M"); }));
        tests.Add(("bare conditional comments and function arguments preserve contexts", () => { var macros = new MacroCatalog(semanticEnvironment.Compatibility); macros.Add(new("M", "1")); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var left = SemanticIrCompiler.CompileExpression("\\@A?left;semi#right\\@", env); var right = SemanticIrCompiler.CompileExpression("\\@A?left#right;semi\\@", env); var closed = SemanticIrCompiler.CompileExpression("\\@A?left#right\\@;ordinary-comment", env); var call = SemanticIrCompiler.CompileExpression("FUNC(\\@A?left;semi {M}#right M\\@)", env); var conditional = call.Nodes.Single(x => x.Kind == SemanticNodeKind.ConditionalFormat); var callNode = call.Nodes[call.Records[0].RootNodeIndex]; Assert(left.ReadSymbol(left.Symbols[left.Nodes[left.Nodes[left.Records[0].RootNodeIndex].B].A]) == "left;semi" && right.ReadSymbol(right.Symbols[right.Nodes[right.Nodes[right.Records[0].RootNodeIndex].C].A]) == "right;semi" && closed.Nodes[closed.Records[0].RootNodeIndex].Kind == SemanticNodeKind.ConditionalFormat && callNode.Kind == SemanticNodeKind.Call && callNode.C == 1 && call.Edges[callNode.B].To == call.Nodes.ToList().FindIndex(x => x.Kind == SemanticNodeKind.ConditionalFormat) && call.Nodes[conditional.A].Kind == SemanticNodeKind.Symbol && call.Nodes.Any(x => x.Kind == SemanticNodeKind.Format && call.Nodes[x.A].Kind == SemanticNodeKind.IntegerLiteral) && call.Nodes.Any(x => x.Kind == SemanticNodeKind.StringLiteral && call.ReadSymbol(call.Symbols[x.A]) == "right M")); }));
        tests.Add(("comment markers preserve semantic CASE and FOR word boundaries", () => { Expect<SemanticParseException>(() => Semantic("A;!;B")); var debug = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, true)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("A;#;B", debug)); var ordinary = SemanticIrCompiler.CompileExpression("A;#;B", semanticEnvironment); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("A;!;B", semanticEnvironment)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCountedLoop("I,A;!;B,10", semanticEnvironment, 0, false)); Assert(ordinary.ReadSymbol(ordinary.Symbols[ordinary.Nodes[ordinary.Records[0].RootNodeIndex].A]) == "A"); }));
        tests.Add(("macro header and expansion preserve token boundaries", () => { var options = semanticEnvironment.Compatibility; var marker = MacroCatalog.FromHeaderText("#DEFINE M A;!;B", options); var markerText = marker.Expand("M", options, out _); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("M", new StructuralSemanticEnvironment(options, marker))); var debugOptions = options with { DebugMode = true }; var debugText = MacroCatalog.FromHeaderText("#DEFINE M A;#;B", debugOptions).Expand("M", debugOptions, out _); var ordinaryText = MacroCatalog.FromHeaderText("#DEFINE M A;#;B", options).Expand("M", options, out _); var quotedText = MacroCatalog.FromHeaderText("#DEFINE Q \"A;B\"", options).Expand("Q", options, out _); Assert(markerText.Contains("A B", StringComparison.Ordinal) && !markerText.Contains("AB", StringComparison.Ordinal) && debugText.Contains("A B", StringComparison.Ordinal) && ordinaryText.Trim() == "A" && quotedText.Trim() == "\"A;B\""); }));
        tests.Add(("macro operator boundaries cannot form increments", () => { var options = semanticEnvironment.Compatibility; var plus = new MacroCatalog(options); plus.Add(new("P", "+")); var pText = plus.Expand("P+B", options, out _); var empty = new MacroCatalog(options); empty.Add(new("EMPTY", "", MacroReplacementKind.Empty)); var emptyText = empty.Expand("+EMPTY+B", options, out _); Assert(!pText.Contains("++", StringComparison.Ordinal) && !emptyText.Contains("++", StringComparison.Ordinal) && SemanticLexicalTokenStream.Tokenize(pText, options).Count(x => x.Kind == SemanticTokenKind.Operator && x.Raw == "+") == 2 && SemanticLexicalTokenStream.Tokenize(emptyText, options).Count(x => x.Kind == SemanticTokenKind.Operator && x.Raw == "+") == 2); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("P+B", new StructuralSemanticEnvironment(options, plus))); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileExpression("+EMPTY+B", new StructuralSemanticEnvironment(options, empty))); }));
        tests.Add(("embedded comments terminate conditional and formatted expressions", () => { var debug = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, true)); foreach (var value in new[] { "\\@A;comment?left#right\\@", "\\@A;#;?left#right\\@" }) ExpectDelimiter(() => Semantic(value)); ExpectDelimiter(() => SemanticIrCompiler.CompileFormat("%A;comment%", semanticEnvironment)); ExpectDelimiter(() => SemanticIrCompiler.CompileFormat("{A;comment}", semanticEnvironment)); ExpectDelimiter(() => SemanticIrCompiler.CompileFormat("%A;#;%", semanticEnvironment)); ExpectDelimiter(() => SemanticIrCompiler.CompileFormat("{A;#;}", semanticEnvironment)); ExpectDelimiter(() => SemanticIrCompiler.CompileFormat("%A,5;comment%", semanticEnvironment)); foreach (var value in new[] { "\\@A;!;?left#right\\@", "%A;!;%", "{A;!;}", "%A,5;!;%" }) Assert((value.StartsWith("\\@", StringComparison.Ordinal) ? Semantic(value) : SemanticIrCompiler.CompileFormat(value, semanticEnvironment)).Records.Length == 1, value); foreach (var value in new[] { "\\@A;#;?left#right\\@", "%A;#;%", "{A;#;}" }) Assert((value.StartsWith("\\@", StringComparison.Ordinal) ? SemanticIrCompiler.CompileExpression(value, debug) : SemanticIrCompiler.CompileFormat(value, debug)).Records.Length == 1, value); var condition = SemanticIrCompiler.CompileExpression("\\@A;!;?left#right\\@", semanticEnvironment); Assert(condition.Nodes[condition.Nodes[condition.Records[0].RootNodeIndex].A].Kind == SemanticNodeKind.Symbol); }));
        tests.Add(("outer tokenizer and quoted embedded semicolons agree", () => { foreach (var value in new[] { "@\"%A;comment%\"", "@\"{A;comment}\"", "@\"\\@A;comment?left#right\\@\"", "@\"%A,5;comment%\"" }) ExpectDelimiter(() => Semantic(value)); ExpectDelimiter(() => SemanticLexicalTokenStream.Tokenize("@\"%A;comment%\"", semanticEnvironment.Compatibility)); ExpectDelimiter(() => SemanticLexicalTokenStream.Tokenize("\\@A;comment?left#right\\@", semanticEnvironment.Compatibility)); foreach (var value in new[] { "@\"%A;!;%\"", "@\"{A;!;}\"", "@\"\\@A;!;?left#right\\@\"" }) Assert(Semantic(value).Records.Length == 1, value); var debug = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, true)); Assert(SemanticIrCompiler.CompileExpression("@\"%A;#;%\"", debug).Records.Length == 1 && SemanticIrCompiler.CompileExpression("@\"\\@A;#;?left#right\\@\"", debug).Records.Length == 1); var condition = Semantic("\\@FUNC(\"a;b\")?left#right\\@"); var conditional = condition.Nodes[condition.Records[0].RootNodeIndex]; var embedded = SemanticIrCompiler.CompileFormat("%FUNC(\"a;#;b\")%", semanticEnvironment); var format = embedded.Nodes[embedded.Records[0].RootNodeIndex]; Assert(condition.Nodes[conditional.A].Kind == SemanticNodeKind.Call && embedded.Nodes[format.A].Kind == SemanticNodeKind.Call && embedded.Nodes.Any(x => x.Kind == SemanticNodeKind.StringLiteral && embedded.ReadSymbol(embedded.Symbols[x.A]) == "a;#;b")); }));
        tests.Add(("formatted atoms are opaque to CASE and FOR structural scanners", new Action(() => { var options = semanticEnvironment.Compatibility; Assert(SemanticLexicalTokenStream.SplitTopLevel("\\@A?x,y#z\\@", ',', options).Count == 1 && SemanticLexicalTokenStream.FindTopLevel("\\@A?TO#x\\@", "TO", options) == -1 && SemanticLexicalTokenStream.FindTopLevel("\\@A?x#y TO z\\@", "TO", options) == -1); foreach (var value in new[] { "\\@A?x,y#z\\@", "\\@A?TO#x\\@", "\\@A?x#y TO z\\@", "@\"x,y TO z\"" }) { var p = SemanticIrCompiler.CompileCase(value, semanticEnvironment); Assert(p.CaseArms.Length == 1 && p.CaseArms[0].ToNode == -1, value); } foreach (var value in new[] { "I,\\@A?x,y#z\\@,10", "I,0,\\@A?x,y#z\\@" }) { var p = SemanticIrCompiler.CompileCountedLoop(value, semanticEnvironment, 0, false); var loop = p.Nodes[p.Records[0].RootNodeIndex]; Assert(loop.Kind == SemanticNodeKind.CountedLoop && (p.Nodes[loop.B].Kind == SemanticNodeKind.ConditionalFormat || p.Nodes[loop.C].Kind == SemanticNodeKind.ConditionalFormat), value); } })));
        tests.Add(("macro-expanded formatted atoms remain structurally opaque", new Action(() => { var macros = new MacroCatalog(semanticEnvironment.Compatibility); macros.Add(new("F", "\\@A?x,y#z\\@")); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, macros); var casePayload = SemanticIrCompiler.CompileCase("F", env); var loopPayload = SemanticIrCompiler.CompileCountedLoop("I,F,10", env, 0, false); var loop = loopPayload.Nodes[loopPayload.Records[0].RootNodeIndex]; Assert(casePayload.CaseArms.Length == 1 && casePayload.CaseArms[0].ToNode == -1 && loopPayload.Nodes[loop.B].Kind == SemanticNodeKind.ConditionalFormat); })));
        tests.Add(("compatibility options govern keywords and macro names", new Action(() => { var exact = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(false, true, true, false)); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileCase("1 to 2", exact)); Assert(SemanticIrCompiler.CompileCase("1 TO 2", exact).CaseArms.Length == 1); Expect<SemanticParseException>(() => SemanticIrCompiler.CompileFormat("%A,1,left%", exact)); var macros = new MacroCatalog(exact.Compatibility.NameComparer); macros.Add(new("M", "1")); Assert(SemanticIrCompiler.CompileExpression("m", new StructuralSemanticEnvironment(exact.Compatibility, macros)).Nodes.All(x => x.Kind != SemanticNodeKind.IntegerLiteral)); var insensitiveMacros = new MacroCatalog(StringComparer.OrdinalIgnoreCase); insensitiveMacros.Add(new("M", "1")); Assert(SemanticIrCompiler.CompileExpression("m", new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false), insensitiveMacros)).Nodes.Any(x => x.Kind == SemanticNodeKind.IntegerLiteral)); })));
         tests.Add(("required actual-game expression regressions", () => { var resolver = new SemanticRenameResolver(new[] { new KeyValuePair<string, string>("[[スキル:百烈突き]]", "229"), new KeyValuePair<string, string>("[[スキル:千烈突き]]", "230") }); var env = new StructuralSemanticEnvironment(semanticEnvironment.Compatibility, renameResolver: resolver); foreach (var line in new[] { "GET_ROLE_PROP(\"CAN_USE_MANTRA\", PALENT, ,\\@IS_TRANS ? 変身形態 # 通常形態\\@)", "IF CFLAG:811 !& 1p0", "IF CFLAG:ARG:１moreフラグ > 0", "IF GROUPMATCH(L_SKILL, [[スキル:百烈突き]], [[スキル:千烈突き]])", "IF !GCREATEFROMFILE(GID, \"90_アイコン\\\\アイコン装飾\\\\\" + ARGS + \".png\")", "SIF ARGS == @\"%\"ＷＡＩＴ\", ACTOR_LENS, LEFT%\"" }) { var input = line.StartsWith("IF ", StringComparison.Ordinal) || line.StartsWith("SIF ", StringComparison.Ordinal) ? line[(line.IndexOf(' ') + 1)..] : line; Assert(SemanticIrCompiler.CompileExpression(input, env).Records.Length == 1, line); } }));
         tests.Add(("semantic payload is optional for zero operands", () => { var normal = compiler.TryCompile(indexed, function).Function!; var semantic = new FunctionCompiler(semanticEnvironment).TryCompile(indexed, function).Function!; Assert(normal.SemanticPayload is null && semantic.SemanticPayload is null); }));
         tests.Add(("semantic target is exactly nine structural opcodes", () => { var p = Path.Combine(root, "semantic-targets.ERB"); WriteBom(p, "@TARGETS\r\nSIF A\r\nIF A\r\nELSEIF A\r\nSELECTCASE A\r\nCASE 1\r\nREPEAT 2\r\nFOR I,0,2\r\nWHILE A\r\nLOOP A\r\nPRINTFORM %A%\r\n"); var f = ErbSourceIndexer.IndexFile(p); var result = new FunctionCompiler(semanticEnvironment).TryCompile(f, f.Functions.Single()); Assert(result.Status == CompileStatus.Compiled && result.Function!.SemanticPayload!.Records.Length == 9); }));
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
        foreach (var name in new[] { "RESET_STAIN", "VARSET", "ALIGNMENT", "ARRAYSHIFT", "SPLIT" })
        {
            tests.Add((name + " compiles as exact opcode", () =>
            {
                var result = CompileLine(name + " X");
                Assert(result.Status == CompileStatus.Compiled && result.Function!.Instructions.Single().Opcode == Enum.Parse<PrototypeOpcode>(name));
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
                    var next = LegacyIdentifierScanner.ReadFirstIdentifier(row.Input, CompilerCompatibilityOptions.LegacyDefaults);
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
                    var options = new CompilerCompatibilityOptions(ignoreCase, scoped, true, false);
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
                    var result = CompileLine(oracle.Input, new CompilerCompatibilityOptions(true, true, enabled, false));
                    var pass = (result.Status == CompileStatus.Compiled) != oracle.IsError;
                    report.Add($"SystemAllowFullSpace={enabled} oracleIsError={oracle.IsError} nextCompiled={result.Status == CompileStatus.Compiled} pass={pass}");
                    Assert(pass, $"fullspace failed: {enabled}");
                }
                WriteReport(fullspaceReportPath, report);
            }));
        }
        // [Emuera改修:NEXT-1B-R6 2026-08-27]
        var debugFalseCorpusPath = Environment.GetEnvironmentVariable("EMUERA_DEBUG_FALSE_CORPUS");
        var debugTrueCorpusPath = Environment.GetEnvironmentVariable("EMUERA_DEBUG_TRUE_CORPUS");
        var debugReportPath = Environment.GetEnvironmentVariable("EMUERA_DEBUG_REPORT");
        if (!string.IsNullOrWhiteSpace(debugFalseCorpusPath) && !string.IsNullOrWhiteSpace(debugTrueCorpusPath) && File.Exists(debugFalseCorpusPath) && File.Exists(debugTrueCorpusPath))
        {
            tests.Add(("R6 DebugMode ;#; oracle and silent-drop gate", () =>
            {
                var falseRows = ReadJsonLines<DebugOracle>(debugFalseCorpusPath);
                var trueRows = ReadJsonLines<DebugOracle>(debugTrueCorpusPath);
                var report = new List<string> { "source=two independent Legacy processes; DebugMode false/true", $"casesFalse={falseRows.Length}", $"casesTrue={trueRows.Length}" };
                var mismatches = 0;
                var silentDropped = 0;
                foreach (var expectedFalse in falseRows)
                {
                    var result = CompileLine(expectedFalse.Input, new CompilerCompatibilityOptions(true, false, true, false));
                    var count = result.Function?.Instructions.Length ?? 0;
                    var pass = count == expectedFalse.InstructionCount && (expectedFalse.InstructionCount == 0 ? result.Status == CompileStatus.Compiled : result.Status == CompileStatus.Compiled);
                    if (!pass) mismatches++;
                    report.Add($"debug=false name={expectedFalse.Name} legacyCount={expectedFalse.InstructionCount} nextStatus={result.Status} nextCount={count} pass={pass}");
                }
                foreach (var expectedTrue in trueRows)
                {
                    var result = CompileLine(expectedTrue.Input, new CompilerCompatibilityOptions(true, false, true, true));
                    var count = result.Function?.Instructions.Length ?? 0;
                    var fallback = expectedTrue.InstructionCount > 0 && result.Status == CompileStatus.Unsupported;
                    var exact = result.Status == CompileStatus.Compiled && count == expectedTrue.InstructionCount;
                    var pass = fallback || exact;
                    if (!pass) mismatches++;
                    if (expectedTrue.InstructionCount > 0 && result.Status == CompileStatus.Compiled && count == 0) silentDropped++;
                    report.Add($"debug=true name={expectedTrue.Name} legacyCount={expectedTrue.InstructionCount} legacyOpcode={expectedTrue.FunctionCode ?? "<none>"} nextStatus={result.Status} nextCount={count} fallback={fallback} pass={pass}");
                }
                report.Add($"silentDroppedInstructionCount={silentDropped}");
                report.Add($"mismatches={mismatches}");
                report.Add($"result={mismatches == 0 && silentDropped == 0}");
                WriteReport(debugReportPath, report);
                Assert(mismatches == 0 && silentDropped == 0);
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
    static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"expected {typeof(T).Name}"); }
    static void ExpectDelimiter(Action action) { try { action(); } catch (SemanticParseException ex) { Assert(ex.Message.Contains("conditional", StringComparison.Ordinal) || ex.Message.Contains("unterminated formatted", StringComparison.Ordinal), ex.Message); return; } throw new InvalidOperationException("expected formatted delimiter failure"); }
    static void Assert(bool condition, string message = "assertion failed") { if (!condition) throw new InvalidOperationException(message); }
}

readonly record struct SetDiff(string[] Missing, string[] Extra);
readonly record struct LexicalOracle(string Name, string Input, string Identifier, int StopPosition);
readonly record struct SeparatorOracle(string Name, string Input, string Kind, bool IsError, string? FunctionCode);
readonly record struct DebugOracle(string Name, string Input, bool DebugMode, string? Kind, bool IsError, string FirstIdentifier, int InstructionCount, string? FunctionCode, int SourceLine, int OperandOffset, int OperandLength);

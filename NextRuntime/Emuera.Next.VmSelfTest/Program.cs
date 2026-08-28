using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

var tests = new List<(string Name, Action Run)>
{
    ("layout 16/16/12/16", () => Check(Marshal.SizeOf<VmInstruction>() == 16 && Marshal.SizeOf<VmFunctionDescriptor>() == 16 && Marshal.SizeOf<VmFrame>() == 12 && Marshal.SizeOf<FunctionCatalogEntry>() == 16)),
    ("PC is next instruction", () => Check(Run([VmInstruction.Branch(2), new((ushort)VmOpcode.SemanticBarrier), VmInstruction.Halt]) == VmStopReason.Halted)),
    ("PC==CodeLength implicit return", () => Check(Run([VmInstruction.Nop]) == VmStopReason.Returned)),
    ("empty function", () => Check(Run([]) == VmStopReason.Returned)),
    ("branch to end", () => Check(Run([VmInstruction.Branch(1)]) == VmStopReason.Returned)),
    ("branch past end invalid", () => Check(Run([VmInstruction.Branch(2)]) == VmStopReason.InvalidLocalPc)),
    ("CALL exact return PC", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(new RuntimeFunctionId(1)), VmInstruction.Halt); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(new RuntimeFunctionId(a)) == VmStopReason.Halted); }),
    ("CALL empty returns", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(new RuntimeFunctionId(1)), VmInstruction.Halt); b.AddFunction(); Check(new VmMachine(b.Build()).Run(new RuntimeFunctionId(a)) == VmStopReason.Halted); }),
    ("JUMP propagation", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Jump(new RuntimeFunctionId(1))); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(new RuntimeFunctionId(a)) == VmStopReason.Returned); }),
    ("nested implicit returns", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(new RuntimeFunctionId(1)), VmInstruction.Halt); b.AddFunction(VmInstruction.Call(new RuntimeFunctionId(2))); b.AddFunction(); Check(new VmMachine(b.Build()).Run(new RuntimeFunctionId(a)) == VmStopReason.Halted); }),
    ("CodeAvailable vs ExecutableReady", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Nop); var p = b.Build(); p.Descriptors[a] = new(a, 0, 1, VmFunctionState.LinkedSemanticPending); Check(new VmMachine(p).Run(new RuntimeFunctionId(a)) == VmStopReason.SemanticNotAvailable); }),
    ("real execution barrier", () => { var c = Catalog("X"); c.MarkCodeAvailable([new RuntimeFunctionId(0)]); var link = ControlLinker.Link(c, [RP(0, [PrototypeOpcode.PRINT])]); Check(link.Program.Descriptors[0].State == VmFunctionState.LinkedSemanticPending && new VmMachine(link.Program).Run(new RuntimeFunctionId(0)) == VmStopReason.SemanticNotAvailable); }),
    ("effective-name catalog", () => { var c = FunctionCatalog.FromDefinitions([D("RAW", "a", Span(1), effectiveName: "EFFECTIVE")]); Check(c.GetPhysicalName(0) == "RAW" && c.GetEffectiveName(0) == "EFFECTIVE" && c.FindByName("EFFECTIVE").SequenceEqual([0])); }),
    ("unknown effective name excluded", () => { var c = FunctionCatalog.FromDefinitions([new("RAW", "a", Span(1))]); Check(!c[0].EffectiveNameKnown && c.GetEffectiveName(0) is null && c.FindByName("RAW").Count == 0); }),
    ("resolver unique normal", () => Check(FixedCallResolver.Resolve(Catalog("X"), "X", true, false).RuntimeId.Value == 0)),
    ("resolver first normal", () => { var c = FunctionCatalog.FromDefinitions([D("X", "a", Span(1)), D("X", "b", Span(2))]); Check(FixedCallResolver.Resolve(c, "X", true, false).RuntimeId.Value == 0); }),
    ("resolver method first is not skipped", () => { var c = FunctionCatalog.FromDefinitions([D("X", "a", Span(1), kind: FunctionKind.Method), D("X", "b", Span(2))]); Check(FixedCallResolver.Resolve(c, "X", true, false).Reason == "WrongKindMethod"); }),
    ("resolver event compatibility", () => { var c = FunctionCatalog.FromDefinitions([D("X", "a", Span(1), kind: FunctionKind.Event), D("X", "b", Span(2))]); Check(FixedCallResolver.Resolve(c, "X", true, false).Reason == "WrongKindEvent" && FixedCallResolver.Resolve(c, "X", true, true).RuntimeId.Value == 0); }),
    ("resolver normal before event", () => { var c = FunctionCatalog.FromDefinitions([D("X", "a", Span(1)), D("X", "b", Span(2), kind: FunctionKind.Event)]); Check(FixedCallResolver.Resolve(c, "X", true, false).RuntimeId.Value == 0); }),
    ("resolver case sensitivity", () => { var c = FunctionCatalog.FromDefinitions([D("Ab", "a", Span(1))], ignoreCase: false); Check(FixedCallResolver.Resolve(c, "Ab", false, false).RuntimeId.Value == 0 && !FixedCallResolver.Resolve(c, "ab", false, false).FunctionResolved); }),
    ("resolver missing", () => Check(FixedCallResolver.Resolve(Catalog("X"), "Y", true, false).Reason == "MissingTarget")),
    ("rename resolution", () => { var c = FunctionCatalog.FromDefinitions([D("[[RAW]]", "a", Span(1), effectiveName: "SET_BASE_5604")]); Check(FixedCallResolver.Resolve(c, "SET_BASE_5604", true, false).RuntimeId.Value == 0); }),
    ("operand span preserved", () => { var c = Catalog("X"); c.MarkCodeAvailable([new RuntimeFunctionId(0)]); var p = ControlLinker.Link(c, [new RuntimeFunctionPrototype(new(0), ImmutableArray.Create(new PrototypeInstruction(PrototypeOpcode.PRINT, PrototypeInstructionFlags.None, 1, 3, 4)), ImmutableArray.Create("abcd"))]); Check(p.Program.Code[0].OperandOffset == 3 && p.Program.Code[0].OperandLength == 4); }),
    ("SIF links", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SIF, PrototypeOpcode.PRINT])]); Check(p.Program.SifLinks.Length == 1 && p.Program.SifLinks[0].FalsePc == 2); }),
    ("SIF final malformed", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SIF])]); Check(p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
    ("IF ordered clauses", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.IF, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF])]); Check(p.Program.IfGroups.Length == 1 && p.Program.IfGroups[0].ClauseCount == 3 && p.Program.IfGroups[0].ExitPc == 4); }),
    ("SELECT ordered cases", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT])]); Check(p.Program.SelectGroups.Length == 1 && p.Program.SelectGroups[0].CaseCount == 2 && p.Program.SelectGroups[0].ExitPc == 4); }),
    ("loop descriptors", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.FOR, PrototypeOpcode.CONTINUE, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT])]); Check(p.Program.Loops.Length == 1 && p.Program.Loops[0].EndPc == 3 && p.Program.StructuralLinks.Any(x => x.Kind == VmStructuralKind.Continue && x.LoopIndex == 0)); }),
    ("IF group indices program-global", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1))]); var p = ControlLinker.Link(c, [RP(0, [PrototypeOpcode.IF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF]), RP(1, [PrototypeOpcode.IF, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF])]).Program; var second = p.StructuralLinks.Where(x => x.FunctionId == 1 && (x.Kind is VmStructuralKind.If or VmStructuralKind.ElseIf or VmStructuralKind.Else or VmStructuralKind.EndIf)).ToArray(); Check(p.IfGroups.Length == 2 && p.IfGroups[0].FirstClauseIndex == 0 && p.IfGroups[1].FirstClauseIndex == 2 && p.IfGroups[1].ClauseCount == 3 && second.Length == 4 && second.All(x => x.GroupIndex == 1)); }),
    ("SELECT group indices program-global", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1))]); var p = ControlLinker.Link(c, [RP(0, [PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT]), RP(1, [PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.ENDSELECT])]).Program; var second = p.StructuralLinks.Where(x => x.FunctionId == 1).ToArray(); Check(p.SelectGroups.Length == 2 && p.SelectGroups[0].FirstCaseIndex == 0 && p.SelectGroups[1].FirstCaseIndex == 2 && p.SelectGroups[1].CaseCount == 1 && second.Length == 3 && second.All(x => x.GroupIndex == 1)); }),
    ("loop indices program-global", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1))]); var p = ControlLinker.Link(c, [RP(0, [PrototypeOpcode.FOR, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT]), RP(1, [PrototypeOpcode.WHILE, PrototypeOpcode.CONTINUE, PrototypeOpcode.WEND])]).Program; var first = p.StructuralLinks.Where(x => x.FunctionId == 0).ToArray(); var second = p.StructuralLinks.Where(x => x.FunctionId == 1).ToArray(); Check(p.Loops.Length == 2 && p.Loops[0].FunctionId == 0 && p.Loops[1].FunctionId == 1 && first.Length == 3 && first.All(x => x.LoopIndex == 0) && second.Length == 3 && second.All(x => x.LoopIndex == 1)); }),
    ("WHILE continue check", () => Check(LoopLink([PrototypeOpcode.WHILE, PrototypeOpcode.CONTINUE, PrototypeOpcode.WEND]).Single().AuxiliaryPc == 0)),
    ("DO continue check", () => Check(LoopLink([PrototypeOpcode.DO, PrototypeOpcode.CONTINUE, PrototypeOpcode.LOOP]).Single().AuxiliaryPc == 2)),
    ("nested loop owners", () => { var rows = LinkRows([PrototypeOpcode.WHILE, PrototypeOpcode.BREAK, PrototypeOpcode.FOR, PrototypeOpcode.CONTINUE, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT, PrototypeOpcode.WEND]); Check(rows.Where(x => x.Kind == VmStructuralKind.Break).Select(x => x.LoopIndex).SequenceEqual([1, 0]) && rows.Single(x => x.Kind == VmStructuralKind.Continue).LoopIndex == 0); }),
    ("outer FOR inner FOR break", () => Check(LoopOwners([PrototypeOpcode.FOR, PrototypeOpcode.FOR, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT, PrototypeOpcode.NEXT]).SequenceEqual([0]))),
    ("outer FOR inner FOR continue", () => Check(LoopOwners([PrototypeOpcode.FOR, PrototypeOpcode.FOR, PrototypeOpcode.CONTINUE, PrototypeOpcode.NEXT, PrototypeOpcode.NEXT]).SequenceEqual([0]))),
    ("outer WHILE inner FOR break", () => Check(LoopOwners([PrototypeOpcode.WHILE, PrototypeOpcode.FOR, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT, PrototypeOpcode.WEND]).SequenceEqual([0]))),
    ("outer FOR inner DO continue", () => Check(LoopOwners([PrototypeOpcode.FOR, PrototypeOpcode.DO, PrototypeOpcode.CONTINUE, PrototypeOpcode.LOOP, PrototypeOpcode.NEXT]).SequenceEqual([0]))),
    ("depth 3 loop owners", () => Check(LoopOwners([PrototypeOpcode.REPEAT, PrototypeOpcode.WHILE, PrototypeOpcode.DO, PrototypeOpcode.BREAK, PrototypeOpcode.LOOP, PrototypeOpcode.WEND, PrototypeOpcode.REND]).SequenceEqual([0]))),
    ("sibling loop owners", () => Check(!LoopOwners([PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.WHILE, PrototypeOpcode.WEND]).Any())),
    ("warning-only ordering", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.IF, PrototypeOpcode.ELSE, PrototypeOpcode.ELSEIF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF])]); Check(p.Program.Descriptors[0].State == VmFunctionState.LinkedSemanticPending && p.StructuralDiagnostics.Count(x => x.Classification == StructuralClassification.ValidWithStructuralWarning) == 2); }),
    ("CASE warning ordering", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.CASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.ENDSELECT])]); Check(p.Program.Descriptors[0].State == VmFunctionState.LinkedSemanticPending && p.StructuralDiagnostics.Count(x => x.Classification == StructuralClassification.ValidWithStructuralWarning) == 2); }),
    ("SIF partial warning", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SIF, PrototypeOpcode.IF, PrototypeOpcode.ENDIF])]); Check(p.StructuralDiagnostics.Any(x => x.Message == "SIF-partial-next")); }),
    ("partial opcode set", () => Check(ControlLinker.IsLegacyPartialOpcode(PrototypeOpcode.PRINTDATA) && ControlLinker.IsLegacyPartialOpcode(PrototypeOpcode.DATAFORM) && !ControlLinker.IsLegacyPartialOpcode(PrototypeOpcode.ENDDATA))),
    ("malformed IF/loop cross", () => Check(ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.IF, PrototypeOpcode.NEXT, PrototypeOpcode.ENDIF])]).Program.Descriptors[0].State == VmFunctionState.InvalidStructure)),
    ("malformed SELECT close", () => Check(ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.ENDSELECT])]).Program.Descriptors[0].State == VmFunctionState.InvalidStructure)),
    ("malformed BREAK outside", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.BREAK])]); Check(p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure && RunLinked(p.Program, 0) == VmStopReason.InvalidStructure); }),
    ("no PC/index confusion", () => Check(LinkRows([PrototypeOpcode.FOR, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT]).All(x => x.TargetPc <= 3))),
    ("runtime-only catalog has no source", () => { var c = FunctionCatalog.FromRuntimeBindings([], [new RuntimeFunctionBinding(new(0), "RUNTIME_ONLY", FunctionKind.Normal, SourceIndexFlags.LineContinuation, true, CatalogSourceRef.None)]); Check(c.Count == 1 && !c.HasSourceDefinition(0) && !c.TryGetSourceDefinition(0, out _)); }),
    ("runtime-only resolver is code unavailable", () => { var c = FunctionCatalog.FromRuntimeBindings([], [new RuntimeFunctionBinding(new(0), "RUNTIME_ONLY", FunctionKind.Normal, SourceIndexFlags.LineContinuation, true, CatalogSourceRef.None)]); var r = FixedCallResolver.Resolve(c, "RUNTIME_ONLY", true, false); Check(r.FunctionResolved && !r.CodeAvailable && r.Reason == "CodeNotAvailable"); }),
    ("physical-only is not runtime catalog", () => { var c = FunctionCatalog.FromRuntimeBindings([], [new RuntimeFunctionBinding(new(0), "RUNTIME", FunctionKind.Normal, SourceIndexFlags.None, true, CatalogSourceRef.None)]); Check(c.FindByName("PHYSICAL_ONLY").Count == 0); }),
    ("remap missing rejects", () => Expect<InvalidOperationException>(() => RuntimeFunctionBinder.Remap([new(new(7), [], [])], new Dictionary<SourceFunctionId, RuntimeFunctionId>()))),
    ("remap duplicate rejects", () => Expect<InvalidOperationException>(() => RuntimeFunctionBinder.Remap([new(new(1), [], []), new(new(2), [], [])], new Dictionary<SourceFunctionId, RuntimeFunctionId> { [new(1)] = new(3), [new(2)] = new(3) }))),
    ("source/runtime domains are distinct", () => Check(!Equals(new SourceFunctionId(1), new RuntimeFunctionId(1)))),
    ("runtime catalog order is explicit", () => { var c = FunctionCatalog.FromRuntimeBindings([], [new(new(0), "FIRST", FunctionKind.Normal, SourceIndexFlags.None, true, CatalogSourceRef.None), new(new(1), "SECOND", FunctionKind.Normal, SourceIndexFlags.None, true, CatalogSourceRef.None)]); Check(FixedCallResolver.Resolve(c, "FIRST", true, false).RuntimeId.Value == 0); }),
    ("source ref absent API is safe", () => { var c = FunctionCatalog.FromRuntimeBindings([], [new(new(0), "R", FunctionKind.Normal, SourceIndexFlags.None, true, CatalogSourceRef.None)]); Check(c.GetPhysicalName(0) is null && c.GetSpan(0) is null && c.GetFileIdentity(0) is null); }),
    ("typed public API boundary", () => {
        var publicStatic = BindingFlags.Public | BindingFlags.Static;
        var publicInstance = BindingFlags.Public | BindingFlags.Instance;
        Check(typeof(VmInstruction).GetMethod("Call", publicStatic, null, [typeof(RuntimeFunctionId)], null) is not null);
        Check(typeof(VmInstruction).GetMethod("Call", publicStatic, null, [typeof(int)], null) is null);
        Check(typeof(VmInstruction).GetMethod("Jump", publicStatic, null, [typeof(RuntimeFunctionId)], null) is not null);
        Check(typeof(VmInstruction).GetMethod("Jump", publicStatic, null, [typeof(int)], null) is null);
        Check(typeof(VmMachine).GetMethod("Run", publicInstance, null, [typeof(RuntimeFunctionId), typeof(int)], null) is not null);
        Check(typeof(VmMachine).GetMethod("Run", publicInstance, null, [typeof(int), typeof(int)], null) is null);
        Check(typeof(FunctionCatalog).GetMethod("From" + "SourceIndex", publicStatic) is null);
    }),
};
var passed = 0;
foreach (var test in tests) try { test.Run(); passed++; Console.WriteLine($"PASS {test.Name}"); } catch (Exception ex) { Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
Console.WriteLine($"VmSelfTest: executed={tests.Count} passed={passed} failed={tests.Count - passed}");
return passed == tests.Count ? 0 : 1;

static void Check(bool value) { if (!value) throw new InvalidOperationException("assertion failed"); }
static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"expected {typeof(T).Name}"); }
static SourceSpan Span(int line) => new(0, 1, line, line);
static FunctionCatalog Catalog(string name) => FunctionCatalog.FromDefinitions([D(name, "test.erb", Span(1))]);
static FunctionDefinition D(string physicalName, string file, SourceSpan span, FunctionKind kind = FunctionKind.Normal, string? effectiveName = null) => new(physicalName, file, span, Kind: kind, EffectiveName: effectiveName ?? physicalName, EffectiveNameKnown: true);
static RuntimeFunctionPrototype RP(int id, IReadOnlyList<PrototypeOpcode> opcodes) => new(new(id), opcodes.Select(P).ToImmutableArray(), opcodes.Select(_ => "").ToImmutableArray());
static VmStopReason RunLinked(LinkedProgram program, int id) => new VmMachine(program).Run(new RuntimeFunctionId(id));
static VmStopReason Run(VmInstruction[] code, int maxSteps = 100_000) { var b = new VmSyntheticProgramBuilder(); var id = b.AddFunction(code); return new VmMachine(b.Build()).Run(new RuntimeFunctionId(id), maxSteps); }
static PrototypeInstruction P(PrototypeOpcode opcode) => new(opcode, PrototypeInstructionFlags.ControlFlow, 1, 2, 3);
static StructuralLinkRecord[] LinkRows(IReadOnlyList<PrototypeOpcode> opcodes) => ControlLinker.Link(Catalog("X"), [RP(0, opcodes)]).Program.StructuralLinks;
static StructuralLinkRecord[] LoopLink(IReadOnlyList<PrototypeOpcode> opcodes) => LinkRows(opcodes).Where(x => x.Kind == VmStructuralKind.Continue).ToArray();
static IEnumerable<int> LoopOwners(IReadOnlyList<PrototypeOpcode> opcodes) => LinkRows(opcodes).Where(x => x.Kind is VmStructuralKind.Break or VmStructuralKind.Continue).Select(x => x.LoopIndex);

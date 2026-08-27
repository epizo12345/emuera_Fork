using System.Runtime.InteropServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

var tests = new List<(string Name, Action Run)>
{
    ("layout 16/16/12", () => Check(Marshal.SizeOf<VmInstruction>() == 16 && Marshal.SizeOf<VmFunctionDescriptor>() == 16 && Marshal.SizeOf<VmFrame>() == 12)),
    ("PC is next instruction", () => Check(Run([VmInstruction.Branch(2), new((ushort)VmOpcode.SemanticBarrier), VmInstruction.Halt]) == VmStopReason.Halted)),
    ("PC==CodeLength implicit return", () => Check(Run([VmInstruction.Nop]) == VmStopReason.Returned)),
    ("empty function", () => Check(Run([]) == VmStopReason.Returned)),
    ("branch to end", () => Check(Run([VmInstruction.Branch(1)]) == VmStopReason.Returned)),
    ("branch past end invalid", () => Check(Run([VmInstruction.Branch(2)]) == VmStopReason.InvalidLocalPc)),
    ("CALL exact return PC", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Halted); }),
    ("CALL empty returns", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Halted); }),
    ("JUMP propagation", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Jump(1)); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Returned); }),
    ("nested implicit returns", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Call(2)); b.AddFunction(); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Halted); }),
    ("CodeAvailable vs ExecutableReady", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Nop); var p = b.Build(); p.Descriptors[a] = new(a, 0, 1, VmFunctionState.LinkedSemanticPending); Check(new VmMachine(p).Run(a) == VmStopReason.SemanticNotAvailable); }),
    ("real execution barrier", () => { var c = Catalog("X"); c.MarkCodeAvailable([0]); var link = ControlLinker.Link(c, [new(0, [P(PrototypeOpcode.PRINT)], ["x"])]); Check(link.Program.Descriptors[0].State == VmFunctionState.LinkedSemanticPending && new VmMachine(link.Program).Run(0) == VmStopReason.SemanticNotAvailable); }),
    ("effective-name catalog", () => { var c = FunctionCatalog.FromDefinitions([new("RAW", "a", Span(1), EffectiveName: "EFFECTIVE")]); Check(c.GetPhysicalName(0) == "RAW" && c.GetEffectiveName(0) == "EFFECTIVE" && c.FindByName("EFFECTIVE").SequenceEqual([0])); }),
    ("resolver unique normal", () => Check(FixedCallResolver.Resolve(Catalog("X"), "X", true, false).FunctionId == 0)),
    ("resolver first normal", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", Span(1)), new("X", "b", Span(2))]); Check(FixedCallResolver.Resolve(c, "X", true, false).FunctionId == 0); }),
    ("resolver method first is not skipped", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", Span(1), Kind: FunctionKind.Method), new("X", "b", Span(2))]); Check(FixedCallResolver.Resolve(c, "X", true, false).Reason == "WrongKindMethod"); }),
    ("resolver event compatibility", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", Span(1), Kind: FunctionKind.Event), new("X", "b", Span(2))]); Check(FixedCallResolver.Resolve(c, "X", true, false).Reason == "WrongKindEvent" && FixedCallResolver.Resolve(c, "X", true, true).FunctionId == 0); }),
    ("resolver normal before event", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", Span(1)), new("X", "b", Span(2), Kind: FunctionKind.Event)]); Check(FixedCallResolver.Resolve(c, "X", true, false).FunctionId == 0); }),
    ("resolver case sensitivity", () => { var c = FunctionCatalog.FromDefinitions([new("Ab", "a", Span(1))], ignoreCase: false); Check(FixedCallResolver.Resolve(c, "Ab", false, false).FunctionId == 0 && !FixedCallResolver.Resolve(c, "ab", false, false).FunctionResolved); }),
    ("resolver missing", () => Check(FixedCallResolver.Resolve(Catalog("X"), "Y", true, false).Reason == "MissingTarget")),
    ("rename resolution", () => { var c = FunctionCatalog.FromDefinitions([new("[[RAW]]", "a", Span(1), EffectiveName: "SET_BASE_5604")]); Check(FixedCallResolver.Resolve(c, "SET_BASE_5604", true, false).FunctionId == 0); }),
    ("operand span preserved", () => { var c = Catalog("X"); c.MarkCodeAvailable([0]); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [new(PrototypeOpcode.PRINT, PrototypeInstructionFlags.None, 1, 3, 4)], ["abcd"])]); Check(p.Program.Code[0].OperandOffset == 3 && p.Program.Code[0].OperandLength == 4); }),
    ("SIF links", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.SIF), P(PrototypeOpcode.PRINT)], ["", "x"])]); Check(p.Program.SifLinks.Length == 1 && p.Program.SifLinks[0].Pc == 0 && p.Program.SifLinks[0].FallthroughPc == 1 && p.Program.SifLinks[0].FalsePc == 2); }),
    ("SIF final malformed", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.SIF)], [""])]); Check(p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
    ("IF ordered clauses", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.IF), P(PrototypeOpcode.ELSEIF), P(PrototypeOpcode.ELSE), P(PrototypeOpcode.ENDIF)], ["", "", "", ""])]); Check(p.Program.IfGroups.Length == 1 && p.Program.IfGroups[0].ClauseCount == 3 && p.Program.IfGroups[0].ExitPc == 4 && p.Program.IfClauses.Select(x => x.Kind).SequenceEqual([VmStructuralKind.If, VmStructuralKind.ElseIf, VmStructuralKind.Else])); }),
    ("SELECT ordered cases", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.SELECTCASE), P(PrototypeOpcode.CASE), P(PrototypeOpcode.CASEELSE), P(PrototypeOpcode.ENDSELECT)], ["", "", "", ""])]); Check(p.Program.SelectGroups.Length == 1 && p.Program.SelectGroups[0].CaseCount == 2 && p.Program.SelectGroups[0].ExitPc == 4 && p.Program.SelectCases.Select(x => x.Kind).SequenceEqual([VmStructuralKind.Case, VmStructuralKind.CaseElse])); }),
    ("loop descriptors", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.FOR), P(PrototypeOpcode.CONTINUE), P(PrototypeOpcode.BREAK), P(PrototypeOpcode.NEXT)], ["", "", "", ""])]); Check(p.Program.Loops.Length == 1 && p.Program.Loops[0].HeaderPc == 0 && p.Program.Loops[0].BodyEntryPc == 1 && p.Program.Loops[0].EndPc == 3 && p.Program.Loops[0].ExitPc == 4 && p.Program.Loops[0].Flags == LoopDescriptorFlags.BreakAdvancesCounter && p.Program.StructuralLinks.Any(x => x.Kind == VmStructuralKind.Continue && x.TargetPc == 1 && x.LoopIndex == 0)); }),
    ("WHILE continue check", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.WHILE), P(PrototypeOpcode.CONTINUE), P(PrototypeOpcode.WEND)], ["", "", ""])]); Check(p.Program.StructuralLinks.Any(x => x.Kind == VmStructuralKind.Continue && x.TargetPc == 0 && x.AuxiliaryPc == 0)); }),
    ("DO continue check", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.DO), P(PrototypeOpcode.CONTINUE), P(PrototypeOpcode.LOOP)], ["", "", ""])]); Check(p.Program.StructuralLinks.Any(x => x.Kind == VmStructuralKind.Continue && x.TargetPc == 2 && x.AuxiliaryPc == 2)); }),
    ("malformed IF/loop cross", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.IF), P(PrototypeOpcode.NEXT), P(PrototypeOpcode.ENDIF)], ["", "", ""])]); Check(p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
    ("malformed SELECT close", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.ENDSELECT)], [""])]); Check(p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
    ("malformed BREAK outside", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.BREAK)], [""])]); Check(p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
    ("no PC/index confusion", () => { var c = Catalog("X"); var p = ControlLinker.Link(c, [new FunctionPrototype(0, [P(PrototypeOpcode.FOR), P(PrototypeOpcode.BREAK), P(PrototypeOpcode.NEXT)], ["", "", ""])]); Check(p.Program.StructuralLinks.All(x => x.TargetPc < 0 || x.TargetPc <= 3) && p.Program.StructuralLinks.Where(x => x.Kind == VmStructuralKind.Break).All(x => x.LoopIndex == 0)); }),
};
var passed = 0;
foreach (var test in tests) try { test.Run(); passed++; Console.WriteLine($"PASS {test.Name}"); } catch (Exception ex) { Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
Console.WriteLine($"VmSelfTest: executed={tests.Count} passed={passed} failed={tests.Count - passed}");
return passed == tests.Count ? 0 : 1;

static void Check(bool value) { if (!value) throw new InvalidOperationException("assertion failed"); }
static SourceSpan Span(int line) => new(0, 1, line, line);
static FunctionCatalog Catalog(string name) => FunctionCatalog.FromDefinitions([new(name, "test.erb", Span(1))]);
static VmStopReason Run(VmInstruction[] code, int maxSteps = 100_000) { var b = new VmSyntheticProgramBuilder(); var id = b.AddFunction(code); return new VmMachine(b.Build()).Run(id, maxSteps); }
static PrototypeInstruction P(PrototypeOpcode opcode) => new(opcode, PrototypeInstructionFlags.ControlFlow, 1, 2, 3);

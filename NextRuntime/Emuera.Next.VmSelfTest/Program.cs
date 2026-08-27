using System.Runtime.InteropServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

var tests = new List<(string Name, Action Run)>
{
    ("VmInstruction is 16 bytes", () => Check(Marshal.SizeOf<VmInstruction>() == 16)),
    ("VmDescriptor is 16 bytes", () => Check(Marshal.SizeOf<VmFunctionDescriptor>() == 16)),
    ("VmFrame is 12 bytes", () => Check(Marshal.SizeOf<VmFrame>() == 12)),
    ("sequential NOP HALT", () => Check(Run([VmInstruction.Nop, VmInstruction.Halt]) == VmStopReason.Halted)),
    ("branch forward", () => Check(Run([VmInstruction.Branch(2), new((ushort)VmOpcode.SemanticBarrier), VmInstruction.Halt]) == VmStopReason.Halted)),
    ("branch backward step limit", () => Check(Run([VmInstruction.Branch(0)], 4) == VmStopReason.StepLimit)),
    ("call returns to exact next PC", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Halted); }),
    ("nested calls", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Call(2), VmInstruction.Return); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Halted); }),
    ("recursion is growable", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(0), VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a, 5) == VmStopReason.StepLimit); }),
    ("code unavailable is explicit", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Return); var p = b.Build(); p.Descriptors[1] = new(1, 0, 0, VmFunctionState.CodeNotAvailable); Check(new VmMachine(p).Run(a) == VmStopReason.CodeNotAvailable); }),
    ("JUMP propagates one frame", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Jump(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Returned); }),
    ("nested CALL plus JUMP returns caller", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Call(1), VmInstruction.Halt); b.AddFunction(VmInstruction.Jump(2), VmInstruction.Halt); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Halted); }),
    ("multiple propagated JUMPs", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Jump(1)); b.AddFunction(VmInstruction.Jump(2)); b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Returned); }),
    ("invalid FunctionId", () => Check(Run([VmInstruction.Call(9)]) == VmStopReason.InvalidFunctionId)),
    ("invalid local PC", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Branch(9)); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.InvalidLocalPc); }),
    ("stack underflow is modeled", () => { var b = new VmSyntheticProgramBuilder(); var a = b.AddFunction(VmInstruction.Return); Check(new VmMachine(b.Build()).Run(a) == VmStopReason.Returned); }),
    ("semantic barrier stops", () => Check(Run([new((ushort)VmOpcode.SemanticBarrier)]) == VmStopReason.SemanticNotAvailable)),
    ("unsupported control stops", () => Check(Run([new((ushort)VmOpcode.UnsupportedControl)]) == VmStopReason.UnsupportedControl)),
    ("scanner trims and stops at form", () => { Check(FixedCallTargetScanner.TryScan("  Target (x)", out var s)); Check(s.Target == "Target"); }),
    ("resolver preserves duplicate order", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1)), new("X", "b", new(0, 1, 1, 1))]); Check(FixedCallResolver.Resolve(c, "X", true, false).FunctionId == 0); }),
    ("resolver separates method and event", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1), Kind: FunctionKind.Method), new("X", "b", new(0, 1, 1, 1), Kind: FunctionKind.Event)]); Check(!FixedCallResolver.Resolve(c, "X", true, false).FunctionResolved); Check(FixedCallResolver.Resolve(c, "X", true, true).FunctionId == 1); }),
    ("catalog assigns physical duplicate IDs", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1)), new("X", "a", new(1, 2, 2, 2))]); Check(c.Count == 2 && c.FindByName("X").SequenceEqual([0, 1])); }),
    ("linker creates semantic barrier", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1))]); c.MarkCodeAvailable([0]); var p = ControlLinker.Link(c, [new(0, [new(PrototypeOpcode.PRINT, PrototypeInstructionFlags.None, 1, 0, 0)], [""])]); Check(p.SemanticBarriers == 1 && p.Program.Code[0].Opcode == (ushort)VmOpcode.SemanticBarrier); }),
    ("linker pairs IF and ENDIF", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1))]); c.MarkCodeAvailable([0]); var p = ControlLinker.Link(c, [new(0, [P(PrototypeOpcode.IF), P(PrototypeOpcode.ENDIF)], ["", ""])]); Check(p.Diagnostics.Count == 0 && p.Program.StructuralLinks[0].TargetPc == 1); }),
    ("linker rejects malformed close", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1))]); c.MarkCodeAvailable([0]); var p = ControlLinker.Link(c, [new(0, [P(PrototypeOpcode.ELSE)], [""])]); Check(p.Diagnostics.Count == 1 && p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
    ("linker rejects missing loop close", () => { var c = FunctionCatalog.FromDefinitions([new("X", "a", new(0, 1, 1, 1))]); c.MarkCodeAvailable([0]); var p = ControlLinker.Link(c, [new(0, [P(PrototypeOpcode.REPEAT)], [""])]); Check(p.Diagnostics.Count == 1 && p.Program.Descriptors[0].State == VmFunctionState.InvalidStructure); }),
};
var passed = 0;
foreach (var test in tests) try { test.Run(); passed++; Console.WriteLine($"PASS {test.Name}"); } catch (Exception ex) { Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
Console.WriteLine($"VmSelfTest: executed={tests.Count} passed={passed} failed={tests.Count - passed}");
return passed == tests.Count ? 0 : 1;

static void Check(bool value) { if (!value) throw new InvalidOperationException("assertion failed"); }
static VmStopReason Run(VmInstruction[] code, int maxSteps = 100_000) { var b = new VmSyntheticProgramBuilder(); var id = b.AddFunction(code); return new VmMachine(b.Build()).Run(id, maxSteps); }
static PrototypeInstruction P(PrototypeOpcode opcode) => new(opcode, PrototypeInstructionFlags.ControlFlow, 1, 0, 0);

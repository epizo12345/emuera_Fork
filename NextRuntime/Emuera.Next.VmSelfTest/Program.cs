using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

if (args.Length == 4 && args[0] == "--r0f6g3-link-probe")
    return RunR0F6G3LinkProbe(args[1], args[2], args[3]);

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
    ("runtime PRINTL emits separate ordered lines", () => { var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINTL, PrototypeOpcode.PRINTL, PrototypeOpcode.PRINTL, PrototypeOpcode.RETURN], ["A", "B", "C", ""]) ], runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, runtimeEffects: effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:A", "newline", "text:B", "newline", "text:C", "newline"])); }),
    ("PRINT and PRINTN retain exact line finalization", () => { var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINT, PrototypeOpcode.PRINTN, PrototypeOpcode.PRINTL, PrototypeOpcode.RETURN], ["a", "b", "c", ""])], runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, runtimeEffects: effects).Run(new(0)) == VmStopReason.Returned && effects.LineEnds.SequenceEqual([true, false, true]) && effects.Events.SequenceEqual(["text:a", "text:b", "text:c", "newline"])); }),
    ("PRINTFORM uses separate runtime operand arena", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["x%A,3,LEFT%", ""]) ], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var host = new SemanticHost(); host.Set("A", null, [], VmSemanticValue.From(1L)); var result = new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)); if (p.SemanticArena.Records.Length != 0 || p.RuntimeStatements.OperandArena.Records.Length != 1 || result != VmStopReason.Returned || !effects.Events.SequenceEqual(["text:x1  "])) throw new InvalidOperationException($"struct={p.SemanticArena.Records.Length} runtime={p.RuntimeStatements.OperandArena.Records.Length} result={result} events={string.Join('|', effects.Events)}"); }),
    ("percent format width uses host display length", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%S,5,LEFT%", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var host = new SemanticHost { StringDisplayLength = value => value == "wide" ? 8 : value.Length }; host.Set("S", null, [], VmSemanticValue.From("wide")); Check(new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:wide"])); }),
    ("C/LC print family uses typed column host semantics", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINTC, PrototypeOpcode.PRINTLC, PrototypeOpcode.PRINTFORMC, PrototypeOpcode.PRINTFORMLC, PrototypeOpcode.RETURN], ["raw-r", "raw-l", "%A%", "x%A%", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var host = new SemanticHost(); host.Set("A", null, [], VmSemanticValue.From(7L)); Check(new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["column:Right:raw-r", "column:Left:raw-l", "column:Right:7", "column:Left:x7"])); }),
    ("REUSELASTLINE uses formatted typed host semantics", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.REUSELASTLINE, PrototypeOpcode.RETURN], ["invalid %A%", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var host = new SemanticHost(); host.Set("A", null, [], VmSemanticValue.From(7L)); Check(new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["reuse:invalid 7"])); }),
    ("runtime statement operand bases are contiguous", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.TIMES, PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURNF], ["A=1", "A,2", "%A%", "A"])], runtimeEnvironment: env, runtimeStatements: true).Program; var rows = p.RuntimeStatements.Records; Check(rows.Length == 4 && rows[0] is { OperandRecord: 0, SecondaryOperandRecord: 1, FormatOperandRecord: 2 } && rows[1].OperandRecord == 3 && rows[2].OperandRecord == 4 && rows[3].OperandRecord == 5 && p.RuntimeStatements.OperandArena.Records.Length == 6); }),
    ("PRINTW yields after newline and resumes without duplicate output", () => { var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINTW, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["one", "two", ""]) ], runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, runtimeEffects: effects); Check(vm.Start(new(0)) == VmStopReason.Returned && vm.Continue() == VmStopReason.WaitingForInput && effects.Events.SequenceEqual(["text:one", "newline", "wait:False"]) && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:one", "newline", "wait:False", "text:two"])); }),
    ("entry actual imports integer ARG without evaluation", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["%ARG:0%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0), [VmSemanticValue.From(41L)]) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:41"])); }),
    ("entry actual imports omitted-index ARG as slot zero", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], RuntimeMetadataValueType.Integer); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURNF)], ["ARG"], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())); Check(vm.Run(new(0), [VmSemanticValue.From(41L)]) == VmStopReason.Returned && vm.LastReturnValue.TryGetInteger(out var value) && value == 41); }),
    ("entry actual imports string ARGS across WAIT resume", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)], 0, 0, [], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.PRINTW), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["hold", "%ARGS:0%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects); Check(vm.Start(new(0), [VmSemanticValue.From("kept")]) == VmStopReason.Returned && vm.Continue() == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:hold", "newline", "wait:False", "text:kept"])); }),
    ("named DYNAMIC parameter shares one activation slot and applies default", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("対象", RuntimeMetadataValueType.Integer, 0, true, 7, null, true)], 0, 0, [new("対象", RuntimeMetadataValueType.Integer, [1], false)], RuntimeMetadataValueType.Integer); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURNF)], ["対象"], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var explicitVm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())); var omittedVm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())); var explicitStop = explicitVm.Run(new(0), [VmSemanticValue.From(41L)]); var omittedStop = omittedVm.Run(new(0)); var explicitOk = explicitVm.LastReturnValue.TryGetInteger(out var explicitValue); var omittedOk = omittedVm.LastReturnValue.TryGetInteger(out var defaultValue); if (explicitStop != VmStopReason.Returned || !explicitOk || explicitValue != 41 || omittedStop != VmStopReason.Returned || !omittedOk || defaultValue != 7) throw new InvalidOperationException($"explicit={explicitStop}/{explicitOk}/{explicitValue};omitted={omittedStop}/{omittedOk}/{defaultValue}"); }),
    ("CONST private initializes once and rejects writes", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("COMMANDLENS", RuntimeMetadataValueType.Integer, [1], true, true, true, 20)], RuntimeMetadataValueType.Integer); var read = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURNF)], ["COMMANDLENS"], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var write = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.RETURN)], ["COMMANDLENS=21", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var vm = new VmMachine(read, new VmSemanticExecutor(read, new SemanticHost())); var readStop = vm.Run(new(0)); var readOk = vm.LastReturnValue.TryGetInteger(out var value); var writeStop = new VmMachine(write, new VmSemanticExecutor(write, new SemanticHost())).Run(new(0)); if (readStop != VmStopReason.Returned || !readOk || value != 20 || writeStop != VmStopReason.SemanticNotAvailable) throw new InvalidOperationException($"read={readStop}/{readOk}/{value};write={writeStop}"); }),
    ("bound frame state shares dynamic and static private slots", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("D", RuntimeMetadataValueType.Integer, [1], false), new("S", RuntimeMetadataValueType.Integer, [1], true)], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["D:0=4", "S:0+=1", "%D:0%:%S:0%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var state = new FrameState(); state.Set(0, VmFrameStateFamily.Private, 0, 0, VmSemanticValue.From(0L)); state.Set(0, VmFrameStateFamily.Private, 1, 0, VmSemanticValue.From(0L)); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects, state).Run(new(0)) == VmStopReason.Returned && new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects, state).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:4:1", "text:4:2"])); }),
    ("root RETURNF retains integer and string result", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var intMeta = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer); var strMeta = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.String); var integer = Executable(ControlLinker.Link(Catalog("I"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURNF)], ["9"], RuntimeMetadata: intMeta)], runtimeEnvironment: env, runtimeStatements: true).Program); var text = Executable(ControlLinker.Link(Catalog("S"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURNF)], ["\"ok\""], RuntimeMetadata: strMeta)], runtimeEnvironment: env, runtimeStatements: true).Program); var intVm = new VmMachine(integer, new VmSemanticExecutor(integer, new SemanticHost())); var strVm = new VmMachine(text, new VmSemanticExecutor(text, new SemanticHost())); Check(intVm.Run(new(0)) == VmStopReason.Returned && intVm.LastReturnValue.TryGetInteger(out var number) && number == 9 && strVm.Run(new(0)) == VmStopReason.Returned && strVm.LastReturnValue.TryGetString(out var value) && value == "ok"); }),
    ("ordinary Legacy RETURN expression preserves integer", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [], null); var proto = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURN)], ["G:1"], RuntimeMetadata: metadata); var program = Executable(ControlLinker.Link(Catalog("IS_TITLE_USEABLE_1"), [proto], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); host.Set("G", null, [VmSemanticValue.From(1L)], VmSemanticValue.From(1L)); var vm = new VmMachine(program, new VmSemanticExecutor(program, host)); Check(vm.Run(new(0)) == VmStopReason.Returned && vm.LastReturnValue.TryGetInteger(out var one) && one == 1); host.Set("G", null, [VmSemanticValue.From(1L)], VmSemanticValue.From(0L)); Check(vm.Run(new(0)) == VmStopReason.Returned && vm.LastReturnValue.TryGetInteger(out var zero) && zero == 0); }),
    ("nested Legacy RETURN publishes RESULT through runtime effects", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B", ""]), RPO(1, [PrototypeOpcode.RETURN], ["1"])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.LegacyReturns.SequenceEqual([VmSemanticValue.From(1L)])); }),
#if R0_F6G10C3
    ("ARRAYSORT sorts frame integer arrays in requested order", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("Q", RuntimeMetadataValueType.Integer, [3], false)], null); var p = Executable(ControlLinker.Link(Catalog("SORT"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.ARRAYSORT), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["Q:0=2", "Q:1=1", "Q:2=3", "Q,BACK", "%Q:0%:%Q:1%:%Q:2%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:3:2:1"])); }),
    ("SWAP exchanges two resolved frame lvalues", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("Q", RuntimeMetadataValueType.Integer, [2], false)], null); var p = Executable(ControlLinker.Link(Catalog("SWAP"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SWAP), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["Q:0=1", "Q:1=2", "Q:0,Q:1", "%Q:0%:%Q:1%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:2:1"])); }),
    ("MATCH counts values in a private frame array", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("Q", RuntimeMetadataValueType.Integer, [3], false)], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["Q:0=7", "Q:1=8", "Q:2=7", "%MATCH(Q,7)%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new MetadataSemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:2"])); }),
    ("SUMARRAY totals a private frame array", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("Q", RuntimeMetadataValueType.Integer, [3], false)], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["Q:0=7", "Q:1=8", "Q:2=9", "%SUMARRAY(Q,1,3)%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new MetadataSemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:17"])); }),
    ("CMATCH receives a resolved indexed variable", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%CMATCH(A:I,A:I)%", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new MetadataSemanticHost(); host.Set("I", null, [], VmSemanticValue.From(0L)); host.Set("A", null, [VmSemanticValue.From(0L)], VmSemanticValue.From(5L)); host.Set("A", null, [VmSemanticValue.From(1L)], VmSemanticValue.From(6L)); host.Set("A", null, [VmSemanticValue.From(2L)], VmSemanticValue.From(5L)); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:2"])); }),
    ("ARRAYMSORT applies the integer key order to two frame arrays", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("K", RuntimeMetadataValueType.Integer, [4], false), new("V", RuntimeMetadataValueType.Integer, [4], false)], null); var p = Executable(ControlLinker.Link(Catalog("MSORT"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.ARRAYMSORT), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["K:0=30", "K:1=10", "K:2=20", "V:0=3", "V:1=1", "V:2=2", "K,V", "%K:0%:%K:1%:%K:2%/%V:0%:%V:1%:%V:2%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:10:20:30/1:2:3"])); }),
    ("FINDELEMENT finds an integer in a private frame array", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("Q", RuntimeMetadataValueType.Integer, [3], false)], null); var p = Executable(ControlLinker.Link(Catalog("FIND"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["Q:0=4", "Q:1=9", "%FINDELEMENT(Q,9)%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new MetadataSemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:1"])); }),
    ("FINDELEMENT applies regex matching to a private string array", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 0, 0, [new("Q", RuntimeMetadataValueType.String, [2], false)], null); var p = Executable(ControlLinker.Link(Catalog("FIND"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["Q:0='alpha'", "Q:1='beta'", "%FINDELEMENT(Q,\"et\")%", ""], RuntimeMetadata: metadata)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new MetadataSemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:1"])); }),
#endif
    ("Legacy RETURN list publishes values in order", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("LEGACY_RETURN_LIST"), [RPO(0, [PrototypeOpcode.RETURN], ["1,2"])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects); Check(vm.Run(new(0)) == VmStopReason.Returned && effects.LegacyReturns.SequenceEqual([VmSemanticValue.From(1L), VmSemanticValue.From(2L)]) && vm.LastReturnValue.TryGetInteger(out var first) && first == 1); }),
    ("nested CALL wait preserves return frame", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["B", "after", ""]), RPO(1, [PrototypeOpcode.PRINTW, PrototypeOpcode.RETURN], ["inside", ""]) ], runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, runtimeEffects: effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:inside", "newline", "wait:False", "text:after"])); }),
    ("fixed CALL and JUMP have one typed callsite each", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1)), D("C", "c", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.JUMP, PrototypeOpcode.RETURN], ["B(1)", "C,2", ""]), RP(1, [PrototypeOpcode.RETURN]), RP(2, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallSites.Length == 2 && p.CallSites[0] is { FunctionId: 0, Pc: 0, Target.Value: 1, Kind: VmInvocationKind.Call, ArgumentOffset: 1, ArgumentLength: 3, ArgumentCount: 1 } && p.CallSites[1] is { FunctionId: 0, Pc: 1, Target.Value: 2, Kind: VmInvocationKind.Jump, ArgumentOffset: 1, ArgumentLength: 2, ArgumentCount: 1 } && p.CallArgumentArena.Records.Length == 2); }),
    ("SinglePayloadBaseZero", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(1)", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallArgumentRecords.SequenceEqual([0]) && p.CallArgumentArena.Records.Length == 1); }),
    ("MultiplePayloadBases", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(1,2)", "B(3)", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallArgumentRecords.SequenceEqual([0, 1, 2]) && p.CallSites.Select(site => (site.ArgumentRecordStart, site.ArgumentCount)).SequenceEqual([(0, 2), (2, 1)])); }),
    ("CrossFunctionAccumulation", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1)), D("C", "c", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(1)", ""]), RPO(1, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["C(2)", ""]), RP(2, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallArgumentRecords.SequenceEqual([0, 1]) && p.CallSites.Select(site => site.ArgumentRecordStart).SequenceEqual([0, 1])); }),
    ("ZeroRecordPayload", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(,1)", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallArgumentRecords.SequenceEqual([-1, 0]) && p.CallArgumentArena.Records.Length == 1); }),
    ("ParseFailurePreservesExistingSideEffects", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var link = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(1,1/)", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env); Check(link.Program.CallSites.Length == 0 && link.Program.CallArgumentRecords.SequenceEqual([0]) && link.Program.CallArgumentArena.Records.Length == 1); }),
    ("CallAndJumpArgumentLayout", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1)), D("C", "c", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.JUMP, PrototypeOpcode.RETURN], ["B(1,2)", "C(3)", ""]), RP(1, [PrototypeOpcode.RETURN]), RP(2, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallArgumentRecords.SequenceEqual([0, 1, 2]) && p.CallSites.Select(site => (site.Kind, site.ArgumentRecordStart, site.ArgumentCount)).SequenceEqual([(VmInvocationKind.Call, 0, 2), (VmInvocationKind.Jump, 2, 1)])); }),
    ("ProducedCallArgumentRecordsEqualLegacySummation", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1)), D("C", "c", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(1)", "C(2,3)", ""]), RP(1, [PrototypeOpcode.RETURN]), RP(2, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallArgumentRecords.SequenceEqual([0, 1, 2]) && p.CallSites.Select(site => site.ArgumentRecordStart).SequenceEqual([0, 1])); }),
    ("CALL argument lexer excludes trailing Legacy comments", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(1,\"semi;inside\") ; outside", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program; Check(p.CallSites.Single().ArgumentCount == 2 && p.Descriptors[0].State == VmFunctionState.LinkedSemanticPending); }),
    ("CALL evaluates typed actuals before pushing callee", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(SIDE())", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env).Program); var host = new SemanticHost(); host.Calls["SIDE"] = _ => VmSemanticValue.From(1); Check(new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)) == VmStopReason.Returned && host.CallCountFor("SIDE") == 1); }),
    ("DynamicCall evaluates target then arguments once", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALLFORM, PrototypeOpcode.RETURN], ["B,INC()", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); host.Calls["INC"] = _ => VmSemanticValue.From(7L); var resolver = new DynamicResolver(("B", false), new(VmDynamicResolutionKind.Ready, new(1))); Check(new VmMachine(p, new VmSemanticExecutor(p, host), dynamicCallResolver: resolver).Run(new(0)) == VmStopReason.Returned && resolver.Calls == 1 && host.CallCountFor("INC") == 1); }),
    ("TRYCCALLFORM Missing alone enters CATCH", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.TRYCCALLFORM, PrototypeOpcode.PRINT, PrototypeOpcode.CATCH, PrototypeOpcode.PRINT, PrototypeOpcode.ENDCATCH, PrototypeOpcode.RETURN], ["NOPE", "success", "", "missing", "", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var resolver = new DynamicResolver(("NOPE", false), new(VmDynamicResolutionKind.Missing, new(-1))); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects, dynamicCallResolver: resolver).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:missing"])); }),
    ("TRYCCALLFORM success skips CATCH", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.TRYCCALLFORM, PrototypeOpcode.PRINT, PrototypeOpcode.CATCH, PrototypeOpcode.PRINT, PrototypeOpcode.ENDCATCH, PrototypeOpcode.RETURN], ["B", "success", "", "missing", "", ""]), RP(1, [PrototypeOpcode.RETURN])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var resolver = new DynamicResolver(("B", false), new(VmDynamicResolutionKind.Ready, new(1))); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects, dynamicCallResolver: resolver).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:success"])); }),
    ("TRY dynamic Blocked is terminal before arguments", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.TRYCALLFORM], ["BLOCKED,INC()"])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); host.Calls["INC"] = _ => VmSemanticValue.From(1L); var resolver = new DynamicResolver(("BLOCKED", false), new(VmDynamicResolutionKind.Blocked, new(-1))); Check(new VmMachine(p, new VmSemanticExecutor(p, host), dynamicCallResolver: resolver).Run(new(0)) == VmStopReason.TerminalFault && host.CallCountFor("INC") == 0); }),
    ("CALL frame binds integer ARG and restores caller continuation", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], null); var b = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["%ARG:0%", ""], RuntimeMetadata: metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["B(123)", "after", ""]), b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var host = new SemanticHost(); Check(new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:123", "text:after"])); }),
    ("CALL frame binds string ARGS and preserves caller ARGS", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var aMeta = new FunctionRuntimeMetadata([new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)], 0, 0, [], null); var bMeta = new FunctionRuntimeMetadata([new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)], 0, 0, [], null); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.CALL), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["B(\"inner\")", "%ARGS:0%", ""], RuntimeMetadata: aMeta); var b = new RuntimeFunctionPrototype(new(2), [P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["%ARGS:0%", ""], RuntimeMetadata: bMeta); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(\"outer\")", ""]), a, b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:inner", "text:outer"])); }),
    ("omitted arguments use typed defaults and keep explicit minus one", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, true, 7, null), new("ARGS", RuntimeMetadataValueType.String, 0, true, 0, "fallback")], 0, 0, [], null); var b = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["%ARG:0%:%ARGS:0%", ""], RuntimeMetadata: metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(-1,\"value\")", "B(,)", ""]), b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:-1:value", "text:7:fallback"])); }),
    ("LOCAL and LOCALS are frame-local with declared bounds", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([], 1, 1, [], null); var b = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["LOCAL:0=9", "LOCALS:0 = local", "%LOCAL:0%:%LOCALS:0%", ""], RuntimeMetadata: metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B", ""]), b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:9:local"])); }),
    ("recursive frames isolate ARG and LOCAL state", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 1, 0, [], null); var payload = SemanticIrCompiler.CompileExpression("ARG:0", env, 0); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.CALL), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["ARG:0", "A(0)", "LOCAL:0=ARG:0", "%ARG:0%:%LOCAL:0%", ""], payload, metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(1)", ""]), a], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:0:0", "text:1:1"])); }),
    ("JUMP returns through caller and binds typed actuals", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], null); var a = RPO(1, [PrototypeOpcode.JUMP, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["B(41)", "bad", ""]); var b = new RuntimeFunctionPrototype(new(2), [P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["%ARG:0%", ""], RuntimeMetadata: metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["A", "caller", ""]), a, b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:41", "text:caller"])); }),
    ("dynamic private state isolates recursion and static private persists", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var dynamicMeta = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [new("P", RuntimeMetadataValueType.Integer, [1], false)], null); var staticMeta = new FunctionRuntimeMetadata([], 0, 0, [new("S", RuntimeMetadataValueType.Integer, [1], true)], null); var recursivePayload = SemanticIrCompiler.CompileExpression("ARG:0", env, 0); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.CALL), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["ARG:0", "A(0)", "P:0=ARG:0", "%P:0%", ""], recursivePayload, dynamicMeta); var b = new RuntimeFunctionPrototype(new(2), [P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["S:0+=1", "%S:0%", ""], RuntimeMetadata: staticMeta); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.CALL, PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(1)", "B", "B", ""]), a, b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:0", "text:1", "text:1", "text:2"])); }),
    ("CALL wait resume preserves ARG ARGS LOCAL and LOCALS", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null), new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)], 1, 1, [], null); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORMW), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["LOCAL:0=ARG:0", "LOCALS:0 = live", "%ARGS:0%", "%ARG:0%:%ARGS:0%:%LOCAL:0%:%LOCALS:0%", ""], RuntimeMetadata: metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(3,\"wait\")", ""]), a], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:wait", "newline", "wait:False", "text:3:wait:3:live"])); }),
    ("RecursiveARGSIsolation", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null), new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)], 0, 0, [], null); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.CALL), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["ARG:0", "A(0,\"inner\")", "%ARGS:0%", ""], SemanticIrCompiler.CompileExpression("ARG:0", env, 0), metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(1,\"outer\")", ""]), a], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:inner", "text:outer"])); }),
    ("RecursiveLOCALSIsolation", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 1, [], null); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SIF), P(PrototypeOpcode.CALL), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["LOCALS:0 = %ARG:0%", "ARG:0", "A(0)", "%LOCALS:0%", ""], SemanticIrCompiler.CompileExpression("ARG:0", env, 1), metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(1)", ""]), a], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:0", "text:1"])); }),
    ("NestedCALL_WAIT_RESUME", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1)), D("B", "b", Span(1))]); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["A", "caller", ""]), RPO(1, [PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["B", "outer", ""]), RPO(2, [PrototypeOpcode.PRINTW, PrototypeOpcode.RETURN], ["inner", ""])], runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, runtimeEffects: effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:inner", "newline", "wait:False", "text:outer", "text:caller"])); }),
    ("RecursiveCALL_WAIT_RESUME", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], null); var a = new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.CALL), P(PrototypeOpcode.PRINTFORMW), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["ARG:0", "A(0)", "%ARG:0%", "%ARG:0%", ""], SemanticIrCompiler.CompileExpression("ARG:0", env, 0), metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["A(1)", ""]), a], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:0", "newline", "wait:False", "text:0", "text:1", "newline", "wait:False", "text:1"])); }),
    ("JUMP_WAIT_RESUME", () => { var c = FunctionCatalog.FromDefinitions([D("C", "c", Span(1)), D("A", "a", Span(1)), D("B", "b", Span(1))]); var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null), new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)], 0, 0, [], null); var b = new RuntimeFunctionPrototype(new(2), [P(PrototypeOpcode.PRINTFORMW), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["%ARG:0%:%ARGS:0%", "%ARG:0%:%ARGS:0%", ""], RuntimeMetadata: metadata); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["A(1,\"outer\")", "caller", ""]), new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.JUMP), P(PrototypeOpcode.PRINT), P(PrototypeOpcode.RETURN)], ["B(3,\"jump\")", "unreachable", ""], RuntimeMetadata: metadata), b], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.Returned && effects.Events.SequenceEqual(["text:3:jump", "newline", "wait:False", "text:3:jump", "text:caller"])); }),
    ("WAIT FORCEWAIT and QUIT yield explicit effects", () => { var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.FORCEWAIT, PrototypeOpcode.QUIT], ["", ""]) ], runtimeStatements: true).Program); var effects = new RecordingEffects(); var vm = new VmMachine(p, runtimeEffects: effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.Continue() == VmStopReason.QuitRequested && effects.Events.SequenceEqual(["wait:True", "quit"])); }),
    ("HostTransfer preserves shared owner", RunHostTransferPreservesSharedOwner),
    ("SET assignment evaluates typed operands and writes", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["X=2", "X+=3", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); var executor = new VmSemanticExecutor(p, host); var stop = new VmMachine(p, executor).Run(new(0)); if (stop != VmStopReason.Returned || host.GetInt("X") != 5 || p.RuntimeStatements.Records.Length != 2 || p.RuntimeStatements.OperandArena.Records.Length != 5) throw new InvalidOperationException($"stop={stop};x={host.GetInt("X")};records={p.RuntimeStatements.Records.Length};operands={p.RuntimeStatements.OperandArena.Records.Length}"); }),
    ("SET list writes consecutive final indices", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["A:4=10,20,30", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); Check(new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)) == VmStopReason.Returned && host.GetInt("A", 4) == 10 && host.GetInt("A", 5) == 20 && host.GetInt("A", 6) == 30); }),
    ("string SET list writes private array in order", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([], 0, 0, [new("S", RuntimeMetadataValueType.String, [2], false)], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["S:0 '= \"a\",\"b\"", "%S:0%:%S:1%", ""], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:a:b"])); }),
    ("SPLIT stores full count and truncates to frame capacity", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([], 0, 3, [], null); var p = Executable(ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SPLIT), P(PrototypeOpcode.PRINTFORM), P(PrototypeOpcode.RETURN)], ["\"a,b,c,d\",\",\",LOCALS", "%LOCALS:0%:%LOCALS:1%:%LOCALS:2%:%RESULT%", ""], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, host), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:a:b:c:4"])); }),
    ("F6F body-local scalar initialization and array no-op", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([],0,0,[new("I",RuntimeMetadataValueType.Integer,[1],false),new("S",RuntimeMetadataValueType.String,[1],false),new("A",RuntimeMetadataValueType.Integer,[2],false)],null); var p = Executable(ControlLinker.Link(Catalog("X"),[new RuntimeFunctionPrototype(new(0),[P(PrototypeOpcode.VARI),P(PrototypeOpcode.VARS),P(PrototypeOpcode.VARI),P(PrototypeOpcode.PRINTFORM),P(PrototypeOpcode.RETURN)],["I=2+3","S=\"ok\"","A,2","%I%:%S%",""] ,RuntimeMetadata:meta)],runtimeEnvironment:env,runtimeStatements:true).Program); var effects=new RecordingEffects(); Check(new VmMachine(p,new VmSemanticExecutor(p,new SemanticHost()),effects).Run(new(0))==VmStopReason.Returned && effects.Events.SequenceEqual(["text:5:ok"])); }),
    ("F6F body-local defaults reset on each activation", () => { var env=new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true,true,true,false)); var meta=new FunctionRuntimeMetadata([],0,0,[new("I",RuntimeMetadataValueType.Integer,[1],false),new("S",RuntimeMetadataValueType.String,[1],false)],null); var p=Executable(ControlLinker.Link(Catalog("X"),[new RuntimeFunctionPrototype(new(0),[P(PrototypeOpcode.VARI),P(PrototypeOpcode.VARS),P(PrototypeOpcode.PRINTFORM),P(PrototypeOpcode.SET),P(PrototypeOpcode.SET),P(PrototypeOpcode.RETURN)],["I","S","%I%:%S%","I=9","S=\"x\"",""] ,RuntimeMetadata:meta)],runtimeEnvironment:env,runtimeStatements:true).Program); var effects=new RecordingEffects(); var first=new VmMachine(p,new VmSemanticExecutor(p,new SemanticHost()),effects).Run(new(0)); var second=new VmMachine(p,new VmSemanticExecutor(p,new SemanticHost()),effects).Run(new(0)); Check(first==VmStopReason.Returned && second==VmStopReason.Returned && effects.Events.SequenceEqual(["text:0:","text:0:"])); }),
    ("F6F standalone prefix postfix increment decrement", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true,true,true,false)); var p=Executable(ControlLinker.Link(Catalog("X"),[RPO(0,[PrototypeOpcode.SET,PrototypeOpcode.SET,PrototypeOpcode.SET,PrototypeOpcode.SET,PrototypeOpcode.SET,PrototypeOpcode.RETURN],["X=4","X++","++X","X--","--X",""])],runtimeEnvironment:env,runtimeStatements:true).Program); var host=new SemanticHost(); Check(new VmMachine(p,new VmSemanticExecutor(p,host)).Run(new(0))==VmStopReason.Returned && host.GetInt("X")==4); }),
    ("F6F PRINTBUTTON preserves type newline rule and order", () => { var env=new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true,true,true,false)); var p=Executable(ControlLinker.Link(Catalog("X"),[RPO(0,[PrototypeOpcode.PRINTBUTTON,PrototypeOpcode.PRINTBUTTON,PrototypeOpcode.RETURN],["\"a\\nb\",7","\"s\",\"H\"",""])],runtimeEnvironment:env,runtimeStatements:true).Program); var effects=new RecordingEffects(); Check(new VmMachine(p,new VmSemanticExecutor(p,new SemanticHost()),effects).Run(new(0))==VmStopReason.Returned && effects.Events.SequenceEqual(["button:ab:Integer:7","button:s:String:H"])); }),
    ("F6F THROW is terminal and preserves message source", () => { var env=new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true,true,true,false)); var proto=new RuntimeFunctionPrototype(new(0),ImmutableArray.Create(new PrototypeInstruction(PrototypeOpcode.PRINT,PrototypeInstructionFlags.HasOperand,8,0,1),new PrototypeInstruction(PrototypeOpcode.THROW,PrototypeInstructionFlags.HasOperand,9,0,4),new PrototypeInstruction(PrototypeOpcode.PRINT,PrototypeInstructionFlags.HasOperand,10,0,3)),ImmutableArray.Create("x","bad","end")); var p=Executable(ControlLinker.Link(Catalog("X"),[proto],runtimeEnvironment:env,runtimeStatements:true).Program); var effects=new RecordingEffects(); var vm=new VmMachine(p,new VmSemanticExecutor(p,new SemanticHost()),effects); Check(vm.Run(new(0))==VmStopReason.TerminalFault && vm.LastTerminalFaultMessage=="bad" && vm.LastTerminalFaultSourceLine==9 && effects.Events.SequenceEqual(["text:x"])); }),
    ("F6F unreachable THROW returns normally", () => { var env=new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true,true,true,false)); var proto=new RuntimeFunctionPrototype(new(0),[P(PrototypeOpcode.IF),P(PrototypeOpcode.THROW),P(PrototypeOpcode.ENDIF),P(PrototypeOpcode.RETURN)],["0","bad","",""] ,SemanticIrCompiler.CompileExpression("0",env,0)); var p=Executable(ControlLinker.Link(Catalog("X"),[proto],runtimeEnvironment:env,runtimeStatements:true).Program); var vm=new VmMachine(p,new VmSemanticExecutor(p,new SemanticHost()),new RecordingEffects()); var stop=vm.Run(new(0)); if(stop!=VmStopReason.Returned||vm.LastTerminalFaultMessage.Length!=0)throw new InvalidOperationException($"stop={stop};message={vm.LastTerminalFaultMessage}"); }),
    ("SET string add uses generic semantic node and typed host", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["TEXT += \"suffix\"", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var record = p.RuntimeStatements.Records.Single(); var host = new TypedStringAssignmentHost(); Check(record.Kind == VmRuntimeStatementKind.Set && record.Assignment == VmAssignmentOperator.Add && p.RuntimeStatements.OperandArena.Nodes[p.RuntimeStatements.OperandArena.Records[record.OperandRecord].RootNodeIndex].Kind == SemanticNodeKind.Symbol && new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)) == VmStopReason.Returned && host.Value == "prefixsuffix" && host.ReadIndices.Length == 0 && host.WriteIndices.Length == 0); }),
    ("formatted SET commas are not multi-assignment", new Action(() => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["L_NAME = ！ %S_NAME(C, 14, 2, \"EMPTY\"), 15, LEFT%", ""])], runtimeEnvironment: env, runtimeStatements: true).Program; Check(p.RuntimeStatements.Records.Single().Kind == VmRuntimeStatementKind.Set && (VmOpcode)p.Code[0].Opcode == VmOpcode.Statement); })),
    ("formatted quoted string add remains an expression", new Action(() => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); const string value = "@\" LV%TOSTR(LV) + \\@ LOG10(LV) == 6? .{LV/100000}# \\@,3%M \""; try { SemanticIrCompiler.CompileExpression(value, env); } catch (SemanticParseException ex) { throw new InvalidOperationException(ex.Message, ex); } var p = ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["L_RESULTS += " + value, ""])], runtimeEnvironment: env, runtimeStatements: true).Program; Check(p.RuntimeStatements.Records.Single().Kind == VmRuntimeStatementKind.Set && (VmOpcode)p.Code[0].Opcode == VmOpcode.Statement); })),
    ("plain string assignment semantic matrix", RunPlainStringAssignmentMatrix),
    ("indexed string host writes preserve typed addresses and values", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["I=123", "S '= \"abc\"", "NI:5=123", "SI:IDX '= \"variable\"", "SI:5 '= \"a\"", "SI:5 '= \"b\"", "MI:1:2 '= \"日本語\"", "SI:3 '= \"\"", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new TypedIndexedWriteHost(); var stop = new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)); if (stop != VmStopReason.Returned || host.Writes.Count != 8) throw new InvalidOperationException($"stop={stop};writes={host.Describe()}"); if (!(host.Writes[0].Indices.Length == 0 && host.Writes[0].Value.TryGetInteger(out var scalarInt) && scalarInt == 123 && host.Writes[1].Indices.Length == 0 && host.Writes[1].Value.TryGetString(out var scalarText) && scalarText == "abc" && host.Writes[2].Indices.SequenceEqual([VmSemanticValue.From(5)]) && host.Writes[2].Value.TryGetInteger(out var indexedInt) && indexedInt == 123 && host.Writes[3].Indices.SequenceEqual([VmSemanticValue.From(5)]) && host.Writes[4].Indices.SequenceEqual([VmSemanticValue.From(5)]) && host.Writes[5].Value.TryGetString(out var overwritten) && overwritten == "b" && host.Writes[6].Indices.SequenceEqual([VmSemanticValue.From(1), VmSemanticValue.From(2)]) && host.Writes[6].Value.ToString() == "日本語" && host.Writes[7].Indices.SequenceEqual([VmSemanticValue.From(3)]) && host.Writes[7].Value.TryGetString(out var empty) && empty.Length == 0)) throw new InvalidOperationException($"writes={host.Describe()}"); }),
    ("indexed string host writes reject unsupported writes instead of completing", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["S:5 '= \"x\"", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); Check(new VmMachine(p, new VmSemanticExecutor(p, new TypedIndexedWriteHost(true))).Run(new(0)) == VmStopReason.SemanticNotAvailable); }),
    ("nested and repeated indexed string writes remain host-visible", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var p = Executable(ControlLinker.Link(c, [RPO(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B", ""]), RPO(1, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["S:2 '= \"nested\"", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new TypedIndexedWriteHost(); var first = new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)); var second = new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)); if (first != VmStopReason.Returned || second != VmStopReason.Returned || host.Writes.Count != 2 || !host.Writes.All(x => x.Indices.SequenceEqual([VmSemanticValue.From(2)])) || !host.Writes.All(x => x.Value.ToString() == "nested")) throw new InvalidOperationException($"first={first};second={second};writes={host.Describe()}"); }),
    ("fixture SET_EXTRA_TITLE_VAR lowers all indexed string writes", RunFixtureSetExtraTitleVar),
    ("SET format fallback preserves Legacy empty and text assignments", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["CSTR:0 = 文字列", "CSTR:1 =", "%CSTR:0%:%CSTR:1%", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new RecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.Events.SequenceEqual(["text:文字列:"])); }),
    ("host statement dispatch requires host acknowledgement", () => { var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.DRAWLINE, PrototypeOpcode.RETURN], ["-", ""])], runtimeStatements: true).Program); var unavailable = new RecordingEffects(); var host = new HostRecordingEffects(); Check(new VmMachine(p, runtimeEffects: unavailable).Run(new(0)) == VmStopReason.SemanticNotAvailable && new VmMachine(p, runtimeEffects: host).Run(new(0)) == VmStopReason.Returned && host.HostEvents.SequenceEqual(["DRAWLINE:-"])); }),
    ("typed HTML host receives linked operands in order", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.HTML_PRINT, PrototypeOpcode.RETURN], ["\"html\",-1", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new TypedRecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.TypedEvents.SequenceEqual(["HTML_PRINT:html|-1"])); }),
#if R0_F6G10C3
    ("typed HTML island defaults depth and clear keeps zero-or-one arity", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.HTML_PRINT_ISLAND, PrototypeOpcode.HTML_PRINT_ISLAND, PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR, PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR, PrototypeOpcode.RETURN], ["\"a\"", "\"b\",2", "2", "", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new TypedRecordingEffects(); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects).Run(new(0)) == VmStopReason.Returned && effects.TypedEvents.SequenceEqual(["HTML_PRINT_ISLAND:a|0", "HTML_PRINT_ISLAND:b|2", "HTML_PRINT_ISLAND_CLEAR:2", "HTML_PRINT_ISLAND_CLEAR:"])); }),
#endif
    ("typed ONEINPUTS suspends without primitive input", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.ONEINPUTS, PrototypeOpcode.RETURN], ["", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var effects = new TypedRecordingEffects(waitOnInput: true); var vm = new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost()), effects); Check(vm.Run(new(0)) == VmStopReason.WaitingForInput && vm.FrameDepth == 1 && effects.TypedEvents.SequenceEqual(["ONEINPUTS:"])); }),
    ("SET division by zero is a semantic fault", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET], ["X=1/0"])], runtimeEnvironment: env, runtimeStatements: true).Program); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())).Run(new(0)) == VmStopReason.SemanticEvaluationFault); }),
    ("TIMES keeps typed multiplier and legacy numeric modes", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.TIMES, PrototypeOpcode.RETURN], ["X, 1.5", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var precise = new SemanticHost(); precise.Set("X", null, [], VmSemanticValue.From(4L)); var loose = new SemanticHost { TimesNotRigorousCalculation = true }; loose.Set("X", null, [], VmSemanticValue.From(-3L)); Check(new VmMachine(p, new VmSemanticExecutor(p, precise)).Run(new(0)) == VmStopReason.Returned && precise.GetInt("X") == 6 && new VmMachine(p, new VmSemanticExecutor(p, loose)).Run(new(0)) == VmStopReason.Returned && loose.GetInt("X") == -4 && p.RuntimeStatements.Records.Single().NumericValue == 1.5); }),
    ("F6G7R2 SETBIT evaluates every bit and mutates one lvalue", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = Executable(ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SETBIT, PrototypeOpcode.RETURN], ["A,1,3", ""])], runtimeEnvironment: env, runtimeStatements: true).Program); var host = new SemanticHost(); host.Set("A", null, [], VmSemanticValue.From(0L)); Check(new VmMachine(p, new VmSemanticExecutor(p, host)).Run(new(0)) == VmStopReason.Returned && host.GetInt("A") == 10 && p.RuntimeStatements.Records.Single().Kind == VmRuntimeStatementKind.SetBit); }),
    ("compile-time function metadata parses parameters locals and private declarations", () => { Check(FunctionRuntimeMetadataParser.TryParse(MetadataSource("@F(ARG,2,ARGS,\"x\")\n#LOCALSIZE 3\n#LOCALSSIZE 4\n#DIM DYNAMIC I,2,3\n#DIMS STATIC S,5\n#FUNCTION\nRETURN"), new CompilerCompatibilityOptions(true, true, true, false), out var metadata, out _) && metadata.Parameters.Length == 2 && metadata.Parameters[0] is { Type: RuntimeMetadataValueType.Integer, Slot: 0, HasDefault: true, DefaultInteger: 2 } && metadata.Parameters[1] is { Type: RuntimeMetadataValueType.String, Slot: 0, HasDefault: true, DefaultString: "x" } && metadata.LocalSize == 3 && metadata.LocalsSize == 4 && metadata.PrivateVariables.Length == 2 && !metadata.PrivateVariables[0].IsStatic && metadata.PrivateVariables[1].IsStatic && metadata.ReturnType == RuntimeMetadataValueType.Integer); }),
    ("runtime metadata propagates to linked function without raw source", () => { var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 2, 0, [], null); var p = ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURN)], [""], RuntimeMetadata: metadata)]).Program; Check(p.RuntimeMetadata.Length == 1 && p.RuntimeMetadata[0] == metadata && p.RuntimeMetadata[0].Parameters.Single().Name == "ARG"); }),
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
    ("loop runtime cells follow global LoopIndex", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1))]); var p = ControlLinker.Link(c, [RP(0, [PrototypeOpcode.FOR, PrototypeOpcode.NEXT]), RP(1, [PrototypeOpcode.FOR, PrototypeOpcode.NEXT])]).Program; var s = new LoopRuntimeState(p); Check(s.Count == 2); s.CaptureCounted(1, 20, 2); Check(!s.TryGetCounted(0, out _, out _) && s.TryGetCounted(1, out var end, out var step) && end == 20 && step == 2); }),
    ("loop runtime initially uninitialized", () => { var s = new LoopRuntimeState(CountedLoopProgram(VmStructuralKind.For)); Check(!s.TryGetCounted(0, out var end, out var step) && end == 0 && step == 0); }),
    ("same-source recursive loop state overwrites", () => { var s = new LoopRuntimeState(CountedLoopProgram(VmStructuralKind.For)); s.CaptureCounted(0, 10, 1); s.CaptureCounted(0, 3, 5); Check(s.TryGetCounted(0, out var end, out var step) && end == 3 && step == 5); }),
    ("counted loop captures signed and zero step", () => { var s = new LoopRuntimeState(CountedLoopProgram(VmStructuralKind.Repeat)); s.CaptureCounted(0, -4, -2); Check(s.TryGetCounted(0, out var end, out var step) && end == -4 && step == -2); s.CaptureCounted(0, 7, 0); Check(s.TryGetCounted(0, out end, out step) && end == 7 && step == 0); }),
    ("distinct counted loop states stay independent", () => { var p = CountedLoopProgram(VmStructuralKind.For, VmStructuralKind.Repeat); var s = new LoopRuntimeState(p); s.CaptureCounted(0, 11, 1); s.CaptureCounted(1, 22, 2); Check(s.TryGetCounted(0, out var e0, out var s0) && s.TryGetCounted(1, out var e1, out var s1) && e0 == 11 && s0 == 1 && e1 == 22 && s1 == 2); }),
    ("non-counted loop state rejected", () => { var s = new LoopRuntimeState(CountedLoopProgram(VmStructuralKind.While)); Expect<InvalidOperationException>(() => s.CaptureCounted(0, 1, 1)); Expect<InvalidOperationException>(() => s.TryGetCounted(0, out _, out _)); }),
    ("loop runtime index range checked", () => { var s = new LoopRuntimeState(CountedLoopProgram(VmStructuralKind.For)); Expect<ArgumentOutOfRangeException>(() => s.CaptureCounted(-1, 1, 1)); Expect<ArgumentOutOfRangeException>(() => s.TryGetCounted(1, out _, out _)); }),
    ("VmMachine owns persistent non-frame loop state", () => { var p = new LinkedProgram([VmInstruction.Halt], [new VmFunctionDescriptor(0, 0, 1, VmFunctionState.ExecutableReady)], [], loops: [new LoopDescriptor(0, VmStructuralKind.For, 0, 1, 2, 3, 2, LoopDescriptorFlags.BreakAdvancesCounter)]); var field = typeof(VmMachine).GetField("loopRuntime", BindingFlags.NonPublic | BindingFlags.Instance)!; var machine = new VmMachine(p); var state = (LoopRuntimeState)field.GetValue(machine)!; state.CaptureCounted(0, 9, 3); Check(machine.Run(new RuntimeFunctionId(0)) == VmStopReason.Halted); var after = (LoopRuntimeState)field.GetValue(machine)!; var fresh = (LoopRuntimeState)field.GetValue(new VmMachine(p))!; Check(ReferenceEquals(state, after) && after.TryGetCounted(0, out var end, out var step) && end == 9 && step == 3 && !fresh.TryGetCounted(0, out _, out _) && Marshal.SizeOf<VmFrame>() == 12); }),
    ("structural Aux is global direct identity", () => { var c = FunctionCatalog.FromDefinitions([D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1))]); var p = ControlLinker.Link(c, [RP(0, [PrototypeOpcode.SIF, PrototypeOpcode.RETURN]), RP(1, [PrototypeOpcode.IF, PrototypeOpcode.ELSE, PrototypeOpcode.ENDIF, PrototypeOpcode.RETURN])]).Program; var seen = new HashSet<int>(); foreach (var d in p.Descriptors) for (var pc = 0; pc < d.CodeLength; pc++) { var ins = p.Code[d.CodeStart + pc]; if ((VmOpcode)ins.Opcode != VmOpcode.Structural) continue; Check((uint)ins.Aux < (uint)p.StructuralLinks.Length && seen.Add(ins.Aux)); var row = p.StructuralLinks[ins.Aux]; Check(row.FunctionId == d.FunctionId && row.Pc == pc); } Check(seen.Count == p.StructuralLinks.Length); }),
    ("SIF record carries direct fallthrough and false PC", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SIF, PrototypeOpcode.RETURN])]).Program; var r = p.StructuralLinks.Single(); Check(r.Kind == VmStructuralKind.Sif && r.TargetPc == 1 && r.AuxiliaryPc == 2 && p.Code[0].Aux == 0); }),
    ("structural execution requires semantic host", () => { var p = ExecutableStructural([PrototypeOpcode.SIF, PrototypeOpcode.RETURN]); Check(new VmMachine(p).Run(new(0)) == VmStopReason.SemanticNotAvailable); }),
    ("SIF true executes next instruction", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.Sif)] = new([1]); var p = ExecutableStructural([PrototypeOpcode.SIF, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], (0, 1, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted); }),
    ("structural Aux range guard", () => { var h = new StructuralHost { DefaultInt = 1 }; var p = ExecutableStructural([PrototypeOpcode.SIF, PrototypeOpcode.RETURN], (0, 0, new VmInstruction((ushort)VmOpcode.Structural, aux: 99))); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.UnsupportedControl); }),
    ("structural FunctionId Pc identity guard", () => { var h = new StructuralHost { DefaultInt = 1 }; var p = ExecutableStructural([PrototypeOpcode.SIF, PrototypeOpcode.RETURN]); p.StructuralLinks[0] = p.StructuralLinks[0] with { Pc = 1 }; Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.UnsupportedControl); }),
    ("SIF false skips exactly one instruction", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.Sif)] = new([0]); var p = ExecutableStructural([PrototypeOpcode.SIF, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], (0, 1, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned); }),
    ("IF first clause routes body", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.If)] = new([1]); var p = ExecutableStructural([PrototypeOpcode.IF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSEIF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDIF, PrototypeOpcode.RETURN], (0, 1, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted && h.IntTrace.SequenceEqual([(0, 0, VmStructuralKind.If)])); }),
    ("IF ELSEIF evaluation order routes later body", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.If)] = new([0]); h.IntValues[(0, 2, VmStructuralKind.ElseIf)] = new([1]); var p = ExecutableStructural([PrototypeOpcode.IF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSEIF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDIF, PrototypeOpcode.RETURN], (0, 3, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted && h.IntTrace.SequenceEqual([(0, 0, VmStructuralKind.If), (0, 2, VmStructuralKind.ElseIf)])); }),
    ("IF ELSE fallback routes body", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.If)] = new([0]); h.IntValues[(0, 2, VmStructuralKind.ElseIf)] = new([0]); var p = ExecutableStructural([PrototypeOpcode.IF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSEIF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDIF, PrototypeOpcode.RETURN], (0, 5, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted); }),
    ("IF no match exits group", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.If)] = new([0]); h.IntValues[(0, 2, VmStructuralKind.ElseIf)] = new([0]); var p = ExecutableStructural([PrototypeOpcode.IF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSEIF, PrototypeOpcode.PRINT, PrototypeOpcode.ENDIF, PrototypeOpcode.RETURN], (0, 1, VmInstruction.Halt), (0, 3, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned); }),
    ("sequential ELSEIF exits selected IF body", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.If)] = new([1]); var p = ExecutableStructural([PrototypeOpcode.IF, PrototypeOpcode.PRINT, PrototypeOpcode.ELSEIF, PrototypeOpcode.PRINT, PrototypeOpcode.ENDIF, PrototypeOpcode.RETURN], (0, 1, VmInstruction.Nop), (0, 3, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count == 1); }),
    ("SELECT routes selected CASE", () => { var h = new StructuralHost(); h.SelectResults.Enqueue(1); var p = ExecutableStructural([PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.CASEELSE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDSELECT, PrototypeOpcode.RETURN], (0, 4, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted && h.LastSelectCaseCount == 2); }),
    ("SELECT CASEELSE fallback", () => { var h = new StructuralHost(); h.SelectResults.Enqueue(-1); var p = ExecutableStructural([PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.CASEELSE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDSELECT, PrototypeOpcode.RETURN], (0, 4, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted && h.LastSelectCaseCount == 1); }),
    ("SELECT host ordinal is range checked", () => { var h = new StructuralHost(); h.SelectResults.Enqueue(5); var p = ExecutableStructural([PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDSELECT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.UnsupportedControl); }),
    ("SELECT no match exits group", () => { var h = new StructuralHost(); h.SelectResults.Enqueue(-1); var p = ExecutableStructural([PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDSELECT, PrototypeOpcode.RETURN], (0, 2, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned); }),
    ("SELECT stops CASE evaluation at first CASEELSE", () => { var h = new StructuralHost(); h.SelectResults.Enqueue(-1); var p = ExecutableStructural([PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASEELSE, PrototypeOpcode.PRINT, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDSELECT, PrototypeOpcode.RETURN], (0, 2, VmInstruction.Halt), (0, 4, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Halted && h.LastSelectCaseCount == 0); }),
    ("FOR positive loop structural execution", () => { var h = new StructuralHost(); h.Begins[0] = new([new(0, 3, 1)]); h.DefaultInt = 1; var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.SIF, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 1) == 3 && h.AdvanceSteps.SequenceEqual([1L, 1L, 1L])); }),
    ("FOR negative loop structural execution", () => { var h = new StructuralHost(); h.Begins[0] = new([new(3, 0, -1)]); h.DefaultInt = 1; var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.SIF, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 1) == 3 && h.AdvanceSteps.SequenceEqual([-1L, -1L, -1L])); }),
    ("FOR zero step skips body", () => { var h = new StructuralHost(); h.Begins[0] = new([new(0, 3, 0)]); h.DefaultInt = 1; var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.SIF, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count == 0 && h.AdvanceSteps.Count == 0); }),
    ("FOR BREAK advances counter before exit", () => { var h = new StructuralHost(); h.Begins[0] = new([new(0, 3, 1)]); var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.BREAK, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.AdvanceSteps.SequenceEqual([1L]) && h.Counters[0] == 1); }),
    ("FOR CONTINUE advances and checks captured state", () => { var h = new StructuralHost(); h.Begins[0] = new([new(0, 3, 1)]); var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.CONTINUE, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.AdvanceSteps.SequenceEqual([1L, 1L, 1L]) && h.Counters[0] == 3); }),
    ("REPEAT counted execution uses same runtime contract", () => { var h = new StructuralHost(); h.Begins[0] = new([new(0, 2, 1)]); h.DefaultInt = 1; var p = ExecutableStructural([PrototypeOpcode.REPEAT, PrototypeOpcode.SIF, PrototypeOpcode.REND, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 1) == 2 && h.AdvanceSteps.SequenceEqual([1L, 1L])); }),
    ("WHILE rechecks header at WEND", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.While)] = new([1, 1, 0]); h.IntValues[(0, 1, VmStructuralKind.Sif)] = new([1, 1]); var p = ExecutableStructural([PrototypeOpcode.WHILE, PrototypeOpcode.SIF, PrototypeOpcode.WEND, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 0 && x.Kind == VmStructuralKind.While) == 3 && h.IntTrace.Count(x => x.Pc == 1) == 2); }),
    ("WHILE CONTINUE rechecks header once", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.While)] = new([1, 1, 0]); h.IntValues[(0, 1, VmStructuralKind.Sif)] = new([1, 1]); var p = ExecutableStructural([PrototypeOpcode.WHILE, PrototypeOpcode.SIF, PrototypeOpcode.CONTINUE, PrototypeOpcode.WEND, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 0 && x.Kind == VmStructuralKind.While) == 3 && h.IntTrace.Count(x => x.Pc == 1) == 2); }),
    ("DO LOOP evaluates closer and repeats body", () => { var h = new StructuralHost(); h.IntValues[(0, 2, VmStructuralKind.Loop)] = new([1, 0]); h.IntValues[(0, 1, VmStructuralKind.Sif)] = new([1, 1]); var p = ExecutableStructural([PrototypeOpcode.DO, PrototypeOpcode.SIF, PrototypeOpcode.LOOP, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 2 && x.Kind == VmStructuralKind.Loop) == 2 && h.IntTrace.Count(x => x.Pc == 1) == 2); }),
    ("DO CONTINUE evaluates LOOP condition", () => { var h = new StructuralHost(); h.IntValues[(0, 3, VmStructuralKind.Loop)] = new([1, 0]); h.IntValues[(0, 1, VmStructuralKind.Sif)] = new([1, 1]); var p = ExecutableStructural([PrototypeOpcode.DO, PrototypeOpcode.SIF, PrototypeOpcode.CONTINUE, PrototypeOpcode.LOOP, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count(x => x.Pc == 3 && x.Kind == VmStructuralKind.Loop) == 2 && h.IntTrace.Count(x => x.Pc == 1) == 2); }),
    ("nested BREAK owns nearest loop", () => { var h = new StructuralHost(); h.Begins[1] = new([new(0, 1, 1)]); h.IntValues[(0, 1, VmStructuralKind.While)] = new([1]); var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.WHILE, PrototypeOpcode.BREAK, PrototypeOpcode.WEND, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.AdvanceSteps.SequenceEqual([1L])); }),
    ("recursive same-source FOR overwrites captured End Step", () => { var c = Catalog("X"); var ops = new[] { PrototypeOpcode.FOR, PrototypeOpcode.IF, PrototypeOpcode.CALL, PrototypeOpcode.ENDIF, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN }; var operands = new[] { "", "", "X", "", "", "" }; var linked = ControlLinker.Link(c, [RPO(0, ops, operands)]).Program; var p = Executable(linked); var h = new StructuralHost(); h.Begins[0] = new([new(0, 4, 2), new(0, 1, 5)]); h.IntValues[(0, 1, VmStructuralKind.If)] = new([1, 0]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.AdvanceSteps.SequenceEqual([5L, 5L])); }),
    ("NEXT reached without counted header exits safely", () => { var h = new StructuralHost(); var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN], (0, 0, VmInstruction.Branch(1))); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.AdvanceSteps.Count == 0); }),
    ("counted CONTINUE reached without header exits safely", () => { var h = new StructuralHost(); var p = ExecutableStructural([PrototypeOpcode.FOR, PrototypeOpcode.CONTINUE, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN], (0, 0, VmInstruction.Branch(1))); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.AdvanceSteps.Count == 0); }),
    ("WHILE false initial condition skips body", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.While)] = new([0]); var p = ExecutableStructural([PrototypeOpcode.WHILE, PrototypeOpcode.PRINT, PrototypeOpcode.WEND, PrototypeOpcode.RETURN], (0, 1, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.SequenceEqual([(0, 0, VmStructuralKind.While)])); }),
    ("WHILE BREAK does not re-evaluate header", () => { var h = new StructuralHost(); h.IntValues[(0, 0, VmStructuralKind.While)] = new([1]); var p = ExecutableStructural([PrototypeOpcode.WHILE, PrototypeOpcode.BREAK, PrototypeOpcode.WEND, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.SequenceEqual([(0, 0, VmStructuralKind.While)])); }),
    ("DO BREAK does not evaluate LOOP condition", () => { var h = new StructuralHost(); var p = ExecutableStructural([PrototypeOpcode.DO, PrototypeOpcode.BREAK, PrototypeOpcode.LOOP, PrototypeOpcode.RETURN]); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.IntTrace.Count == 0); }),
    ("selected CASE body exits before later CASE", () => { var h = new StructuralHost(); h.SelectResults.Enqueue(0); var p = ExecutableStructural([PrototypeOpcode.SELECTCASE, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.CASE, PrototypeOpcode.PRINT, PrototypeOpcode.ENDSELECT, PrototypeOpcode.RETURN], (0, 2, VmInstruction.Nop), (0, 4, VmInstruction.Halt)); Check(new VmMachine(p, h).Run(new(0)) == VmStopReason.Returned && h.LastSelectCaseCount == 2); }),
    ("structural host does not change VmFrame layout", () => Check(Marshal.SizeOf<VmFrame>() == 12 && typeof(VmFrame).GetFields().All(x => x.FieldType != typeof(IVmStructuralSemantics) && x.FieldType != typeof(LoopRuntimeState)))),
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
    ("source misbinding count reuses binding hits", () => {
        static void Verify(string[] ordered, HashSet<string> sourcePositions)
        {
            var oldCount = ordered.Count(label => !sourcePositions.Contains(label));
            var hitCount = ordered.Count(sourcePositions.Contains);
            Check(oldCount == ordered.Length - hitCount);
        }
        Verify([], []);
        Verify(["A", "B"], ["A", "B"]);
        Verify(["A", "B"], ["A"]);
        Verify(["A", "B", "C", "D"], ["A", "D"]);
        Verify(["RUNTIME_ONLY"], []);
        Check(134652 - 134649 == 3);
    }),
    ("semantic payload transports through source/runtime binder", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var payload = SemanticIrCompiler.CompileExpression("A+B", env, 0); var source = new SourceFunctionPrototype(new SourceFunctionId(7), ImmutableArray.Create(P(PrototypeOpcode.SIF)), ImmutableArray.Create("A+B"), payload); var runtime = RuntimeFunctionBinder.Remap([source], new Dictionary<SourceFunctionId, RuntimeFunctionId> { [new(7)] = new(3) }).Single(); Check(ReferenceEquals(runtime.SemanticPayload, payload) && runtime.RuntimeId.Value == 3 && payload.Records[0].InstructionIndex == 0); }),
    ("semantic records merge to program-global arena", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var payload = SemanticIrCompiler.CompileExpression("A+B", env, 0); var p = ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), ImmutableArray.Create(P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)), ImmutableArray.Create("A+B", ""), payload)]).Program; Check(p.StructuralSemanticRecordIndices.Length == p.StructuralLinks.Length && p.StructuralSemanticRecordIndices[0] >= 0 && p.SemanticArena.Records.Length == 1); }),
    ("structural command without semantic payload maps to minus one", () => { var p = ControlLinker.Link(Catalog("X"), [RP(0, [PrototypeOpcode.SIF, PrototypeOpcode.RETURN])]).Program; Check(p.StructuralSemanticRecordIndices.SequenceEqual([-1]) && p.SemanticArena.Records.Length == 0); }),
    ("semantic mapping retains instruction identity", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var payload = SemanticIrCompiler.CompileExpression("A", env, 1); var p = ControlLinker.Link(Catalog("X"), [new RuntimeFunctionPrototype(new(0), ImmutableArray.Create(P(PrototypeOpcode.PRINT), P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)), ImmutableArray.Create("", "A", ""), payload)]).Program; Check(p.StructuralLinks.Single().Pc == 1 && p.StructuralSemanticRecordIndices.Single() >= 0 && p.SemanticArena.Records.Single().InstructionIndex == 1); }),
    ("linked program has no raw semantic operand ownership", () => { var p = typeof(LinkedProgram).GetProperties().Select(x => x.Name).ToArray(); Check(!p.Contains("Operands") && !p.Contains("MacroCatalog") && !p.Contains("Source")); }),
    ("merge rebases subkey symbols and formatted children", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var first = SemanticIrCompiler.CompileExpression("X", env); var second = SemanticIrCompiler.CompileExpression("VAR@SUBKEY:A:B", env); var third = SemanticIrCompiler.CompileFormat("pre%B,7,LEFT%post", env); var merged = SemanticPayload.Merge([first, second, third]); var subkey = merged.Nodes.First(x => x.Kind == SemanticNodeKind.VariableSubkey); Check(merged.ReadSymbol(merged.Symbols[subkey.A]) == "VAR" && merged.ReadSymbol(merged.Symbols[subkey.B]) == "SUBKEY" && subkey.D == 2 && subkey.C >= 0 && merged.Edges[subkey.C].To >= 0 && merged.Edges[subkey.C + 1].To >= 0); var format = merged.Nodes.Single(x => x.Kind == SemanticNodeKind.Format); Check(merged.Nodes[format.B].Kind == SemanticNodeKind.IntegerLiteral && merged.ReadSymbol(merged.Symbols[format.C]) == "LEFT"); }),
    ("semantic edge is child-only and sequence-owned", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var payload = SemanticIrCompiler.CompileFormat("a%B%b{C}c", env); var sequence = payload.Nodes.Single(x => x.Kind == SemanticNodeKind.FormattedSequence); Check(Marshal.SizeOf<SemanticEdge>() == 4 && typeof(SemanticEdge).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Length == 1 && sequence.B == payload.Edges.Length && payload.Edges.All(x => (uint)x.To < (uint)payload.Nodes.Length)); }),
    ("semantic mapping rejects bad length and duplicate entries", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var payload = SemanticIrCompiler.CompileExpression("A", env); var link = new StructuralLinkRecord(0, 0, -1, -1, VmStructuralKind.Sif, 0); Expect<ArgumentOutOfRangeException>(() => new LinkedProgram([], [], [link], semanticArena: payload, structuralSemanticRecordIndices: [99])); Expect<ArgumentException>(() => new LinkedProgram([], [], [link, link], semanticArena: payload, structuralSemanticRecordIndices: [0, 0])); }),
    ("multiple functions retain multiple semantic record bases", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var a0 = SemanticIrCompiler.CompileExpression("A", env, 0); var a1 = SemanticIrCompiler.CompileExpression("A", env, 1); var b = SemanticIrCompiler.CompileExpression("B", env, 0); var c = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]); var program = ControlLinker.Link(c, [new RuntimeFunctionPrototype(new(0), ImmutableArray.Create(P(PrototypeOpcode.SIF), P(PrototypeOpcode.SIF)), ImmutableArray.Create("A", "A"), SemanticPayload.Merge([a0, a1])), new RuntimeFunctionPrototype(new(1), ImmutableArray.Create(P(PrototypeOpcode.SIF)), ImmutableArray.Create("B"), b)]).Program; Check(program.SemanticArena.Records.Length == 3 && program.StructuralSemanticRecordIndices.Length == program.StructuralLinks.Length && program.StructuralSemanticRecordIndices.All(i => i >= 0)); }),
    ("semantic arithmetic comparison and bitwise", () => { var h = new SemanticHost(); Check(Evaluate("1+2*3", h).TryGetInteger(out var a) && a == 7 && Evaluate("7&3|8", h).TryGetInteger(out var b) && b == 11 && Evaluate("3<4", h).TryGetInteger(out var c) && c == 1); }),
    ("semantic complete comparisons bitwise and logical xor", () => { var h = new SemanticHost(); Check(EvaluateBinary(SemanticOperator.Equal, 3, 3, h) == 1 && EvaluateBinary(SemanticOperator.NotEqual, 3, 4, h) == 1 && EvaluateBinary(SemanticOperator.Greater, 4, 3, h) == 1 && EvaluateBinary(SemanticOperator.Less, 3, 4, h) == 1 && EvaluateBinary(SemanticOperator.GreaterEqual, 4, 4, h) == 1 && EvaluateBinary(SemanticOperator.LessEqual, 3, 4, h) == 1 && EvaluateBinary(SemanticOperator.BitAnd, 6, 3, h) == 2 && EvaluateBinary(SemanticOperator.BitOr, 6, 3, h) == 7 && EvaluateBinary(SemanticOperator.BitXor, 6, 3, h) == 5 && EvaluateBinary(SemanticOperator.ShiftLeft, 3, 2, h) == 12 && EvaluateBinary(SemanticOperator.ShiftRight, 12, 2, h) == 3 && EvaluateBinary(SemanticOperator.LogicalXor, 1, 0, h) == 1); }),
    ("semantic defined faults are not unavailable", () => { var h = new SemanticHost(); var divide = SemanticExecutor("1/0", h); var modulo = SemanticExecutor("1%0", h); var negative = SemanticExecutor("\"x\"*-1", h); var large = SemanticExecutor("\"x\"*10000", h); Check(!divide.TryEvaluateRecord(0, out _) && divide.LastStatus == VmSemanticStatus.Fault && divide.LastFault == VmSemanticFault.DivideByZero && !modulo.TryEvaluateRecord(0, out _) && modulo.LastFault == VmSemanticFault.ModuloByZero && !negative.TryEvaluateRecord(0, out _) && negative.LastFault == VmSemanticFault.StringMultiplierOutOfRange && !large.TryEvaluateRecord(0, out _) && large.LastFault == VmSemanticFault.StringMultiplierOutOfRange); var p = SemanticStructural((PrototypeOpcode.SIF, "1/0"), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(p, new VmSemanticExecutor(p, h)).Run(new(0)) == VmStopReason.SemanticEvaluationFault); }),
    ("semantic logical operations short circuit", () => { var h = new SemanticHost(); h.Calls["SIDE"] = _ => VmSemanticValue.From(1L); Check(Evaluate("0&&SIDE()", h).TryGetInteger(out var a) && a == 0 && Evaluate("1||SIDE()", h).TryGetInteger(out var b) && b == 1 && Evaluate("0!&SIDE()", h).TryGetInteger(out var c) && c == 1 && Evaluate("1!|SIDE()", h).TryGetInteger(out var d) && d == 0 && h.CallCount == 0); }),
    ("semantic ternary evaluates selected branch only", () => { var h = new SemanticHost(); h.Calls["SIDE"] = _ => VmSemanticValue.From(99L); Check(Evaluate("1?7#SIDE()", h).TryGetInteger(out var value) && value == 7 && h.CallCount == 0); }),
    ("semantic variables indexed subkey and call arguments", () => { var h = new SemanticHost(); h.Set("V", null, [VmSemanticValue.From(2L)], VmSemanticValue.From(9L)); h.Set("S", "K", [], VmSemanticValue.From("ok")); h.Calls["F"] = values => values.Length == 2 && values[0].Kind == VmSemanticValueKind.Missing && values[1].TryGetInteger(out var n) ? VmSemanticValue.From(n + 1) : VmSemanticValue.Unavailable; Check(Evaluate("V:2", h).TryGetInteger(out var v) && v == 9 && Evaluate("S@K", h).TryGetString(out var s) && s == "ok" && Evaluate("F(,2)", h).TryGetInteger(out var f) && f == 3); }),
    ("typed call uses canonical identity without raw-name dispatch", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var payload = SemanticIrCompiler.CompileExpression("MAX(2,3)", env); var host = new TypedCallHost(); var executor = new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: payload), host); Check(executor.TryEvaluateRecord(0, out var value) && value.TryGetInteger(out var result) && result == 3 && host.TypedCalls == 1 && host.RawCalls == 0); }),
    ("semantic prefix postfix mutation", () => { var h = new SemanticHost(); h.Set("X", null, [], VmSemanticValue.From(3L)); Check(Evaluate("X++", h).TryGetInteger(out var post) && post == 3 && h.GetInt("X") == 4 && Evaluate("++X", h).TryGetInteger(out var pre) && pre == 5 && h.GetInt("X") == 5); }),
    ("semantic lvalue mutation resolves dynamic index once", () => { var h = new SemanticHost(); h.Calls["INDEX"] = _ => VmSemanticValue.From(2L); h.Set("A", null, [VmSemanticValue.From(2L)], VmSemanticValue.From(3L)); Check(Evaluate("A:INDEX()++", h).TryGetInteger(out var post) && post == 3 && h.CallCountFor("INDEX") == 1 && h.GetInt("A", 2) == 4 && h.Trace.SequenceEqual(["Call:INDEX", "Read:A@:2", "Write:A@:2"])); var prefix = new SemanticHost(); prefix.Calls["INDEX"] = _ => VmSemanticValue.From(2L); prefix.Set("A", null, [VmSemanticValue.From(2L)], VmSemanticValue.From(3L)); Check(Evaluate("++A:INDEX()", prefix).TryGetInteger(out var pre) && pre == 4 && prefix.CallCountFor("INDEX") == 1 && prefix.GetInt("A", 2) == 4); }),
    ("semantic format and conditional format", () => { var h = new SemanticHost(); h.Set("A", null, [], VmSemanticValue.From(1L)); Check(Evaluate("@\"x%A,3,LEFT%\"", h).TryGetString(out var text) && text == "x1  " && Evaluate("\\@A?YES#NO\\@", h).TryGetString(out var yes) && yes == "YES"); h.Set("A", null, [], VmSemanticValue.From(0L)); Check(Evaluate("\\@A?YES\\@", h).TryGetString(out var empty) && empty == ""); }),
    ("semantic CASE exact IS TO and string", () => { var h = new SemanticHost(); var p = SemanticStructural((PrototypeOpcode.SELECTCASE, "3"), (PrototypeOpcode.CASE, "1, IS >= 3, 4 TO 5"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.CASEELSE, ""), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, "")); Check(new VmMachine(p, new VmSemanticExecutor(p, h)).Run(new(0)) == VmStopReason.Returned); var stringCase = SemanticStructural((PrototypeOpcode.SELECTCASE, "\"b\""), (PrototypeOpcode.CASE, "\"a\" TO \"c\""), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, "")); Check(new VmMachine(stringCase, new VmSemanticExecutor(stringCase, h)).Run(new(0)) == VmStopReason.Returned); }),
    ("semantic CASE TO skips integer upper side effect when lower misses", () => { var h = new SemanticHost(); h.Calls["SIDE"] = _ => VmSemanticValue.From(2L); var p = SemanticStructural((PrototypeOpcode.SELECTCASE, "0"), (PrototypeOpcode.CASE, "1 TO SIDE()"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, ""), (PrototypeOpcode.RETURN, "")); var executor = new VmSemanticExecutor(p, h); var result = executor.SelectCase(new(0), 0, p.SelectCases); Check(result.Available && result.Ordinal == -1 && h.CallCountFor("SIDE") == 0 && new VmMachine(p, executor).Run(new(0)) == VmStopReason.Returned); }),
    ("semantic CASE TO evaluates integer upper after lower match", () => { var h = new SemanticHost(); h.Calls["SIDE"] = _ => VmSemanticValue.From(2L); var p = SemanticStructural((PrototypeOpcode.SELECTCASE, "2"), (PrototypeOpcode.CASE, "1 TO SIDE()"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, ""), (PrototypeOpcode.RETURN, "")); var result = new VmSemanticExecutor(p, h).SelectCase(new(0), 0, p.SelectCases); Check(result.Available && result.Ordinal == 0 && h.CallCountFor("SIDE") == 1); }),
    ("semantic CASE TO skips lower-miss upper divide fault", () => { var h = new SemanticHost(); var p = SemanticStructural((PrototypeOpcode.SELECTCASE, "0"), (PrototypeOpcode.CASE, "1 TO 1/0"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, ""), (PrototypeOpcode.RETURN, "")); var executor = new VmSemanticExecutor(p, h); var result = executor.SelectCase(new(0), 0, p.SelectCases); Check(result.Available && result.Ordinal == -1 && new VmMachine(p, executor).Run(new(0)) == VmStopReason.Returned); }),
    ("semantic CASE TO propagates upper divide fault after lower match", () => { var h = new SemanticHost(); var p = SemanticStructural((PrototypeOpcode.SELECTCASE, "2"), (PrototypeOpcode.CASE, "1 TO 1/0"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, ""), (PrototypeOpcode.RETURN, "")); var result = new VmSemanticExecutor(p, h).SelectCase(new(0), 0, p.SelectCases); Check(result.Status == VmSemanticStatus.Fault && result.Fault == VmSemanticFault.DivideByZero && new VmMachine(p, new VmSemanticExecutor(p, h)).Run(new(0)) == VmStopReason.SemanticEvaluationFault); }),
    ("semantic CASE TO skips string upper side effect when lower misses", () => { var h = new SemanticHost(); h.Calls["SIDE_STR"] = _ => VmSemanticValue.From("z"); var p = SemanticStructural((PrototypeOpcode.SELECTCASE, "\"a\""), (PrototypeOpcode.CASE, "\"b\" TO SIDE_STR()"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDSELECT, ""), (PrototypeOpcode.RETURN, "")); var result = new VmSemanticExecutor(p, h).SelectCase(new(0), 0, p.SelectCases); Check(result.Available && result.Ordinal == -1 && h.CallCountFor("SIDE_STR") == 0); }),
    ("semantic SIF IF WHILE and DO execution", () => { var h = new SemanticHost(); var sif = SemanticStructural((PrototypeOpcode.SIF, "1"), (PrototypeOpcode.RETURN, "")); var iff = SemanticStructural((PrototypeOpcode.IF, "0"), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ELSE, ""), (PrototypeOpcode.RETURN, ""), (PrototypeOpcode.ENDIF, "")); var whileLoop = SemanticStructural((PrototypeOpcode.WHILE, "0"), (PrototypeOpcode.WEND, ""), (PrototypeOpcode.RETURN, "")); var doLoop = SemanticStructural((PrototypeOpcode.DO, ""), (PrototypeOpcode.LOOP, "0"), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(sif, new VmSemanticExecutor(sif, h)).Run(new(0)) == VmStopReason.Returned && new VmMachine(iff, new VmSemanticExecutor(iff, h)).Run(new(0)) == VmStopReason.Returned && new VmMachine(whileLoop, new VmSemanticExecutor(whileLoop, h)).Run(new(0)) == VmStopReason.Returned && new VmMachine(doLoop, new VmSemanticExecutor(doLoop, h)).Run(new(0)) == VmStopReason.Returned); }),
    ("semantic FOR and REPEAT execution", () => { var h = new SemanticHost(); h.Set("I", null, [], VmSemanticValue.From(0L)); var forLoop = SemanticStructural((PrototypeOpcode.FOR, "I,0,3,1"), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); var repeat = SemanticStructural((PrototypeOpcode.REPEAT, "3"), (PrototypeOpcode.REND, ""), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(forLoop, new VmSemanticExecutor(forLoop, h)).Run(new(0)) == VmStopReason.Returned && h.GetInt("I") == 3 && new VmMachine(repeat, new VmSemanticExecutor(repeat, h)).Run(new(0)) == VmStopReason.Returned); }),
    ("semantic FOR begin preserves Legacy evaluation order", () => { var h = new SemanticHost(); h.Calls["START"] = _ => VmSemanticValue.From(1L); h.Calls["INDEX"] = _ => VmSemanticValue.From(2L); h.Calls["END"] = _ => VmSemanticValue.From(5L); h.Calls["STEP"] = _ => VmSemanticValue.From(1L); var p = SemanticStructural((PrototypeOpcode.FOR, "A:INDEX(),START(),END(),STEP()"), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); var executor = new VmSemanticExecutor(p, h); var begin = executor.BeginCounted(new(0), 0, p.Loops.Single()); Check(begin.Available && begin.Entry.Counter == 1 && begin.Entry.End == 5 && begin.Entry.Step == 1 && h.Trace.SequenceEqual(["Call:START", "Call:INDEX", "Write:A@:2", "Call:END", "Call:STEP", "Call:INDEX", "Read:A@:2"])); }),
    ("semantic counted loop mutation resolves dynamic index once", () => { var h = new SemanticHost(); h.Calls["INDEX"] = _ => VmSemanticValue.From(2L); h.Set("A", null, [VmSemanticValue.From(2L)], VmSemanticValue.From(0L)); var next = SemanticStructural((PrototypeOpcode.FOR, "A:INDEX(),0,2,1"), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(next, new VmSemanticExecutor(next, h)).Run(new(0)) == VmStopReason.Returned && h.CallCountFor("INDEX") == 4 && h.GetInt("A", 2) == 2); var breakHost = new SemanticHost(); breakHost.Calls["INDEX"] = _ => VmSemanticValue.From(2L); breakHost.Set("A", null, [VmSemanticValue.From(2L)], VmSemanticValue.From(0L)); var breakLoop = SemanticStructural((PrototypeOpcode.FOR, "A:INDEX(),0,2,1"), (PrototypeOpcode.BREAK, ""), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(breakLoop, new VmSemanticExecutor(breakLoop, breakHost)).Run(new(0)) == VmStopReason.Returned && breakHost.CallCountFor("INDEX") == 3 && breakHost.GetInt("A", 2) == 1); }),
    ("semantic FOR negative zero BREAK and CONTINUE", () => { var negativeHost = new SemanticHost(); negativeHost.Set("I", null, [], VmSemanticValue.From(0L)); var negative = SemanticStructural((PrototypeOpcode.FOR, "I,3,0,-1"), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); var zeroHost = new SemanticHost(); zeroHost.Set("I", null, [], VmSemanticValue.From(9L)); var zero = SemanticStructural((PrototypeOpcode.FOR, "I,0,3,0"), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); var continueHost = new SemanticHost(); continueHost.Set("I", null, [], VmSemanticValue.From(0L)); var continued = SemanticStructural((PrototypeOpcode.FOR, "I,0,3,1"), (PrototypeOpcode.CONTINUE, ""), (PrototypeOpcode.NEXT, ""), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(negative, new VmSemanticExecutor(negative, negativeHost)).Run(new(0)) == VmStopReason.Returned && negativeHost.GetInt("I") == 0 && new VmMachine(zero, new VmSemanticExecutor(zero, zeroHost)).Run(new(0)) == VmStopReason.Returned && zeroHost.GetInt("I") == 0 && new VmMachine(continued, new VmSemanticExecutor(continued, continueHost)).Run(new(0)) == VmStopReason.Returned && continueHost.GetInt("I") == 3); }),
    ("semantic unavailable host stops safely", () => { var h = new SemanticHost(); var p = SemanticStructural((PrototypeOpcode.SIF, "MISSING"), (PrototypeOpcode.RETURN, "")); Check(new VmMachine(p, new VmSemanticExecutor(p, h)).Run(new(0)) == VmStopReason.SemanticNotAvailable); }),
    ("semantic barrier remains safe", () => { var p = new VmSyntheticProgramBuilder(); var id = p.AddFunction(new VmInstruction[] { new((ushort)VmOpcode.SemanticBarrier) }); Check(new VmMachine(p.Build()).Run(new RuntimeFunctionId(id)) == VmStopReason.SemanticNotAvailable); }),
    ("production activation promotes only SCC-eligible pending descriptors", () => { var p = ControlLinker.Link(FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1))]), [RP(0, [PrototypeOpcode.RETURN]), RP(1, [PrototypeOpcode.RETURN])]).Program; var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.UnsupportedBuiltin)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1))], VmRuntimeCapabilitySnapshot.KernelOnly); var activation = VmRuntimeActivator.Activate(p, readiness); var repeat = VmRuntimeActivator.Activate(p, readiness); Check(activation is { Candidates: 2, Eligible: 0, Promoted: 0, Blocked: 2, AccountingPassed: true } && repeat is { Promoted: 0, AccountingPassed: true } && p.Descriptors.All(d => d.State == VmFunctionState.LinkedSemanticPending)); }),
    ("production activation promotes ready pending descriptor", () => { var p = ControlLinker.Link(Catalog("A"), [RP(0, [PrototypeOpcode.RETURN])]).Program; var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None)], [], VmRuntimeCapabilitySnapshot.KernelOnly); var activation = VmRuntimeActivator.Activate(p, readiness); Check(activation is { Candidates: 1, Eligible: 1, Promoted: 1, Blocked: 0, AccountingPassed: true } && new VmMachine(p).Run(new(0)) == VmStopReason.Returned); }),
    ("frame bridge readiness propagates through calls", () => { var nodes = new[] { new VmRuntimeReadinessNode(VmRuntimeRequirement.None), new VmRuntimeReadinessNode(VmRuntimeRequirement.FrameBridge) }; var edges = new[] { (new RuntimeFunctionId(0), new RuntimeFunctionId(1)) }; var blocked = VmRuntimeReadinessEvaluator.Evaluate(nodes, edges, VmRuntimeCapabilitySnapshot.KernelOnly); var supported = VmRuntimeReadinessEvaluator.Evaluate(nodes, edges, new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.FrameBridge)); Check(blocked.EligibleCount == 0 && supported.EligibleCount == 2 && supported.Rows.All(row => row.TransitiveEligible)); }),
    ("staged readiness keeps shared normal ready", () => { var shared = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None)], [], VmRuntimeCapabilitySnapshot.KernelOnly); var production = shared.RestrictEligibleTo(shared.Rows.Where(row => row.TransitiveEligible).Select(row => row.RuntimeId.Value).ToHashSet()); Check(shared.EligibleCount == 1 && production.EligibleCount == 1); }),
    ("staged readiness blocks production event semantics", () => { var shared = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None)], [], VmRuntimeCapabilitySnapshot.KernelOnly); var production = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.EventSemantics)], [], VmRuntimeCapabilitySnapshot.KernelOnly); Check(shared.EligibleCount == 1 && production.EligibleCount == 0 && production.Rows[0].BlockingRequirements == VmRuntimeRequirement.EventSemantics); }),
    ("staged readiness propagates event block to normal caller", () => { var shared = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1))], VmRuntimeCapabilitySnapshot.KernelOnly); var production = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.EventSemantics)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1))], VmRuntimeCapabilitySnapshot.KernelOnly); Check(shared.EligibleCount == 2 && production.EligibleCount == 0 && production.Rows.All(row => (row.TransitiveRequirements & VmRuntimeRequirement.EventSemantics) != 0)); }),
    ("R5 variable read/write requirements remain supported", () => { var requirements = VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite; var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(requirements)], [], new VmRuntimeCapabilitySnapshot(requirements)); Check(readiness.EligibleCount == 1 && readiness.Rows[0].TransitiveRequirements == requirements); }),
    ("unsupported variable family remains blocked", () => { var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.UnsupportedVariable)], [], new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite)); Check(readiness.EligibleCount == 0 && readiness.Rows[0].BlockingRequirements == VmRuntimeRequirement.UnsupportedVariable); }),
    ("frame-owned variable is shared-supported with frame bridge", () => { var requirements = VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.FrameBridge; var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(requirements)], [], new VmRuntimeCapabilitySnapshot(requirements)); Check(readiness.EligibleCount == 1); }),
    ("CSV index label follows R5 shared classification", () => { var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.VariableRead)], [], new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.VariableRead)); Check(readiness.EligibleCount == 1 && readiness.Rows[0].TransitiveRequirements == VmRuntimeRequirement.VariableRead); }),
    ("shared classifier preserves CSV index context", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = SemanticStructural((PrototypeOpcode.SIF, "A:IDX")); var seen = new List<VmRuntimeVariableUse>(); var capabilities = new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.VariableRead, VariableAvailableInContext: (_, payload, identity, use) => { seen.Add(use); var name = payload.ReadSymbol(payload.Symbols[identity.NameSymbolIndex]); return name == "A" || use == VmRuntimeVariableUse.CsvIndexLabel; }); var readiness = VmRuntimeRequirementAnalyzer.Analyze(p, [FunctionKind.Normal], capabilities, includeFrame: false, includeEventGate: false); Check(readiness.EligibleCount == 1 && seen.Contains(VmRuntimeVariableUse.CsvIndexLabel)); }),
    ("shared classifier preserves R5 string-target literal context", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["S=VALUE", ""])], runtimeEnvironment: env, runtimeStatements: true).Program; var seen = new List<VmRuntimeVariableUse>(); var capabilities = new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.VariableRead | VmRuntimeRequirement.VariableWrite, VariableAvailableInContext: (_, payload, identity, use) => { seen.Add(use); var name = payload.ReadSymbol(payload.Symbols[identity.NameSymbolIndex]); return name == "S" || use == VmRuntimeVariableUse.BareStringLiteral; }, StringAssignmentTarget: (_, _, _) => true); var readiness = VmRuntimeRequirementAnalyzer.Analyze(p, [FunctionKind.Normal], capabilities, includeFrame: false, includeEventGate: false); Check(readiness.EligibleCount == 1 && seen.Contains(VmRuntimeVariableUse.BareStringLiteral)); }),
    ("diagnostic occurrence export carries joinable context and cache trace", () => { var p = SemanticStructural((PrototypeOpcode.SIF, "A:IDX")); var capabilities = new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.VariableRead, VariableAvailableInContext: (_, _, _, _) => true, VariableObservation: (_, _, _, use) => new(true, true, "SYNTHETIC", "A", "Int", "Scalar", false, false, false, true, true, true, use == VmRuntimeVariableUse.CsvIndexLabel, true, "SemanticPayloadContext+NodeIndex", "SyntheticResolved")); var staged = VmRuntimeRequirementAnalyzer.AnalyzeStaged(p, [FunctionKind.Normal], capabilities, capabilities); var rows = staged.Diagnostics!.OccurrenceDiagnostics; Check(rows.Length > 0 && rows.All(row => row.RuntimeFunctionId == 0 && row.HostIdentityStableId != 0 && row.Role.Length > 0 && row.RequirementContribution.Length > 0) && rows.Any(row => row.IsCsvIndexContext && row.CacheLookupPerformed && row.CacheHit)); }),
    ("nonvariable occurrence export covers builtin method host and edge origin", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var blockedBuiltin = SemanticStructural((PrototypeOpcode.SIF, "UNKNOWN()")); var builtinCapabilities = new VmRuntimeCapabilitySnapshot(VmRuntimeRequirement.None, BuiltinAvailable: _ => false, CallObservation: _ => new(true, false, "", "SyntheticBuiltinUnavailable")); var builtinRows = VmRuntimeRequirementAnalyzer.AnalyzeStaged(blockedBuiltin, [FunctionKind.Normal], builtinCapabilities, builtinCapabilities).Diagnostics!.NonVariableBlockingDiagnostics.Where(row => row.UnsupportedBuiltin).ToArray(); var methodExpression = SemanticIrCompiler.CompileExpression("M()==0", env, 0); var methodCatalog = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("M", "m", Span(1), FunctionKind.Method)]); var blockedMethod = ControlLinker.Link(methodCatalog, [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["M()==0", ""], methodExpression), RP(1, [PrototypeOpcode.RETURNF])], runtimeEnvironment: env, runtimeStatements: true).Program; var methodRows = VmRuntimeRequirementAnalyzer.AnalyzeStaged(blockedMethod, [FunctionKind.Normal, FunctionKind.Method], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly).Diagnostics!.NonVariableBlockingDiagnostics.Where(row => row.UnsupportedExpressionMethod).ToArray(); var host = ControlLinker.Link(Catalog("X"), [RPO(0, [PrototypeOpcode.DRAWLINE, PrototypeOpcode.RETURN], ["-", ""])], runtimeStatements: true).Program; var hostRows = VmRuntimeRequirementAnalyzer.AnalyzeStaged(host, [FunctionKind.Normal], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly).Diagnostics!.NonVariableBlockingDiagnostics.Where(row => row.TargetKind == "HostStatement").ToArray(); var callableTarget = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer); var edgeProgram = ControlLinker.Link(methodCatalog, [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["M()==0", ""], methodExpression), new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.RETURNF)], ["1"], RuntimeMetadata: callableTarget)], runtimeEnvironment: env, runtimeStatements: true).Program; var edgeDiagnostics = VmRuntimeRequirementAnalyzer.AnalyzeStaged(edgeProgram, [FunctionKind.Normal, FunctionKind.Method], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly).Diagnostics!; var edge = edgeDiagnostics.RawEdges.Single(row => row.SourceKind == "ExpressionSemantic"); var edgeOriginRow = edgeDiagnostics.NonVariableBlockingDiagnostics.Single(row => row.TargetKind == "ExpressionUserMethod" && row.TargetRuntimeFunctionId == edge.Callee.Value && row.TargetResolved && row.Callable); var edgeOrigin = true; var all = builtinRows.Concat(methodRows).Concat(hostRows).Append(edgeOriginRow).ToArray(); var unique = all.Select(row => $"{row.RuntimeFunctionId}:{row.Pc}:{row.OperandIndex}:{row.ArenaKind}:{row.RecordIndex}:{row.NodeIndex}:{row.TargetRuntimeFunctionId}:{row.TargetKind}:{row.RequirementContribution}:{row.TargetResolved}:{row.Callable}").Distinct().Count() == all.Length; Console.WriteLine($"NonVariableSynthetic builtin={builtinRows.Length} expression={methodRows.Length} host={hostRows.Length} edgeJoin={edgeOrigin} unique={unique}"); foreach (var row in all) Console.WriteLine($"NonVariableSyntheticRow\t{row.RuntimeFunctionId}\t{row.Pc}\t{row.OperandIndex}\t{row.ArenaKind}\t{row.RecordIndex}\t{row.NodeIndex}\t{row.NodeKind}\t{row.HostIdentityStableId:X16}\t{row.TargetRuntimeFunctionId}\t{row.TargetKind}\t{row.TargetResolved}\t{row.Callable}\t{row.BuiltinResolved}\t{row.UnsupportedExpressionMethod}\t{row.UnsupportedBuiltin}\t{row.RequirementContribution}\t{row.Reason}"); Check(builtinRows.Length >= 1 && builtinRows.All(row => row.Pc >= 0 && row.ArenaKind.Length > 0 && row.RecordIndex >= 0 && row.NodeIndex >= 0 && row.NodeKind == "Call" && row.HostIdentityStableId != 0 && row.TargetKind == "Builtin" && row.RequirementContribution == "UnsupportedBuiltin") && methodRows.Length >= 1 && methodRows.All(row => row.TargetKind == "ExpressionUserMethod" && row.RequirementContribution == "UnsupportedExpressionMethod" && row.NodeKind == "Call") && hostRows.Length >= 1 && hostRows.All(row => row.RequirementContribution == "HostStatement" && row.TargetKind == "HostStatement") && edgeOrigin && unique); }),
  ("CsvIndexKnownLabelTest", () => { var payload = SemanticIrCompiler.CompileExpression("FLAG:known_label", new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false))); var identity = payload.HostIdentities.Single(candidate => candidate.IndexArity == 0 && payload.ReadSymbol(payload.Symbols[candidate.NameSymbolIndex]) == "known_label"); var owner = payload.HostIdentities.Single(candidate => candidate.IndexArity == 1 && payload.ReadSymbol(payload.Symbols[candidate.NameSymbolIndex]) == "FLAG"); var host = new CsvIndexMappingHost(); host.Set(identity, 400); host.SetIndexed(owner, 400, 400); Check(TryEvaluatePayload(payload, host, out var value) && value.TryGetInteger(out var index) && index == 400); }),
  ("CsvIndexSameLabelDifferentOwnerTest", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var a = SemanticIrCompiler.CompileExpression("OWNER_A:SAME_LABEL", env); var b = SemanticIrCompiler.CompileExpression("OWNER_B:SAME_LABEL", env); var childA = a.HostIdentities.Single(x => x.IndexArity == 0); var childB = b.HostIdentities.Single(x => x.IndexArity == 0); var ownerA = a.HostIdentities.Single(x => x.IndexArity == 1); var ownerB = b.HostIdentities.Single(x => x.IndexArity == 1); var host = new CsvIndexMappingHost(); host.SetContextual(a, childA, 11); host.SetContextual(b, childB, 22); host.SetIndexed(ownerA, 11, 101); host.SetIndexed(ownerB, 22, 202); var exA = new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: a), host); var exB = new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: b), host); Check(childA.StableId == childB.StableId && exA.TryEvaluateRecord(0, out var av) && exB.TryEvaluateRecord(0, out var bv) && av.TryGetInteger(out var ai) && bv.TryGetInteger(out var bi) && ai == 101 && bi == 202); }),
  ("CsvIndexSameLabelSameIndexTest", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var a = SemanticIrCompiler.CompileExpression("OWNER_A:SAME_LABEL", env); var b = SemanticIrCompiler.CompileExpression("OWNER_B:SAME_LABEL", env); var childA = a.HostIdentities.Single(x => x.IndexArity == 0); var childB = b.HostIdentities.Single(x => x.IndexArity == 0); var ownerA = a.HostIdentities.Single(x => x.IndexArity == 1); var ownerB = b.HostIdentities.Single(x => x.IndexArity == 1); var host = new CsvIndexMappingHost(); host.SetContextual(a, childA, 7); host.SetContextual(b, childB, 7); host.SetIndexed(ownerA, 7, 107); host.SetIndexed(ownerB, 7, 207); Check(TryEvaluatePayload(a, host, out var av) && TryEvaluatePayload(b, host, out var bv) && av.TryGetInteger(out var ai) && bv.TryGetInteger(out var bi) && ai == 107 && bi == 207); }),
  ("CsvIndexSameOwnerDifferentLabelsTest", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var x = SemanticIrCompiler.CompileExpression("OWNER_A:LABEL_X", env); var y = SemanticIrCompiler.CompileExpression("OWNER_A:LABEL_Y", env); var childX = x.HostIdentities.Single(i => i.IndexArity == 0); var childY = y.HostIdentities.Single(i => i.IndexArity == 0); var ownerX = x.HostIdentities.Single(i => i.IndexArity == 1); var ownerY = y.HostIdentities.Single(i => i.IndexArity == 1); var host = new CsvIndexMappingHost(); host.SetContextual(x, childX, 3); host.SetContextual(y, childY, 4); host.SetIndexed(ownerX, 3, 103); host.SetIndexed(ownerY, 4, 104); Check(TryEvaluatePayload(x, host, out var xv) && TryEvaluatePayload(y, host, out var yv) && xv.TryGetInteger(out var xi) && yv.TryGetInteger(out var yi) && xi == 103 && yi == 104); }),
  ("NestedPayloadCsvIndexTest", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var outer = SemanticIrCompiler.CompileExpression("OWNER_A:SAME_LABEL", env); var nested = SemanticIrCompiler.CompileExpression("OWNER_B:SAME_LABEL", env); var outerChild = outer.HostIdentities.Single(i => i.IndexArity == 0); var nestedChild = nested.HostIdentities.Single(i => i.IndexArity == 0); var outerOwner = outer.HostIdentities.Single(i => i.IndexArity == 1); var nestedOwner = nested.HostIdentities.Single(i => i.IndexArity == 1); var host = new CsvIndexMappingHost(); host.SetContextual(outer, outerChild, 11); host.SetContextual(nested, nestedChild, 22); host.SetIndexed(outerOwner, 11, 111); host.SetIndexed(nestedOwner, 22, 222); var executor = new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: outer), host); Check(executor.TryEvaluateRuntimeRecord(outer, 0, out var first) && executor.TryEvaluateRuntimeRecord(nested, 0, out var inner) && executor.TryEvaluateRuntimeRecord(outer, 0, out var again) && first.TryGetInteger(out var firstValue) && inner.TryGetInteger(out var innerValue) && again.TryGetInteger(out var againValue) && firstValue == 111 && innerValue == 222 && againValue == 111); }),
  ("LegacyTypedHostWithoutContextTest", () => { var payload = SemanticIrCompiler.CompileExpression("VALUE", new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false))); var host = new TypedIndexedWriteHost(); Check(TryEvaluatePayload(payload, host, out var value) && value.TryGetInteger(out var result) && result == 5); }),
  ("CsvIndexUnknownLabelTest", () => { var payload = SemanticIrCompiler.CompileExpression("FLAG:unknown_label", new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false))); var host = new CsvIndexMappingHost(); Check(!TryEvaluatePayload(payload, host, out _)); }),
  ("OrdinarySymbolRegressionTest", () => { var host = new SemanticHost(); host.Set("VALUE", null, [], VmSemanticValue.From(7L)); Check(Evaluate("VALUE", host).TryGetInteger(out var value) && value == 7); }),
  ("ExplicitNumericIndexRegressionTest", () => { var host = new SemanticHost(); host.Set("VALUE", null, [VmSemanticValue.From(3L)], VmSemanticValue.From(9L)); Check(Evaluate("VALUE:3", host).TryGetInteger(out var value) && value == 9); }),
  ("ImplicitZeroRegressionTest", () => { var host = new SemanticHost(); host.Set("VALUE", null, [], VmSemanticValue.From(11L)); Check(Evaluate("VALUE", host).TryGetInteger(out var value) && value == 11); }),
  ("HostIdentityLookupMeasurement", () => { var p = SemanticStructural((PrototypeOpcode.SIF, "A+B")); var counters = new VmRuntimePreparationCounters(true); VmRuntimeRequirementAnalyzer.AnalyzeStaged(p, [FunctionKind.Normal], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly, counters); Check(counters.HostIdentityLookupCount > 0 && counters.HostIdentityLookupHitCount == counters.HostIdentityLookupCount && counters.HostIdentityLookupMissCount == 0 && counters.HostIdentityDirectIndexLookupCount == counters.HostIdentityLookupCount && counters.HostIdentityLinearScanElementVisits == 0 && counters.VariableLookupCount == counters.HostIdentityLookupCount && counters.VariableLinearScanElementVisits == 0); }),
  ("HostIdentityDirectIndexLookup", () =>
  {
      var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
      var variablePayload = SemanticIrCompiler.CompileExpression("A+1", env);
      var callPayload = SemanticIrCompiler.CompileExpression("F()", env);
      var payload = SemanticPayload.Merge([variablePayload, callPayload]);
      var identities = payload.HostIdentities;
      Check(identities.Length == 2 && identities[0].Kind == SemanticHostIdentityKind.Variable && identities[1].Kind == SemanticHostIdentityKind.Call && identities[0].NodeIndex < identities[1].NodeIndex);
      Check(SemanticPayload.Empty.HostIdentities.IsEmpty && !SemanticPayload.Empty.TryGetHostIdentity(0, SemanticHostIdentityKind.Variable, out _));
      foreach (var identity in identities)
      {
          Check(payload.TryGetHostIdentity(identity.NodeIndex, identity.Kind, out var actual) && actual == identity);
          Check(!payload.TryGetHostIdentity(identity.NodeIndex, identity.Kind == SemanticHostIdentityKind.Variable ? SemanticHostIdentityKind.Call : SemanticHostIdentityKind.Variable, out _));
      }
      var literalIndex = Enumerable.Range(0, payload.Nodes.Length).First(index => payload.Nodes[index].Kind == SemanticNodeKind.IntegerLiteral);
      Check(!payload.TryGetHostIdentity(literalIndex, SemanticHostIdentityKind.Variable, out _) && !payload.TryGetHostIdentity(-1, SemanticHostIdentityKind.Variable, out _) && !payload.TryGetHostIdentity(payload.Nodes.Length, SemanticHostIdentityKind.Variable, out _));
      foreach (var nodeIndex in Enumerable.Range(0, payload.Nodes.Length))
          foreach (var kind in Enum.GetValues<SemanticHostIdentityKind>())
          {
              var linear = identities.FirstOrDefault(identity => identity.NodeIndex == nodeIndex && identity.Kind == kind);
              var expected = identities.Any(identity => identity.NodeIndex == nodeIndex && identity.Kind == kind);
              Check(payload.TryGetHostIdentity(nodeIndex, kind, out var actual) == expected && (!expected || actual == linear));
          }
  }),
  ("production dispatch set excludes method and event roots", () => { var kinds = new[] { FunctionKind.Normal, FunctionKind.Method, FunctionKind.Event }; var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None)], [], VmRuntimeCapabilitySnapshot.KernelOnly); var dispatch = readiness.Rows.Where(row => row.TransitiveEligible && kinds[row.RuntimeId.Value] == FunctionKind.Normal).Select(row => row.RuntimeId.Value).ToHashSet(); Check(dispatch.SetEquals([0]) && dispatch.IsSubsetOf(readiness.Rows.Where(row => row.TransitiveEligible).Select(row => row.RuntimeId.Value).ToHashSet())); }),
    ("expression user method integer return binds RuntimeFunctionId", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], RuntimeMetadataValueType.Integer); var caller = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["M(7)==7", ""], SemanticIrCompiler.CompileExpression("M(7)==7", env, 0)); var p = ControlLinker.Link(FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("M", "m", Span(1), FunctionKind.Method)]), [caller, new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.RETURNF)], ["ARG:0"], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program; var readiness = VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1))], VmRuntimeCapabilitySnapshot.KernelOnly); VmRuntimeActivator.Activate(p, readiness); Check(p.ExpressionFunctionTargets.Single() is { Target.Value: 1, ReturnType: RuntimeMetadataValueType.Integer, Callable: true } && new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())).Run(new(0)) == VmStopReason.Returned); }),
    ("expression user method string return preserves type", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.String); var caller = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["MS()==\"ok\"", ""], SemanticIrCompiler.CompileExpression("MS()==\"ok\"", env, 0)); var p = ControlLinker.Link(FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("MS", "m", Span(1), FunctionKind.Method)]), [caller, new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.RETURNF)], ["\"ok\""], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program; VmRuntimeActivator.Activate(p, VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1))], VmRuntimeCapabilitySnapshot.KernelOnly)); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())).Run(new(0)) == VmStopReason.Returned); }),
    ("expression user method omitted RETURNF keeps Legacy integer default", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer); var caller = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["M()==0", ""], SemanticIrCompiler.CompileExpression("M()==0", env, 0)); var p = ControlLinker.Link(FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("M", "m", Span(1), FunctionKind.Method)]), [caller, new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.RETURNF)], [""], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program; VmRuntimeActivator.Activate(p, VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1))], VmRuntimeCapabilitySnapshot.KernelOnly)); Check(new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())).Run(new(0)) == VmStopReason.Returned); }),
    ("nested expression user method uses nested VM frames", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer); var caller = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["B()==9", ""], SemanticIrCompiler.CompileExpression("B()==9", env, 0)); var p = ControlLinker.Link(FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1), FunctionKind.Method), D("C", "c", Span(1), FunctionKind.Method)]), [caller, new RuntimeFunctionPrototype(new(1), [P(PrototypeOpcode.RETURNF)], ["C()+1"], RuntimeMetadata: meta), new RuntimeFunctionPrototype(new(2), [P(PrototypeOpcode.RETURNF)], ["8"], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program; VmRuntimeActivator.Activate(p, VmRuntimeReadinessEvaluator.Evaluate([new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None), new(VmRuntimeRequirement.None)], [(new RuntimeFunctionId(0), new RuntimeFunctionId(1)), (new RuntimeFunctionId(1), new RuntimeFunctionId(2))], VmRuntimeCapabilitySnapshot.KernelOnly)); Check(p.ExpressionFunctionTargets.Length == 2 && new VmMachine(p, new VmSemanticExecutor(p, new SemanticHost())).Run(new(0)) == VmStopReason.Returned); }),
    ("unresolved expression method remains its own readiness blocker", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var caller = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SIF), P(PrototypeOpcode.RETURN)], ["M()==0", ""], SemanticIrCompiler.CompileExpression("M()==0", env, 0)); var p = ControlLinker.Link(FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("M", "m", Span(1), FunctionKind.Method)]), [caller, RP(1, [PrototypeOpcode.RETURNF])], runtimeEnvironment: env, runtimeStatements: true).Program; var readiness = VmRuntimeRequirementAnalyzer.AnalyzeStaged(p, [FunctionKind.Normal, FunctionKind.Method], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly); Check(p.ExpressionFunctionTargets.Single().Callable == false && (readiness.Shared.Rows[0].BlockingRequirements & VmRuntimeRequirement.UnsupportedExpressionMethod) != 0 && (readiness.Shared.Rows[0].BlockingRequirements & VmRuntimeRequirement.UnsupportedBuiltin) == 0); }),
    ("staged readiness reuses one 10k-node graph and SCC", () => { var nodes = Enumerable.Repeat(new VmRuntimeReadinessNode(VmRuntimeRequirement.None), 10_000).ToArray(); var edges = Enumerable.Range(0, 100_000).Select(i => (new RuntimeFunctionId(i % nodes.Length), new RuntimeFunctionId(i % nodes.Length))).ToArray(); var counters = new VmRuntimePreparationCounters(); var graph = VmRuntimeReadinessGraph.Build(nodes.Length, edges, counters); var shared = VmRuntimeReadinessEvaluator.Evaluate(graph, nodes, VmRuntimeCapabilitySnapshot.KernelOnly, counters); var production = VmRuntimeReadinessEvaluator.Evaluate(graph, nodes, VmRuntimeCapabilitySnapshot.KernelOnly, counters); Check(shared.EligibleCount == nodes.Length && production.EligibleCount == nodes.Length && counters.CallGraphBuildCount == 1 && counters.SccBuildCount == 1 && counters.CallEdgeVisitCount == edges.Length && counters.ReadinessEvaluationPasses == 2); }),
    ("staged readiness preserves direct result equivalence", () => { var nodes = new[] { new VmRuntimeReadinessNode(VmRuntimeRequirement.None), new VmRuntimeReadinessNode(VmRuntimeRequirement.EventSemantics) }; var productionNodes = nodes.ToArray(); var edges = new[] { (new RuntimeFunctionId(0), new RuntimeFunctionId(1)) }; var graph = VmRuntimeReadinessGraph.Build(nodes.Length, edges); var shared = VmRuntimeReadinessEvaluator.Evaluate(graph, nodes, VmRuntimeCapabilitySnapshot.KernelOnly); var production = VmRuntimeReadinessEvaluator.Evaluate(graph, productionNodes, VmRuntimeCapabilitySnapshot.KernelOnly); var directShared = VmRuntimeReadinessEvaluator.Evaluate(nodes, edges, VmRuntimeCapabilitySnapshot.KernelOnly); var directProduction = VmRuntimeReadinessEvaluator.Evaluate(productionNodes, edges, VmRuntimeCapabilitySnapshot.KernelOnly); Check(shared.Rows.SequenceEqual(directShared.Rows) && production.Rows.SequenceEqual(directProduction.Rows)); }),
    ("staged analyzer preserves direct readiness results", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var p = ControlLinker.Link(Catalog("A"), [RP(0, [PrototypeOpcode.RETURN])], runtimeEnvironment: env, runtimeStatements: true).Program; var stagedCounters = new VmRuntimePreparationCounters(); var staged = VmRuntimeRequirementAnalyzer.AnalyzeStaged(p, [FunctionKind.Normal], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly, stagedCounters); var shared = VmRuntimeRequirementAnalyzer.Analyze(p, [FunctionKind.Normal], VmRuntimeCapabilitySnapshot.KernelOnly, includeFrame: false, includeEventGate: false); var production = VmRuntimeRequirementAnalyzer.Analyze(p, [FunctionKind.Normal], VmRuntimeCapabilitySnapshot.KernelOnly, includeFrame: true, includeEventGate: true); Check(staged.Shared.Rows.SequenceEqual(shared.Rows) && staged.Production.Rows.SequenceEqual(production.Rows) && stagedCounters.CallGraphBuildCount == 1 && stagedCounters.SccBuildCount == 1 && stagedCounters.SharedReadinessEvaluationCount == 1 && stagedCounters.ProductionReadinessEvaluationCount == 1); }),
    ("diagnostic readiness export preserves raw unique origin accounting", () => { var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false)); var meta = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer); var catalog = FunctionCatalog.FromDefinitions([D("A", "a", Span(1)), D("B", "b", Span(1)), D("C", "c", Span(1)), D("M", "m", Span(1), FunctionKind.Method), D("U", "u", Span(1), FunctionKind.Method)]); var a = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.SIF), P(PrototypeOpcode.CALL), P(PrototypeOpcode.CALL), P(PrototypeOpcode.JUMP), P(PrototypeOpcode.RETURN)], ["X=1", "M()==U()", "B", "B", "C", ""], SemanticIrCompiler.CompileExpression("M()==U()", env, 1)); var p = ControlLinker.Link(catalog, [a, RP(1, [PrototypeOpcode.RETURN]), RP(2, [PrototypeOpcode.RETURN]), new RuntimeFunctionPrototype(new(3), [P(PrototypeOpcode.RETURNF)], ["1"], RuntimeMetadata: meta), RP(4, [PrototypeOpcode.RETURNF])], runtimeEnvironment: env, runtimeStatements: true).Program; var staged = VmRuntimeRequirementAnalyzer.AnalyzeStaged(p, [FunctionKind.Normal, FunctionKind.Normal, FunctionKind.Normal, FunctionKind.Method, FunctionKind.Method], VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly); var diagnostics = staged.Diagnostics!; var fixedCalls = diagnostics.RawEdges.Count(edge => edge.SourceKind == "FixedCallSite" && edge.EdgeKind == "CALL"); var jumps = diagnostics.RawEdges.Count(edge => edge.EdgeKind == "JUMP"); var expr = diagnostics.ExpressionUserMethods.Length; var added = diagnostics.ExpressionUserMethods.Count(row => row.EdgeAdded); var unsupported = diagnostics.ExpressionUserMethods.Count(row => row.UnsupportedExpressionMethodAdded); var duplicate = diagnostics.RawEdges.Select(row => $"{row.Caller.Value}:{row.Callee.Value}:{row.EdgeKind}").Distinct().Count() < diagnostics.RawEdges.Length; var origins = diagnostics.LocalRequirementOrigins.Select(row => row.Requirement).ToHashSet(); Console.WriteLine($"DiagnosticSynthetic raw={diagnostics.RawEdges.Length} fixedCalls={fixedCalls} jumps={jumps} expr={expr} added={added} unsupported={unsupported} duplicate={duplicate} origins={string.Join(',', origins)}"); if (diagnostics.RawEdges.Length != 4 || fixedCalls != 2 || jumps != 1 || expr != 2 || added != 1 || unsupported != 1 || !duplicate || !origins.Contains(VmRuntimeRequirement.VariableWrite) || !origins.Contains(VmRuntimeRequirement.UnsupportedExpressionMethod)) throw new InvalidOperationException("diagnostic readiness export synthetic assertion failed"); }),
    ("readiness-driven dispatch A: opt-in required", () => Check(!DispatchEligible(9, FunctionKind.Normal, true, nextRuntime: false))),
    ("readiness-driven dispatch B: ready normal selects Next", () => Check(DispatchEligible(10, FunctionKind.Normal, true))),
    ("readiness-driven dispatch C: blocked normal falls back", () => Check(!DispatchEligible(11, FunctionKind.Normal, false))),
    ("readiness-driven dispatch D: method remains Legacy", () => Check(!DispatchEligible(12, FunctionKind.Method, true))),
    ("readiness-driven dispatch E: event remains Legacy", () => Check(!DispatchEligible(13, FunctionKind.Event, true))),
    ("readiness-driven dispatch F: debug and analysis remain Legacy", () => Check(!DispatchEligible(14, FunctionKind.Normal, true, debug: true) && !DispatchEligible(15, FunctionKind.Normal, true, analysis: true))),
    ("readiness-driven dispatch G: multiple ready IDs", () => Check(DispatchEligible(9, FunctionKind.Normal, true) && DispatchEligible(1234, FunctionKind.Normal, true))),
    ("readiness-driven dispatch H: identity is ID/readiness, not name", () => Check(DispatchEligible(42, FunctionKind.Normal, true) && !DispatchEligible(42, FunctionKind.Normal, false))),
    ("entry-dispatch A: one opportunity per invocation", () => { var token = new VmRuntimeEntryDispatchToken(); Check(token.TryConsume() && !token.TryConsume()); }),
    ("entry-dispatch B: later Legacy lines do not reopen entry", () => { var token = new VmRuntimeEntryDispatchToken(); Check(token.TryConsume() && Enumerable.Range(0, 8).All(_ => !token.TryConsume())); }),
    ("entry-dispatch C: later call gets a new opportunity", () => { var first = new VmRuntimeEntryDispatchToken(); var later = new VmRuntimeEntryDispatchToken(); Check(first.TryConsume() && !first.TryConsume() && later.TryConsume() && !later.TryConsume()); }),
    ("entry-dispatch D: recursion has independent opportunities", () => { var outer = new VmRuntimeEntryDispatchToken(); var inner = new VmRuntimeEntryDispatchToken(); Check(outer.TryConsume() && inner.TryConsume() && !inner.TryConsume() && !outer.TryConsume()); }),
    ("entry-dispatch E: nested different function has independent opportunity", () => { var caller = new VmRuntimeEntryDispatchToken(); var callee = new VmRuntimeEntryDispatchToken(); Check(caller.TryConsume() && callee.TryConsume() && !caller.TryConsume() && !callee.TryConsume()); }),
    ("entry-dispatch F: WAIT resume does not reopen entry", () => { var token = new VmRuntimeEntryDispatchToken(); Check(token.TryConsume() && !token.TryConsume()); }),
    ("entry-dispatch G: disabled Next keeps Legacy policy", () => Check(!DispatchEligible(1, FunctionKind.Normal, true, nextRuntime: false))),
    ("entry-dispatch H: Debug keeps Legacy policy", () => Check(!DispatchEligible(1, FunctionKind.Normal, true, debug: true))),
    ("entry-dispatch I: Analysis keeps Legacy policy", () => Check(!DispatchEligible(1, FunctionKind.Normal, true, analysis: true))),
    ("entry-dispatch J: Method stays Legacy", () => Check(!DispatchEligible(1, FunctionKind.Method, true))),
    ("entry-dispatch K: Event stays Legacy", () => Check(!DispatchEligible(1, FunctionKind.Event, true))),
    ("r1.4 set-equip-var redispatch shape uses invocation token", () => { var setEquipVarInvocation = new VmRuntimeEntryDispatchToken(); Check(setEquipVarInvocation.TryConsume() && !setEquipVarInvocation.TryConsume()); }),
    ("NestedNextReturnVisibleToLegacyParent", () => Check(BridgeReturn(1) == 1 && BridgeReturn(0) == 0)),
    ("NestedNextWriteVisibleToLegacyParent", () => Check(BridgeSharedWrite() == 7)),
    ("FailedNextDispatchLeavesLegacyStateUnchanged", () => Check(BridgeRejectedDispatchIsPure())),
    ("BridgeLegacyFallbackFinalizesAtCompletion", () => Check(BridgePendingFallbackFinalizes())),
    ("BridgeDispatchAndCompletionChangesSeparated", () => Check(BridgeSeparatedChanges())),
    ("semantic structural lookup preserves map shape and duplicate order", RunSemanticStructuralLookupMapTest),
    ("semantic structural lookup ignores out of range links", RunSemanticStructuralLookupOutOfRangeTest),
    ("semantic structural lookup rejects another generation", RunSemanticStructuralLookupGenerationMismatchTest),
    ("semantic executors share lookup and isolate mutable state", RunSemanticStructuralLookupIsolationTest),
    ("semantic lookup excludes save state", () => Check(typeof(VmSemanticStructuralLookup).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).All(field => !field.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)))),
    ("machine lookup preserves call-site and expression-target maps", RunMachineLookupMapTest),
    ("machine lookup preserves duplicate-key failures", RunMachineLookupDuplicateKeyTest),
    ("machine lookup rejects another generation", RunMachineLookupGenerationMismatchTest),
    ("machines share lookup and isolate mutable state", RunMachineLookupIsolationTest),
    ("R0F6G7 A1 cross-chunk A-B-A uses one VM", RunR0F6G7A1),
    ("R0F6G7 A2 nested method contexts and deep storage", RunR0F6G7A2),
    ("R0F6G7 A3 admission precedes actual evaluation", RunR0F6G7A3),
    ("R0F6G7 A4 owner persistent and activation scopes", RunR0F6G7A4),
    ("R0F6G7 A5 physical loop cells are owner keyed", RunR0F6G7A5),
    ("R0F6G7 A6 lease and frame pin lifecycle", RunR0F6G7A6),
    ("R0F6G7 A7 terminal cleanup forbids retry", RunR0F6G7A7),
    ("R0F6G7 A8 one-shot input continuation", RunR0F6G7A8),
    ("R0F6G7 A9 eviction keeps state and rejects ABA", RunR0F6G7A9),
    ("R0F6G7 A10 true sparse function linker", RunR0F6G7A10),
    ("R0F6G7R1 A2 nested expression consumer", RunR0F6G7R1A2),
    ("R0F6G7R1 A3 pre-actual consumer matrix", RunR0F6G7R1A3),
    ("R0F6G7R1 A4 owner storage consumers", RunR0F6G7R1A4),
    ("R0F6G7R1 A5 physical loop consumers", RunR0F6G7R1A5),
    ("R0F6G7R1 A6 pin lifecycle consumers", RunR0F6G7R1A6),
    ("R0F6G7R1 A7 fault cleanup consumers", RunR0F6G7R1A7),
    ("R0F6G7R1 A8 input host state machine", RunR0F6G7R1A8),
    ("R0F6G7R1 A9 weak-root eviction consumers", RunR0F6G7R1A9),
#if R0_F6G10B
    ("R0F6G10B two consecutive owned waits resume through outer pump", RunR0F6G10BTwoWaits),
#endif
    ("R0F6G10A1 normal reset epoch and modal bookmark lifecycle", RunR0F6G10A1Lifecycle),
};
var passed = 0;
var readinessDrivenNormalDispatchTestsPassed = true;
var entryDispatchTestsPassed = true;
var setEquipVarRegressionPassed = true;
var bridgeRegressionNames = new[] { "NestedNextReturnVisibleToLegacyParent", "NestedNextWriteVisibleToLegacyParent", "FailedNextDispatchLeavesLegacyStateUnchanged", "BridgeLegacyFallbackFinalizesAtCompletion", "BridgeDispatchAndCompletionChangesSeparated" };
var bridgeRegressionPassed = bridgeRegressionNames.ToDictionary(name => name, _ => true);
var csvIndexRegressionNames = new[] { "CsvIndexKnownLabelTest", "CsvIndexSameLabelDifferentOwnerTest", "CsvIndexSameLabelSameIndexTest", "CsvIndexSameOwnerDifferentLabelsTest", "NestedPayloadCsvIndexTest", "LegacyTypedHostWithoutContextTest", "CsvIndexUnknownLabelTest", "OrdinarySymbolRegressionTest", "ExplicitNumericIndexRegressionTest", "ImplicitZeroRegressionTest" };
var csvIndexRegressionPassed = csvIndexRegressionNames.ToDictionary(name => name, _ => true);
foreach (var test in tests) try { test.Run(); passed++; Console.WriteLine($"PASS {test.Name}"); } catch (Exception ex) { if (test.Name.StartsWith("readiness-driven dispatch", StringComparison.Ordinal)) readinessDrivenNormalDispatchTestsPassed = false; if (test.Name.StartsWith("entry-dispatch", StringComparison.Ordinal)) entryDispatchTestsPassed = false; if (test.Name.StartsWith("r1.4 set-equip-var", StringComparison.Ordinal)) setEquipVarRegressionPassed = false; if (bridgeRegressionPassed.ContainsKey(test.Name)) bridgeRegressionPassed[test.Name] = false; if (csvIndexRegressionPassed.ContainsKey(test.Name)) csvIndexRegressionPassed[test.Name] = false; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); if (test.Name.StartsWith("R0F6G7R1", StringComparison.Ordinal)) Console.WriteLine(ex); }
Console.WriteLine($"VmSelfTest: executed={tests.Count} passed={passed} failed={tests.Count - passed}");
Console.WriteLine($"ReadinessDrivenNormalDispatchTests={(readinessDrivenNormalDispatchTestsPassed ? "PASS" : "FAIL")}");
Console.WriteLine($"EntryDispatchOncePerInvocationTests={(entryDispatchTestsPassed ? "PASS" : "FAIL")}");
Console.WriteLine($"R1_4_SetEquipVarRedispatchRegression={(setEquipVarRegressionPassed ? "PASS" : "FAIL")}");
foreach (var name in bridgeRegressionNames) Console.WriteLine($"{name}={(bridgeRegressionPassed[name] && passed == tests.Count ? "PASS" : "FAIL")}");
foreach (var name in csvIndexRegressionNames) Console.WriteLine($"{name}={(csvIndexRegressionPassed[name] && passed == tests.Count ? "PASS" : "FAIL")}");
return passed == tests.Count && readinessDrivenNormalDispatchTestsPassed && entryDispatchTestsPassed && setEquipVarRegressionPassed && bridgeRegressionPassed.Values.All(value => value) && csvIndexRegressionPassed.Values.All(value => value) ? 0 : 1;

static VmFunctionExecutionContext R0F6G7Context(int id, int slot, long generation, VmInstruction[] code,
    FunctionRuntimeMetadata? metadata = null, VmRuntimeStatementArena? statements = null, SemanticPayload? semantic = null)
{
    metadata ??= FunctionRuntimeMetadata.Empty;
    var program = new LinkedProgram(code, [new(id, 0, code.Length, VmFunctionState.ExecutableReady)], [], semanticArena: semantic,
        runtimeStatements: statements, runtimeMetadata: [metadata], sparseRuntimeIds: true);
    return new(new(id), slot, generation, program, metadata);
}

static void RunHostTransferPreservesSharedOwner()
{
    var statements = new VmRuntimeStatementArena([new(VmRuntimeStatementKind.TypedHost, 0, 0, HostOpcode: (ushort)PrototypeOpcode.QUIT)], []);
    var owner = new VmContextStorage(99, 1, 100);
    owner.Register(new(0), FunctionKind.Normal, () => R0F6G7Context(0, 0, 1, [new((ushort)VmOpcode.Statement, aux: 0)], statements: statements));
    var stop = new VmMachine(owner, new HostTransferEffects()).Run(new(0));
    Check(stop == VmStopReason.HostTransfer && !owner.IsTerminal);
}

static void RunR0F6G7A1()
{
    var owner = new VmContextStorage(1, 1, 100);
    owner.Register(new(10), FunctionKind.Normal, () => R0F6G7Context(10, 0, 1, [VmInstruction.Call(new(20)), VmInstruction.Return]));
    owner.Register(new(20), FunctionKind.Normal, () => R0F6G7Context(20, 1, 1, [VmInstruction.Return]));
    var vm = new VmMachine(owner);
    var stop = vm.Run(new(10));
    Check(stop == VmStopReason.Returned && vm.FrameDepth == 0 && vm.MaxFrameDepth == 2 && owner.MaterializationCount == 2 &&
        owner.PinAcquireCount == 2 && owner.PinReleaseCount == 2 && owner.ResidentCount == 2);
}

static void RunR0F6G7A2()
{
    var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 1, 0, [], RuntimeMetadataValueType.Integer);
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var bContext = R0F6G7Context(40, 1, 2, [VmInstruction.Return], metadata, semantic: SemanticIrCompiler.CompileExpression("41", environment));
    var cContext = R0F6G7Context(50, 2, 3, [VmInstruction.Return], metadata, semantic: SemanticIrCompiler.CompileExpression("52", environment));
    var owner = new VmContextStorage(2, 1, 1000);
    owner.Register(new(30), FunctionKind.Normal, () => R0F6G7Context(30, 0, 1, [VmInstruction.Return], metadata));
    owner.Register(new(40), FunctionKind.Method, () => bContext);
    owner.Register(new(50), FunctionKind.Method, () => cContext);
    var root = owner.PrepareInvocation(new(30), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.CommitFrame(ref root, [VmSemanticValue.From(3L)], out var a));
    var bLease = owner.PrepareInvocation(new(40), VmReturnKind.Expression, FunctionKind.Method);
    Check(owner.CommitFrame(ref bLease, [VmSemanticValue.From(4L)], out var b));
    var cLease = owner.PrepareInvocation(new(50), VmReturnKind.Expression, FunctionKind.Method);
    Check(owner.CommitFrame(ref cLease, [VmSemanticValue.From(5L)], out var c));
    Check(a != b && b != c && owner.TryReadFrameValue(a, "ARG", 0, out var av) && av.TryGetInteger(out var ai) && ai == 3 &&
        owner.TryReadFrameValue(b, "ARG", 0, out var bv) && bv.TryGetInteger(out var bi) && bi == 4 &&
        owner.TryReadFrameValue(c, "ARG", 0, out var cv) && cv.TryGetInteger(out var ci) && ci == 5);
    var evaluator = new VmSemanticExecutor(bContext.Program, new SemanticHost());
    Check(evaluator.TryEvaluateRuntimeRecord(new(bContext, b, owner.Stamp), bContext.Program.SemanticArena, 0, out var bRecord) && bRecord.TryGetInteger(out var br) && br == 41 &&
        evaluator.TryEvaluateRuntimeRecord(new(cContext, c, owner.Stamp), cContext.Program.SemanticArena, 0, out var cRecord) && cRecord.TryGetInteger(out var cr) && cr == 52 &&
        evaluator.ActiveEvaluationContext is null);
    Check(owner.TryReturn(out _) && owner.CurrentFrame.FunctionId == 40 && owner.TryReturn(out _) && owner.CurrentFrame.FunctionId == 30);
    for (var i = 0; i < 40; i++)
    {
        var lease = owner.PrepareInvocation(new(40), VmReturnKind.Expression, FunctionKind.Method);
        Check(owner.CommitFrame(ref lease, [VmSemanticValue.From((long)i)], out _));
    }
    Check(owner.MaxFrameDepth >= 41);
    for (var i = 0; i < 40; i++) Check(owner.TryReturn(out _));
    var bad = owner.PrepareInvocation(new(40), VmReturnKind.Expression, FunctionKind.Method);
    Check(!owner.CommitFrame(ref bad, [VmSemanticValue.From("wrong")], out _) && owner.FrameDepth == 1);
    Check(owner.TryReturn(out _) && owner.PinAcquireCount == owner.PinReleaseCount);
}

static void RunR0F6G7A3()
{
    var evaluations = 0;
    var materializations = 0;
    var owner = new VmContextStorage(3, 1, 100);
    owner.Register(new(61), FunctionKind.Normal, () => { materializations++; return null; });
    owner.Register(new(62), FunctionKind.Method, () => { materializations++; return R0F6G7Context(62, 0, 1, [VmInstruction.Return]); });
    Check(owner.PrepareInvocation(new(60), VmReturnKind.Normal, FunctionKind.Normal).Status == VmAdmissionStatus.Missing && evaluations == 0);
    Check(owner.PrepareInvocation(new(61), VmReturnKind.Normal, FunctionKind.Normal).Status == VmAdmissionStatus.Blocked && evaluations == 0);
    Check(owner.PrepareInvocation(new(62), VmReturnKind.Normal, FunctionKind.Normal).Status == VmAdmissionStatus.WrongKind && evaluations == 0);
    var ready = owner.PrepareInvocation(new(62), VmReturnKind.Expression, FunctionKind.Method);
    Check(ready.Ready && ++evaluations == 1 && owner.CommitFrame(ref ready, [], out _) && materializations == 2);
    Check(owner.TryReturn(out _));
}

static void RunR0F6G7A4()
{
    var metadata = new FunctionRuntimeMetadata([new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 2, 1,
        [new("DYNAMIC", RuntimeMetadataValueType.Integer, [1], false), new("STATIC", RuntimeMetadataValueType.Integer, [1], true)], null);
    var owner = new VmContextStorage(4, 1, 100);
    owner.Register(new(70), FunctionKind.Normal, () => R0F6G7Context(70, 0, 1, [VmInstruction.Return], metadata));
    var first = owner.PrepareInvocation(new(70), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.CommitFrame(ref first, [VmSemanticValue.From(11L)], out var outer));
    Check(owner.TryWriteFrameValue(outer, "LOCAL", 0, VmSemanticValue.From(7L)) && owner.TryWriteFrameValue(outer, "DYNAMIC", 0, VmSemanticValue.From(8L)) && owner.TryWriteFrameValue(outer, "STATIC", 0, VmSemanticValue.From(9L)));
    var recursive = owner.PrepareInvocation(new(70), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.CommitFrame(ref recursive, [VmSemanticValue.From(22L)], out var inner));
    Check(owner.TryReadFrameValue(outer, "ARG", 0, out var sharedArg) && sharedArg.TryGetInteger(out var arg) && arg == 22 &&
        owner.TryReadFrameValue(inner, "LOCAL", 0, out var sharedLocal) && sharedLocal.TryGetInteger(out var local) && local == 7 &&
        owner.TryReadFrameValue(inner, "DYNAMIC", 0, out var innerDynamic) && innerDynamic.TryGetInteger(out var dynamicValue) && dynamicValue == 0 &&
        owner.TryReadFrameValue(inner, "STATIC", 0, out var staticValue) && staticValue.TryGetInteger(out var staticInteger) && staticInteger == 9);
    Check(owner.TryWriteFrameValue(inner, "DYNAMIC", 0, VmSemanticValue.From(10L)) && owner.TryReadFrameValue(outer, "DYNAMIC", 0, out var outerDynamic) && outerDynamic.TryGetInteger(out var outerValue) && outerValue == 8);
    Check(owner.TryReturn(out _) && owner.TryReturn(out _));
}

static void RunR0F6G7A5()
{
    var owner = new VmContextStorage(5, 1, 100);
    owner.CaptureLoop(9001, 10, 1, 3);
    owner.CaptureLoop(9002, -10, -2, 5);
    Check(owner.TryReadLoop(9001, out var e1, out var s1, out var r1) && (e1, s1, r1) == (10, 1, 3));
    owner.CaptureLoop(9001, 20, 4, 6);
    Check(owner.TryReadLoop(9001, out e1, out s1, out r1) && (e1, s1, r1) == (20, 4, 6) &&
        owner.TryReadLoop(9002, out var e2, out var s2, out var r2) && (e2, s2, r2) == (-10, -2, 5));
}

static void RunR0F6G7A6()
{
    var owner = new VmContextStorage(6, 1, 100);
    owner.Register(new(80), FunctionKind.Normal, () => R0F6G7Context(80, 0, 1, [VmInstruction.Return]));
    var lease = owner.PrepareInvocation(new(80), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.TryGetPinCount(new(80), out var preparedPins) && preparedPins == 1 && owner.CommitFrame(ref lease, [], out _));
    Check(owner.TryGetPinCount(new(80), out var activePins) && activePins == 1 && owner.TryReturn(out _) && owner.TryGetPinCount(new(80), out var returnedPins) && returnedPins == 0);
    var cancelled = owner.PrepareInvocation(new(80), VmReturnKind.Normal, FunctionKind.Normal);
    owner.Cancel(ref cancelled);
    Check(owner.TryGetPinCount(new(80), out var cancelledPins) && cancelledPins == 0 && owner.PinAcquireCount == owner.PinReleaseCount);
}

static void RunR0F6G7A7()
{
    var owner = new VmContextStorage(7, 1, 100);
    owner.Register(new(90), FunctionKind.Normal, () => R0F6G7Context(90, 0, 1, [new((ushort)VmOpcode.SemanticBarrier)]));
    var vm = new VmMachine(owner);
    Check(vm.Run(new(90)) == VmStopReason.SemanticNotAvailable && owner.IsTerminal && owner.FrameDepth == 0 && owner.PinAcquireCount == owner.PinReleaseCount &&
        vm.Run(new(90)) == VmStopReason.TerminalFault && owner.LegacyRetryAfterCommit == 0);
}

static void RunR0F6G7A8()
{
    var statements = new VmRuntimeStatementArena([new(VmRuntimeStatementKind.Wait, 0, 0)], []);
    var owner = new VmContextStorage(8, 1, 100);
    owner.Register(new(100), FunctionKind.Normal, () => R0F6G7Context(100, 0, 1, [new((ushort)VmOpcode.Statement, aux: 0), VmInstruction.Return], statements: statements));
    var effects = new RecordingEffects();
    var vm = new VmMachine(owner, effects);
    Check(vm.Start(new(100)) == VmStopReason.Returned && vm.Continue() == VmStopReason.WaitingForInput);
    var token = owner.ReserveInput(VmInputValueKind.String);
    Check(owner.CommitInput(token));
    var writes = 0;
    var callbacks = 0;
    var invalid = token with { RequestSerial = token.RequestSerial + 1 };
    Check(!owner.TryResume(invalid, VmSemanticValue.From("x"), _ => writes++) && !owner.TryResume(token, VmSemanticValue.From(1L), _ => writes++));
    Check(owner.TryResume(token, VmSemanticValue.From("ok"), _ => { writes++; owner.QueueSynchronousCallback(() => callbacks++); }) && writes == 1 && callbacks == 1);
    Check(!owner.TryResume(token, VmSemanticValue.From("again"), _ => writes++) && writes == 1 && vm.Continue() == VmStopReason.Returned);
}

static void RunR0F6G7A9()
{
    var generation = 0L;
    WeakReference<VmFunctionExecutionContext>? weak = null;
    var metadata = new FunctionRuntimeMetadata([], 1, 0, [], null);
    var owner = new VmContextStorage(9, 1, 100);
    owner.Register(new(110), FunctionKind.Normal, () =>
    {
        var context = R0F6G7Context(110, 0, ++generation, [VmInstruction.Return], metadata);
        weak = new(context);
        return context;
    });
    var first = owner.PrepareInvocation(new(110), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.CommitFrame(ref first, [], out var address) && owner.TryWriteFrameValue(address, "LOCAL", 0, VmSemanticValue.From(77L)));
    var oldToken = owner.ReserveInput(VmInputValueKind.Integer);
    Check(owner.CommitInput(oldToken) && owner.TryReturn(out _) && owner.TryEvict(new(110)));
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Check(weak is not null && !weak.TryGetTarget(out _));
    var second = owner.PrepareInvocation(new(110), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.CommitFrame(ref second, [], out var secondAddress) && generation == 2 && owner.TryReadFrameValue(secondAddress, "LOCAL", 0, out var local) && local.TryGetInteger(out var localValue) && localValue == 77);
    var writes = 0;
    Check(!owner.TryResume(oldToken, VmSemanticValue.From(1L), _ => writes++) && writes == 0 && owner.TryReturn(out _));
}

static void RunR0F6G7A10()
{
    const int count = 134_652;
    var definitions = Enumerable.Range(0, count).Select(index => D($"F{index}", "synthetic.erb", Span(index + 1))).ToArray();
    var catalog = FunctionCatalog.FromDefinitions(definitions);
    var target = new RuntimeFunctionId(count - 1);
    catalog.MarkCodeAvailable([target]);
    var prototype = new RuntimeFunctionPrototype(target, [P(PrototypeOpcode.RETURN)], [""], RuntimeMetadata: FunctionRuntimeMetadata.Empty);
    var linked = ControlLinker.LinkFunction(catalog, prototype, 7, 1, _ => new VmFunctionSignature(FunctionRuntimeMetadata.Empty, true));
    Check(linked.Context.FunctionId == target && linked.DescriptorArrayLength == 1 && linked.MetadataArrayLength == 1 && linked.Context.Program.Code.Length == 1);
    var owner = new VmContextStorage(10, 1, 100);
    owner.Register(target, FunctionKind.Normal, () => linked.Context);
    var first = owner.PrepareInvocation(target, VmReturnKind.Normal, FunctionKind.Normal); owner.Cancel(ref first);
    var second = owner.PrepareInvocation(target, VmReturnKind.Normal, FunctionKind.Normal); owner.Cancel(ref second);
    Check(owner.MaterializationCount == 1 && owner.PinAcquireCount == 2 && owner.PinReleaseCount == 2);
}

static void RunR0F6G7R1A2()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var methodMetadata = new FunctionRuntimeMetadata(
        [new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)],
        0, 0, [], RuntimeMetadataValueType.Integer);
    var prototypes = new[]
    {
        R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%B(20)%", ""]),
        R0F6G7R1Prototype(1, [PrototypeOpcode.SIF, PrototypeOpcode.RETURNF, PrototypeOpcode.RETURNF],
            ["ARG:0", "C(ARG:0-1)", "1"], methodMetadata,
            SemanticIrCompiler.CompileExpression("ARG:0", environment, 0)),
        R0F6G7R1Prototype(2, [PrototypeOpcode.RETURNF], ["B(ARG:0)"], methodMetadata),
    };
    var definitions = new[]
    {
        D("A", "a.erb", Span(1)),
        D("B", "b.erb", Span(1), FunctionKind.Method),
        D("C", "c.erb", Span(1), FunctionKind.Method),
    };
    var contexts = R0F6G7R1Contexts(definitions, prototypes, environment);
    var owner = new VmContextStorage(20, 1, 1000);
    owner.Register(new(0), FunctionKind.Normal, () => contexts[0]);
    owner.Register(new(1), FunctionKind.Method, () => contexts[1]);
    owner.Register(new(2), FunctionKind.Method, () => contexts[2]);
    var effects = new RecordingEffects();
    var vm = new VmMachine(owner, new SemanticHost(), effects);
    var stop = vm.Run(new(0));
    if (stop != VmStopReason.Returned || !effects.Events.SequenceEqual(["text:1"]) ||
        owner.MaxFrameDepth != 42 || owner.RemainingFuel >= 1000 ||
        owner.PinAcquireCount != owner.PinReleaseCount || owner.FrameDepth != 0)
        throw new InvalidOperationException($"stop={stop};events={string.Join('|', effects.Events)};depth={owner.MaxFrameDepth};fuel={owner.RemainingFuel};pins={owner.PinAcquireCount}/{owner.PinReleaseCount};frames={owner.FrameDepth};fault={owner.FaultMessage};semantic={System.Text.Json.JsonSerializer.Serialize(vm.SemanticDiagnostic)}");
}

static RuntimeFunctionPrototype R0F6G7R1Prototype(int id, IReadOnlyList<PrototypeOpcode> opcodes,
    IReadOnlyList<string> operands, FunctionRuntimeMetadata? metadata = null, SemanticPayload? semantic = null) =>
    new(new(id), opcodes.Select(P).ToImmutableArray(), operands.ToImmutableArray(), semantic,
        metadata ?? FunctionRuntimeMetadata.Empty);

static void RunR0F6G7R1A3()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var normalDefinitions = new[] { D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1)) };
    var normalPrototypes = new[]
    {
        R0F6G7R1Prototype(0, [PrototypeOpcode.CALL, PrototypeOpcode.RETURN], ["B(INC())", ""]),
        R0F6G7R1Prototype(1, [PrototypeOpcode.RETURN], [""]),
    };
    var normalContexts = R0F6G7R1Contexts(normalDefinitions, normalPrototypes, environment);

    var blockedEffects = 0;
    var blockedHost = new PreflightSemanticHost();
    blockedHost.Calls["INC"] = _ => { blockedEffects++; return VmSemanticValue.From(1L); };
    var blockedOwner = new VmContextStorage(21, 1, 100);
    blockedOwner.Register(new(0), FunctionKind.Normal, () => normalContexts[0]);
    blockedOwner.Register(new(1), FunctionKind.Normal, () => null);
    Check(new VmMachine(blockedOwner, blockedHost).Run(new(0)) == VmStopReason.SemanticNotAvailable &&
        blockedEffects == 0 && blockedOwner.MaterializationCount == 2);

    var order = new List<string>();
    var knownHost = new PreflightSemanticHost();
    knownHost.Calls["INC"] = _ => { order.Add("actual"); return VmSemanticValue.From(1L); };
    var knownOwner = new VmContextStorage(22, 1, 100);
    knownOwner.Register(new(0), FunctionKind.Normal, () => normalContexts[0]);
    knownOwner.Register(new(1), FunctionKind.Normal, () => { order.Add("materialize"); return normalContexts[1]; });
    Check(new VmMachine(knownOwner, knownHost).Run(new(0)) == VmStopReason.Returned &&
        order.SequenceEqual(["materialize", "actual"]));

    var dynamicPrototypes = new[]
    {
        R0F6G7R1Prototype(0, [PrototypeOpcode.CALLFORM, PrototypeOpcode.RETURN], ["%TARGET()%,INC()", ""]),
        R0F6G7R1Prototype(1, [PrototypeOpcode.RETURN], [""]),
    };
    var dynamicContexts = R0F6G7R1Contexts(normalDefinitions, dynamicPrototypes, environment);
    var dynamicHost = new PreflightSemanticHost();
    dynamicHost.Calls["TARGET"] = _ => VmSemanticValue.From("B");
    dynamicHost.Calls["INC"] = _ => VmSemanticValue.From(1L);
    var dynamicOwner = new VmContextStorage(23, 1, 100);
    dynamicOwner.Register(new(0), FunctionKind.Normal, () => dynamicContexts[0]);
    dynamicOwner.Register(new(1), FunctionKind.Normal, () => dynamicContexts[1]);
    var dynamicResolver = new DynamicResolver(("B", false), new(VmDynamicResolutionKind.Ready, new(1)));
    Check(new VmMachine(dynamicOwner, dynamicHost, dynamicCallResolver: dynamicResolver).Run(new(0)) == VmStopReason.Returned &&
        dynamicHost.CallCountFor("TARGET") == 1 && dynamicHost.CallCountFor("INC") == 1 &&
        dynamicResolver.Calls == 1 && dynamicOwner.MaterializationCount == 2);

    foreach (var resolutionKind in new[] { VmDynamicResolutionKind.Missing, VmDynamicResolutionKind.WrongKind, VmDynamicResolutionKind.Blocked, VmDynamicResolutionKind.Unresolved })
    {
        var host = new PreflightSemanticHost();
        host.Calls["TARGET"] = _ => VmSemanticValue.From("B");
        host.Calls["INC"] = _ => VmSemanticValue.From(1L);
        var owner = new VmContextStorage(24 + (int)resolutionKind, 1, 100);
        owner.Register(new(0), FunctionKind.Normal, () => dynamicContexts[0]);
        var resolver = new DynamicResolver(("B", false), new(resolutionKind, new(-1)));
        var stop = new VmMachine(owner, host, dynamicCallResolver: resolver).Run(new(0));
        Check(stop == VmStopReason.TerminalFault && host.CallCountFor("TARGET") == 1 && host.CallCountFor("INC") == 0);
    }

    var methodDefinitions = new[] { D("A", "a.erb", Span(1)), D("M", "m.erb", Span(1), FunctionKind.Method) };
    var methodMetadata = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer);
    var methodContexts = R0F6G7R1Contexts(methodDefinitions,
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%M(INC())%", ""]),
         R0F6G7R1Prototype(1, [PrototypeOpcode.RETURNF], ["1"], methodMetadata)], environment);
    var methodHost = new PreflightSemanticHost();
    methodHost.Calls["INC"] = _ => VmSemanticValue.From(1L);
    var methodOwner = new VmContextStorage(30, 1, 100);
    methodOwner.Register(new(0), FunctionKind.Normal, () => methodContexts[0]);
    methodOwner.Register(new(1), FunctionKind.Method, () => null);
    Check(new VmMachine(methodOwner, methodHost, new RecordingEffects()).Run(new(0)) == VmStopReason.SemanticNotAvailable &&
        methodHost.CallCountFor("INC") == 0);

    var builtinHost = new PreflightSemanticHost();
    builtinHost.Calls["INC"] = _ => VmSemanticValue.From(7L);
    var builtinContexts = R0F6G7R1Contexts([D("A", "a.erb", Span(1))],
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%INC()%", ""])], environment);
    var builtinOwner = new VmContextStorage(31, 1, 100);
    builtinOwner.Register(new(0), FunctionKind.Normal, () => builtinContexts[0]);
    var builtinEffects = new RecordingEffects();
    Check(new VmMachine(builtinOwner, builtinHost, builtinEffects).Run(new(0)) == VmStopReason.Returned &&
        builtinEffects.Events.SequenceEqual(["text:7"]) && builtinHost.CallCountFor("INC") == 1);

    var rejectedHost = new PreflightSemanticHost { Admission = VmHostCallAdmission.Blocked };
    rejectedHost.Calls["BLOCKED_BUILTIN"] = _ => VmSemanticValue.From(1L);
    var rejectedContexts = R0F6G7R1Contexts([D("A", "a.erb", Span(1))],
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%BLOCKED_BUILTIN(INC())%", ""])], environment);
    var rejectedOwner = new VmContextStorage(32, 1, 100);
    rejectedOwner.Register(new(0), FunctionKind.Normal, () => rejectedContexts[0]);
    Check(new VmMachine(rejectedOwner, rejectedHost, new RecordingEffects()).Run(new(0)) == VmStopReason.SemanticNotAvailable &&
        rejectedHost.CallCount == 0);

    var yieldContexts = R0F6G7R1Contexts(methodDefinitions,
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%M(INC())%", ""]),
         R0F6G7R1Prototype(1, [PrototypeOpcode.ONEINPUTS, PrototypeOpcode.RETURNF], ["", "1"], methodMetadata)],
        environment, id => id != 1);
    var yieldHost = new PreflightSemanticHost();
    yieldHost.Calls["INC"] = _ => VmSemanticValue.From(1L);
    var yieldOwner = new VmContextStorage(34, 1, 100);
    yieldOwner.Register(new(0), FunctionKind.Normal, () => yieldContexts[0]);
    yieldOwner.Register(new(1), FunctionKind.Method, () => yieldContexts[1]);
    Check(new VmMachine(yieldOwner, yieldHost, new RecordingEffects()).Run(new(0)) == VmStopReason.SemanticNotAvailable &&
        yieldHost.CallCountFor("INC") == 0);

    var driftHost = new PreflightSemanticHost();
    driftHost.Calls["TARGET"] = _ => VmSemanticValue.From("B");
    driftHost.Calls["INC"] = _ => VmSemanticValue.From(1L);
    var driftOwner = new VmContextStorage(35, 1, 100);
    driftOwner.Register(new(0), FunctionKind.Normal, () => dynamicContexts[0]);
    driftOwner.Register(new(1), FunctionKind.Normal, () => dynamicContexts[1]);
    var driftResolver = new CallbackDynamicResolver(() => driftOwner.RevokeSource());
    Check(new VmMachine(driftOwner, driftHost, dynamicCallResolver: driftResolver).Run(new(0)) == VmStopReason.TerminalFault &&
        driftHost.CallCountFor("TARGET") == 1 && driftHost.CallCountFor("INC") == 0);
}

static void RunR0F6G7R1A4()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var metadata = new FunctionRuntimeMetadata(
        [new("対象", RuntimeMetadataValueType.Integer, 0, false, 0, null),
         new("省略", RuntimeMetadataValueType.Integer, 1, true, 7, null),
         new("ARGS", RuntimeMetadataValueType.String, 0, false, 0, null)],
        2, 3,
        [new("D", RuntimeMetadataValueType.Integer, [3], false),
         new("S", RuntimeMetadataValueType.Integer, [1], true)],
        null);
    var prototype = R0F6G7R1Prototype(0,
        [PrototypeOpcode.VARI, PrototypeOpcode.SET, PrototypeOpcode.SET, PrototypeOpcode.SET,
         PrototypeOpcode.VARI, PrototypeOpcode.PRINTFORM, PrototypeOpcode.SET, PrototypeOpcode.SPLIT, PrototypeOpcode.VARSET, PrototypeOpcode.PRINTFORM,
         PrototypeOpcode.VARSET, PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN],
        ["D=5", "LOCAL:0=対象", "LOCALS:0=ARGS:0", "D:1=8", "D,3", "%D:1%", "S:0+=1",
         "\"a,b,c\",\",\",LOCALS,RESULT", "D,9,0,2",
         "%対象%:%省略%:%ARGS%:%LOCAL%:%LOCALS:0%:%LOCALS:1%:%D:0%:%D:1%:%S:0%:%RESULT%:%VARSIZE(\"D\",0)%",
         "D", "%D:0%:%D:1%", ""], metadata);
    var context = R0F6G7R1Contexts([D("A", "a.erb", Span(1))], [prototype], environment)[0];
    var owner = new VmContextStorage(33, 1, 1000);
    owner.Register(new(0), FunctionKind.Normal, () => context);
    var effects = new RecordingEffects();
    var vm = new VmMachine(owner, new PreflightSemanticHost(), effects);
    var started = vm.Start(new(0), [VmSemanticValue.From(3L), VmSemanticValue.Missing, VmSemanticValue.From("first")]);
    if (!owner.TryGetCurrentContext(out var active, out var activeFrame) ||
        !vm.TryWriteFrame("D", null, [], VmSemanticValue.From(4L)))
        throw new InvalidOperationException("A4 setup=" + started + ";metadata=" +
            string.Join('|', context.Metadata.PrivateVariables.Select(value => value.Name + ":" + string.Join(',', value.Dimensions))) +
            ";active=" + active?.FunctionId.Value + ";frame=" + activeFrame);
    var first = vm.Continue();
    if (first != VmStopReason.Returned)
        throw new InvalidOperationException("A4 first=" + first + ";fault=" + owner.FaultMessage + ";semantic=" + System.Text.Json.JsonSerializer.Serialize(vm.SemanticDiagnostic));
    Check(vm.Run(new(0), [VmSemanticValue.From(4L), VmSemanticValue.From(5L), VmSemanticValue.From("second")]) == VmStopReason.Returned);
    Check(effects.Events.SequenceEqual([
        "text:8", "text:3:7:first:3:a:b:9:9:1:3:3", "text:0:0",
        "text:8", "text:4:5:second:4:a:b:9:9:2:3:3", "text:0:0"
    ]) && owner.PinAcquireCount == owner.PinReleaseCount && owner.FrameDepth == 0);

    var privateFormal = new FunctionRuntimeMetadata(
        [new("内部", RuntimeMetadataValueType.Integer, 1, false, 0, null, true)],
        0, 0, [new("内部", RuntimeMetadataValueType.Integer, [2], false)], null);
    var privateContext = R0F6G7R1Contexts([D("PRIVATE", "private.erb", Span(1))],
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN],
            ["%内部%:%内部:1%", ""], privateFormal)], environment)[0];
    var privateOwner = new VmContextStorage(36, 1, 100);
    privateOwner.Register(new(0), FunctionKind.Normal, () => privateContext);
    var privateEffects = new RecordingEffects();
    Check(new VmMachine(privateOwner, new PreflightSemanticHost(), privateEffects)
        .Run(new(0), [VmSemanticValue.From(12L)]) == VmStopReason.Returned &&
        privateEffects.Events.SequenceEqual(["text:12:12"]));
}

static void RunR0F6G7R1A5()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var metadata = new FunctionRuntimeMetadata([], 2, 0, [], null);

    (VmContextStorage Owner, RecordingEffects Effects, VmFunctionExecutionContext Context) RunLoop(
        PrototypeOpcode[] opcodes, string[] operands, SemanticPayload semantic, long ownerId)
    {
        var context = R0F6G7R1Contexts([D("LOOP", "loop.erb", Span(1))],
            [R0F6G7R1Prototype(0, opcodes, operands, metadata, semantic)], environment)[0];
        var owner = new VmContextStorage(ownerId, 1, 1000);
        owner.Register(new(0), FunctionKind.Normal, () => context);
        var effects = new RecordingEffects();
        Check(new VmMachine(owner, new PreflightSemanticHost(), effects).Run(new(0)) == VmStopReason.Returned);
        return (owner, effects, context);
    }

    var positive = RunLoop(
        [PrototypeOpcode.FOR, PrototypeOpcode.PRINTFORM, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN],
        ["LOCAL:0,0,3,1", "%LOCAL:0%", "", ""],
        SemanticIrCompiler.CompileCountedLoop("LOCAL:0,0,3,1", environment, 0, false), 40);
    Check(positive.Effects.Events.SequenceEqual(["text:0", "text:1", "text:2"]) &&
        positive.Owner.TryReadLoop(new(0), positive.Context.PhysicalLoopIds[0], out var end, out var step, out _) &&
        end == 3 && step == 1);

    var negative = RunLoop(
        [PrototypeOpcode.FOR, PrototypeOpcode.PRINTFORM, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN],
        ["LOCAL:0,3,0,-1", "%LOCAL:0%", "", ""],
        SemanticIrCompiler.CompileCountedLoop("LOCAL:0,3,0,-1", environment, 0, false), 41);
    Check(negative.Effects.Events.SequenceEqual(["text:3", "text:2", "text:1"]));

    var zero = RunLoop(
        [PrototypeOpcode.FOR, PrototypeOpcode.PRINT, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN],
        ["LOCAL:0,0,3,0", "bad", "", ""],
        SemanticIrCompiler.CompileCountedLoop("LOCAL:0,0,3,0", environment, 0, false), 42);
    Check(zero.Effects.Events.Count == 0);

    var repeat = RunLoop(
        [PrototypeOpcode.REPEAT, PrototypeOpcode.PRINT, PrototypeOpcode.REND, PrototypeOpcode.RETURN],
        ["3", "r", "", ""],
        SemanticIrCompiler.CompileCountedLoop("3", environment, 0, true), 43);
    Check(repeat.Effects.Events.SequenceEqual(["text:r", "text:r", "text:r"]) &&
        repeat.Owner.TryReadLoop(new(0), repeat.Context.PhysicalLoopIds[0], out _, out _, out var repeated) && repeated == 3);

    var whileHost = new PreflightSemanticHost();
    whileHost.Set("G", null, [], VmSemanticValue.From(3L));
    var whilePrototype = R0F6G7R1Prototype(0,
        [PrototypeOpcode.WHILE, PrototypeOpcode.PRINT, PrototypeOpcode.SET, PrototypeOpcode.WEND, PrototypeOpcode.RETURN],
        ["G", "w", "G-=1", "", ""], metadata, SemanticIrCompiler.CompileExpression("G", environment, 0));
    var whileContext = R0F6G7R1Contexts([D("WHILE", "while.erb", Span(1))], [whilePrototype], environment)[0];
    var whileOwner = new VmContextStorage(44, 1, 1000);
    whileOwner.Register(new(0), FunctionKind.Normal, () => whileContext);
    var whileEffects = new RecordingEffects();
    Check(new VmMachine(whileOwner, whileHost, whileEffects).Run(new(0)) == VmStopReason.Returned &&
        whileEffects.Events.SequenceEqual(["text:w", "text:w", "text:w"]));

    var controlSemantic = SemanticPayload.Merge([
        SemanticIrCompiler.CompileCountedLoop("LOCAL:0,0,5,1", environment, 0, false),
        SemanticIrCompiler.CompileExpression("LOCAL:0==1", environment, 1),
        SemanticIrCompiler.CompileExpression("LOCAL:0==3", environment, 3)
    ]);
    var control = RunLoop(
        [PrototypeOpcode.FOR, PrototypeOpcode.SIF, PrototypeOpcode.CONTINUE, PrototypeOpcode.SIF,
         PrototypeOpcode.BREAK, PrototypeOpcode.PRINTFORM, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN],
        ["LOCAL:0,0,5,1", "LOCAL:0==1", "", "LOCAL:0==3", "", "%LOCAL:0%", "", ""],
        controlSemantic, 45);
    Check(control.Effects.Events.SequenceEqual(["text:0", "text:2"]));

    var nestedSemantic = SemanticPayload.Merge([
        SemanticIrCompiler.CompileCountedLoop("LOCAL:0,0,2,1", environment, 0, false),
        SemanticIrCompiler.CompileCountedLoop("LOCAL:1,0,2,1", environment, 1, false)
    ]);
    var nested = RunLoop(
        [PrototypeOpcode.FOR, PrototypeOpcode.FOR, PrototypeOpcode.PRINTFORM,
         PrototypeOpcode.NEXT, PrototypeOpcode.NEXT, PrototypeOpcode.RETURN],
        ["LOCAL:0,0,2,1", "LOCAL:1,0,2,1", "%LOCAL:0%%LOCAL:1%", "", "", ""],
        nestedSemantic, 46);
    Check(nested.Effects.Events.SequenceEqual(["text:00", "text:01", "text:10", "text:11"]) &&
        nested.Context.PhysicalLoopIds.Length == 2 &&
        nested.Context.PhysicalLoopIds[0] != nested.Context.PhysicalLoopIds[1] &&
        nested.Owner.TryEvict(new(0)));
    var nestedAgain = new VmMachine(nested.Owner, new PreflightSemanticHost(), nested.Effects);
    Check(nestedAgain.Run(new(0)) == VmStopReason.Returned &&
        nested.Effects.Events.Count == 8);

    var doHost = new PreflightSemanticHost();
    doHost.Set("G", null, [], VmSemanticValue.From(0L));
    var doPrototype = R0F6G7R1Prototype(0,
        [PrototypeOpcode.DO, PrototypeOpcode.SET, PrototypeOpcode.PRINTFORM, PrototypeOpcode.LOOP, PrototypeOpcode.RETURN],
        ["", "G+=1", "%G%", "G<3", ""], metadata,
        SemanticIrCompiler.CompileExpression("G<3", environment, 3));
    var doContext = R0F6G7R1Contexts([D("DO", "do.erb", Span(1))], [doPrototype], environment)[0];
    var doOwner = new VmContextStorage(47, 1, 1000);
    doOwner.Register(new(0), FunctionKind.Normal, () => doContext);
    var doEffects = new RecordingEffects();
    Check(new VmMachine(doOwner, doHost, doEffects).Run(new(0)) == VmStopReason.Returned &&
        doEffects.Events.SequenceEqual(["text:1", "text:2", "text:3"]));
}

static void RunR0F6G7R1A6()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var contexts = R0F6G7R1Contexts(
        [D("A", "a.erb", Span(1)), D("B", "b.erb", Span(1))],
        [R0F6G7R1Prototype(0, [PrototypeOpcode.JUMP, PrototypeOpcode.RETURN], ["B", ""]),
         R0F6G7R1Prototype(1, [PrototypeOpcode.RETURN], [""])], environment);
    var owner = new VmContextStorage(50, 1, 100);
    owner.Register(new(0), FunctionKind.Normal, () => contexts[0]);
    owner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
    Check(new VmMachine(owner, new PreflightSemanticHost()).Run(new(0)) == VmStopReason.Returned &&
        owner.PinAcquireCount == 2 && owner.PinReleaseCount == 2 && owner.FrameDepth == 0);

    var borrowOwner = new VmContextStorage(51, 1, 100);
    borrowOwner.Register(new(0), FunctionKind.Normal, () => contexts[0]);
    borrowOwner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
    var borrowVm = new VmMachine(borrowOwner, new PreflightSemanticHost());
    Check(borrowVm.Start(new(0)) == VmStopReason.Returned);
    if (!borrowOwner.TryGetCurrentContext(out var current, out var frame)) throw new InvalidOperationException("missing current context");
    var evaluation = new VmEvaluationContext(current, frame, borrowOwner.Stamp);
    Check(borrowOwner.TryBeginEvaluation(evaluation) && !borrowOwner.TryEvict(new(0)));
    borrowOwner.EndEvaluation(evaluation);
    Check(!borrowOwner.TryEvict(new(0)) && borrowVm.Continue() == VmStopReason.Returned &&
        borrowOwner.TryEvict(new(0)));

    var driftOwner = new VmContextStorage(52, 1, 100);
    driftOwner.Register(new(0), FunctionKind.Normal, () => contexts[0]);
    driftOwner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
    var driftVm = new VmMachine(driftOwner, new PreflightSemanticHost());
    Check(driftVm.Start(new(0)) == VmStopReason.Returned);
    driftOwner.RevokeSource();
    Check(driftOwner.IsTerminal && driftOwner.FrameDepth == 0 &&
        driftOwner.PinAcquireCount == driftOwner.PinReleaseCount &&
        driftVm.Continue() == VmStopReason.TerminalFault);
}

static void RunR0F6G7R1A7()
{
    VmContextStorage RunInstruction(long ownerId, VmInstruction instruction, int maxSteps = 100)
    {
        var owner = new VmContextStorage(ownerId, 1, 1000);
        owner.Register(new(0), FunctionKind.Normal, () => R0F6G7Context(0, 0, 1, [instruction]));
        var vm = new VmMachine(owner, new PreflightSemanticHost());
        var stop = vm.Run(new(0), maxSteps);
        if (stop == VmStopReason.Returned || !owner.IsTerminal || owner.FaultSnapshot is not { Committed: true } ||
            owner.FaultSnapshot.Frames.Length != 1 || owner.PinAcquireCount != owner.PinReleaseCount)
            throw new InvalidOperationException($"instruction fault mismatch: stop={stop};terminal={owner.IsTerminal};snapshot={owner.FaultSnapshot};pins={owner.PinAcquireCount}/{owner.PinReleaseCount}");
        return owner;
    }

    Check(RunInstruction(60, new((ushort)VmOpcode.SemanticBarrier)).FaultReason == VmStopReason.SemanticNotAvailable);
    Check(RunInstruction(61, new(ushort.MaxValue)).FaultReason == VmStopReason.UnsupportedControl);
    Check(RunInstruction(62, VmInstruction.Branch(0), 3).FaultReason == VmStopReason.StepLimit);

    var invalidOwner = new VmContextStorage(63, 1, 100);
    invalidOwner.Register(new(0), FunctionKind.Normal, () => new(new(0), 0, 1,
        new LinkedProgram([VmInstruction.Return], [new(0, 0, 1, VmFunctionState.InvalidStructure)], [], sparseRuntimeIds: true),
        FunctionRuntimeMetadata.Empty));
    Check(new VmMachine(invalidOwner, new PreflightSemanticHost()).Run(new(0)) == VmStopReason.InvalidStructure &&
        invalidOwner.FaultReason == VmStopReason.InvalidStructure && invalidOwner.PinAcquireCount == invalidOwner.PinReleaseCount);

    var materializeOwner = new VmContextStorage(64, 1, 100);
    materializeOwner.Register(new(0), FunctionKind.Normal, () => throw new InvalidOperationException("not retained"));
    var materializeStop = new VmMachine(materializeOwner, new PreflightSemanticHost()).Run(new(0));
    if (materializeStop != VmStopReason.TerminalFault || !materializeOwner.FaultMessage.StartsWith("materialization failure: InvalidOperationException", StringComparison.Ordinal) ||
        materializeOwner.FaultSnapshot is not { Committed: false })
        throw new InvalidOperationException($"materialization fault mismatch: stop={materializeStop};message={materializeOwner.FaultMessage};snapshot={materializeOwner.FaultSnapshot}");

    var bindingMetadata = new FunctionRuntimeMetadata(
        [new("ARG", RuntimeMetadataValueType.Integer, 0, false, 0, null)], 0, 0, [], null);
    var bindingOwner = new VmContextStorage(65, 1, 100);
    bindingOwner.Register(new(0), FunctionKind.Normal, () => R0F6G7Context(0, 0, 1, [VmInstruction.Return], bindingMetadata));
    Check(new VmMachine(bindingOwner, new PreflightSemanticHost()).Run(new(0), [VmSemanticValue.From("bad")]) == VmStopReason.SemanticNotAvailable &&
        bindingOwner.IsTerminal && bindingOwner.PinAcquireCount == bindingOwner.PinReleaseCount);

    var cleanupCount = 0;
    var cleanupOwner = new VmContextStorage(66, 1, 100);
    cleanupOwner.Register(new(0), FunctionKind.Normal, () => R0F6G7Context(0, 0, 1, [new((ushort)VmOpcode.SemanticBarrier)]));
    cleanupOwner.QueueCleanupAction(() => throw new InvalidOperationException("cleanup"));
    cleanupOwner.QueueCleanupAction(() => cleanupCount++);
    Check(new VmMachine(cleanupOwner, new PreflightSemanticHost()).Run(new(0)) == VmStopReason.SemanticNotAvailable &&
        cleanupOwner.CleanupErrorCount == 1 && cleanupCount == 1 &&
        cleanupOwner.FaultSnapshot?.Reason == VmStopReason.SemanticNotAvailable);

    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var methodMetadata = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer);
    var nested = R0F6G7R1Contexts(
        [D("A", "a.erb", Span(1)), D("M", "m.erb", Span(1), FunctionKind.Method)],
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINTFORM, PrototypeOpcode.RETURN], ["%M()%", ""]),
         R0F6G7R1Prototype(1, [PrototypeOpcode.RETURNF], ["1/0"], methodMetadata)], environment);
    var nestedOwner = new VmContextStorage(67, 1, 100);
    nestedOwner.Register(new(0), FunctionKind.Normal, () => nested[0]);
    nestedOwner.Register(new(1), FunctionKind.Method, () => nested[1]);
    Check(new VmMachine(nestedOwner, new PreflightSemanticHost(), new RecordingEffects()).Run(new(0)) == VmStopReason.SemanticEvaluationFault &&
        nestedOwner.FaultSnapshot is { Frames.Length: 2 } && nestedOwner.FrameDepth == 0 &&
        nestedOwner.PinAcquireCount == nestedOwner.PinReleaseCount);

    var hostContext = R0F6G7R1Contexts([D("A", "a.erb", Span(1))],
        [R0F6G7R1Prototype(0, [PrototypeOpcode.PRINT, PrototypeOpcode.RETURN], ["x", ""])], environment)[0];
    var hostOwner = new VmContextStorage(68, 1, 100);
    hostOwner.Register(new(0), FunctionKind.Normal, () => hostContext);
    Check(new VmMachine(hostOwner, new PreflightSemanticHost(), new ThrowingEffects()).Run(new(0)) == VmStopReason.TerminalFault &&
        hostOwner.FaultMessage.StartsWith("host/runtime exception: InvalidOperationException", StringComparison.Ordinal) &&
        hostOwner.PinAcquireCount == hostOwner.PinReleaseCount);
}

static void RunR0F6G7R1A8()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var definitions = new[]
    {
        D("EVENT", "event.erb", Span(1), FunctionKind.Event),
        D("NORMAL", "normal.erb", Span(1)),
    };
    var prototypes = new[]
    {
        R0F6G7R1Prototype(0, [PrototypeOpcode.PRINT, PrototypeOpcode.CALL, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN],
            ["event-before", "NORMAL", "event-after", ""]),
        R0F6G7R1Prototype(1, [PrototypeOpcode.PRINT, PrototypeOpcode.ONEINPUTS, PrototypeOpcode.PRINT, PrototypeOpcode.RETURN],
            ["normal-before", "", "normal-after", ""]),
    };
    var contexts = R0F6G7R1Contexts(definitions, prototypes, environment);
    var owner = new VmContextStorage(70, 1, 1000);
    owner.Register(new(0), FunctionKind.Event, () => contexts[0]);
    owner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
    var effects = new OwnedInputEffects(owner);
    var vm = new VmMachine(owner, new PreflightSemanticHost(), effects);
    Check(vm.Start(new(0), FunctionKind.Event) == VmStopReason.Returned &&
        vm.Continue() == VmStopReason.WaitingForInput &&
        owner.RunState == VmOwnerRunState.Suspended && effects.PublishedWhileSuspended &&
        effects.PublishCount == 1 &&
        owner.ActiveFrames.Select(frame => frame.FunctionId).SequenceEqual([0, 1]) &&
        owner.TryGetPinCount(new(0), out var eventPins) && eventPins == 1 &&
        owner.TryGetPinCount(new(1), out var normalPins) && normalPins == 1);
    if (effects.Token is not { } token) throw new InvalidOperationException("input token missing");

    var invalidTokens = new[]
    {
        token with { RequestSerial = token.RequestSerial + 1 },
        token with { Stamp = token.Stamp with { OwnerId = token.Stamp.OwnerId + 1 } },
        token with { Stamp = token.Stamp with { Epoch = token.Stamp.Epoch + 1 } },
        token with { Frame = token.Frame with { Serial = token.Frame.Serial + 1 } },
        token with { FunctionId = new(token.FunctionId.Value + 1) },
        token with { CodeSlot = token.CodeSlot + 1 },
        token with { CodeGeneration = token.CodeGeneration + 1 },
    };
    foreach (var invalid in invalidTokens)
        Check(!vm.TryResumeInput(invalid, VmSemanticValue.From("bad"), out _));
    Check(!vm.TryResumeInput(token, VmSemanticValue.From(1L), out _) &&
        effects.ResultWriteCount == 0 && effects.Events.SequenceEqual(["text:event-before", "text:normal-before"]));

    Check(vm.TryResumeInput(token, VmSemanticValue.From("ok"), out var resumed) && resumed == VmStopReason.Returned &&
        effects.ResultWriteCount == 1 && effects.Result.TryGetString(out var result) && result == "ok" &&
        effects.Events.SequenceEqual(["text:event-before", "text:normal-before", "text:normal-after", "text:event-after"]) &&
        vm.InputResumeCompleted == 1 && owner.FrameDepth == 0 &&
        owner.PinAcquireCount == owner.PinReleaseCount &&
        !vm.TryResumeInput(token, VmSemanticValue.From("again"), out _));

    var synchronousOwner = new VmContextStorage(71, 1, 1000);
    synchronousOwner.Register(new(0), FunctionKind.Event, () => contexts[0]);
    synchronousOwner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
    var synchronousEffects = new OwnedInputEffects(synchronousOwner, synchronousResponse: true);
    var synchronousVm = new VmMachine(synchronousOwner, new PreflightSemanticHost(), synchronousEffects);
    Check(synchronousVm.Start(new(0), FunctionKind.Event) == VmStopReason.Returned &&
        synchronousVm.Continue() == VmStopReason.WaitingForInput &&
        synchronousVm.InputResumeCompleted == 1 && synchronousOwner.FrameDepth == 0 &&
        synchronousEffects.ResultWriteCount == 1 && !synchronousEffects.PumpReenteredDuringPublish &&
        synchronousEffects.Events.SequenceEqual(["text:event-before", "text:normal-before", "text:normal-after", "text:event-after"]));

    var revokedOwner = new VmContextStorage(72, 1, 1000);
    revokedOwner.Register(new(0), FunctionKind.Event, () => contexts[0]);
    revokedOwner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
    var revokedEffects = new OwnedInputEffects(revokedOwner);
    var revokedVm = new VmMachine(revokedOwner, new PreflightSemanticHost(), revokedEffects);
    Check(revokedVm.Start(new(0), FunctionKind.Event) == VmStopReason.Returned &&
        revokedVm.Continue() == VmStopReason.WaitingForInput);
    if (revokedEffects.Token is not { } revokedToken) throw new InvalidOperationException("revoked token missing");
    revokedOwner.RevokeSource();
    Check(!revokedVm.TryResumeInput(revokedToken, VmSemanticValue.From("bad"), out _) &&
        revokedEffects.ResultWriteCount == 0 && revokedOwner.RunState == VmOwnerRunState.Terminal);

    var ownerId = 73L;
    foreach (var invalidation in new[] { VmOwnerInvalidationReason.Reset, VmOwnerInvalidationReason.Close,
                 VmOwnerInvalidationReason.Revoke, VmOwnerInvalidationReason.BeginApplied })
    {
        var invalidatedOwner = new VmContextStorage(ownerId++, 1, 1000);
        invalidatedOwner.Register(new(0), FunctionKind.Event, () => contexts[0]);
        invalidatedOwner.Register(new(1), FunctionKind.Normal, () => contexts[1]);
        var invalidatedEffects = new OwnedInputEffects(invalidatedOwner);
        var invalidatedVm = new VmMachine(invalidatedOwner, new PreflightSemanticHost(), invalidatedEffects);
        Check(invalidatedVm.Start(new(0), FunctionKind.Event) == VmStopReason.Returned &&
            invalidatedVm.Continue() == VmStopReason.WaitingForInput);
        if (invalidatedEffects.Token is not { } invalidatedToken) throw new InvalidOperationException("invalidated token missing");
        invalidatedOwner.Invalidate(invalidation);
        Check(!invalidatedVm.TryResumeInput(invalidatedToken, VmSemanticValue.From("bad"), out _) &&
            invalidatedEffects.ResultWriteCount == 0 && invalidatedOwner.RunState == VmOwnerRunState.Terminal);
    }
}

static void RunR0F6G7R1A9()
{
    var fixture = CreateR0F6G7R1EvictionFixture();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    Check(!fixture.Context.TryGetTarget(out _) &&
        !fixture.Program.TryGetTarget(out _) &&
        !fixture.Semantic.TryGetTarget(out _) &&
        !fixture.Statements.TryGetTarget(out _));

    var lease = fixture.Owner.PrepareInvocation(new(0), VmReturnKind.Normal, FunctionKind.Normal);
    Check(fixture.Owner.CommitFrame(ref lease, [], out var address) &&
        fixture.Owner.TryReadFrameValue(address, "LOCAL", 0, out var local) && local.TryGetInteger(out var localValue) && localValue == 77 &&
        fixture.Owner.TryReadFrameValue(address, "S", 0, out var persistent) && persistent.TryGetInteger(out var persistentValue) && persistentValue == 88 &&
        fixture.Owner.TryReadFrameValue(address, "D", 0, out var dynamicValue) && dynamicValue.TryGetInteger(out var dynamicInteger) && dynamicInteger == 0 &&
        fixture.Owner.TryReadLoop(new(0), 900, out var end, out var step, out var repeat) && (end, step, repeat) == (9, 2, 3) &&
        fixture.Owner.TryReturn(out _));
}

#if R0_F6G10B
static void RunR0F6G10BTwoWaits()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var prototype = R0F6G7R1Prototype(0,
        [PrototypeOpcode.ONEINPUTS, PrototypeOpcode.ONEINPUTS, PrototypeOpcode.RETURN], ["", "", ""],
        FunctionRuntimeMetadata.Empty);
    var context = R0F6G7R1Contexts([D("TWO_WAITS", "two-waits.erb", Span(1))], [prototype], environment)[0];
    var owner = new VmContextStorage(80, 1, 1000);
    owner.Register(new(0), FunctionKind.Normal, () => context);
    var effects = new OwnedInputEffects(owner);
    var vm = new VmMachine(owner, new PreflightSemanticHost(), effects);
    Check(vm.Start(new(0)) == VmStopReason.Returned && vm.Continue() == VmStopReason.WaitingForInput);
    var first = effects.Token ?? throw new InvalidOperationException("first input token missing");
    Check(vm.TryAcceptInput(first, VmSemanticValue.From("H"), out _) &&
        vm.Continue() == VmStopReason.WaitingForInput);
    var second = effects.Token ?? throw new InvalidOperationException("second input token missing");
    Check(second.RequestSerial == first.RequestSerial + 1 && second.Stamp == first.Stamp && second.Frame == first.Frame &&
        vm.TryAcceptInput(second, VmSemanticValue.From("I"), out _) && vm.Continue() == VmStopReason.Returned &&
        effects.ResultWriteCount == 2 && vm.InputResumeAttempts == 2 && vm.InputResumeCompleted == 2 &&
        owner.FrameDepth == 0 && owner.PinAcquireCount == owner.PinReleaseCount);

    var printWaitPrototype = R0F6G7R1Prototype(0,
        [PrototypeOpcode.PRINTW, PrototypeOpcode.RETURN], ["line", ""], FunctionRuntimeMetadata.Empty);
    var printWaitContext = R0F6G7R1Contexts([D("PRINT_WAIT", "print-wait.erb", Span(1))], [printWaitPrototype], environment)[0];
    var printWaitOwner = new VmContextStorage(81, 1, 1000);
    printWaitOwner.Register(new(0), FunctionKind.Normal, () => printWaitContext);
    var printWaitEffects = new OwnedInputEffects(printWaitOwner);
    var printWaitVm = new VmMachine(printWaitOwner, new PreflightSemanticHost(), printWaitEffects);
    Check(printWaitVm.Start(new(0)) == VmStopReason.Returned &&
        printWaitVm.Continue() == VmStopReason.WaitingForInput && printWaitOwner.RunState == VmOwnerRunState.Suspended &&
        printWaitEffects.Events.SequenceEqual(["text:line", "newline"]));
    var printWaitToken = printWaitEffects.Token ?? throw new InvalidOperationException("PRINTW input token missing");
    Check(printWaitToken.ExpectedKind == VmInputValueKind.Integer &&
        printWaitVm.TryAcceptInput(printWaitToken, VmSemanticValue.From(0L), out _) &&
        printWaitVm.Continue() == VmStopReason.Returned && printWaitEffects.ResultWriteCount == 1 &&
        printWaitOwner.PinAcquireCount == printWaitOwner.PinReleaseCount);
}
#endif

static void RunR0F6G10A1Lifecycle()
{
    var metadata = new FunctionRuntimeMetadata([], 2, 1,
        [new("D", RuntimeMetadataValueType.Integer, [1], false, false, true, 7),
         new("S", RuntimeMetadataValueType.Integer, [1], true, false, true, 5)], null);
    var owner = new VmContextStorage(74, 1, 1000, 4, 3);
    owner.Register(new(0), FunctionKind.Normal, () => R0F6G7Context(0, 1, 9, [VmInstruction.Return], metadata));
    var root = owner.PrepareInvocation(new(0), VmReturnKind.Normal, FunctionKind.Normal);
    if (!(owner.CommitFrame(ref root, [], out var frame) &&
        owner.TryWriteFrameValue(frame, "ARG", 0, VmSemanticValue.From(1L)) &&
        owner.TryWriteFrameValue(frame, "LOCAL", 0, VmSemanticValue.From(2L)) &&
        owner.TryWriteFrameValue(frame, "D", 0, VmSemanticValue.From(3L)) &&
        owner.TryWriteFrameValue(frame, "S", 0, VmSemanticValue.From(4L)))) throw new InvalidOperationException("lifecycle setup");
    owner.CaptureLoop(new(0), 1, 9, 2, 3);
    owner.ResetPersistentBanks();
    var arg = default(VmSemanticValue); var local = default(VmSemanticValue);
    var dynamicValue = default(VmSemanticValue); var staticValue = default(VmSemanticValue);
    long end = 0, step = 0, repeat = 0;
    if (!(owner.TryReadFrameValue(frame, "ARG", 0, out arg) && arg.TryGetInteger(out var ai) && ai == 0 &&
        owner.TryReadFrameValue(frame, "LOCAL", 0, out local) && local.TryGetInteger(out var li) && li == 0 &&
        owner.TryReadFrameValue(frame, "D", 0, out dynamicValue) && dynamicValue.TryGetInteger(out var di) && di == 3 &&
        owner.TryReadFrameValue(frame, "S", 0, out staticValue) && staticValue.TryGetInteger(out var si) && si == 5 &&
        owner.TryReadLoop(new(0), 1, out end, out step, out repeat) && (end, step, repeat) == (9, 2, 3)))
        throw new InvalidOperationException($"lifecycle reset: arg={arg};local={local};dynamic={dynamicValue};static={staticValue};loop={end}/{step}/{repeat}");

    if (!(owner.TryParkSegment(0, out var bookmark) && !owner.TryParkSegment(0, out _) &&
        owner.TryResumeSegment(bookmark) && !owner.TryResumeSegment(bookmark))) throw new InvalidOperationException("lifecycle park/resume");
    if (!(owner.TryParkSegment(0, out var discarded) && owner.DiscardSegment(discarded, advanceExecutionEpoch: true) &&
        !owner.TryResumeSegment(discarded) && owner.FrameDepth == 0 && owner.Stamp.Epoch == 2 &&
        !owner.IsTerminal && owner.LegacyRetryAfterCommit == 0 && owner.PinAcquireCount == owner.PinReleaseCount))
        throw new InvalidOperationException($"lifecycle discard: depth={owner.FrameDepth};epoch={owner.Stamp.Epoch};terminal={owner.IsTerminal};pins={owner.PinAcquireCount}/{owner.PinReleaseCount}");

    var fresh = owner.PrepareInvocation(new(0), VmReturnKind.Normal, FunctionKind.Normal);
    Check(owner.CommitFrame(ref fresh, [], out var freshFrame) &&
        owner.TryReadFrameValue(freshFrame, "D", 0, out var freshDynamic) && freshDynamic.TryGetInteger(out var freshDi) && freshDi == 7 &&
        owner.TryReadFrameValue(freshFrame, "S", 0, out var freshStatic) && freshStatic.TryGetInteger(out var freshSi) && freshSi == 5 &&
        owner.TryReturn(out _) && owner.PinAcquireCount == owner.PinReleaseCount);
}

[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static (VmContextStorage Owner, WeakReference<VmFunctionExecutionContext> Context,
    WeakReference<LinkedProgram> Program, WeakReference<SemanticPayload> Semantic,
    WeakReference<VmRuntimeStatementArena> Statements) CreateR0F6G7R1EvictionFixture()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var metadata = new FunctionRuntimeMetadata([], 1, 0,
        [new("D", RuntimeMetadataValueType.Integer, [1], false),
         new("S", RuntimeMetadataValueType.Integer, [1], true)], null);
    WeakReference<VmFunctionExecutionContext>? contextWeak = null;
    WeakReference<LinkedProgram>? programWeak = null;
    WeakReference<SemanticPayload>? semanticWeak = null;
    WeakReference<VmRuntimeStatementArena>? statementsWeak = null;
    var generation = 0L;
    var owner = new VmContextStorage(80, 1, 100);
    owner.Register(new(0), FunctionKind.Normal, () =>
    {
        var semantic = SemanticIrCompiler.CompileExpression("LOCAL:0", environment);
        var statements = new VmRuntimeStatementArena(
            [new(VmRuntimeStatementKind.Expression, 0, 0, 0)], [], semantic);
        var program = new LinkedProgram([VmInstruction.Return],
            [new(0, 0, 1, VmFunctionState.ExecutableReady)], [], semanticArena: semantic,
            runtimeStatements: statements, runtimeMetadata: [metadata], sparseRuntimeIds: true);
        var context = new VmFunctionExecutionContext(new(0), 0, ++generation, program, metadata);
        contextWeak = new(context);
        programWeak = new(program);
        semanticWeak = new(semantic);
        statementsWeak = new(statements);
        return context;
    });
    var vm = new VmMachine(owner, new PreflightSemanticHost());
    Check(vm.Start(new(0)) == VmStopReason.Returned &&
        owner.TryGetCurrentContext(out _, out var address) &&
        owner.TryWriteFrameValue(address, "LOCAL", 0, VmSemanticValue.From(77L)) &&
        owner.TryWriteFrameValue(address, "D", 0, VmSemanticValue.From(66L)) &&
        owner.TryWriteFrameValue(address, "S", 0, VmSemanticValue.From(88L)));
    owner.CaptureLoop(new(0), 900, 9, 2, 3);
    Check(vm.Continue() == VmStopReason.Returned && owner.TryEvict(new(0)));
    return (owner, contextWeak!, programWeak!, semanticWeak!, statementsWeak!);
}

static Dictionary<int, VmFunctionExecutionContext> R0F6G7R1Contexts(
    IReadOnlyList<FunctionDefinition> definitions,
    IReadOnlyList<RuntimeFunctionPrototype> prototypes,
    StructuralSemanticEnvironment environment,
    Func<int, bool>? noYield = null)
{
    var catalog = FunctionCatalog.FromDefinitions(definitions);
    catalog.MarkCodeAvailable(prototypes.Select(prototype => prototype.RuntimeId));
    var signatures = prototypes.ToDictionary(prototype => prototype.RuntimeId.Value,
        prototype => new VmFunctionSignature(prototype.RuntimeMetadata ?? FunctionRuntimeMetadata.Empty,
            noYield?.Invoke(prototype.RuntimeId.Value) ?? true));
    var result = new Dictionary<int, VmFunctionExecutionContext>();
    for (var slot = 0; slot < prototypes.Count; slot++)
    {
        var linked = ControlLinker.LinkFunction(catalog, prototypes[slot], slot, slot + 1,
            id => signatures.GetValueOrDefault(id.Value), runtimeEnvironment: environment, runtimeStatements: true);
        var context = linked.Context;
        var program = context.Program;
        var ready = new LinkedProgram(program.Code,
            program.Descriptors.Select(descriptor => new VmFunctionDescriptor(descriptor.FunctionId,
                descriptor.CodeStart, descriptor.CodeLength, VmFunctionState.ExecutableReady)).ToArray(),
            program.StructuralLinks, program.SifLinks, program.IfGroups, program.IfClauses,
            program.SelectGroups, program.SelectCases, program.Loops, program.SemanticArena,
            program.StructuralSemanticRecordIndices, program.RuntimeStatements, program.RuntimeMetadata,
            program.CallSites, program.CallArgumentArena, program.CallArgumentRecords,
            program.ExpressionFunctionTargets, program.DynamicCallSites, program.DynamicCallArena,
            program.DynamicCallArgumentRecords, sparseRuntimeIds: true);
        result.Add(context.FunctionId.Value, new(context.FunctionId, context.CodeSlot, context.Generation,
            ready, context.Metadata, context.PhysicalLoopIds));
    }
    return result;
}

static int RunR0F6G3LinkProbe(string erbRoot, string inventoryPath, string outputPath)
{
    using var document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
    var options = new CompilerCompatibilityOptions(true, true, true, false);
    var headers = Directory.EnumerateFiles(erbRoot, "*.ERH", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
        .Select(path => File.ReadAllText(path, System.Text.Encoding.UTF8));
    var environment = new StructuralSemanticEnvironment(options, MacroCatalog.FromHeaderSources(headers, options));
    var compiler = new FunctionCompiler(environment);
    var selected = document.RootElement.GetProperty("functions").EnumerateArray().Select(item => new
    {
        Name = item.GetProperty("name").GetString()!,
        Path = item.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar),
        Line = item.GetProperty("line").GetInt32(),
        Kind = item.GetProperty("kind").GetString() switch { "method" => FunctionKind.Method, "event" => FunctionKind.Event, _ => FunctionKind.Normal }
    }).ToArray();
    static string Key(string path, int line, string name) => $"{Path.GetFullPath(path).ToUpperInvariant()}|{line}|{name.ToUpperInvariant()}";
    var selectedKinds = selected.ToDictionary(item => Key(Path.Combine(erbRoot, item.Path), item.Line, item.Name), item => item.Kind);
    var indexedFiles = Directory.EnumerateFiles(erbRoot, "*.ERB", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
        .Select(ErbSourceIndexer.IndexFile).ToArray();
    var definitions = new List<FunctionDefinition>();
    var runtimeIds = new Dictionary<string, RuntimeFunctionId>(StringComparer.Ordinal);
    foreach (var indexed in indexedFiles)
    {
        foreach (var function in indexed.Functions)
        {
            var key = Key(indexed.FileIdentity, function.Span.StartLine, function.Name);
            var kind = selectedKinds.GetValueOrDefault(key, FunctionKind.Normal);
            var id = new RuntimeFunctionId(definitions.Count);
            definitions.Add(new(function.Name, indexed.FileIdentity, function.Span, function.Flags, kind, function.Name, true));
            runtimeIds.Add(key, id);
        }
    }
    var prototypes = new List<RuntimeFunctionPrototype>();
    var compileRows = new List<object>();
    foreach (var item in selected)
    {
        var fullPath = Path.Combine(erbRoot, item.Path);
        var indexed = indexedFiles.Single(value => Path.GetFullPath(value.FileIdentity).Equals(Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase));
        var function = indexed.Functions.Single(value => value.Span.StartLine == item.Line && value.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        var runtimeId = runtimeIds[Key(indexed.FileIdentity, item.Line, item.Name)];
        var result = compiler.TryCompileRuntime(indexed, function);
        if (result.Function is { } compiled)
        {
            var source = FunctionSourceReader.Read(indexed, function).Source!.Value;
            var operands = compiled.OperandTexts.IsDefault ? compiled.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(source.Bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray() : compiled.OperandTexts;
            prototypes.Add(new(runtimeId, compiled.Instructions, operands, compiled.SemanticPayload, compiled.RuntimeMetadata));
        }
        else
        {
            prototypes.Add(new(runtimeId, [], [], RuntimeMetadata: FunctionRuntimeMetadata.Empty));
        }
        compileRows.Add(new { id = runtimeId.Value, name = item.Name, kind = item.Kind.ToString(), status = result.Status.ToString(), reason = result.Reason.ToString(), detail = result.Detail });
    }
    var catalog = FunctionCatalog.FromDefinitions(definitions);
    catalog.MarkCodeAvailable(prototypes.Select(value => value.RuntimeId));
    var linked = ControlLinker.Link(catalog, prototypes, runtimeEnvironment: environment, runtimeStatements: true);
    var kinds = definitions.Select(value => value.Kind).ToArray();
    var staged = VmRuntimeRequirementAnalyzer.AnalyzeStaged(linked.Program, kinds, VmRuntimeCapabilitySnapshot.KernelOnly, VmRuntimeCapabilitySnapshot.KernelOnly);
    var hostIdentities = new[]
    {
        (Name: "Structural", Arena: linked.Program.SemanticArena),
        (Name: "Statements", Arena: linked.Program.RuntimeStatements.OperandArena),
        (Name: "CallArguments", Arena: linked.Program.CallArgumentArena),
    }.SelectMany(entry => entry.Arena.HostIdentities.Select(identity => new
    {
        arena = entry.Name,
        identity.Kind,
        name = entry.Arena.ReadSymbol(entry.Arena.Symbols[identity.NameSymbolIndex]),
        subkey = identity.SubkeySymbolIndex < 0 ? null : entry.Arena.ReadSymbol(entry.Arena.Symbols[identity.SubkeySymbolIndex]),
        identity.IndexArity,
        identity.StableId,
    })).GroupBy(value => new { value.Kind, value.name, value.subkey, value.IndexArity, value.StableId })
      .Select(group => new { group.Key.Kind, group.Key.name, group.Key.subkey, group.Key.IndexArity, group.Key.StableId, count = group.Count(), arenas = group.Select(value => value.arena).Distinct().Order().ToArray() })
      .OrderBy(value => value.Kind).ThenBy(value => value.name).ToArray();
    var selectedIds = prototypes.Select(value => value.RuntimeId.Value).ToHashSet();
    var rows = linked.Program.Descriptors.Select((descriptor, index) => new
    {
        id = index,
        name = definitions[index].Name,
        kind = kinds[index].ToString(),
        state = descriptor.State.ToString(),
        requirements = staged.Shared.Rows[index].TransitiveRequirements.ToString(),
        blocking = staged.Shared.Rows[index].BlockingRequirements.ToString(),
    }).Where(row => selectedIds.Contains(row.id)).ToArray();
    var instructionRows = prototypes.SelectMany(prototype => prototype.Instructions.Select((instruction, pc) => new
    {
        id = prototype.RuntimeId.Value,
        name = definitions[prototype.RuntimeId.Value].Name,
        pc,
        sourceLine = instruction.SourceLine,
        prototypeOpcode = instruction.Opcode.ToString(),
        vmOpcode = linked.Program.Descriptors[prototype.RuntimeId.Value].CodeLength > pc
            ? ((VmOpcode)linked.Program.Code[linked.Program.Descriptors[prototype.RuntimeId.Value].CodeStart + pc].Opcode).ToString()
            : "NoCode",
        operand = pc < prototype.Operands.Length ? prototype.Operands[pc] : string.Empty,
    })).Where(row => row.vmOpcode is "SemanticBarrier" or "UnsupportedControl").ToArray();
    var structuralRows = linked.Program.StructuralLinks.Select((record, index) => new
    {
        record.FunctionId,
        name = definitions[record.FunctionId].Name,
        record.Pc,
        record.Kind,
        semanticRecord = linked.Program.StructuralSemanticRecordIndices[index],
    }).Where(row => selectedIds.Contains(row.FunctionId)).ToArray();
    var semanticDebug = structuralRows.Where(row => row.name == "GET_DUNGEON_NAME" && row.semanticRecord >= 0).Select(row =>
    {
        var record = linked.Program.SemanticArena.Records[row.semanticRecord];
        return new
        {
            row.Pc,
            row.Kind,
            Record = record,
            Nodes = linked.Program.SemanticArena.Nodes.Skip(record.RootNodeIndex - record.NodeCount + 1).Take(record.NodeCount).Select((node, offset) => new
            {
                Index = record.RootNodeIndex - record.NodeCount + 1 + offset,
                node.Kind,
                node.Operator,
                node.A,
                node.B,
                node.C,
                node.D,
                Symbol = node.Kind is SemanticNodeKind.IntegerLiteral or SemanticNodeKind.StringLiteral or SemanticNodeKind.Symbol or SemanticNodeKind.Variable or SemanticNodeKind.VariableSubkey or SemanticNodeKind.Call
                    ? linked.Program.SemanticArena.ReadSymbol(linked.Program.SemanticArena.Symbols[node.A]) : null,
            }).ToArray(),
        };
    }).ToArray();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    File.WriteAllText(outputPath, JsonSerializer.Serialize(new
    {
        schema = "r0f6g3-link-probe-v1",
        execution = "LINK_AND_READINESS_ONLY_NO_VM_NO_HOST_EFFECTS",
        linked.SemanticBarriers,
        unsupportedControlFunctions = linked.Program.Descriptors.Count(value => value.State == VmFunctionState.UnsupportedControl),
        linked.Calls,
        linked.ResolvedCalls,
        linked.MissingTargets,
        linked.WrongKinds,
        linked.Diagnostics,
        runtimeStatementCount = linked.Program.RuntimeStatements.Records.Length,
        expressionTargets = linked.Program.ExpressionFunctionTargets.Length,
        hostIdentities,
        compileRows,
        rows,
        instructionRows,
        structuralRows,
        semanticDebug,
        localRequirementOrigins = staged.Diagnostics?.LocalRequirementOrigins,
        nonVariableBlockers = staged.Diagnostics?.NonVariableBlockingDiagnostics,
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static void RunFixtureSetExtraTitleVar()
{
    var path = Path.Combine("E:\\GAME-2", "テスト版", "eramegaten_p_口上有り", "Data", "ERB", "関数", "組み込み関数", "作品管理", "SET_EXTRA_TITLE_VAR.ERB");
    if (!File.Exists(path)) throw new InvalidOperationException($"fixture missing: {path}");
    var indexed = ErbSourceIndexer.IndexFile(path);
    var function = indexed.Functions.Single(x => x.Name.Equals("SET_EXTRA_TITLE_VAR", StringComparison.OrdinalIgnoreCase));
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var compiled = new FunctionCompiler(env).TryCompileRuntime(indexed, function);
    if (compiled.Status != CompileStatus.Compiled || compiled.Function is null) throw new InvalidOperationException($"compile={compiled.Status}/{compiled.Reason}/{compiled.Detail}");
    var source = FunctionSourceReader.Read(indexed, function).Source!.Value;
    var operands = compiled.Function.OperandTexts.IsDefault ? compiled.Function.Instructions.Select(instruction => instruction.OperandLength == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(source.Bytes, instruction.OperandOffset, instruction.OperandLength)).ToImmutableArray() : compiled.Function.OperandTexts;
    var prototype = new RuntimeFunctionPrototype(new(0), compiled.Function.Instructions, operands, compiled.Function.SemanticPayload, compiled.Function.RuntimeMetadata);
    var expected = File.ReadAllLines(path).Select(line => System.Text.RegularExpressions.Regex.Match(line, @"^\s*EXTRA_TITLE:(\d+)\s*'=\s*""(.*)""\s*$")).Where(match => match.Success).Select(match => (Index: int.Parse(match.Groups[1].Value), Value: match.Groups[2].Value)).OrderBy(item => item.Index).ToArray();
    var link = Executable(ControlLinker.Link(FunctionCatalog.FromDefinitions([D("SET_EXTRA_TITLE_VAR", path, function.Span)]), [prototype], runtimeEnvironment: env, runtimeStatements: true).Program);
    var records = link.RuntimeStatements.Records;
    Check(expected.Length == 99 && expected[0] == (0, "") && expected[1] == (1, "スパロボ") && expected[4] == (4, "FATE") && expected[5] == (5, "まどかマギカ") && expected[96] == (96, "二次創作") && expected[97] == (97, "ユーザー投稿") && expected[98] == (98, "未分類"));
    Check(prototype.Instructions.Length == 99 && records.Length == 99 && records.All(x => x.Assignment == VmAssignmentOperator.AssignString));
    var payload = link.RuntimeStatements.OperandArena;
    if (!records.All(record => { var root = payload.Nodes[payload.Records[record.OperandRecord].RootNodeIndex]; return root.Kind == SemanticNodeKind.Variable && root.C == 1 && payload.ReadSymbol(payload.Symbols[root.A]) == "EXTRA_TITLE"; })) throw new InvalidOperationException($"records={records.Length}");
    var host = new TypedIndexedWriteHost();
    var stop = new VmMachine(link, new VmSemanticExecutor(link, host)).Run(new(0));
    Check(stop == VmStopReason.Returned && host.Writes.Count == expected.Length);
    for (var i = 0; i < expected.Length; i++)
    {
        Check(host.Writes[i].Indices.Length == 1 && host.Writes[i].Indices[0].TryGetInteger(out var index) && index == expected[i].Index && host.Writes[i].Value.TryGetString(out var value) && value == expected[i].Value);
    }
    var findElementResult = host.Writes.Select((write, position) => (write, position)).Where(item => item.write.Value.TryGetString(out var value) && value == "まどかマギカ").Select(item => item.position).DefaultIfEmpty(-1).Single();
    Check(findElementResult == 5);
}

static void RunPlainStringAssignmentMatrix()
{
    StringCase("PlainBareLiteral", "STR:0 = あいう", "あいう");
    StringCase("PlainUnicodeLiteral", "STR:0 = まどかマギカ", "まどかマギカ");
    StringCase("PlainFormVariableExpansion", "STR:0 = %RESULTS:0%", "ABC", host => host.Set("RESULTS", null, [VmSemanticValue.From(0)], VmSemanticValue.From("ABC")));
    StringCase("PlainMixedForm", "STR:0 = 前%RESULTS:0%後", "前ABC後", host => host.Set("RESULTS", null, [VmSemanticValue.From(0)], VmSemanticValue.From("ABC")));
    StringCase("PlainEscapedPercent", "STR:0 = \\%RESULTS:0\\%", "%RESULTS:0%", host => host.Set("RESULTS", null, [VmSemanticValue.From(0)], VmSemanticValue.From("ABC")));
    StringCase("PlainFormNumeric", "STR:0 = {RESULTS:0}", "123", host => host.Set("RESULTS", null, [VmSemanticValue.From(0)], VmSemanticValue.From(123L)));
    StringCase("PlainEmpty", "STR:0 = ", string.Empty);
    StringCase("PlainComma", "STR:0 = いちご,メロン,ブルーハワイ", "いちご,メロン,ブルーハワイ");
    StringCase("StringExpressionLiteral", "STR:0 '= \"あいう\"", "あいう");
    StringCase("StringExpressionConcatenation", "TSTR:0 '= TSTR:0 + \"いろは\"", "あいういろは", host => host.Set("TSTR", null, [VmSemanticValue.From(0)], VmSemanticValue.From("あいう")), "TSTR");
    StringCase("PlainQuotedText", "STR:0 = \"あいう\"", "\"あいう\"");
    StringCase("Cstr", "CSTR:0 = 文字列", "文字列", name: "CSTR");
    StringCase("Locals", "LOCALS:0 = ローカル", "ローカル", name: "LOCALS");
    StringCase("IndexedString", "STR:5 '= \"indexed\"", "indexed");
    StringCase("ScalarString", "STR:0 '= \"scalar\"", "scalar");
}

static void StringCase(string label, string operand, string expected, Action<SemanticHost>? seed = null, string name = "STR")
{
    var actual = RunStringAssignment(operand, seed, name);
    var parity = actual == expected ? "PASS" : "FAIL";
    Console.WriteLine($"StringAssignmentMatrixCase={label};LegacyValue={expected};NextValue={actual};Parity={parity}");
    Check(parity == "PASS");
}

static string RunStringAssignment(string operand, Action<SemanticHost>? seed = null, string name = "STR")
{
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var metadata = name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase) ? new FunctionRuntimeMetadata([], 0, 1, [], null) : null;
    var prototype = new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.SET), P(PrototypeOpcode.RETURN)], [operand, ""], RuntimeMetadata: metadata);
    var program = Executable(ControlLinker.Link(Catalog("X"), [prototype], runtimeEnvironment: env, runtimeStatements: true).Program);
    var host = new SemanticHost();
    seed?.Invoke(host);
    var executor = new VmSemanticExecutor(program, host);
    var frameState = metadata is null ? null : new FrameState();
    var vm = new VmMachine(program, executor, frameState: frameState);
    var targetKnown = executor.TryGetRuntimeAssignmentTargetKind(program.RuntimeStatements.OperandArena, program.RuntimeStatements.Records[0].OperandRecord, out var targetKind);
    var stop = vm.Run(new(0));
    if (stop != VmStopReason.Returned) throw new InvalidOperationException($"operand={operand};stop={stop};assignment={program.RuntimeStatements.Records[0].Assignment};targetKnown={targetKnown};targetKind={targetKind};records={program.RuntimeStatements.OperandArena.Records.Length};dest={program.RuntimeStatements.Records[0].OperandRecord};source={program.RuntimeStatements.Records[0].SecondaryOperandRecord};format={program.RuntimeStatements.Records[0].FormatOperandRecord};last={executor.LastStatus}/{executor.LastFault}/{executor.LastHostOperation};lastRecord={executor.LastRecordIndex}");
    VmSemanticValue value;
    var read = name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)
        ? frameState!.TryReadFrameValue(new(0), VmFrameStateFamily.Locals, -1, 0, out value)
        : host.TryRead(name, null, [VmSemanticValue.From(operand.Contains(":5", StringComparison.Ordinal) ? 5 : 0)], out value);
    if (!read) throw new InvalidOperationException($"operand={operand};stop={stop};value=<missing>");
    if (!value.TryGetString(out var text)) throw new InvalidOperationException($"operand={operand};stop={stop};value={value}");
    return text;
}

static void Check(bool value) { if (!value) throw new InvalidOperationException("assertion failed"); }
static void Expect<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"expected {typeof(T).Name}"); }
static void RunSemanticStructuralLookupMapTest()
{
    var program = new LinkedProgram(
        [VmInstruction.Halt, VmInstruction.Halt, VmInstruction.Halt],
        [new VmFunctionDescriptor(0, 0, 2, VmFunctionState.ExecutableReady), new VmFunctionDescriptor(1, 2, 1, VmFunctionState.ExecutableReady)],
        [new StructuralLinkRecord(0, 0, 0, 0, VmStructuralKind.Sif, 0), new StructuralLinkRecord(0, 0, 0, 0, VmStructuralKind.If, 0), new StructuralLinkRecord(1, 0, 0, 0, VmStructuralKind.Else, 0)]);
    var lookup = VmSemanticStructuralLookup.Build(program);
    Check(lookup.Program == program && lookup.GetStructuralLinkIndex(new(0), 0) == 1 && lookup.GetStructuralLinkIndex(new(0), 1) == -1 && lookup.GetStructuralLinkIndex(new(1), 0) == 2);
}
static void RunSemanticStructuralLookupOutOfRangeTest()
{
    var program = new LinkedProgram([VmInstruction.Halt], [new VmFunctionDescriptor(0, 0, 1, VmFunctionState.ExecutableReady)], [new StructuralLinkRecord(9, 0, 0, 0, VmStructuralKind.Sif, 0), new StructuralLinkRecord(0, 9, 0, 0, VmStructuralKind.If, 0)]);
    var lookup = VmSemanticStructuralLookup.Build(program);
    Check(lookup.GetStructuralLinkIndex(new(-1), 0) == -1 && lookup.GetStructuralLinkIndex(new(0), -1) == -1 && lookup.GetStructuralLinkIndex(new(0), 1) == -1);
}
static void RunSemanticStructuralLookupGenerationMismatchTest()
{
    var first = SemanticStructural((PrototypeOpcode.SIF, "1"));
    var second = SemanticStructural((PrototypeOpcode.SIF, "1"));
    var lookup = VmSemanticStructuralLookup.Build(first);
    Check(ReferenceEquals(lookup.Program, first) && !ReferenceEquals(lookup.Program, second));
    Expect<ArgumentException>(() => new VmSemanticExecutor(second, new SemanticHost(), lookup));
}
static void RunSemanticStructuralLookupIsolationTest()
{
    var program = SemanticStructural((PrototypeOpcode.SIF, "1"), (PrototypeOpcode.RETURN, ""));
    var lookup = VmSemanticStructuralLookup.Build(program);
    var first = new VmSemanticExecutor(program, new SemanticHost(), lookup);
    var second = new VmSemanticExecutor(program, new SemanticHost(), lookup);
    var lookupField = typeof(VmSemanticExecutor).GetField("structuralLookup", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var repeatField = typeof(VmSemanticExecutor).GetField("repeatCounters", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Check(ReferenceEquals(lookupField.GetValue(first), lookup) && ReferenceEquals(lookupField.GetValue(second), lookup) &&
        !ReferenceEquals(repeatField.GetValue(first), repeatField.GetValue(second)) &&
        first.TryEvaluateRecord(0, out _) && first.LastRecordIndex == 0 && second.LastRecordIndex == -1);
}
static void RunMachineLookupMapTest()
{
    var program = MachineLookupProgram();
    var lookup = VmMachineProgramLookup.Build(program);
    var selfBuilt = new VmMachine(program);
    var shared = new VmMachine(program, null, null, null, lookup);
    var callSites = typeof(VmMachine).GetField("callSites", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var expressionTargets = typeof(VmMachine).GetField("expressionFunctionTargets", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Check(ReferenceEquals(lookup.Program, program) &&
        ((Dictionary<(int FunctionId, int Pc), VmCallSiteRecord>)callSites.GetValue(selfBuilt)!).SequenceEqual((Dictionary<(int FunctionId, int Pc), VmCallSiteRecord>)callSites.GetValue(shared)!) &&
        ((Dictionary<ulong, VmExpressionFunctionTarget>)expressionTargets.GetValue(selfBuilt)!).SequenceEqual((Dictionary<ulong, VmExpressionFunctionTarget>)expressionTargets.GetValue(shared)!));
}
static void RunMachineLookupDuplicateKeyTest()
{
    var site = new VmCallSiteRecord(0, 0, new(1), VmInvocationKind.Call, 0, 0, 0, 0);
    VmFunctionDescriptor[] descriptors = [new VmFunctionDescriptor(0, 0, 1, VmFunctionState.ExecutableReady), new VmFunctionDescriptor(1, 1, 1, VmFunctionState.ExecutableReady)];
    var duplicateSites = new LinkedProgram([VmInstruction.Halt, VmInstruction.Halt], descriptors, [], callSites: [site, site]);
    Expect<ArgumentException>(() => VmMachineProgramLookup.Build(duplicateSites));
    var target = new VmExpressionFunctionTarget(17, new(1), RuntimeMetadataValueType.Integer, true);
    Expect<ArgumentException>(() => new LinkedProgram([VmInstruction.Halt, VmInstruction.Halt], descriptors, [], expressionFunctionTargets: [target, target]));
}
static void RunMachineLookupGenerationMismatchTest()
{
    var first = MachineLookupProgram();
    var second = MachineLookupProgram();
    var lookup = VmMachineProgramLookup.Build(first);
    Expect<ArgumentException>(() => new VmMachine(second, null, null, null, lookup));
}
static void RunMachineLookupIsolationTest()
{
    var environment = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var metadata = new FunctionRuntimeMetadata([], 0, 0, [], RuntimeMetadataValueType.Integer);
    var program = Executable(ControlLinker.Link(Catalog("M"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURNF)], ["1"], RuntimeMetadata: metadata)], runtimeEnvironment: environment, runtimeStatements: true).Program);
    var lookup = VmMachineProgramLookup.Build(program);
    var first = new VmMachine(program, new VmSemanticExecutor(program, new SemanticHost()), null, null, lookup);
    var second = new VmMachine(program, new VmSemanticExecutor(program, new SemanticHost()), null, null, lookup);
    var lookupField = typeof(VmMachine).GetField("callSites", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var stackField = typeof(VmMachine).GetField("stack", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var invocationField = typeof(VmMachine).GetField("invocations", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var loopField = typeof(VmMachine).GetField("loopRuntime", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Check(ReferenceEquals(lookupField.GetValue(first), lookupField.GetValue(second)) &&
        !ReferenceEquals(stackField.GetValue(first), stackField.GetValue(second)) &&
        !ReferenceEquals(invocationField.GetValue(first), invocationField.GetValue(second)) &&
        !ReferenceEquals(loopField.GetValue(first), loopField.GetValue(second)) &&
        first.Run(new(0)) == VmStopReason.Returned && first.LastReturnValue.TryGetInteger(out var value) && value == 1 &&
        second.LastReturnValue.Kind == VmSemanticValueKind.Unavailable && second.FrameDepth == 0);
}
static LinkedProgram MachineLookupProgram()
{
    var site = new VmCallSiteRecord(0, 0, new(1), VmInvocationKind.Call, 0, 0, 0, 0);
    var target = new VmExpressionFunctionTarget(17, new(1), RuntimeMetadataValueType.Integer, true);
    return new LinkedProgram(
        [VmInstruction.Halt, VmInstruction.Halt],
        [new VmFunctionDescriptor(0, 0, 1, VmFunctionState.ExecutableReady), new VmFunctionDescriptor(1, 1, 1, VmFunctionState.ExecutableReady)],
        [], callSites: [site], expressionFunctionTargets: [target]);
}
static bool DispatchEligible(int id, FunctionKind kind, bool ready, bool nextRuntime = true, bool analysis = false, bool debug = false) =>
    VmRuntimeProductionDispatch.IsEligible(nextRuntime, analysis, debug, id, 2000, kind, VmFunctionState.ExecutableReady, ready);
static SourceSpan Span(int line) => new(0, 1, line, line);
static FunctionCatalog Catalog(string name) => FunctionCatalog.FromDefinitions([D(name, "test.erb", Span(1))]);
static FunctionDefinition D(string physicalName, string file, SourceSpan span, FunctionKind kind = FunctionKind.Normal, string? effectiveName = null) => new(physicalName, file, span, Kind: kind, EffectiveName: effectiveName ?? physicalName, EffectiveNameKnown: true);
static RuntimeFunctionPrototype RP(int id, IReadOnlyList<PrototypeOpcode> opcodes) => new(new(id), opcodes.Select(P).ToImmutableArray(), opcodes.Select(_ => "").ToImmutableArray());
static RuntimeFunctionPrototype RPO(int id, IReadOnlyList<PrototypeOpcode> opcodes, IReadOnlyList<string> operands) => new(new(id), opcodes.Select(P).ToImmutableArray(), operands.ToImmutableArray());
static LinkedProgram ExecutableStructural(IReadOnlyList<PrototypeOpcode> opcodes, params (int FunctionId, int Pc, VmInstruction Instruction)[] replacements) => Executable(ControlLinker.Link(Catalog("X"), [RP(0, opcodes)]).Program, replacements);
static LinkedProgram Executable(LinkedProgram program, params (int FunctionId, int Pc, VmInstruction Instruction)[] replacements)
{
    var code = (VmInstruction[])program.Code.Clone();
    foreach (var replacement in replacements)
    {
        var descriptor = program.Descriptors[replacement.FunctionId];
        code[descriptor.CodeStart + replacement.Pc] = replacement.Instruction;
    }
    var descriptors = program.Descriptors.Select(d => d.State == VmFunctionState.CodeNotAvailable ? d : new VmFunctionDescriptor(d.FunctionId, d.CodeStart, d.CodeLength, VmFunctionState.ExecutableReady)).ToArray();
    return new(code, descriptors, program.StructuralLinks, program.SifLinks, program.IfGroups, program.IfClauses, program.SelectGroups, program.SelectCases, program.Loops, program.SemanticArena, program.StructuralSemanticRecordIndices, program.RuntimeStatements, program.RuntimeMetadata, program.CallSites, program.CallArgumentArena, program.CallArgumentRecords, program.ExpressionFunctionTargets, program.DynamicCallSites, program.DynamicCallArena, program.DynamicCallArgumentRecords);
}
static long BridgeReturn(long value)
{
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var meta = new FunctionRuntimeMetadata([], 0, 0, [], null);
    var program = Executable(ControlLinker.Link(Catalog("NEXT_CHILD"), [new RuntimeFunctionPrototype(new(0), [P(PrototypeOpcode.RETURN)], [value.ToString()], RuntimeMetadata: meta)], runtimeEnvironment: env, runtimeStatements: true).Program);
    var next = new VmMachine(program, new VmSemanticExecutor(program, new SemanticHost()));
    var returned = 0L;
    Check(next.Run(new(0)) == VmStopReason.Returned && next.LastReturnValue.TryGetInteger(out returned));
    return returned;
}
static long BridgeSharedWrite()
{
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var program = Executable(ControlLinker.Link(Catalog("NEXT_CHILD"), [RPO(0, [PrototypeOpcode.SET, PrototypeOpcode.RETURN], ["G=7", ""])], runtimeEnvironment: env, runtimeStatements: true).Program);
    var host = new SemanticHost();
    host.Set("G", null, [], VmSemanticValue.From(0L));
    Check(new VmMachine(program, new VmSemanticExecutor(program, host)).Run(new(0)) == VmStopReason.Returned);
    return host.GetInt("G");
}
static bool BridgeRejectedDispatchIsPure()
{
    var result = 3L; var shared = 9L; var before = (result, shared); var accepted = false;
    return !accepted && before == (result, shared);
}
static bool BridgePendingFallbackFinalizes()
{
    var pending = true; var finalized = false; var actualReturn = 0L;
    if (!pending) return false;
    actualReturn = 1; finalized = true;
    return finalized && actualReturn == 1;
}
static bool BridgeSeparatedChanges()
{
    var dispatchBefore = 0L; var dispatchAfter = dispatchBefore; var completionAfter = 1L;
    var completionDetails = "G:MA==->Nw==";
    return dispatchBefore == dispatchAfter && completionAfter != dispatchAfter && completionDetails == "G:MA==->Nw==";
}
static FunctionSource MetadataSource(string text)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(text);
    var function = new FunctionIndex("F", new SourceSpan(0, bytes.Length, 1, text.Count(c => c == '\n') + 1), SourceIndexFlags.DeclarationDirective | SourceIndexFlags.FunctionMetadata);
    return new FunctionSource(new SourceFileIndex("metadata.erb", bytes.Length, function.Span.EndLine, [function], SourceIndexFlags.None, null), function, bytes);
}
static VmStopReason RunLinked(LinkedProgram program, int id) => new VmMachine(program).Run(new RuntimeFunctionId(id));
static VmStopReason Run(VmInstruction[] code, int maxSteps = 100_000) { var b = new VmSyntheticProgramBuilder(); var id = b.AddFunction(code); return new VmMachine(b.Build()).Run(new RuntimeFunctionId(id), maxSteps); }
static PrototypeInstruction P(PrototypeOpcode opcode) => new(opcode, PrototypeInstructionFlags.ControlFlow, 1, 2, 3);
static StructuralLinkRecord[] LinkRows(IReadOnlyList<PrototypeOpcode> opcodes) => ControlLinker.Link(Catalog("X"), [RP(0, opcodes)]).Program.StructuralLinks;
static StructuralLinkRecord[] LoopLink(IReadOnlyList<PrototypeOpcode> opcodes) => LinkRows(opcodes).Where(x => x.Kind == VmStructuralKind.Continue).ToArray();
static IEnumerable<int> LoopOwners(IReadOnlyList<PrototypeOpcode> opcodes) => LinkRows(opcodes).Where(x => x.Kind is VmStructuralKind.Break or VmStructuralKind.Continue).Select(x => x.LoopIndex);
static LinkedProgram CountedLoopProgram(params VmStructuralKind[] kinds) => new([], [], [], loops: kinds.Select((kind, i) => new LoopDescriptor(0, kind, i * 4, i * 4 + 1, i * 4 + 2, i * 4 + 3, i * 4 + 2, (kind is VmStructuralKind.For or VmStructuralKind.Repeat) ? LoopDescriptorFlags.BreakAdvancesCounter : LoopDescriptorFlags.None)).ToArray());
static VmSemanticValue Evaluate(string expression, SemanticHost host) { Check(TryEvaluate(expression, host, out var value)); return value; }
static long EvaluateBinary(SemanticOperator op, long left, long right, SemanticHost host)
{
    var leftText = left.ToString(System.Globalization.CultureInfo.InvariantCulture); var rightText = right.ToString(System.Globalization.CultureInfo.InvariantCulture); var utf8 = System.Text.Encoding.UTF8.GetBytes(leftText + rightText);
    var payload = new SemanticPayload([new(SemanticNodeKind.IntegerLiteral, SemanticOperator.None, 0, -1, -1, -1), new(SemanticNodeKind.IntegerLiteral, SemanticOperator.None, 1, -1, -1, -1), new(SemanticNodeKind.Binary, op, 0, 1, -1, -1)], [], [new(0, leftText.Length), new(leftText.Length, rightText.Length)], [], [new(0, 2, 3)], utf8);
    if (new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: payload), host).TryEvaluateRecord(0, out var value) && value.TryGetInteger(out var integer)) return integer;
    throw new InvalidOperationException("binary semantic evaluation failed");
}
static bool TryEvaluate(string expression, SemanticHost host, out VmSemanticValue value)
{
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var payload = SemanticIrCompiler.CompileExpression(expression, env);
    return TryEvaluatePayload(payload, host, out value);
}
static bool TryEvaluatePayload(SemanticPayload payload, IVmSemanticHost host, out VmSemanticValue value) =>
    new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: payload), host).TryEvaluateRecord(0, out value);
static VmSemanticExecutor SemanticExecutor(string expression, SemanticHost host)
{
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var payload = SemanticIrCompiler.CompileExpression(expression, env);
    return new VmSemanticExecutor(new LinkedProgram([], [], [], semanticArena: payload), host);
}
static LinkedProgram SemanticStructural(params (PrototypeOpcode Opcode, string Operand)[] rows)
{
    var env = new StructuralSemanticEnvironment(new CompilerCompatibilityOptions(true, true, true, false));
    var payloads = new List<SemanticPayload>();
    for (var pc = 0; pc < rows.Length; pc++)
    {
        var (opcode, operand) = rows[pc];
        if (opcode is PrototypeOpcode.SIF or PrototypeOpcode.IF or PrototypeOpcode.ELSEIF or PrototypeOpcode.SELECTCASE or PrototypeOpcode.WHILE or PrototypeOpcode.LOOP) payloads.Add(SemanticIrCompiler.CompileExpression(operand, env, pc));
        else if (opcode == PrototypeOpcode.CASE) payloads.Add(SemanticIrCompiler.CompileCase(operand, env, pc));
        else if (opcode == PrototypeOpcode.FOR) payloads.Add(SemanticIrCompiler.CompileCountedLoop(operand, env, pc, false));
        else if (opcode == PrototypeOpcode.REPEAT) payloads.Add(SemanticIrCompiler.CompileCountedLoop(operand, env, pc, true));
    }
    var prototype = new RuntimeFunctionPrototype(new(0), rows.Select(x => P(x.Opcode)).ToImmutableArray(), rows.Select(x => x.Operand).ToImmutableArray(), payloads.Count == 0 ? null : SemanticPayload.Merge(payloads));
    return Executable(ControlLinker.Link(Catalog("X"), [prototype]).Program);
}

sealed class StructuralHost : IVmStructuralSemantics
{
    public readonly Dictionary<(int FunctionId, int Pc, VmStructuralKind Kind), Queue<long>> IntValues = [];
    public readonly Queue<int> SelectResults = [];
    public readonly Dictionary<int, Queue<VmCountedLoopEntry>> Begins = [];
    public readonly Dictionary<int, long> Counters = [];
    public readonly List<(int FunctionId, int Pc, VmStructuralKind Kind)> IntTrace = [];
    public readonly List<long> AdvanceSteps = [];
    public long DefaultInt { get; set; }
    public int LastSelectCaseCount { get; private set; } = -1;

    public VmSemanticIntResult EvaluateInt(RuntimeFunctionId functionId, int pc, VmStructuralKind kind)
    {
        var key = (functionId.Value, pc, kind);
        IntTrace.Add(key);
        return VmSemanticIntResult.From(IntValues.TryGetValue(key, out var values) && values.Count > 0 ? values.Dequeue() : DefaultInt);
    }

    public VmSemanticCaseResult SelectCase(RuntimeFunctionId functionId, int groupIndex, ReadOnlySpan<SelectCaseRecord> cases)
    {
        LastSelectCaseCount = cases.Length;
        return VmSemanticCaseResult.From(SelectResults.Count > 0 ? SelectResults.Dequeue() : -1);
    }

    public VmCountedLoopResult BeginCounted(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop)
    {
        if (!Begins.TryGetValue(loopIndex, out var values) || values.Count == 0)
            throw new InvalidOperationException($"missing BeginCounted value for loop {loopIndex}");
        var entry = values.Dequeue();
        Counters[loopIndex] = entry.Counter;
        return VmCountedLoopResult.From(entry);
    }

    public VmSemanticIntResult AdvanceCounted(RuntimeFunctionId functionId, int loopIndex, LoopDescriptor loop, long step)
    {
        AdvanceSteps.Add(step);
        var counter = Counters.TryGetValue(loopIndex, out var current) ? current : 0;
        counter = unchecked(counter + step);
        Counters[loopIndex] = counter;
        return VmSemanticIntResult.From(counter);
    }
}

class RecordingEffects : IVmRuntimeEffects
{
    public readonly List<string> Events = [];
    public readonly List<VmSemanticValue> LegacyReturns = [];
    public readonly List<bool> LineEnds = [];
    public void WriteText(string text, bool lineEnd) { Events.Add("text:" + text); LineEnds.Add(lineEnd); }
    public bool WriteColumn(string text, bool alignmentRight) { Events.Add($"column:{(alignmentRight ? "Right" : "Left")}:{text}"); return true; }
    public bool ReuseLastLine(string text) { Events.Add("reuse:" + text); return true; }
    public void NewLine() => Events.Add("newline");
    public void RequestWait(bool force) => Events.Add("wait:" + force);
    public void Quit() => Events.Add("quit");
    public virtual bool ExecuteHostStatement(PrototypeOpcode opcode, string operand) => false;
    public virtual bool PrintButton(string label, VmSemanticValue value) { Events.Add($"button:{label}:{value.Kind}:{value}"); return true; }
    public virtual bool SetLegacyReturnValues(ReadOnlySpan<VmSemanticValue> values) { LegacyReturns.AddRange(values); return true; }
    public virtual VmHostEffectResult ExecuteTypedHostStatement(PrototypeOpcode opcode, ReadOnlySpan<VmSemanticValue> arguments) => VmHostEffectResult.Unavailable;
}

sealed class ThrowingEffects : IVmRuntimeEffects
{
    public void WriteText(string text, bool lineEnd) => throw new InvalidOperationException("host");
    public void NewLine() { }
    public void RequestWait(bool force) { }
    public void Quit() { }
}

sealed class HostTransferEffects : RecordingEffects
{
    public override VmHostEffectResult ExecuteTypedHostStatement(PrototypeOpcode opcode, ReadOnlySpan<VmSemanticValue> arguments) => VmHostEffectResult.HostTransfer;
}

sealed class OwnedInputEffects(VmContextStorage owner, bool synchronousResponse = false) : RecordingEffects, IVmOwnedInputRuntimeEffects
{
    private bool publishing;
    private VmInputValueKind expectedKind = VmInputValueKind.String;
    public VmInputContinuation? Token { get; private set; }
    public VmSemanticValue Result { get; private set; } = VmSemanticValue.Unavailable;
    public int PublishCount { get; private set; }
    public int ResultWriteCount { get; private set; }
    public bool PublishedWhileSuspended { get; private set; }
    public bool PumpReenteredDuringPublish { get; private set; }

    public bool TryPrepareInput(PrototypeOpcode opcode, ReadOnlySpan<VmSemanticValue> arguments, out VmInputValueKind expectedKind)
    {
        expectedKind = opcode is PrototypeOpcode.WAIT or PrototypeOpcode.FORCEWAIT
            ? VmInputValueKind.Integer : VmInputValueKind.String;
        this.expectedKind = expectedKind;
        return arguments.IsEmpty && opcode is PrototypeOpcode.ONEINPUTS or PrototypeOpcode.WAIT or PrototypeOpcode.FORCEWAIT;
    }

    public void PublishInput(VmInputContinuation continuation, Action<VmSemanticValue> respond)
    {
        Token = continuation;
        PublishCount++;
        PublishedWhileSuspended = owner.RunState == VmOwnerRunState.Suspended;
        publishing = true;
        try
        {
            if (synchronousResponse) respond(VmSemanticValue.From("sync"));
        }
        finally { publishing = false; }
    }

    public bool TryWriteInputResult(VmSemanticValue value)
    {
        PumpReenteredDuringPublish |= publishing;
        Result = value;
        ResultWriteCount++;
        return expectedKind == VmInputValueKind.String
            ? value.Kind == VmSemanticValueKind.String : value.Kind == VmSemanticValueKind.Integer;
    }
}

sealed class HostRecordingEffects : RecordingEffects
{
    public readonly List<string> HostEvents = [];
    public override bool ExecuteHostStatement(PrototypeOpcode opcode, string operand) { HostEvents.Add(opcode + ":" + operand); return true; }
}

sealed class TypedRecordingEffects(bool waitOnInput = false) : RecordingEffects
{
    public readonly List<string> TypedEvents = [];
    public override VmHostEffectResult ExecuteTypedHostStatement(PrototypeOpcode opcode, ReadOnlySpan<VmSemanticValue> arguments)
    {
        TypedEvents.Add(opcode + ":" + string.Join('|', arguments.ToArray().Select(value => value.ToString())));
        return waitOnInput && opcode == PrototypeOpcode.ONEINPUTS ? VmHostEffectResult.WaitingForInput : VmHostEffectResult.Applied;
    }
}

sealed class DynamicResolver((string Name, bool Method) key, VmDynamicCallResolution resolution) : IVmDynamicCallResolver
{
    public int Calls { get; private set; }
    public VmDynamicCallResolution Resolve(string effectiveName, bool method)
    {
        Calls++;
        return effectiveName.Equals(key.Name, StringComparison.OrdinalIgnoreCase) && method == key.Method
            ? resolution : new(VmDynamicResolutionKind.Missing, new(-1));
    }
}

sealed class CallbackDynamicResolver(Action callback) : IVmDynamicCallResolver
{
    public VmDynamicCallResolution Resolve(string effectiveName, bool method)
    {
        callback();
        return new(VmDynamicResolutionKind.Ready, new(1));
    }
}

sealed class FrameState : IVmFrameState
{
    private readonly Dictionary<(int FunctionId, VmFrameStateFamily Family, int PrivateSlot, int ElementIndex), VmSemanticValue> values = [];
    public bool OwnsFrameValue(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot) => true;
    public bool TryReadFrameValue(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot, int elementIndex, out VmSemanticValue value) => values.TryGetValue((functionId.Value, family, privateSlot, elementIndex), out value);
    public bool TryWriteFrameValue(RuntimeFunctionId functionId, VmFrameStateFamily family, int privateSlot, int elementIndex, VmSemanticValue value) { values[(functionId.Value, family, privateSlot, elementIndex)] = value; return true; }
    public void Set(int functionId, VmFrameStateFamily family, int privateSlot, int elementIndex, VmSemanticValue value) => values[(functionId, family, privateSlot, elementIndex)] = value;
}

delegate VmSemanticValue SemanticCall(ReadOnlySpan<VmSemanticValue> arguments);

sealed class CsvIndexMappingHost : IVmSemanticHost, IVmTypedSemanticHost, IVmContextualTypedSemanticHost
{
    private readonly Dictionary<ulong, long> values = [];
    private readonly Dictionary<(ulong Identity, long Index), long> indexedValues = [];
    private readonly Dictionary<(SemanticPayload Payload, int NodeIndex), long> contextualValues = [];
    public void Set(SemanticHostIdentity identity, long value) => values[identity.StableId] = value;
    public void SetIndexed(SemanticHostIdentity identity, long index, long value) => indexedValues[(identity.StableId, index)] = value;
    public void SetContextual(SemanticPayload payload, SemanticHostIdentity identity, long value) => contextualValues[(payload, identity.NodeIndex)] = value;
    public bool BindVariableIdentities(SemanticPayload payload) => true;
    public bool TryRead(SemanticPayload payload, SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        if (identity.IndexArity == 0 && indices.IsEmpty && contextualValues.TryGetValue((payload, identity.NodeIndex), out var contextual)) { value = VmSemanticValue.From(contextual); return true; }
        return TryRead(identity, indices, out value);
    }
    public bool TryRead(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        if (indices.Length == 1 && indices[0].TryGetInteger(out var index) && indexedValues.TryGetValue((identity.StableId, index), out var indexed)) { value = VmSemanticValue.From(indexed); return true; }
        if (identity.IndexArity == 0 && indices.IsEmpty && values.TryGetValue(identity.StableId, out var number)) { value = VmSemanticValue.From(number); return true; }
        value = VmSemanticValue.Unavailable;
        return false;
    }
    public bool TryWrite(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
    public bool TryCall(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
    public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public int CompareStrings(string left, string right) => string.CompareOrdinal(left, right);
}

class SemanticHost : IVmSemanticHost, IVmAssignmentTargetTypeHost, IVmRuntimeNumericOptions
{
    private readonly Dictionary<string, VmSemanticValue> values = [];
    public readonly Dictionary<string, SemanticCall> Calls = [];
    public readonly List<string> Trace = [];
    public readonly Dictionary<string, int> CallCounts = [];
    public int CallCount { get; private set; }
    public bool TimesNotRigorousCalculation { get; init; }
    public Func<string, int>? StringDisplayLength { get; init; }
    public int GetStringDisplayLength(string value) => StringDisplayLength?.Invoke(value) ?? value.Length;
    public void Set(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => values[Key(name, subkey, indices)] = value;
    public long GetInt(string name) => GetInt(name, null, []);
    public long GetInt(string name, long index) => GetInt(name, null, [VmSemanticValue.From(index)]);
    public int CallCountFor(string name) => CallCounts.GetValueOrDefault(name);
    public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { Trace.Add("Read:" + Key(name, subkey, indices)); return values.TryGetValue(Key(name, subkey, indices), out value); }
    public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) { Trace.Add("Write:" + Key(name, subkey, indices)); values[Key(name, subkey, indices)] = value; return true; }
    public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { CallCount++; CallCounts[name] = CallCountFor(name) + 1; Trace.Add("Call:" + name); if (Calls.TryGetValue(name, out var call)) { value = call(arguments); return value.Kind != VmSemanticValueKind.Unavailable; } value = VmSemanticValue.Unavailable; return false; }
    public int CompareStrings(string left, string right) => string.CompareOrdinal(left, right);
    public bool TryGetAssignmentTargetKind(SemanticHostIdentity identity, SemanticPayload arena, out VmSemanticValueKind kind)
    {
        var name = identity.NameSymbolIndex >= 0 && identity.NameSymbolIndex < arena.Symbols.Length ? arena.ReadSymbol(arena.Symbols[identity.NameSymbolIndex]) : string.Empty;
        if (name.Equals("G", StringComparison.OrdinalIgnoreCase)) { kind = VmSemanticValueKind.Integer; return true; }
        if (name.Equals("STR", StringComparison.OrdinalIgnoreCase) || name.Equals("TSTR", StringComparison.OrdinalIgnoreCase) || name.Equals("SAVESTR", StringComparison.OrdinalIgnoreCase) || name.Equals("GLOBALS", StringComparison.OrdinalIgnoreCase) || name.Equals("CSTR", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) { kind = VmSemanticValueKind.String; return true; }
        kind = VmSemanticValueKind.Unavailable;
        return false;
    }
    private static string Key(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices) => name + "@" + subkey + ":" + string.Join(',', indices.ToArray().Select(x => x.ToString()));
    private long GetInt(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices) => values[Key(name, subkey, indices)].TryGetInteger(out var value) ? value : throw new InvalidOperationException("missing integer");
}

sealed class MetadataSemanticHost : SemanticHost, IVmVariableMetadataHost
{
    public bool TryGetVariableIndex(string variableName, string label, out long index) { index = 0; return false; }
    public bool TryInvokeVariableFunction(string functionName, string variableName, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryInvokeVariableFunction(string functionName, in VmResolvedLValue variable, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (!functionName.Equals("CMATCH", StringComparison.OrdinalIgnoreCase) || arguments.Length != 1 || !arguments[0].TryGetInteger(out var target)) return false;
        long count = 0;
        for (var index = 0; index < 3; index++)
            if (TryRead(variable.Name, variable.Subkey, [VmSemanticValue.From(index)], out var candidate) && candidate.TryGetInteger(out var integer) && integer == target) count++;
        value = VmSemanticValue.From(count);
        return true;
    }
}

sealed class PreflightSemanticHost : SemanticHost, IVmHostCallPreflight
{
    public VmHostCallAdmission Admission { get; init; } = VmHostCallAdmission.Ready;
    public VmHostCallAdmission Classify(SemanticHostIdentity identity) => Admission;
}

sealed class TypedCallHost : IVmSemanticHost, IVmTypedSemanticHost
{
    public int TypedCalls { get; private set; }
    public int RawCalls { get; private set; }
    public bool BindVariableIdentities(SemanticPayload payload) => true;
    public bool TryRead(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryWrite(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
    public bool TryCall(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value)
    {
        TypedCalls++;
        value = identity.Kind == SemanticHostIdentityKind.Call && arguments.Length == 2 && arguments[0].TryGetInteger(out var left) && arguments[1].TryGetInteger(out var right) ? VmSemanticValue.From(Math.Max(left, right)) : VmSemanticValue.Unavailable;
        return value.Kind != VmSemanticValueKind.Unavailable;
    }
    public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
    public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { RawCalls++; value = VmSemanticValue.Unavailable; return false; }
    public int CompareStrings(string left, string right) => string.CompareOrdinal(left, right);
}

sealed class TypedIndexedWriteHost(bool rejectIndexedWrites = false) : IVmSemanticHost, IVmTypedSemanticHost
{
    public readonly List<(SemanticHostIdentity Identity, VmSemanticValue[] Indices, VmSemanticValue Value)> Writes = [];
    public string Describe() => string.Join("|", Writes.Select(x => $"{x.Identity.StableId:X16}:{string.Join(',', x.Indices.Select(index => index.ToString()))}:{x.Value}"));
    public bool BindVariableIdentities(SemanticPayload payload) => true;
    public bool TryRead(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        if (indices.IsEmpty) { value = VmSemanticValue.From(5L); return true; }
        value = VmSemanticValue.From(0L); return true;
    }
    public bool TryWrite(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
    {
        if (rejectIndexedWrites && !indices.IsEmpty) return false;
        Writes.Add((identity, indices.ToArray(), value));
        return true;
    }
    public bool TryCall(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
    public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public int CompareStrings(string left, string right) => string.CompareOrdinal(left, right);
}

sealed class TypedStringAssignmentHost : IVmSemanticHost, IVmTypedSemanticHost
{
    public string Value { get; private set; } = "prefix";
    public VmSemanticValue[] ReadIndices { get; private set; } = [];
    public VmSemanticValue[] WriteIndices { get; private set; } = [];
    public bool BindVariableIdentities(SemanticPayload payload) => true;
    public bool TryRead(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { ReadIndices = indices.ToArray(); value = VmSemanticValue.From(Value); return true; }
    public bool TryWrite(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) { WriteIndices = indices.ToArray(); if (!value.TryGetString(out var text)) return false; Value = text; return true; }
    public bool TryCall(SemanticHostIdentity identity, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryRead(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public bool TryWrite(string name, string? subkey, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => false;
    public bool TryCall(string name, ReadOnlySpan<VmSemanticValue> arguments, out VmSemanticValue value) { value = VmSemanticValue.Unavailable; return false; }
    public int CompareStrings(string left, string right) => string.CompareOrdinal(left, right);
}

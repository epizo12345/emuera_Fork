using System.Collections.Immutable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.Next.Vm;

public readonly record struct VmOwnerStamp(long OwnerId, long Epoch);
public readonly record struct VmFrameAddress(int Depth, long Serial);
public readonly record struct VmEvaluationContext(VmFunctionExecutionContext Function, VmFrameAddress Frame, VmOwnerStamp Stamp);

public sealed class VmFunctionExecutionContext
{
    public RuntimeFunctionId FunctionId { get; }
    public int CodeSlot { get; }
    public long Generation { get; }
    public LinkedProgram Program { get; }
    public VmSemanticStructuralLookup StructuralLookup { get; }
    public FunctionRuntimeMetadata Metadata { get; }
    public ImmutableArray<int> PhysicalLoopIds { get; }
    public int ScopeKey { get; }

    public VmFunctionExecutionContext(RuntimeFunctionId functionId, int codeSlot, long generation, LinkedProgram program,
        FunctionRuntimeMetadata metadata, ImmutableArray<int> physicalLoopIds = default, int? scopeKey = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(metadata);
        if (program.Descriptors.Length != 1)
            throw new ArgumentException("A context must contain exactly one function descriptor", nameof(program));
        FunctionId = functionId;
        CodeSlot = codeSlot;
        Generation = generation;
        Program = program;
        StructuralLookup = VmSemanticStructuralLookup.Build(program);
        Metadata = metadata;
        PhysicalLoopIds = physicalLoopIds.IsDefault ? [] : physicalLoopIds;
        ScopeKey = scopeKey ?? functionId.Value;
    }
}

public enum VmAdmissionStatus : byte
{
    Ready,
    Missing,
    Blocked,
    WrongKind,
    Terminal,
}

public readonly record struct VmAdmissionLease(long Serial, RuntimeFunctionId Target, VmReturnKind ReturnKind,
    int CodeSlot, long Generation, VmAdmissionStatus Status)
{
    public bool Ready => Status == VmAdmissionStatus.Ready;
}

public readonly record struct VmOwnerReturn(VmReturnKind ReturnKind, VmSemanticValue Value);
public enum VmInputValueKind : byte { Integer, String }
public readonly record struct VmInputContinuation(long RequestSerial, VmOwnerStamp Stamp, VmFrameAddress Frame,
    RuntimeFunctionId FunctionId, int CodeSlot, long CodeGeneration, VmInputValueKind ExpectedKind);
public readonly record struct VmSegmentBookmark(long Serial, VmOwnerStamp Stamp, int Floor, int Depth, VmFrameAddress Top);
public readonly record struct VmFaultFrameSnapshot(long FrameSerial, int FunctionId, int Pc, int CodeSlot, long CodeGeneration, int PinCount);
public sealed record VmFaultSnapshot(VmStopReason Reason, int FunctionId, int Pc, string Message,
    ImmutableArray<VmFaultFrameSnapshot> Frames, int PendingLeases, bool Committed);
public enum VmOwnerRunState : byte { Running, Suspended, Terminal }
public enum VmOwnerInvalidationReason : byte { SourceDrift, Reset, Close, Revoke, BeginApplied }

// The main runtime owns one implementation for the lifetime of one graph-free session.
// VmMachine borrows this boundary; it never owns general frames, sidecars, banks, pins or loop cells.
public interface IVmContextOwner
{
    VmOwnerStamp Stamp { get; }
    bool IsTerminal { get; }
    int FrameDepth { get; }
    int MaxFrameDepth { get; }
    long RemainingFuel { get; }
    long MaterializationCount { get; }
    long PinAcquireCount { get; }
    long PinReleaseCount { get; }
    long LegacyRetryAfterCommit { get; }
    VmOwnerRunState RunState { get; }
    VmFaultSnapshot? FaultSnapshot { get; }
    VmFrame CurrentFrame { get; }
    VmFrame[] ActiveFrames { get; }
    bool TryConsumeFuel();
    VmAdmissionLease PrepareInvocation(RuntimeFunctionId target, VmReturnKind returnKind, FunctionKind expectedKind);
    void Cancel(ref VmAdmissionLease lease);
    bool CommitFrame(ref VmAdmissionLease lease, ReadOnlySpan<VmSemanticValue> actuals, out VmFrameAddress address);
    bool TryGetCurrentContext(out VmFunctionExecutionContext context, out VmFrameAddress address);
    bool TrySetCurrentPc(int pc);
    bool TryReturn(out VmOwnerReturn result);
    void SetCurrentReturnValue(VmSemanticValue value);
    void TerminalFault(VmStopReason reason, int functionId, int pc, string message);
    bool TryBeginEvaluation(in VmEvaluationContext context);
    void EndEvaluation(in VmEvaluationContext context);
    bool OwnsFrameVariable(VmFrameAddress frame, string name);
    bool TryGetFrameVariableKind(VmFrameAddress frame, string name, out VmSemanticValueKind kind);
    bool TryGetFrameVariableLength(VmFrameAddress frame, string name, int dimension, out long length);
    bool TryReadFrameValue(VmFrameAddress frame, string name, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value);
    bool TryWriteFrameValue(VmFrameAddress frame, string name, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value);
    bool TryClearFrameVariable(VmFrameAddress frame, string name);
    int FrameVariableCapacity(VmFrameAddress frame, string name, ReadOnlySpan<VmSemanticValue> indices);
    void CaptureLoop(RuntimeFunctionId functionId, int sourceLoopOrdinal, long end, long step, long repeat = 0);
    bool TryReadLoop(RuntimeFunctionId functionId, int sourceLoopOrdinal, out long end, out long step, out long repeat);
    bool TryAdvanceRepeat(RuntimeFunctionId functionId, int sourceLoopOrdinal, out long value);
    VmInputContinuation ReserveInput(VmInputValueKind expectedKind);
    bool CommitInput(VmInputContinuation token);
    void PublishInput(VmInputContinuation token, Action<Action<VmSemanticValue>> publish, Action<VmSemanticValue> resume);
    bool TryResume(VmInputContinuation token, VmSemanticValue value, Action<VmSemanticValue> publish);
    void QueueSynchronousCallback(Action callback);
}

// Owner-owned storage and cold admission implementation. CompactRuntimeOwner contains exactly one instance.
// It is public only so the VM self-test can exercise the same storage contract without the main assembly.
public sealed class VmContextStorage : IVmContextOwner
{
    private sealed record Registration(FunctionKind Kind, Func<VmFunctionExecutionContext?>? Materialize);
    private sealed class Resident
    {
        public required VmFunctionExecutionContext Context;
        public int PinCount;
        public int EvaluationBorrows;
    }
    private sealed class ScopeBank
    {
        public required VmSemanticValue[] Arg;
        public required VmSemanticValue[] Args;
        public required VmSemanticValue[] Local;
        public required VmSemanticValue[] Locals;
        public required VmSemanticValue[][] Private;
    }
    private struct Sidecar
    {
        public long Serial;
        public int CodeSlot;
        public long Generation;
        public VmSemanticValue ReturnValue;
        public int ActivationStart;
        public int ActivationCount;
        public bool Active;
    }
    private struct LeaseState
    {
        public long Serial;
        public int Target;
        public int CodeSlot;
        public long Generation;
        public bool Prepared;
    }
    private struct LoopCell
    {
        public long End;
        public long Step;
        public long Repeat;
        public bool Initialized;
    }

    private readonly Dictionary<int, Registration> registrations = [];
    private readonly Dictionary<int, Resident> residents = [];
    private readonly Dictionary<int, ScopeBank> banks = [];
    private readonly Dictionary<(int FunctionId, int PrivateOrdinal), VmSemanticValue[]> statics = [];
    private readonly Dictionary<(int FunctionId, int PrivateOrdinal), FunctionRuntimePrivate> staticDeclarations = [];
    private readonly Dictionary<(int FunctionId, int SourceLoopOrdinal), LoopCell> loops = [];
    private readonly Dictionary<long, LeaseState> leases = [];
    private VmFrame[] frames = new VmFrame[16];
    private Sidecar[] sidecars = new Sidecar[16];
    private VmSemanticValue[] activationValues = new VmSemanticValue[64];
    private int activationTop;
    private int frameDepth;
    private long nextSerial;
    private long nextInputSerial;
    private long nextBookmarkSerial;
    private VmInputContinuation? pendingInput;
    private VmSegmentBookmark? activeBookmark;
    private bool inputCommitted;
    private readonly Queue<Action> synchronousCallbacks = [];
    private bool publishingInput;
    private bool terminal;
    private VmStopReason faultReason;
    private int faultFunctionId = -1;
    private int faultPc = -1;
    private string faultMessage = string.Empty;
    private VmFaultSnapshot? faultSnapshot;
    private readonly Queue<Action> cleanupActions = [];
    private int cleanupErrorCount;
    private Func<RuntimeFunctionId, VmFunctionExecutionContext?>? sharedMaterializer;
    private readonly int minimumArgSize;
    private readonly int minimumArgsSize;

    public VmContextStorage(long ownerId, long epoch, long fuel, int minimumArgSize = 1, int minimumArgsSize = 1)
    {
        if (ownerId <= 0 || epoch <= 0 || fuel <= 0 || minimumArgSize < 1 || minimumArgsSize < 1) throw new ArgumentOutOfRangeException();
        Stamp = new(ownerId, epoch);
        RemainingFuel = fuel;
        this.minimumArgSize = minimumArgSize;
        this.minimumArgsSize = minimumArgsSize;
    }

    public VmOwnerStamp Stamp { get; private set; }
    public bool IsTerminal => terminal;
    public int FrameDepth => frameDepth;
    public int MaxFrameDepth { get; private set; }
    public long RemainingFuel { get; private set; }
    public long MaterializationCount { get; private set; }
    public long PinAcquireCount { get; private set; }
    public long PinReleaseCount { get; private set; }
    public long LegacyRetryAfterCommit { get; private set; }
    public VmOwnerRunState RunState => terminal ? VmOwnerRunState.Terminal : inputCommitted ? VmOwnerRunState.Suspended : VmOwnerRunState.Running;
    public VmFaultSnapshot? FaultSnapshot => faultSnapshot;
    public int CleanupErrorCount => cleanupErrorCount;
    public VmStopReason FaultReason => faultReason;
    public int FaultFunctionId => faultFunctionId;
    public int FaultPc => faultPc;
    public string FaultMessage => faultMessage;
    public VmFrame CurrentFrame => frameDepth == 0 ? default : frames[frameDepth - 1];
    public VmFrame[] ActiveFrames => frames[..frameDepth].ToArray();
#if R0_F6G10B
    public VmFrameAddress[] ActiveFrameAddresses => Enumerable.Range(0, frameDepth)
        .Select(depth => new VmFrameAddress(depth, sidecars[depth].Serial)).ToArray();
#endif
    public int ResidentCount => residents.Count;
    public int PendingLeaseCount => leases.Count;

    public void Register(RuntimeFunctionId id, FunctionKind kind, Func<VmFunctionExecutionContext?> materialize)
    {
        ArgumentNullException.ThrowIfNull(materialize);
        if (terminal || id.Value < 0 || !registrations.TryAdd(id.Value, new(kind, materialize)))
            throw new InvalidOperationException("owner registration rejected");
    }

    public void SetSharedMaterializer(Func<RuntimeFunctionId, VmFunctionExecutionContext?> materialize)
    {
        ArgumentNullException.ThrowIfNull(materialize);
        if (terminal || sharedMaterializer is not null || registrations.Count != 0)
            throw new InvalidOperationException("owner shared materializer rejected");
        sharedMaterializer = materialize;
    }

    public void Register(RuntimeFunctionId id, FunctionKind kind)
    {
        if (terminal || sharedMaterializer is null || id.Value < 0 || !registrations.TryAdd(id.Value, new(kind, null)))
            throw new InvalidOperationException($"owner registration rejected: id={id.Value};kind={kind};terminal={terminal};sharedMaterializer={sharedMaterializer is not null};duplicate={registrations.ContainsKey(id.Value)}");
    }

    public bool TryConsumeFuel()
    {
        if (terminal || RemainingFuel <= 0) return false;
        RemainingFuel--;
        return true;
    }

    public VmAdmissionLease PrepareInvocation(RuntimeFunctionId target, VmReturnKind returnKind, FunctionKind expectedKind)
    {
        if (terminal) return new(0, target, returnKind, -1, 0, VmAdmissionStatus.Terminal);
        if (!registrations.TryGetValue(target.Value, out var registration))
            return new(0, target, returnKind, -1, 0, VmAdmissionStatus.Missing);
        if (registration.Kind != expectedKind)
            return new(0, target, returnKind, -1, 0, VmAdmissionStatus.WrongKind);
        if (!residents.TryGetValue(target.Value, out var resident))
        {
            VmFunctionExecutionContext? context;
            try { context = registration.Materialize is not null ? registration.Materialize() : sharedMaterializer?.Invoke(target); }
            catch (Exception ex)
            {
                TerminalFault(VmStopReason.TerminalFault, frameDepth == 0 ? target.Value : CurrentFrame.FunctionId,
                    frameDepth == 0 ? -1 : CurrentFrame.Pc, "materialization failure: " + ex.GetType().Name + ": " + ex.Message);
                return new(0, target, returnKind, -1, 0, VmAdmissionStatus.Terminal);
            }
            MaterializationCount++;
            if (context is null) return new(0, target, returnKind, -1, 0, VmAdmissionStatus.Blocked);
            resident = new() { Context = context };
            residents.Add(target.Value, resident);
        }
        resident.PinCount++;
        PinAcquireCount++;
        var serial = checked(++nextSerial);
        leases.Add(serial, new() { Serial = serial, Target = target.Value, CodeSlot = resident.Context.CodeSlot, Generation = resident.Context.Generation, Prepared = true });
        return new(serial, target, returnKind, resident.Context.CodeSlot, resident.Context.Generation, VmAdmissionStatus.Ready);
    }

    public void Cancel(ref VmAdmissionLease lease)
    {
        if (!lease.Ready || !leases.Remove(lease.Serial, out var state) || !state.Prepared) return;
        ReleasePin(lease.Target.Value, state.CodeSlot, state.Generation);
        lease = lease with { Status = VmAdmissionStatus.Blocked };
    }

    public bool CommitFrame(ref VmAdmissionLease lease, ReadOnlySpan<VmSemanticValue> actuals, out VmFrameAddress address)
    {
        address = default;
        if (terminal || !lease.Ready || !leases.Remove(lease.Serial, out var state) || !state.Prepared ||
            !residents.TryGetValue(lease.Target.Value, out var resident) || resident.Context.CodeSlot != state.CodeSlot || resident.Context.Generation != state.Generation)
            return false;
        var bank = GetOrCreateBank(resident.Context);
        var activationStart = activationTop;
        var activationCount = ActivationValueCount(resident.Context.Metadata);
        EnsureActivationCapacity(activationCount);
        InitializeActivation(resident.Context.Metadata, activationStart);
        activationTop += activationCount;
        if (!BindActuals(resident.Context.Metadata, bank, activationStart, actuals))
        {
            Array.Clear(activationValues, activationStart, activationCount);
            activationTop = activationStart;
            ReleasePin(lease.Target.Value, state.CodeSlot, state.Generation);
            return false;
        }
        EnsureFrameCapacity();
        var depth = frameDepth;
        frames[depth] = new(lease.Target.Value, 0, lease.ReturnKind);
        sidecars[depth] = new() { Serial = lease.Serial, CodeSlot = state.CodeSlot, Generation = state.Generation, ReturnValue = VmSemanticValue.Missing, ActivationStart = activationStart, ActivationCount = activationCount, Active = true };
        frameDepth++;
        MaxFrameDepth = Math.Max(MaxFrameDepth, frameDepth);
        address = new(depth, lease.Serial);
        lease = lease with { Status = VmAdmissionStatus.Blocked };
        return true;
    }

    public bool TryGetCurrentContext(out VmFunctionExecutionContext context, out VmFrameAddress address)
    {
        context = null!;
        address = default;
        if (terminal || frameDepth == 0) return false;
        var depth = frameDepth - 1;
        var sidecar = sidecars[depth];
        var frame = frames[depth];
        if (!sidecar.Active || !residents.TryGetValue(frame.FunctionId, out var resident) ||
            resident.Context.CodeSlot != sidecar.CodeSlot || resident.Context.Generation != sidecar.Generation)
            return false;
        context = resident.Context;
        address = new(depth, sidecar.Serial);
        return true;
    }

    public bool TrySetCurrentPc(int pc)
    {
        if (!TryGetCurrentContext(out var context, out _)) return false;
        var descriptor = context.Program.Descriptors[0];
        if (pc < 0 || pc > descriptor.CodeLength) return false;
        var frame = frames[frameDepth - 1];
        frames[frameDepth - 1] = new(frame.FunctionId, pc, frame.ReturnKind);
        return true;
    }

    public void SetCurrentReturnValue(VmSemanticValue value)
    {
        if (frameDepth == 0 || terminal) throw new InvalidOperationException();
        sidecars[frameDepth - 1].ReturnValue = value;
    }

    public bool TryReturn(out VmOwnerReturn result)
    {
        result = default;
        if (frameDepth == 0 || terminal || !TryGetCurrentContext(out var context, out _)) return false;
        var depth = frameDepth - 1;
        var frame = frames[depth];
        var sidecar = sidecars[depth];
        var value = sidecar.ReturnValue.Kind == VmSemanticValueKind.Missing
            ? context.Metadata.ReturnType == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(0L)
            : context.Metadata.ReturnType == RuntimeMetadataValueType.String ? VmSemanticValue.From(string.Empty)
            : VmSemanticValue.Unavailable
            : sidecar.ReturnValue;
        sidecars[depth] = default;
        frames[depth] = default;
        frameDepth--;
        Array.Clear(activationValues, sidecar.ActivationStart, sidecar.ActivationCount);
        activationTop = sidecar.ActivationStart;
        ReleasePin(frame.FunctionId, sidecar.CodeSlot, sidecar.Generation);
        result = new(frame.ReturnKind, value);
        return true;
    }

    public void TerminalFault(VmStopReason reason, int functionId, int pc, string message)
    {
        if (terminal) return;
        var snapshot = ImmutableArray.CreateBuilder<VmFaultFrameSnapshot>(frameDepth);
        for (var depth = 0; depth < frameDepth; depth++)
        {
            var frame = frames[depth];
            var sidecar = sidecars[depth];
            var pins = residents.TryGetValue(frame.FunctionId, out var resident) ? resident.PinCount : 0;
            snapshot.Add(new(sidecar.Serial, frame.FunctionId, frame.Pc, sidecar.CodeSlot, sidecar.Generation, pins));
        }
        faultSnapshot = new(reason, functionId, pc, message, snapshot.ToImmutable(), leases.Count, frameDepth != 0);
        terminal = true;
        faultReason = reason;
        faultFunctionId = functionId;
        faultPc = pc;
        faultMessage = message;
        foreach (var lease in leases.Values)
            ReleasePin(lease.Target, lease.CodeSlot, lease.Generation);
        leases.Clear();
        pendingInput = null;
        synchronousCallbacks.Clear();
        for (var depth = frameDepth - 1; depth >= 0; depth--)
        {
            var frame = frames[depth];
            var sidecar = sidecars[depth];
            if (sidecar.Active) ReleasePin(frame.FunctionId, sidecar.CodeSlot, sidecar.Generation);
            if (sidecar.Active) Array.Clear(activationValues, sidecar.ActivationStart, sidecar.ActivationCount);
            sidecars[depth] = default;
            frames[depth] = default;
        }
        frameDepth = 0;
        activationTop = 0;
        while (cleanupActions.TryDequeue(out var cleanup))
        {
            try { cleanup(); }
            catch { cleanupErrorCount++; }
        }
    }

    public bool TryEvict(RuntimeFunctionId id)
    {
        if (terminal || !residents.TryGetValue(id.Value, out var resident) || resident.PinCount != 0 || resident.EvaluationBorrows != 0) return false;
        residents.Remove(id.Value);
        return true;
    }

    public bool TryBeginEvaluation(in VmEvaluationContext context)
    {
        if (terminal || context.Stamp != Stamp || !TryGetFrame(context.Frame, out var active, out _) ||
            !ReferenceEquals(active, context.Function) || !residents.TryGetValue(context.Function.FunctionId.Value, out var resident) ||
            !ReferenceEquals(resident.Context, context.Function))
            return false;
        resident.EvaluationBorrows++;
        return true;
    }

    public void EndEvaluation(in VmEvaluationContext context)
    {
        if (residents.TryGetValue(context.Function.FunctionId.Value, out var resident) &&
            ReferenceEquals(resident.Context, context.Function) && resident.EvaluationBorrows > 0)
            resident.EvaluationBorrows--;
    }

    public void QueueCleanupAction(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        cleanupActions.Enqueue(action);
    }

    public void RevokeSource() => Invalidate(VmOwnerInvalidationReason.SourceDrift);

    public void RevokeInput()
    {
        pendingInput = null;
        inputCommitted = false;
        synchronousCallbacks.Clear();
    }

    public bool TryParkSegment(int floor, out VmSegmentBookmark bookmark)
    {
        bookmark = default;
        if (terminal || activeBookmark is not null || floor < 0 || floor > frameDepth) return false;
        var top = frameDepth == 0 ? default : new VmFrameAddress(frameDepth - 1, sidecars[frameDepth - 1].Serial);
        bookmark = new(checked(++nextBookmarkSerial), Stamp, floor, frameDepth, top);
        activeBookmark = bookmark;
        return true;
    }

    public bool TryResumeSegment(VmSegmentBookmark bookmark)
    {
        if (terminal || activeBookmark != bookmark || bookmark.Stamp != Stamp || bookmark.Depth != frameDepth ||
            frameDepth != 0 && bookmark.Top != new VmFrameAddress(frameDepth - 1, sidecars[frameDepth - 1].Serial)) return false;
        activeBookmark = null;
        return true;
    }

    public bool DiscardSegment(VmSegmentBookmark bookmark, bool advanceExecutionEpoch)
    {
        if (terminal || activeBookmark != bookmark || bookmark.Stamp != Stamp) return false;
        activeBookmark = null;
        return UnwindToFloor(bookmark.Floor) && (!advanceExecutionEpoch || AdvanceExecutionEpoch());
    }

    public void ResetPersistentBanks()
    {
        foreach (var bank in banks.Values)
        {
            Array.Fill(bank.Arg, VmSemanticValue.From(0L));
            Array.Fill(bank.Args, VmSemanticValue.From(string.Empty));
            Array.Fill(bank.Local, VmSemanticValue.From(0L));
            Array.Fill(bank.Locals, VmSemanticValue.From(string.Empty));
        }
        foreach (var pair in statics)
        {
            if (!staticDeclarations.TryGetValue(pair.Key, out var declaration)) continue;
            var defaults = CreatePrivateValues(declaration);
            defaults.CopyTo(pair.Value, 0);
        }
    }

    public bool UnwindToFloor(int floor)
    {
        if (terminal || floor < 0 || floor > frameDepth) return false;
        RevokeInput();
        foreach (var lease in leases.Values) ReleasePin(lease.Target, lease.CodeSlot, lease.Generation);
        leases.Clear();
        while (frameDepth > floor)
        {
            var depth = --frameDepth;
            var frame = frames[depth];
            var sidecar = sidecars[depth];
            if (sidecar.Active) ReleasePin(frame.FunctionId, sidecar.CodeSlot, sidecar.Generation);
            if (sidecar.Active) Array.Clear(activationValues, sidecar.ActivationStart, sidecar.ActivationCount);
            activationTop = sidecar.ActivationStart;
            sidecars[depth] = default;
            frames[depth] = default;
        }
        return true;
    }

    public bool AdvanceExecutionEpoch()
    {
        if (terminal) return false;
        activeBookmark = null;
        Stamp = Stamp with { Epoch = checked(Stamp.Epoch + 1) };
        return true;
    }

    public void Invalidate(VmOwnerInvalidationReason reason) => TerminalFault(VmStopReason.TerminalFault,
        frameDepth == 0 ? -1 : CurrentFrame.FunctionId, frameDepth == 0 ? -1 : CurrentFrame.Pc,
        "owner invalidated: " + reason);

    public void ReleaseOwnedCodeAfterTerminal()
    {
        if (!terminal) throw new InvalidOperationException("code release requires terminal owner");
        residents.Clear();
        registrations.Clear();
        banks.Clear();
        statics.Clear();
        staticDeclarations.Clear();
        sharedMaterializer = null;
    }

    public bool TryGetPinCount(RuntimeFunctionId id, out int count)
    {
        if (residents.TryGetValue(id.Value, out var resident)) { count = resident.PinCount; return true; }
        count = 0; return false;
    }

    public VmInputContinuation ReserveInput(VmInputValueKind expectedKind)
    {
        if (terminal || pendingInput is not null || !TryGetCurrentContext(out var context, out var address))
            throw new InvalidOperationException("input reservation rejected");
        var token = new VmInputContinuation(checked(++nextInputSerial), Stamp, address, context.FunctionId, context.CodeSlot, context.Generation, expectedKind);
        pendingInput = token;
        inputCommitted = false;
        return token;
    }

    public bool CommitInput(VmInputContinuation token)
    {
        if (terminal || pendingInput != token || inputCommitted) return false;
        inputCommitted = true;
        return true;
    }

    public void PublishInput(VmInputContinuation token, Action<Action<VmSemanticValue>> publish, Action<VmSemanticValue> resume)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(resume);
        if (terminal || !inputCommitted || pendingInput != token) throw new InvalidOperationException("input publication rejected");
        publishingInput = true;
        try { publish(value => QueueSynchronousCallback(() => resume(value))); }
        finally
        {
            publishingInput = false;
            while (synchronousCallbacks.TryDequeue(out var callback)) callback();
        }
    }

    public bool TryResume(VmInputContinuation token, VmSemanticValue value, Action<VmSemanticValue> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (terminal || !inputCommitted || pendingInput != token || token.Stamp != Stamp ||
            !TryGetCurrentContext(out var context, out var address) || address != token.Frame ||
            context.FunctionId != token.FunctionId || context.CodeSlot != token.CodeSlot || context.Generation != token.CodeGeneration)
            return false;
        if (token.ExpectedKind == VmInputValueKind.Integer && value.Kind != VmSemanticValueKind.Integer ||
            token.ExpectedKind == VmInputValueKind.String && value.Kind != VmSemanticValueKind.String)
            return false;
        pendingInput = null;
        inputCommitted = false;
        publishingInput = true;
        try { publish(value); }
        finally
        {
            publishingInput = false;
            while (synchronousCallbacks.TryDequeue(out var callback)) callback();
        }
        return true;
    }

    public void QueueSynchronousCallback(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (publishingInput) synchronousCallbacks.Enqueue(callback); else callback();
    }

    public void CaptureLoop(RuntimeFunctionId functionId, int sourceLoopOrdinal, long end, long step, long repeat = 0) =>
        loops[(functionId.Value, sourceLoopOrdinal)] = new() { End = end, Step = step, Repeat = repeat, Initialized = true };

    public bool TryReadLoop(RuntimeFunctionId functionId, int sourceLoopOrdinal, out long end, out long step, out long repeat)
    {
        if (loops.TryGetValue((functionId.Value, sourceLoopOrdinal), out var cell) && cell.Initialized)
        { end = cell.End; step = cell.Step; repeat = cell.Repeat; return true; }
        end = step = repeat = 0; return false;
    }

    public bool TryAdvanceRepeat(RuntimeFunctionId functionId, int sourceLoopOrdinal, out long value)
    {
        var key = (functionId.Value, sourceLoopOrdinal);
        if (!loops.TryGetValue(key, out var cell) || !cell.Initialized) { value = 0; return false; }
        value = cell.Repeat = unchecked(cell.Repeat + cell.Step);
        loops[key] = cell;
        return true;
    }

    // Compatibility helpers retained for the G7 primitive fixtures.
    public void CaptureLoop(int sourceLoopOrdinal, long end, long step, long repeat = 0) =>
        CaptureLoop(new(0), sourceLoopOrdinal, end, step, repeat);
    public bool TryReadLoop(int sourceLoopOrdinal, out long end, out long step, out long repeat) =>
        TryReadLoop(new(0), sourceLoopOrdinal, out end, out step, out repeat);

    public bool OwnsFrameVariable(VmFrameAddress address, string name) =>
        TryGetFrame(address, out var context, out _) &&
        (name.Equals("ARG", StringComparison.OrdinalIgnoreCase) || name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase) ||
         FindPrivate(context.Metadata, name) >= 0 || FindParameter(context.Metadata, name) >= 0);

    public bool TryGetFrameVariableKind(VmFrameAddress address, string name, out VmSemanticValueKind kind)
    {
        kind = VmSemanticValueKind.Unavailable;
        if (!TryGetFrame(address, out var context, out _)) return false;
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase))
        { kind = VmSemanticValueKind.Integer; return true; }
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase))
        { kind = VmSemanticValueKind.String; return true; }
        var parameterOrdinal = FindParameter(context.Metadata, name);
        if (parameterOrdinal >= 0)
        {
            kind = context.Metadata.Parameters[parameterOrdinal].Type == RuntimeMetadataValueType.Integer
                ? VmSemanticValueKind.Integer : VmSemanticValueKind.String;
            return true;
        }
        var ordinal = FindPrivate(context.Metadata, name);
        if (ordinal < 0) return false;
        kind = context.Metadata.PrivateVariables[ordinal].Type == RuntimeMetadataValueType.Integer
            ? VmSemanticValueKind.Integer : VmSemanticValueKind.String;
        return true;
    }

    public bool TryGetFrameVariableLength(VmFrameAddress address, string name, int dimension, out long length)
    {
        length = 0;
        if (dimension < 0 || !TryGetFrame(address, out var context, out var bank)) return false;
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = bank.Arg.Length; return true; }
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = bank.Args.Length; return true; }
        if (name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = bank.Local.Length; return true; }
        if (name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) { if (dimension != 0) return false; length = bank.Locals.Length; return true; }
        if (FindParameter(context.Metadata, name) >= 0) { if (dimension != 0) return false; length = 1; return true; }
        var ordinal = FindPrivate(context.Metadata, name);
        if (ordinal < 0 || (uint)dimension >= (uint)context.Metadata.PrivateVariables[ordinal].Dimensions.Length) return false;
        length = context.Metadata.PrivateVariables[ordinal].Dimensions[dimension];
        return true;
    }

    public bool TryReadFrameValue(VmFrameAddress address, string name, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value)
    {
        value = VmSemanticValue.Unavailable;
        if (!TryGetFrame(address, out var context, out var bank)) return false;
        if (TryParameterValue(context.Metadata, bank, address, name, indices, out var parameterIndex, out var parameterValues, out var parameterActivationIndex))
        {
            value = parameterActivationIndex >= 0 ? activationValues[parameterActivationIndex] : parameterValues![parameterIndex];
            return value.Kind != VmSemanticValueKind.Missing;
        }
        if (!TryValueIndex(context.Metadata, name, indices, out var index)) return false;
        if (TryActivationIndex(address, context.Metadata, name, index, out var activationIndex, out _))
        {
            value = activationValues[activationIndex];
            return value.Kind != VmSemanticValueKind.Missing;
        }
        var values = SelectPersistentValues(context.Metadata, bank, name, out _);
        if (values is null || (uint)index >= (uint)values.Length || values[index].Kind == VmSemanticValueKind.Missing) return false;
        value = values[index]; return true;
    }

    public bool TryWriteFrameValue(VmFrameAddress address, string name, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value)
    {
        if (!TryGetFrame(address, out var context, out var bank)) return false;
        if (TryParameterValue(context.Metadata, bank, address, name, indices, out var parameterIndex, out var parameterValues, out var parameterActivationIndex))
        {
            if (parameterActivationIndex >= 0) activationValues[parameterActivationIndex] = value;
            else parameterValues![parameterIndex] = value;
            return true;
        }
        if (!TryValueIndex(context.Metadata, name, indices, out var index)) return false;
        if (TryActivationIndex(address, context.Metadata, name, index, out var activationIndex, out var activationConst))
        {
            if (activationConst) return false;
            activationValues[activationIndex] = value;
            return true;
        }
        var values = SelectPersistentValues(context.Metadata, bank, name, out var isConst);
        if (values is null || isConst || (uint)index >= (uint)values.Length) return false;
        values[index] = value; return true;
    }

    public bool TryReadFrameValue(VmFrameAddress address, string name, int index, out VmSemanticValue value) =>
        TryReadFrameValue(address, name, [VmSemanticValue.From(index)], out value);
    public bool TryWriteFrameValue(VmFrameAddress address, string name, int index, VmSemanticValue value) =>
        TryWriteFrameValue(address, name, [VmSemanticValue.From(index)], value);

    public bool TryClearFrameVariable(VmFrameAddress address, string name)
    {
        if (!TryGetFrame(address, out var context, out var bank)) return false;
        var values = SelectPersistentValues(context.Metadata, bank, name, out var isConst);
        if (values is null)
        {
            var ordinal = FindPrivate(context.Metadata, name);
            if (ordinal < 0 || context.Metadata.PrivateVariables[ordinal].IsStatic || context.Metadata.PrivateVariables[ordinal].IsConst) return false;
            var start = sidecars[address.Depth].ActivationStart + ActivationOffset(context.Metadata, ordinal);
            var length = PrivateLength(context.Metadata.PrivateVariables[ordinal]);
            var fill = context.Metadata.PrivateVariables[ordinal].Type == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(0L) : VmSemanticValue.From(string.Empty);
            Array.Fill(activationValues, fill, start, length);
            return true;
        }
        if (isConst) return false;
        var kind = name.Equals("ARGS", StringComparison.OrdinalIgnoreCase) || name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase) ||
            (FindPrivate(context.Metadata, name) is var privateOrdinal && privateOrdinal >= 0 &&
             context.Metadata.PrivateVariables[privateOrdinal].Type == RuntimeMetadataValueType.String);
        Array.Fill(values, kind ? VmSemanticValue.From(string.Empty) : VmSemanticValue.From(0L));
        return true;
    }

    public int FrameVariableCapacity(VmFrameAddress address, string name, ReadOnlySpan<VmSemanticValue> indices)
    {
        if (!TryGetFrameVariableLength(address, name, 0, out var length) || !TryValueIndexForCapacity(indices, out var index)) return int.MaxValue;
        return (int)Math.Max(0, Math.Min(int.MaxValue, length - index));
    }

    private bool TryGetFrame(VmFrameAddress address, out VmFunctionExecutionContext context, out ScopeBank bank)
    {
        context = null!; bank = null!;
        if (terminal || (uint)address.Depth >= (uint)frameDepth || sidecars[address.Depth].Serial != address.Serial) return false;
        var functionId = frames[address.Depth].FunctionId;
        if (!residents.TryGetValue(functionId, out var resident) || !banks.TryGetValue(resident.Context.ScopeKey, out bank!)) return false;
        context = resident.Context; return true;
    }

    private ScopeBank GetOrCreateBank(VmFunctionExecutionContext context)
    {
        if (banks.TryGetValue(context.ScopeKey, out var bank)) return bank;
        var metadata = context.Metadata;
        bank = new()
        {
            Arg = Filled(Math.Max(minimumArgSize, metadata.Parameters.Where(p => !p.IsPrivate && p.Type == RuntimeMetadataValueType.Integer).Select(p => p.Slot + 1).DefaultIfEmpty(0).Max()), VmSemanticValue.From(0L)),
            Args = Filled(Math.Max(minimumArgsSize, metadata.Parameters.Where(p => !p.IsPrivate && p.Type == RuntimeMetadataValueType.String).Select(p => p.Slot + 1).DefaultIfEmpty(0).Max()), VmSemanticValue.From(string.Empty)),
            Local = Filled(metadata.LocalSize, VmSemanticValue.From(0L)),
            Locals = Filled(metadata.LocalsSize, VmSemanticValue.From(string.Empty)),
            Private = new VmSemanticValue[metadata.PrivateVariables.Length][],
        };
        for (var ordinal = 0; ordinal < bank.Private.Length; ordinal++)
        {
            var declaration = metadata.PrivateVariables[ordinal];
            bank.Private[ordinal] = declaration.IsStatic
                ? statics.GetValueOrDefault((context.FunctionId.Value, ordinal)) ?? (statics[(context.FunctionId.Value, ordinal)] = CreatePrivateValues(declaration))
                : [];
            if (declaration.IsStatic) staticDeclarations[(context.FunctionId.Value, ordinal)] = declaration;
        }
        banks.Add(context.ScopeKey, bank);
        return bank;
    }

    private bool BindActuals(FunctionRuntimeMetadata metadata, ScopeBank bank, int activationStart, ReadOnlySpan<VmSemanticValue> actuals)
    {
        for (var ordinal = 0; ordinal < metadata.Parameters.Length; ordinal++)
        {
            var parameter = metadata.Parameters[ordinal];
            var value = ordinal < actuals.Length ? actuals[ordinal] : VmSemanticValue.Missing;
            if (value.Kind == VmSemanticValueKind.Missing)
                value = parameter.Type == RuntimeMetadataValueType.Integer
                    ? VmSemanticValue.From(parameter.HasDefault ? parameter.DefaultInteger : 0L)
                    : VmSemanticValue.From(parameter.HasDefault ? parameter.DefaultString ?? string.Empty : string.Empty);
            if (value.Kind != (parameter.Type == RuntimeMetadataValueType.Integer ? VmSemanticValueKind.Integer : VmSemanticValueKind.String)) return false;
            if (parameter.IsPrivate)
            {
                var privateOrdinal = FindPrivate(metadata, parameter.Name);
                if (privateOrdinal < 0) return false;
                var declaration = metadata.PrivateVariables[privateOrdinal];
                if (declaration.IsStatic)
                {
                    var target = bank.Private[privateOrdinal];
                    if ((uint)parameter.Slot >= (uint)target.Length) return false;
                    target[parameter.Slot] = value;
                }
                else
                {
                    var targetIndex = activationStart + ActivationOffset(metadata, privateOrdinal) + parameter.Slot;
                    if ((uint)targetIndex >= (uint)activationValues.Length) return false;
                    activationValues[targetIndex] = value;
                }
            }
            else
            {
                var target = parameter.Type == RuntimeMetadataValueType.Integer ? bank.Arg : bank.Args;
                if ((uint)parameter.Slot >= (uint)target.Length) return false;
                target[parameter.Slot] = value;
            }
        }
        return true;
    }

    private static VmSemanticValue[]? SelectPersistentValues(FunctionRuntimeMetadata metadata, ScopeBank bank, string name, out bool isConst)
    {
        isConst = false;
        if (name.Equals("ARG", StringComparison.OrdinalIgnoreCase)) return bank.Arg;
        if (name.Equals("ARGS", StringComparison.OrdinalIgnoreCase)) return bank.Args;
        if (name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase)) return bank.Local;
        if (name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase)) return bank.Locals;
        var ordinal = FindPrivate(metadata, name);
        if (ordinal < 0 || !metadata.PrivateVariables[ordinal].IsStatic) return null;
        isConst = metadata.PrivateVariables[ordinal].IsConst;
        return bank.Private[ordinal];
    }

    private bool TryActivationIndex(VmFrameAddress address, FunctionRuntimeMetadata metadata, string name, int index, out int activationIndex, out bool isConst)
    {
        activationIndex = -1;
        isConst = false;
        var ordinal = FindPrivate(metadata, name);
        if (ordinal < 0) return false;
        var declaration = metadata.PrivateVariables[ordinal];
        if (declaration.IsStatic) return false;
        var length = PrivateLength(declaration);
        if ((uint)index >= (uint)length) return false;
        activationIndex = sidecars[address.Depth].ActivationStart + ActivationOffset(metadata, ordinal) + index;
        isConst = declaration.IsConst;
        return true;
    }

    private static int FindPrivate(FunctionRuntimeMetadata metadata, string name)
    {
        for (var ordinal = 0; ordinal < metadata.PrivateVariables.Length; ordinal++)
            if (metadata.PrivateVariables[ordinal].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return ordinal;
        return -1;
    }

    private static int FindParameter(FunctionRuntimeMetadata metadata, string name)
    {
        for (var ordinal = 0; ordinal < metadata.Parameters.Length; ordinal++)
            if (metadata.Parameters[ordinal].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return ordinal;
        return -1;
    }

    private bool TryParameterValue(FunctionRuntimeMetadata metadata, ScopeBank bank, VmFrameAddress address, string name,
        ReadOnlySpan<VmSemanticValue> indices, out int index, out VmSemanticValue[]? values, out int activationIndex)
    {
        index = activationIndex = -1;
        values = null;
        var ordinal = FindParameter(metadata, name);
        if (ordinal < 0 || indices.Length != 0 ||
            name.Equals("ARG", StringComparison.OrdinalIgnoreCase) || name.Equals("ARGS", StringComparison.OrdinalIgnoreCase))
            return false;
        var parameter = metadata.Parameters[ordinal];
        index = parameter.Slot;
        if (!parameter.IsPrivate)
        {
            values = parameter.Type == RuntimeMetadataValueType.Integer ? bank.Arg : bank.Args;
            return (uint)index < (uint)values.Length;
        }
        var privateOrdinal = FindPrivate(metadata, parameter.Name);
        if (privateOrdinal < 0) return false;
        var declaration = metadata.PrivateVariables[privateOrdinal];
        if (declaration.IsStatic)
        {
            values = bank.Private[privateOrdinal];
            return (uint)index < (uint)values.Length;
        }
        activationIndex = sidecars[address.Depth].ActivationStart + ActivationOffset(metadata, privateOrdinal) + index;
        return (uint)index < (uint)PrivateLength(declaration) && (uint)activationIndex < (uint)activationValues.Length;
    }

    private static bool TryValueIndex(FunctionRuntimeMetadata metadata, string name, ReadOnlySpan<VmSemanticValue> indices, out int index)
    {
        var ordinal = FindPrivate(metadata, name);
        if (ordinal >= 0) return TryPrivateIndex(metadata.PrivateVariables[ordinal].Dimensions, indices, out index);
        return TryValueIndexForCapacity(indices, out index);
    }

    private static bool TryValueIndexForCapacity(ReadOnlySpan<VmSemanticValue> indices, out int index)
    {
        index = 0;
        return indices.Length == 0 || indices.Length == 1 && indices[0].TryGetInteger(out var value) &&
            value is >= 0 and <= int.MaxValue && (index = (int)value) >= 0;
    }

    private static bool TryPrivateIndex(ImmutableArray<int> dimensions, ReadOnlySpan<VmSemanticValue> indices, out int index)
    {
        index = 0;
        if (indices.Length == 0 && dimensions.Length == 1) return true;
        if (indices.Length != dimensions.Length) return false;
        for (var i = 0; i < dimensions.Length; i++)
        {
            if (!indices[i].TryGetInteger(out var value) || value < 0 || value >= dimensions[i]) return false;
            index = checked(index * dimensions[i] + (int)value);
        }
        return true;
    }

    private static VmSemanticValue[] Filled(int length, VmSemanticValue value) => Enumerable.Repeat(value, Math.Max(0, length)).ToArray();

    private static VmSemanticValue[] CreatePrivateValues(FunctionRuntimePrivate declaration)
    {
        var length = declaration.Dimensions.Aggregate(1, static (count, dimension) => checked(count * dimension));
        var values = Filled(length, declaration.Type == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(0L) : VmSemanticValue.From(string.Empty));
        if (declaration.Type == RuntimeMetadataValueType.Integer && !declaration.InitialIntegers.IsDefaultOrEmpty)
            for (var i = 0; i < declaration.InitialIntegers.Length; i++) values[i] = VmSemanticValue.From(declaration.InitialIntegers[i]);
        else if (declaration.Type == RuntimeMetadataValueType.String && !declaration.InitialStrings.IsDefaultOrEmpty)
            for (var i = 0; i < declaration.InitialStrings.Length; i++) values[i] = VmSemanticValue.From(declaration.InitialStrings[i]);
        else if (declaration.HasInitializer)
            values[0] = declaration.Type == RuntimeMetadataValueType.Integer ? VmSemanticValue.From(declaration.InitialInteger) : VmSemanticValue.From(declaration.InitialString ?? string.Empty);
        return values;
    }

    private static int PrivateLength(FunctionRuntimePrivate declaration) =>
        declaration.Dimensions.Aggregate(1, static (count, dimension) => checked(count * dimension));

    private static int ActivationValueCount(FunctionRuntimeMetadata metadata) =>
        metadata.PrivateVariables.Where(variable => !variable.IsStatic).Sum(PrivateLength);

    private static int ActivationOffset(FunctionRuntimeMetadata metadata, int privateOrdinal)
    {
        var offset = 0;
        for (var ordinal = 0; ordinal < privateOrdinal; ordinal++)
            if (!metadata.PrivateVariables[ordinal].IsStatic) offset = checked(offset + PrivateLength(metadata.PrivateVariables[ordinal]));
        return offset;
    }

    private void InitializeActivation(FunctionRuntimeMetadata metadata, int start)
    {
        var offset = 0;
        foreach (var declaration in metadata.PrivateVariables)
        {
            if (declaration.IsStatic) continue;
            var initial = CreatePrivateValues(declaration);
            initial.CopyTo(activationValues, start + offset);
            offset += initial.Length;
        }
    }

    private void EnsureActivationCapacity(int additional)
    {
        var required = checked(activationTop + additional);
        if (required <= activationValues.Length) return;
        var capacity = activationValues.Length;
        while (capacity < required) capacity = checked(capacity * 2);
        Array.Resize(ref activationValues, capacity);
    }

    private void EnsureFrameCapacity()
    {
        if (frameDepth != frames.Length) return;
        Array.Resize(ref frames, checked(frames.Length * 2));
        Array.Resize(ref sidecars, frames.Length);
    }

    private void ReleasePin(int functionId, int codeSlot, long generation)
    {
        if (!residents.TryGetValue(functionId, out var resident) || resident.Context.CodeSlot != codeSlot || resident.Context.Generation != generation || resident.PinCount <= 0)
            return;
        resident.PinCount--;
        PinReleaseCount++;
    }
}

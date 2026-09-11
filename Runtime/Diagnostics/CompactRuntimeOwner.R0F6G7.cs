#if R0_F6G7
#nullable enable
using System;
using System.Threading;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal sealed partial class CompactRuntimeOwner : IVmContextOwner
{
    private static long nextContextNeutralOwnerId;
    private VmContextStorage? contextNeutralStorage;
    private VmMachine? contextNeutralMachine;
#if R0_F6G7R2
    private IVmDynamicCallResolver? contextNeutralDynamicResolver;
    private LegacyVmSemanticHost? contextNeutralSemanticHost;
    private Process.LegacyVmRuntimeEffects? contextNeutralEffects;
#endif

    private VmContextStorage ContextNeutralStorage
    {
        get
        {
            if (contextNeutralStorage is not null) return contextNeutralStorage;
            var sizes = hostIdentity is Process process ? process.GetNextRuntimeDefaultFrameSizes() : (Arg: 1, Args: 1);
            return contextNeutralStorage = new VmContextStorage(Interlocked.Increment(ref nextContextNeutralOwnerId), 1,
#if R0_F6G7R2
                50_000_000,
#else
                10_000_000,
#endif
                sizes.Arg, sizes.Args);
        }
    }

    internal VmMachine ContextNeutralMachine => contextNeutralMachine ??= hostIdentity is Process process
#if R0_F6G7R2
        ? new VmMachine(this, ContextNeutralSemanticHost, ContextNeutralEffects, dynamicCallResolver: contextNeutralDynamicResolver)
#else
        ? new VmMachine(this, process.CreateNextRuntimeSemanticHost(), process.CreateNextRuntimeEffects())
#endif
        : throw new InvalidOperationException("context-neutral VM requires a Process host");

#if R0_F6G7R2
    internal LegacyVmSemanticHost ContextNeutralSemanticHost => contextNeutralSemanticHost ??= hostIdentity is Process process
        ? process.CreateGraphFreeNextRuntimeSemanticHost()
        : throw new InvalidOperationException("context-neutral semantic host requires a Process host");
    internal Process.LegacyVmRuntimeEffects ContextNeutralEffects => contextNeutralEffects ??= hostIdentity is Process process
        ? process.CreateGraphFreeNextRuntimeEffects()
        : throw new InvalidOperationException("context-neutral effects require a Process host");
    internal VmOwnerStamp ContextNeutralStamp => ContextNeutralStorage.Stamp;
    internal int ContextNeutralResidentCount => ContextNeutralStorage.ResidentCount;
    internal int ContextNeutralPendingLeaseCount => ContextNeutralStorage.PendingLeaseCount;
    internal int ContextNeutralFrameDepth => ContextNeutralStorage.FrameDepth;
    internal VmFrame[] ContextNeutralFrames => ContextNeutralStorage.ActiveFrames;
#if R0_F6G10B
    internal VmFrameAddress[] ContextNeutralFrameAddresses => ContextNeutralStorage.ActiveFrameAddresses;
#endif
    internal long ContextNeutralMaterializationCount => ContextNeutralStorage.MaterializationCount;
    internal long ContextNeutralPinAcquireCount => ContextNeutralStorage.PinAcquireCount;
    internal long ContextNeutralPinReleaseCount => ContextNeutralStorage.PinReleaseCount;
    internal VmFaultSnapshot? ContextNeutralFaultSnapshot => ContextNeutralStorage.FaultSnapshot;
    internal void ConfigureContextNeutralSharedMaterializer(Func<RuntimeFunctionId, VmFunctionExecutionContext?> materialize,
        IVmDynamicCallResolver resolver)
    {
        if (contextNeutralMachine is not null) throw new InvalidOperationException("context-neutral VM already created");
        ContextNeutralStorage.SetSharedMaterializer(materialize);
        contextNeutralDynamicResolver = resolver;
    }
    internal void RegisterContext(RuntimeFunctionId id, FunctionKind kind) => ContextNeutralStorage.Register(id, kind);
    internal void ResetContextNeutralPersistentBanks() => ContextNeutralStorage.ResetPersistentBanks();
    internal bool UnwindContextNeutralToFloor(int floor) => ContextNeutralStorage.UnwindToFloor(floor);
    internal void RevokeContextNeutralInput() => ContextNeutralStorage.RevokeInput();
#if R0_F6G10B
    internal void RevokeContextNeutralSource() => ContextNeutralStorage.RevokeSource();
#if R0_F6G10C
    internal void ShutdownContextNeutral()
    {
        if (!ContextNeutralStorage.IsTerminal)
            ContextNeutralStorage.TerminalFault(VmStopReason.TerminalFault,
                ContextNeutralStorage.FrameDepth == 0 ? -1 : ContextNeutralStorage.CurrentFrame.FunctionId,
                ContextNeutralStorage.FrameDepth == 0 ? -1 : ContextNeutralStorage.CurrentFrame.Pc,
                "owner shutdown");
        ContextNeutralStorage.ReleaseOwnedCodeAfterTerminal();
        r0f6g7r2Cursor = null;
    }
#endif
#endif
    internal bool AdvanceContextNeutralExecutionEpoch() => ContextNeutralStorage.AdvanceExecutionEpoch();
    internal bool TryParkContextNeutralSegment(int floor, out VmSegmentBookmark bookmark) => ContextNeutralStorage.TryParkSegment(floor, out bookmark);
    internal bool TryResumeContextNeutralSegment(VmSegmentBookmark bookmark) => ContextNeutralStorage.TryResumeSegment(bookmark);
    internal bool DiscardContextNeutralSegment(VmSegmentBookmark bookmark, bool advanceExecutionEpoch) => ContextNeutralStorage.DiscardSegment(bookmark, advanceExecutionEpoch);
#endif

    internal void RegisterContext(RuntimeFunctionId id, FunctionKind kind, Func<VmFunctionExecutionContext?> materialize) =>
        ContextNeutralStorage.Register(id, kind, materialize);

    VmOwnerStamp IVmContextOwner.Stamp => ContextNeutralStorage.Stamp;
    bool IVmContextOwner.IsTerminal => ContextNeutralStorage.IsTerminal;
    int IVmContextOwner.FrameDepth => ContextNeutralStorage.FrameDepth;
    int IVmContextOwner.MaxFrameDepth => ContextNeutralStorage.MaxFrameDepth;
    long IVmContextOwner.RemainingFuel => ContextNeutralStorage.RemainingFuel;
    long IVmContextOwner.MaterializationCount => ContextNeutralStorage.MaterializationCount;
    long IVmContextOwner.PinAcquireCount => ContextNeutralStorage.PinAcquireCount;
    long IVmContextOwner.PinReleaseCount => ContextNeutralStorage.PinReleaseCount;
    long IVmContextOwner.LegacyRetryAfterCommit => ContextNeutralStorage.LegacyRetryAfterCommit;
    VmOwnerRunState IVmContextOwner.RunState => ContextNeutralStorage.RunState;
    VmFaultSnapshot? IVmContextOwner.FaultSnapshot => ContextNeutralStorage.FaultSnapshot;
    VmFrame IVmContextOwner.CurrentFrame => ContextNeutralStorage.CurrentFrame;
    VmFrame[] IVmContextOwner.ActiveFrames => ContextNeutralStorage.ActiveFrames;
    bool IVmContextOwner.TryConsumeFuel() => ContextNeutralStorage.TryConsumeFuel();
    VmAdmissionLease IVmContextOwner.PrepareInvocation(RuntimeFunctionId target, VmReturnKind returnKind, FunctionKind expectedKind) => ContextNeutralStorage.PrepareInvocation(target, returnKind, expectedKind);
    void IVmContextOwner.Cancel(ref VmAdmissionLease lease) => ContextNeutralStorage.Cancel(ref lease);
    bool IVmContextOwner.CommitFrame(ref VmAdmissionLease lease, ReadOnlySpan<VmSemanticValue> actuals, out VmFrameAddress address)
    {
#if R0_F6G7R2
        return R0F6G7R2CommitFrame(ref lease, actuals, out address);
#else
        return ContextNeutralStorage.CommitFrame(ref lease, actuals, out address);
#endif
    }
    bool IVmContextOwner.TryGetCurrentContext(out VmFunctionExecutionContext context, out VmFrameAddress address) => ContextNeutralStorage.TryGetCurrentContext(out context, out address);
    bool IVmContextOwner.TrySetCurrentPc(int pc) => ContextNeutralStorage.TrySetCurrentPc(pc);
    bool IVmContextOwner.TryReturn(out VmOwnerReturn result)
    {
        if (!ContextNeutralStorage.TryReturn(out result)) return false;
#if R0_F6G7R2
        R0F6G7R2AfterFrameReturn(result);
#endif
        return true;
    }
    void IVmContextOwner.SetCurrentReturnValue(VmSemanticValue value) => ContextNeutralStorage.SetCurrentReturnValue(value);
    void IVmContextOwner.TerminalFault(VmStopReason reason, int functionId, int pc, string message) => ContextNeutralStorage.TerminalFault(reason, functionId, pc, message);
    bool IVmContextOwner.TryBeginEvaluation(in VmEvaluationContext context) => ContextNeutralStorage.TryBeginEvaluation(context);
    void IVmContextOwner.EndEvaluation(in VmEvaluationContext context) => ContextNeutralStorage.EndEvaluation(context);
    bool IVmContextOwner.OwnsFrameVariable(VmFrameAddress frame, string name) => ContextNeutralStorage.OwnsFrameVariable(frame, name);
    bool IVmContextOwner.TryGetFrameVariableKind(VmFrameAddress frame, string name, out VmSemanticValueKind kind) => ContextNeutralStorage.TryGetFrameVariableKind(frame, name, out kind);
    bool IVmContextOwner.TryGetFrameVariableLength(VmFrameAddress frame, string name, int dimension, out long length) => ContextNeutralStorage.TryGetFrameVariableLength(frame, name, dimension, out length);
    bool IVmContextOwner.TryReadFrameValue(VmFrameAddress frame, string name, ReadOnlySpan<VmSemanticValue> indices, out VmSemanticValue value) => ContextNeutralStorage.TryReadFrameValue(frame, name, indices, out value);
    bool IVmContextOwner.TryWriteFrameValue(VmFrameAddress frame, string name, ReadOnlySpan<VmSemanticValue> indices, VmSemanticValue value) => ContextNeutralStorage.TryWriteFrameValue(frame, name, indices, value);
    bool IVmContextOwner.TryClearFrameVariable(VmFrameAddress frame, string name) => ContextNeutralStorage.TryClearFrameVariable(frame, name);
    int IVmContextOwner.FrameVariableCapacity(VmFrameAddress frame, string name, ReadOnlySpan<VmSemanticValue> indices) => ContextNeutralStorage.FrameVariableCapacity(frame, name, indices);
    void IVmContextOwner.CaptureLoop(RuntimeFunctionId functionId, int sourceLoopOrdinal, long end, long step, long repeat) => ContextNeutralStorage.CaptureLoop(functionId, sourceLoopOrdinal, end, step, repeat);
    bool IVmContextOwner.TryReadLoop(RuntimeFunctionId functionId, int sourceLoopOrdinal, out long end, out long step, out long repeat) => ContextNeutralStorage.TryReadLoop(functionId, sourceLoopOrdinal, out end, out step, out repeat);
    bool IVmContextOwner.TryAdvanceRepeat(RuntimeFunctionId functionId, int sourceLoopOrdinal, out long value) => ContextNeutralStorage.TryAdvanceRepeat(functionId, sourceLoopOrdinal, out value);
    VmInputContinuation IVmContextOwner.ReserveInput(VmInputValueKind expectedKind) => ContextNeutralStorage.ReserveInput(expectedKind);
    bool IVmContextOwner.CommitInput(VmInputContinuation token) => ContextNeutralStorage.CommitInput(token);
    void IVmContextOwner.PublishInput(VmInputContinuation token, Action<Action<VmSemanticValue>> publish, Action<VmSemanticValue> resume) => ContextNeutralStorage.PublishInput(token, publish, resume);
    bool IVmContextOwner.TryResume(VmInputContinuation token, VmSemanticValue value, Action<VmSemanticValue> publish) => ContextNeutralStorage.TryResume(token, value, publish);
    void IVmContextOwner.QueueSynchronousCallback(Action callback) => ContextNeutralStorage.QueueSynchronousCallback(callback);
}
#endif

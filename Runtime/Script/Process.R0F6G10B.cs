#if R0_F6G10B
#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Script;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal enum CompactInputDisposition : byte { NotCompact, Accepted, Rejected }

    internal void R0F6G10BBindInput(VmInputContinuation continuation, Action<VmSemanticValue> respond,
        PrototypeOpcode opcode, long timeLimit, long inputFlag) =>
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
        .BindInput(continuation, respond, opcode, timeLimit, inputFlag);

    internal CompactInputDisposition R0F6G10BAcceptInput(InputRequest request, VmSemanticValue value) =>
        Program.RuntimeMode != Program.ScriptRuntimeMode.CompactStrict ? CompactInputDisposition.NotCompact :
        (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
        .AcceptInput(request, value);

    internal void R0F6G10BRevokeForClose() => compactProduction?.RevokeForClose();

    internal object R0F6G10BRun(string mode)
    {
        const int saveSlot = 219;
        var runtime = compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized");
        if (!vEvaluator.LoadFrom(saveSlot)) throw new InvalidOperationException("save219 decode failed");
        runtime.RecordHostLifecycle("SaveDecoded:219");
        runtime.ApplyDataReset(discardExecution: true);
        state.SystemState = SystemStateCode.LoadData_DataLoaded;
        runtime.RecordHostLifecycle("HostState:LoadData_DataLoaded");
        PumpExecution();
        var beforeResponse = runtime.G10BEvidence("D9-before-response");
        if (mode == "Invalid") return RunInvalidInputOracle(runtime, beforeResponse);
        if (mode == "WriteFail") return RunWriteFailureOracle(runtime, beforeResponse);
        if (mode is "Reset" or "Load" or "Reload" or "Close") return RunRevocationOracle(runtime, beforeResponse, mode);
        var controlledH = mode == "H";
        var responseAccepted = controlledH && console.R0F6G10BAcceptControlledInput("H");
        var afterResponse = controlledH ? runtime.G10BEvidence("after-controlled-H") : null;
        return new
        {
            Schema = "emuera-r0f6g10b-real-production-event-input-v1",
            Mode = controlledH ? "ControlledH" : "D9NoResponse",
            SaveDecoded = true,
            FreshProcess = true,
            BeforeResponse = beforeResponse,
            ControlledHSubmitted = controlledH,
            ControlledHAccepted = responseAccepted,
            AfterResponse = afterResponse,
            Counters = runtime.Evidence(),
            GuiLaunched = false,
            SendKeysUsed = false,
            UserInputRequested = false,
            PerformanceMeasured = false,
        };
    }

    private object RunInvalidInputOracle(CompactProductionRuntime runtime, object before)
    {
        var request1 = runtime.PendingInputRequest;
        var stale = new InputRequest { InputType = request1.InputType, OneInput = request1.OneInput };
        var staleDisposition = R0F6G10BAcceptInput(stale, VmSemanticValue.From("H"));
        var wrongTypeDisposition = R0F6G10BAcceptInput(request1, VmSemanticValue.From(72L));
        var system = new InputRequest { InputType = InputType.IntValue, IsSystemInput = true };
        var systemDisposition = R0F6G10BAcceptInput(system, VmSemanticValue.From(777L));
        var systemBefore = systemResult;
        InputSystemInteger(777);
        var systemAfter = systemResult;
        var guardMatrix = runtime.InputGuardMatrix();
        var accepted = console.R0F6G10BAcceptControlledInput("H");
        var afterFirst = runtime.G10BEvidence("after-first-response");
        var duplicateDisposition = R0F6G10BAcceptInput(request1, VmSemanticValue.From("H"));
        var request2 = runtime.PendingInputRequest;
        var secondAccepted = false;
        string? ordinaryStop = null;
        try { secondAccepted = console.R0F6G10BAcceptControlledInput(string.Empty); }
        catch (InvalidOperationException ex)
        {
            secondAccepted = runtime.AcceptedInputCount == 2;
            ordinaryStop = ex.Message;
        }
        var afterSecond = runtime.G10BEvidence("after-second-response");
        return new
        {
            Schema = "emuera-r0f6g10b-input-oracle-v1", Mode = "Invalid", Before = before,
            StaleRequestDisposition = staleDisposition.ToString(), WrongTypeDisposition = wrongTypeDisposition.ToString(),
            SystemDisposition = systemDisposition.ToString(), SystemResultBefore = systemBefore, SystemResultAfter = systemAfter,
            GuardMatrix = guardMatrix, FirstAccepted = accepted, DuplicateDisposition = duplicateDisposition.ToString(),
            RequestIdsDistinct = request1.ID != request2.ID, SecondAccepted = secondAccepted,
            OrdinaryPostSecondStop = ordinaryStop,
            AfterFirst = afterFirst, AfterSecond = afterSecond, Counters = runtime.Evidence(),
            GuiLaunched = false, SendKeysUsed = false, PerformanceMeasured = false,
        };
    }

    private object RunWriteFailureOracle(CompactProductionRuntime runtime, object before)
    {
        var request = runtime.PendingInputRequest;
        var resultBefore = vEvaluator.RESULT;
        var resultsBefore = vEvaluator.RESULTS;
        runtime.ArmInputWriteFailure();
        var first = R0F6G10BAcceptInput(request, VmSemanticValue.From("H"));
        var second = R0F6G10BAcceptInput(request, VmSemanticValue.From("H"));
        return new
        {
            Schema = "emuera-r0f6g10b-input-write-failure-v1", Mode = "WriteFail", Before = before,
            FirstDisposition = first.ToString(), SecondDisposition = second.ToString(),
            ResultUnchanged = resultBefore == vEvaluator.RESULT, ResultsUnchanged = resultsBefore == vEvaluator.RESULTS,
            After = runtime.G10BEvidence("after-write-failure"), Counters = runtime.Evidence(),
            GuiLaunched = false, SendKeysUsed = false, PerformanceMeasured = false,
        };
    }

    private object RunRevocationOracle(CompactProductionRuntime runtime, object before, string mode)
    {
        var request = runtime.PendingInputRequest;
        var rejectedOperation = false;
        if (mode == "Reset") runtime.ApplyDataReset(discardExecution: true);
        else if (mode == "Load")
        {
            if (!vEvaluator.LoadFrom(219)) throw new InvalidOperationException("revocation fixture load failed");
            runtime.ApplyDataReset(discardExecution: true);
        }
        else if (mode == "Reload")
        {
            try { R0F6G10ARejectReload(); } catch (NotSupportedException) { rejectedOperation = true; }
        }
        else runtime.RevokeForClose();
        var delayed = R0F6G10BAcceptInput(request, VmSemanticValue.From("H"));
        return new
        {
            Schema = "emuera-r0f6g10b-input-revocation-v1", Mode = mode, Before = before,
            RejectedOperation = rejectedOperation, DelayedDisposition = delayed.ToString(),
            After = runtime.G10BEvidence("after-" + mode), Counters = runtime.Evidence(),
            GuiLaunched = false, SendKeysUsed = false, PerformanceMeasured = false,
        };
    }

    private sealed partial class CompactProductionRuntime
    {
        private sealed record InputBinding(InputRequest Request, VmInputContinuation Continuation,
            Action<VmSemanticValue> Respond, long GameStateEpoch, long ExecutionEpoch,
            long SourceCatalogGeneration, PrototypeOpcode Opcode);

        private InputBinding? inputBinding;
        private bool inputPublished;
        private bool resumeScheduled;
        private int inputPublishCount;
        private int inputPublishReentryCount;
        private int acceptedInputCount;
        private int rejectedInputCount;
        private int resumeCount;
        private int revokedInputCount;

        private void RecordInputTrace(string value) { if (CollectDetailedEvidence) callbackOrder.Add(value); }

        internal InputRequest PendingInputRequest => inputBinding?.Request ??
            throw new InvalidOperationException("CompactStrict has no pending input");
        internal int AcceptedInputCount => acceptedInputCount;
        internal void ArmInputWriteFailure() => owner.ContextNeutralEffects.FailNextInputWrite = true;

        internal object InputGuardMatrix()
        {
            var value = inputBinding ?? throw new InvalidOperationException("CompactStrict has no pending input");
            bool Rejected(InputBinding candidate) => !BindingCurrent(candidate);
            var token = value.Continuation;
            return new
            {
                OldOwner = Rejected(value with { Continuation = token with { Stamp = token.Stamp with { OwnerId = token.Stamp.OwnerId + 1 } } }),
                OldOwnerEpoch = Rejected(value with { Continuation = token with { Stamp = token.Stamp with { Epoch = token.Stamp.Epoch + 1 } } }),
                OldFrameSerial = Rejected(value with { Continuation = token with { Frame = token.Frame with { Serial = token.Frame.Serial + 1 } } }),
                WrongFunction = Rejected(value with { Continuation = token with { FunctionId = new(token.FunctionId.Value + 1) } }),
                WrongCodeSlot = Rejected(value with { Continuation = token with { CodeSlot = token.CodeSlot + 1 } }),
                WrongCodeGeneration = Rejected(value with { Continuation = token with { CodeGeneration = token.CodeGeneration + 1 } }),
                WrongGameStateEpoch = Rejected(value with { GameStateEpoch = value.GameStateEpoch + 1 }),
                WrongExecutionEpoch = Rejected(value with { ExecutionEpoch = value.ExecutionEpoch + 1 }),
                WrongSourceCatalogGeneration = Rejected(value with { SourceCatalogGeneration = value.SourceCatalogGeneration + 1 }),
            };
        }

        internal void RejectSourceReload()
        {
            if (inputBinding is null && !resumeScheduled) return;
            RevokePendingInput("source reload");
            owner.RevokeContextNeutralSource();
            sourceCatalogGeneration++;
            failed = true;
        }

        internal void RevokeForClose()
        {
            failed = true;
            pending = null;
            RevokePendingInput("close");
            owner.RevokeContextNeutralInput();
#if R0_F6G10C
            owner.ShutdownContextNeutral();
#endif
        }

        internal void BindInput(VmInputContinuation continuation, Action<VmSemanticValue> respond,
            PrototypeOpcode opcode, long timeLimit, long inputFlag)
        {
            if (!pumping || failed || inputBinding is not null)
                throw new InvalidOperationException("CompactStrict input binding rejected");
            var request = opcode switch
            {
                PrototypeOpcode.ONEINPUTS => new InputRequest { InputType = InputType.StrValue, OneInput = true },
                PrototypeOpcode.INPUT => new InputRequest { InputType = InputType.IntValue },
                PrototypeOpcode.TWAIT => new InputRequest { InputType = inputFlag == 0 ? InputType.EnterKey : InputType.Void },
                PrototypeOpcode.WAITANYKEY => new InputRequest { InputType = InputType.AnyKey },
                PrototypeOpcode.WAIT => new InputRequest { InputType = InputType.EnterKey },
                PrototypeOpcode.FORCEWAIT => new InputRequest { InputType = InputType.EnterKey, StopMesskip = true },
                _ => throw new NotSupportedException($"CompactStrict input blocked before publish: {opcode}"),
            };
            if (opcode == PrototypeOpcode.TWAIT || timeLimit > 0) request.Timelimit = timeLimit;
            inputBinding = new(request, continuation, respond, gameStateEpoch, executionEpoch,
                sourceCatalogGeneration, opcode);
            inputPublished = false;
            RecordInputTrace($"InputBound:{request.ID}:{continuation.RequestSerial}:{opcode}");
        }

        internal void PublishPendingInput()
        {
            if (failed || inputBinding is not { } binding || inputPublished) return;
            if (pumping) { inputPublishReentryCount++; throw new InvalidOperationException("input publication during Compact pump"); }
            if (!BindingCurrent(binding)) throw new InvalidOperationException("stale Compact input before publication");
            inputPublished = true;
            inputPublishCount++;
            RecordInputTrace($"InputPublished:{binding.Request.ID}:{binding.Continuation.RequestSerial}");
            process.console.WaitInput(binding.Request);
        }

        internal CompactInputDisposition AcceptInput(InputRequest request, VmSemanticValue value)
        {
            if (request.IsSystemInput) return CompactInputDisposition.NotCompact;
            if (inputBinding is not { } binding || !inputPublished || request.ID != binding.Request.ID ||
                !ReferenceEquals(request, binding.Request) || !BindingCurrent(binding) || !KindMatches(binding, value))
            {
                rejectedInputCount++;
                return CompactInputDisposition.Rejected;
            }
            var before = owner.ContextNeutralMachine.InputResumeCompleted;
            binding.Respond(value);
            inputBinding = null;
            inputPublished = false;
            if (owner.ContextNeutralMachine.InputResumeCompleted != before + 1)
            {
                failed = true;
                rejectedInputCount++;
                return CompactInputDisposition.Rejected;
            }
            acceptedInputCount++;
            resumeScheduled = true;
            RecordInputTrace($"InputAccepted:{request.ID}:{binding.Continuation.RequestSerial}");
            return CompactInputDisposition.Accepted;
        }

        private bool BindingCurrent(InputBinding binding)
        {
            if (binding.GameStateEpoch != gameStateEpoch || binding.ExecutionEpoch != executionEpoch ||
                binding.SourceCatalogGeneration != sourceCatalogGeneration ||
                binding.Continuation.Stamp != owner.ContextNeutralStamp ||
                !((IVmContextOwner)owner).TryGetCurrentContext(out var context, out var address)) return false;
            return address == binding.Continuation.Frame && context.FunctionId == binding.Continuation.FunctionId &&
                context.CodeSlot == binding.Continuation.CodeSlot && context.Generation == binding.Continuation.CodeGeneration;
        }

        private static bool KindMatches(InputBinding binding, VmSemanticValue value) =>
            binding.Continuation.ExpectedKind == VmInputValueKind.String && value.Kind == VmSemanticValueKind.String ||
            binding.Continuation.ExpectedKind == VmInputValueKind.Integer && value.Kind == VmSemanticValueKind.Integer;

        internal void RevokePendingInput(string reason)
        {
            if (inputBinding is not null || resumeScheduled) revokedInputCount++;
            inputBinding = null;
            inputPublished = false;
            resumeScheduled = false;
            RecordInputTrace("InputRevoked:" + reason);
        }

        internal VmStopReason RunWork()
        {
            if (!resumeScheduled) return RunPendingInvocation();
            resumeScheduled = false;
            resumeCount++;
            var stop = owner.ContextNeutralMachine.Continue(50_000_000);
            RecordInputTrace("CompactResumeStop:" + stop);
            lastStop = stop;
            if (stop == VmStopReason.Returned && process.state.isBegun)
            {
                RecordInputTrace("BeginApplyBoundary:" + process.state.R0F5BPendingBegin);
                process.state.Begin();
                executionEpoch++;
            }
            return stop;
        }

        internal object G10BEvidence(string checkpoint)
        {
            var machine = owner.ContextNeutralMachine;
            var addresses = owner.ContextNeutralFrameAddresses;
            var active = owner.ContextNeutralFrames.Select((frame, index) =>
            {
                var source = definitions[frame.FunctionId];
                var sourceLine = instructions.TryGetValue(frame.FunctionId, out var rows) && frame.Pc > 0 && frame.Pc - 1 < rows.Length
                    ? rows[frame.Pc - 1].SourceLine : -1;
                return new { frame.FunctionId, source.Name, source.RelativePath, HeaderLine = source.Line,
                    frame.Pc, SourceLine = sourceLine, FrameSerial = addresses[index].Serial };
            }).ToArray();
            var binding = inputBinding;
            var effects = owner.ContextNeutralEffects;
            var hashes = process.vEvaluator.GetDifferentialStateHashes();
            return new
            {
                Checkpoint = checkpoint,
                process.state.SystemState,
                PendingBegin = process.state.R0F5BPendingBegin.ToString(),
                StopReason = lastStop.ToString(),
                InputSuspended = lastStop == VmStopReason.WaitingForInput,
                ActiveFrames = active,
                EventCursor = owner.R0F6G7R2EventCursorEvidence,
                EventCursorActive = owner.R0F6G7R2EventCursorActive,
                OwnerId = owner.ContextNeutralStamp.OwnerId,
                OwnerEpoch = owner.ContextNeutralStamp.Epoch,
                VmIdentity = RuntimeHelpers.GetHashCode(machine),
                VmMachineCreationCount = machineCreations,
                EventSetInvocationCount = owner.EventSetInvocationCount,
                InputBinding = binding is null ? null : new
                {
                    binding.Request.ID,
                    InputType = binding.Request.InputType.ToString(),
                    binding.Request.OneInput,
                    binding.Request.IsSystemInput,
                    TimeLimit = binding.Request.Timelimit,
                    binding.Continuation.RequestSerial,
                    binding.Continuation.Stamp.OwnerId,
                    binding.Continuation.Stamp.Epoch,
                    binding.Continuation.Frame,
                    ExpectedKind = binding.Continuation.ExpectedKind.ToString(),
                    binding.GameStateEpoch,
                    binding.ExecutionEpoch,
                    binding.SourceCatalogGeneration,
                    binding.Continuation.CodeGeneration,
                    Published = inputPublished,
                },
                InputRequestCount = effects.InputRequests,
                InputPublishCount = inputPublishCount,
                AcceptedUserInputCount = acceptedInputCount,
                RejectedInputCount = rejectedInputCount,
                InputResultWriteCount = effects.InputResultWriteCount,
                InputResumeCount = resumeCount,
                InputPublishReentryCount = inputPublishReentryCount,
                InputRevocationCount = revokedInputCount,
                machine.InputResumeAttempts,
                machine.InputResumeCompleted,
                TypedHostOperations = effects.TypedOperations,
                TypedHostTrace = effects.TypedTrace.ToArray(),
                Buttons = effects.Buttons.ToArray(),
                State = hashes,
                Result = process.vEvaluator.RESULT,
                Results = process.vEvaluator.RESULTS,
                BodyCompiles = bodyCompiles,
                MaterializedFunctions = materializationTrace.Count,
                InvocationCounts = owner.R0F6G7R2InvocationCounts.Select(pair => new
                {
                    FunctionId = pair.Key,
                    definitions[pair.Key].Name,
                    definitions[pair.Key].RelativePath,
                    definitions[pair.Key].Line,
                    Count = pair.Value,
                }).ToArray(),
                LastExecuted = machine.LastExecutedFunctionId < 0 ? null : new
                {
                    FunctionId = machine.LastExecutedFunctionId,
                    definitions[machine.LastExecutedFunctionId].Name,
                    definitions[machine.LastExecutedFunctionId].RelativePath,
                    definitions[machine.LastExecutedFunctionId].Line,
                    machine.LastExecutedPc,
                    machine.LastTerminalFaultSourceLine,
                    Opcode = machine.LastExecutedOpcode.ToString(),
                },
                Fault = owner.ContextNeutralFaultSnapshot,
                ResidentFunctions = owner.ContextNeutralResidentCount,
                PendingLeases = owner.ContextNeutralPendingLeaseCount,
                PinAcquireCount = owner.ContextNeutralPinAcquireCount,
                PinReleaseCount = owner.ContextNeutralPinReleaseCount,
                OwnerTerminal = ((IVmContextOwner)owner).IsTerminal,
                GameStateEpoch = gameStateEpoch,
                ExecutionEpoch = executionEpoch,
                SourceCatalogGeneration = sourceCatalogGeneration,
                LegacyRetryAfterCompact = legacyRetryAfterCompact,
                ProductionBridgeUsed = productionBridgeUsed,
            };
        }
    }
}
#endif

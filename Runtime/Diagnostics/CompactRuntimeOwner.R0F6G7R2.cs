#if R0_F6G7R2
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal readonly record struct R0F6G7R2EventDefinition(RuntimeFunctionId Id, string Group,
    int GroupOrdinal, int DefinitionOrdinal, int PhysicalDefinitionId, bool Only, bool Single);

internal sealed partial class CompactRuntimeOwner
{
    private sealed class R0F6G7R2Cursor
    {
        internal required string EventSetId;
        internal required R0F6G7R2EventDefinition[] Definitions;
        internal required string HostContinuation;
        internal required string PendingBeginRelation;
        internal required long StateEpoch;
        internal int Index;
        internal VmFrameAddress CurrentFrame;
        internal bool Active;
    }

    private R0F6G7R2Cursor? r0f6g7r2Cursor;
    private int r0f6g7r2MachineCreationCount;
    private readonly Dictionary<int, long> r0f6g7r2InvocationCounts = [];

    internal int R0F6G7R2MachineCreationCount => r0f6g7r2MachineCreationCount;
    internal IReadOnlyDictionary<int, long> R0F6G7R2InvocationCounts => r0f6g7r2InvocationCounts;
    internal bool R0F6G7R2EventCursorActive => r0f6g7r2Cursor?.Active == true;
    internal object? R0F6G7R2EventCursorEvidence => r0f6g7r2Cursor is not { } cursor ? null : new
    {
        cursor.EventSetId,
        Group = cursor.Definitions[cursor.Index].Group,
        cursor.Definitions[cursor.Index].DefinitionOrdinal,
        cursor.Definitions[cursor.Index].PhysicalDefinitionId,
        CurrentFrameDepth = cursor.CurrentFrame.Depth,
        CurrentFrameSerial = cursor.CurrentFrame.Serial,
        cursor.HostContinuation,
        cursor.PendingBeginRelation,
        OwnerId = ContextNeutralStamp.OwnerId,
        OwnerEpoch = ContextNeutralStamp.Epoch,
        cursor.StateEpoch,
        cursor.Active,
    };

    internal VmStopReason RunR0F6G7R2Event(string eventSetId,
        IReadOnlyList<R0F6G7R2EventDefinition> definitions, string hostContinuation,
        string pendingBeginRelation, long stateEpoch, int maxSteps)
    {
        if (R0F6G7R2StartRejection(r0f6g7r2Cursor?.Active == true, ContextNeutralFrameDepth, definitions.Count) is { } rejection)
            throw new InvalidOperationException(rejection);
        var ordered = definitions.OrderBy(value => value.GroupOrdinal)
            .ThenBy(value => value.DefinitionOrdinal).ToArray();
        r0f6g7r2Cursor = new()
        {
            EventSetId = eventSetId,
            Definitions = ordered,
            HostContinuation = hostContinuation,
            PendingBeginRelation = pendingBeginRelation,
            StateEpoch = stateEpoch,
            Active = true,
        };
        r0f6g7r2MachineCreationCount++;
        var machine = ContextNeutralMachine;
        var start = machine.Start(ordered[0].Id, FunctionKind.Event);
        if (start != VmStopReason.Returned) return start;
        if (!ContextNeutralStorage.TryGetCurrentContext(out _, out var address))
            throw new InvalidOperationException("event root frame missing after commit");
        r0f6g7r2Cursor.CurrentFrame = address;
        return machine.Continue(maxSteps);
    }

    private bool R0F6G7R2CommitFrame(ref VmAdmissionLease lease, ReadOnlySpan<VmSemanticValue> actuals, out VmFrameAddress address)
    {
        var target = lease.Target;
        var committed = ContextNeutralStorage.CommitFrame(ref lease, actuals, out address);
        if (committed)
            r0f6g7r2InvocationCounts[target.Value] = r0f6g7r2InvocationCounts.GetValueOrDefault(target.Value) + 1;
        return committed;
    }

    private static string? R0F6G7R2StartRejection(bool active, int frameDepth, int definitionCount) =>
        active || frameDepth != 0 ? "active event reentry rejected" : definitionCount == 0 ? "event set has no definitions" : null;

    private static int R0F6G7R2NextDefinition(IReadOnlyList<R0F6G7R2EventDefinition> definitions, int current, VmSemanticValue result)
    {
        var completed = definitions[current];
        if (completed.Only) return -1;
        var next = current + 1;
        if (completed.Single && result.TryGetInteger(out var value) && value == 1)
            while (next < definitions.Count && definitions[next].GroupOrdinal == completed.GroupOrdinal) next++;
        return next < definitions.Count ? next : -1;
    }

    internal object R0F6G7R2EventMatrixEvidence()
    {
        static R0F6G7R2EventDefinition D(int id, string group, int groupOrdinal, int ordinal, bool only = false, bool single = false) =>
            new(new(id), group, groupOrdinal, ordinal, id, only, single);
        var baseRows = new[] { D(1, "Pri", 1, 1, single: true), D(2, "Pri", 1, 2), D(3, "Normal", 2, 3), D(4, "Later", 3, 4) };
        var duplicate = new[] { D(7, "Pri", 1, 1), D(7, "Later", 3, 1) };
        var rows = new[]
        {
            new { Case = "PRI/LATER order", Pass = baseRows.Select(value => value.Group).SequenceEqual(["Pri", "Pri", "Normal", "Later"]) },
            new { Case = "ONLY", Pass = R0F6G7R2NextDefinition([D(1, "Only", 0, 1, only: true), D(2, "Pri", 1, 2)], 0, VmSemanticValue.From(0L)) == -1 },
            new { Case = "SINGLE return0", Pass = R0F6G7R2NextDefinition(baseRows, 0, VmSemanticValue.From(0L)) == 1 },
            new { Case = "SINGLE return1", Pass = R0F6G7R2NextDefinition(baseRows, 0, VmSemanticValue.From(1L)) == 2 },
            new { Case = "SINGLE return2", Pass = R0F6G7R2NextDefinition(baseRows, 0, VmSemanticValue.From(2L)) == 1 },
            new { Case = "PRI+LATER duplicate", Pass = duplicate.Length == 2 && duplicate[0].PhysicalDefinitionId == duplicate[1].PhysicalDefinitionId },
            new { Case = "no definitions rejected", Pass = R0F6G7R2StartRejection(false, 0, 0) == "event set has no definitions" },
            new { Case = "active event reentry rejected", Pass = R0F6G7R2StartRejection(true, 1, 1) == "active event reentry rejected" },
        };
        return new { Count = rows.Length, AllPassed = rows.All(value => value.Pass), Rows = rows };
    }

    private void R0F6G7R2AfterFrameReturn(VmOwnerReturn result)
    {
        var cursor = r0f6g7r2Cursor;
        if (cursor is null || !cursor.Active || ContextNeutralStorage.FrameDepth != cursor.CurrentFrame.Depth) return;
        var completed = cursor.Definitions[cursor.Index];
        if (completed.Only)
        {
            cursor.Active = false;
            return;
        }
        var next = R0F6G7R2NextDefinition(cursor.Definitions, cursor.Index, result.Value);
        if (next < 0)
        {
            cursor.Active = false;
            return;
        }
        cursor.Index = next;
        var selected = cursor.Definitions[next];
        var lease = ContextNeutralStorage.PrepareInvocation(selected.Id, VmReturnKind.Normal, FunctionKind.Event);
        if (!lease.Ready || !R0F6G7R2CommitFrame(ref lease, [], out var address))
        {
            ContextNeutralStorage.Cancel(ref lease);
            ContextNeutralStorage.TerminalFault(VmStopReason.SemanticNotAvailable, selected.Id.Value, -1,
                "event definition admission failed");
            return;
        }
        cursor.CurrentFrame = address;
    }
}
#endif

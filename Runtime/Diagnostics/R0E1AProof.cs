#if R0_E1A
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal enum R0E1AGuard
{
    LoadErbDir, LoadErbList, LegacyErbLoad, LegacyScriptParse, HydrateLazyFile,
    FunctionLabelLineConstruction, InstructionLineConstruction, OtherLogicalLineConstruction,
    AddLabel, PrepareNextRuntimeProductionProgram, ProductionBridgeDispatch,
    LegacyMethodExecution, DoScript, IntoFunction, UserFunctionResolution,
    UserDefinedMethodTermConstruction,
    CalledFunctionConstruction
}

internal sealed record R0E1ABoundary(int LabelCount, int FunctionFrames, bool CurrentLineNull,
    bool NextRuntimeEnabled, bool DifferentialCheckpointCapture);
internal sealed record R0E1ACodecObservation(string Domain, string? Name, string SaveType,
    bool Resolved, bool Compatible);

internal static class R0E1AProof
{
    internal static bool Active;
    internal static readonly long[] Counters = new long[Enum.GetValues<R0E1AGuard>().Length];
    internal static R0E1ABoundary? Boundary;
    private static readonly List<R0E1ACodecObservation> CodecRows = [];

    internal static void Reset(bool active)
    {
        Array.Clear(Counters);
        lock (CodecRows) CodecRows.Clear();
        Boundary = null;
        Active = active;
    }

    internal static void Hit(R0E1AGuard guard)
    {
        if (!Active) return;
        Interlocked.Increment(ref Counters[(int)guard]);
        throw new InvalidOperationException("R0-E1A forbidden path: " + guard);
    }

    internal static void CaptureBoundary(int labels, int frames, bool currentLineNull,
        bool nextRuntime, bool checkpointCapture)
    {
        Boundary = new(labels, frames, currentLineNull, nextRuntime, checkpointCapture);
        if (labels != 0 || frames != 0 || !currentLineNull || nextRuntime || checkpointCapture)
            throw new InvalidOperationException("R0-E1A HostHeadersReady invariant failed");
    }

    internal static void ObserveCodec(string domain, string? name, EraSaveDataType saveType,
        VariableToken? token, object? destination = null)
    {
        if (!Active && !Program.R0E1AControl) return;
        var marker = saveType is EraSaveDataType.Separator or EraSaveDataType.EOC or EraSaveDataType.EOF;
        var resolved = marker || token is not null || destination is not null;
        var compatible = marker || destination switch
        {
            long => saveType == EraSaveDataType.Int,
            string => saveType == EraSaveDataType.Str,
            long[] => saveType == EraSaveDataType.IntArray,
            long[,] => saveType == EraSaveDataType.IntArray2D,
            long[,,] => saveType == EraSaveDataType.IntArray3D,
            string[] => saveType == EraSaveDataType.StrArray,
            string[,] => saveType == EraSaveDataType.StrArray2D,
            string[,,] => saveType == EraSaveDataType.StrArray3D,
            not null => false,
            null when token is null => false,
            _ => saveType switch
            {
                EraSaveDataType.Int => token!.IsInteger && token.Dimension == 0,
                EraSaveDataType.Str => token!.IsString && token.Dimension == 0,
                EraSaveDataType.IntArray => token!.IsInteger && token.Dimension == 1,
                EraSaveDataType.IntArray2D => token!.IsInteger && token.Dimension == 2,
                EraSaveDataType.IntArray3D => token!.IsInteger && token.Dimension == 3,
                EraSaveDataType.StrArray => token!.IsString && token.Dimension == 1,
                EraSaveDataType.StrArray2D => token!.IsString && token.Dimension == 2,
                EraSaveDataType.StrArray3D => token!.IsString && token.Dimension == 3,
                _ => false
            }
        };
        lock (CodecRows) CodecRows.Add(new(domain, name, saveType.ToString(), resolved, compatible));
    }

    internal static object GuardSnapshot() => Enum.GetValues<R0E1AGuard>()
        .Select(value => new { Name = value.ToString(), Count = Counters[(int)value] }).ToArray();
    internal static R0E1ACodecObservation[] CodecSnapshot()
    { lock (CodecRows) return CodecRows.ToArray(); }
#if R0_E2
    internal static (int Rows, int UnknownOrMismatch) CodecCounts()
    {
        lock (CodecRows) return (CodecRows.Count, CodecRows.Count(row => !row.Resolved || !row.Compatible));
    }
#endif
}
#endif

#if R0_E1B
#nullable enable
using System;
using System.Runtime.CompilerServices;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal enum CompactSourceType : byte { Integer, String, Reference }
internal enum CompactConversion : byte { None, IntToString }

internal readonly record struct CompactArgumentPlan(sbyte SourceIndex, short DestinationCell,
    CompactSourceType DestinationType, bool Omitted, CompactConversion Conversion,
    long DefaultInteger, string? DefaultString);

[InlineArray(4)]
internal struct CompactArgumentPlans { private CompactArgumentPlan first; }

// The descriptor owns no producer, Legacy call object, function name, path, or heap argument array.
internal struct CompactBoundCallsite
{
    private readonly CompactFunctionHandle target;
    private readonly byte destinationCount;
    private readonly byte sourceCount;
    private readonly CompactSourceType returnType;
    private CompactArgumentPlans plans;

    private CompactBoundCallsite(CompactFunctionHandle target, QueryParameter[] parameters,
        ReadOnlySpan<CompactSourceType> sourceTypes, bool optional, bool autoConvert)
    {
        this.target = target;
        destinationCount = checked((byte)parameters.Length);
        sourceCount = checked((byte)sourceTypes.Length);
        returnType = CompactSourceType.Integer;
        plans = default;
        for (var i = 0; i < parameters.Length; i++)
        {
            var destination = parameters[i];
            var destinationType = destination.String ? CompactSourceType.String : CompactSourceType.Integer;
            if (i >= sourceTypes.Length)
            {
                if (!destination.HasDefault && !optional)
                    throw new InvalidOperationException("Flat bind FAIL: required argument omitted");
                plans[i] = new(-1, checked((short)destination.Cell), destinationType, true,
                    CompactConversion.None, destination.Default.Integer, destination.Default.Text);
                continue;
            }
            var source = sourceTypes[i];
            if (source == CompactSourceType.Reference)
                throw new NotSupportedException("Flat bind FAIL: REF argument");
            var conversion = source == destinationType ? CompactConversion.None
                : source == CompactSourceType.Integer && destinationType == CompactSourceType.String
                    ? autoConvert ? CompactConversion.IntToString
                        : throw new InvalidOperationException("Flat bind FAIL: INT to STR autoconvert disabled")
                    : throw new InvalidOperationException("Flat bind FAIL: STR to INT");
            plans[i] = new(checked((sbyte)i), checked((short)destination.Cell), destinationType,
                false, conversion, 0, null);
        }
    }

    internal static CompactBoundCallsite Create(CompactFunctionHandle target, QueryParameter[]? parameters,
        ReadOnlySpan<CompactSourceType> sourceTypes, bool defaultsSupported = true)
    {
        if (parameters is null) throw new NotSupportedException("Flat bind FAIL: unresolved metadata");
        if (!defaultsSupported) throw new NotSupportedException("Flat bind FAIL: unsupported default");
        if (parameters.Length > 4 || sourceTypes.Length > parameters.Length)
            throw new InvalidOperationException("Flat bind FAIL: argument arity");
        return new(target, parameters, sourceTypes, RuntimeConfig.CompatiFuncArgOptional,
            RuntimeConfig.CompatiFuncArgAutoConvert);
    }

    internal QueryValue Execute(CompactRuntimeOwner expectedOwner, object host,
        ReadOnlySpan<QueryValue> sourceValues, int stepLimit = 1_000_000)
    {
        if (sourceValues.Length != sourceCount)
            throw new InvalidOperationException("Flat explicit FAIL: bound source arity");
        CompactArgumentFrame storage = default;
        Span<QueryValue> frame = storage;
        try
        {
            for (var i = 0; i < destinationCount; i++)
            {
                ref readonly var plan = ref plans[i];
                if (plan.Omitted)
                    frame[i] = plan.DestinationType == CompactSourceType.String
                        ? QueryValue.S(plan.DefaultString) : QueryValue.I(plan.DefaultInteger);
                else
                {
                    var value = sourceValues[plan.SourceIndex];
                    frame[i] = plan.Conversion == CompactConversion.IntToString
                        ? QueryValue.S(value.Integer.ToString()) : value;
                }
            }
            return target.Owner?.ExecuteBound(target, expectedOwner, host, frame[..destinationCount], stepLimit)
                ?? throw new InvalidOperationException("Flat explicit FAIL: default handle");
        }
        finally { frame.Clear(); }
    }

    internal void EnsureLive(CompactRuntimeOwner expectedOwner, object host)
    {
        if (target.Owner is null) throw new InvalidOperationException("Flat explicit FAIL: default handle");
        target.Owner.ValidateBoundLive(target, expectedOwner, host);
    }

    internal object Describe()
    {
        var rows = new object[destinationCount];
        for (var i = 0; i < destinationCount; i++)
        {
            ref readonly var plan = ref plans[i];
            rows[i] = new { plan.SourceIndex, plan.DestinationCell,
                DestinationType = plan.DestinationType.ToString(), plan.Omitted,
                Conversion = plan.Conversion.ToString() };
        }
        return new { TargetId = target.Id.Value, DestinationCount = destinationCount,
            SourceCount = sourceCount, ReturnType = returnType.ToString(), Plans = rows };
    }
}

[InlineArray(4)]
internal struct CompactArgumentFrame { private QueryValue first; }

internal sealed partial class CompactRuntimeOwner
{
    internal CompactBoundCallsite Bind(CompactFunctionHandle handle, CompactRuntimeOwner expectedOwner,
        object host, ReadOnlySpan<CompactSourceType> sourceTypes)
    {
        RequireLive(handle, expectedOwner, host);
        Materialize(handle.Id.Value);
        RequireLive(handle, expectedOwner, host);
        var entry = entries[handle.Id.Value];
        if (entry.State != EntryState.Ready) throw new InvalidOperationException("Flat bind FAIL: target not Ready");
        return CompactBoundCallsite.Create(handle, programs![entry.ProgramId].Parameters, sourceTypes);
    }

    internal QueryValue ExecuteBound(CompactFunctionHandle handle, CompactRuntimeOwner expectedOwner,
        object host, ReadOnlySpan<QueryValue> arguments, int stepLimit)
    {
        RequireLive(handle, expectedOwner, host);
        var entry = entries[handle.Id.Value];
        if (entry.State != EntryState.Ready) throw new InvalidOperationException("Flat explicit FAIL: target not Ready");
        activeCalls++;
        Attempts++;
        try
        {
            var value = context!.Execute(entry.ProgramId, arguments, stepLimit);
            Completed++;
            return value;
        }
        catch { Faults++; throw; }
        finally { activeCalls--; }
    }

    internal void ValidateBoundLive(CompactFunctionHandle handle, CompactRuntimeOwner expectedOwner, object host)
        => RequireLive(handle, expectedOwner, host);

    internal void BlockMaterializationForE1BSelfTest(string name)
    {
        var id = Handle(name).Id.Value;
        entries[id].State = EntryState.Blocked;
        entries[id].Blocker = "E1B synthetic admission failure";
    }
}
#endif

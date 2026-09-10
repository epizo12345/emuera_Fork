#if R0_E2
#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal readonly record struct R0E2RetainedEstimate(long SourceIndexBytes, long CatalogAndSignatureBytes,
    long QueryProgramBytes, long FlatContextBytes)
{
    internal long TotalBytes => SourceIndexBytes + CatalogAndSignatureBytes + QueryProgramBytes + FlatContextBytes;
}

internal sealed partial class CompactRuntimeOwner
{
    internal R0E2RetainedEstimate R0E2EstimateRetained()
    {
        static long Text(string? value) => value is null ? 0 : 24 + value.Length * 2L;
        long source = 24 + (sourceFiles?.Count ?? 0) * 8L;
        if (sourceFiles is not null)
            foreach (var file in sourceFiles)
                source += 128 + Text(file.FileIdentity) + Text(file.Error) + 24 + file.Functions.Count * 48L
                    + file.Functions.Sum(function => Text(function.Name))
                    + 24 + (file.ContinuationBlocks?.Count ?? 0) * 8L;
        long signatures = 160 + Specs.Length * 96L + Specs.Sum(spec => Text(spec.Name) + Text(spec.Path)
            + Text(spec.Fingerprint) + 24 + spec.Strings.Length + 24 + spec.Cells.Length * 4L);
        long catalogBytes = 128 + Catalog.Count * 16L + Catalog.CandidateIdCount * 4L
            + Catalog.NameRangeCount * 64L + Catalog.NameTable.Sum(Text);
        long code = 24 + (programs?.Length ?? 0) * 8L;
        if (programs is not null)
            foreach (var program in programs)
            {
                code += 112 + Text(program.Key) + 24 + program.Code.Length * Unsafe.SizeOf<QueryInstruction>()
                    + 24 + program.Text.Length * 8L + program.Text.Sum(Text)
                    + 24 + program.RegisterStrings.Length + 24 + program.Parameters.Length * Unsafe.SizeOf<QueryParameter>()
                    + 24 + program.Reads.Length * 8L + program.Reads.Sum(read => 48 + 24 + read.Indices.Length * 4L)
                    + 24 + program.Calls.Length * 8L + program.Calls.Sum(call => 40 + 24 + call.Arguments.Length * 4L)
                    + 24 + program.Builtins.Length * 8L + program.Builtins.Sum(builtin => 64 + 24 + builtin.Registers.Length * 4L
                        + 24 + builtin.Strings.Length + 24 + builtin.ReferenceIndices.Length * 4L);
                if (program.FastLeaf is not null)
                    code += 96 + 24 + program.FastLeaf.Code.Length * Unsafe.SizeOf<FlatInstruction>()
                        + 24 + program.FastLeaf.Strings.Length * 8L + program.FastLeaf.Strings.Sum(Text)
                        + 24 + program.FastLeaf.ReadSlots.Length * 8L + 24 + program.FastLeaf.Control.Length * Unsafe.SizeOf<MethodControl>();
            }
        return new(source, signatures + catalogBytes, code, context?.R0E2RetainedEstimate() ?? 0);
    }
}
#endif

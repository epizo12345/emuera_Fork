#if R0_D2
#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    // Diagnostic batch selection only; D1 and normal production defaults remain unchanged.
    private bool r0D2Direct;

    internal object RunR0D2Regression(string root, string phase)
    {
        r0D2Direct = true;
        return RunR0D1SelfTest(root, phase);
    }

    internal object RunR0D2Reload(string phase)
    {
        r0D2Direct = true;
        return RunR0D1ReloadTest(phase.Replace("r0d2", "r0d1", StringComparison.Ordinal));
    }

    [InlineArray(4)]
    private struct R0D2ArgumentFrame { private QueryValue first; }

    internal sealed partial class R0CRegistry
    {
        internal sealed partial class Binding
        {
            internal QueryParameter[] ParametersForD2Proof => program.Parameters;
            private void ValidateD2Arguments()
            {
                var native = registry.ids.Single(pair => pair.Value == ProgramId).Key.Arg;
                if (native.Length != program.Parameters.Length || native.Length > 4)
                    throw new InvalidOperationException("D2 scalar frame arity mismatch");
                for (var i = 0; i < native.Length; i++)
                    if (native[i].Identifier.IsReference || native[i].GetOperandType() !=
                        (program.Parameters[i].String ? typeof(string) : typeof(long)))
                        throw new InvalidOperationException("D2 native/compact scalar type mismatch");
            }

            internal void FillD2Arguments(Process caller, AExpression[] expressions, Span<QueryValue> frame)
            {
                for (var i = 0; i < program.Parameters.Length; i++)
                {
                    var expression = expressions[i];
                    frame[i] = program.Parameters[i].String
                        ? QueryValue.S(expression is null ? "" : expression.GetStrValue(caller.exm))
                        : QueryValue.I(expression is null ? 0 : expression.GetIntValue(caller.exm));
                }
            }

            private SingleTerm ExecuteDirectArguments(Process caller, UserDefinedMethodTerm term)
            {
                RequireLive(caller);
                R0D2ArgumentFrame storage = default;
                Span<QueryValue> frame = storage;
                try
                {
                    FillD2Arguments(caller, term.Argument.Arguments, frame);
                    RequireLive(caller);
                    registry.Attempts++;
                    PerformanceMetrics.RecordR0CFlatAttempt();
                    var oldGuard = FlatQueryProof.ForbidLegacy;
                    FlatQueryProof.ForbidLegacy = true;
                    try
                    {
                        var value = registry.context.Execute(ProgramId, frame[..program.Parameters.Length], registry.StepLimit);
                        var result = program.ReturnsString ? (SingleTerm)SingleStrTerm.FromValue(value.Text ?? "") : SingleLongTerm.FromValue(value.Integer);
                        registry.Completed++;
                        PerformanceMetrics.RecordR0CFlatComplete();
                        return result;
                    }
                    catch
                    {
                        registry.Faults++;
                        PerformanceMetrics.RecordR0CFlatFault();
                        throw;
                    }
                    finally { FlatQueryProof.ForbidLegacy = oldGuard; }
                }
                finally { frame.Clear(); }
            }

            // Batch-only diagnostics. Same fill loop as D2, with no core/attempt counters.
            internal long MeasureD2Arguments(Process caller, UserDefinedMethodTerm term)
            {
                R0D2ArgumentFrame storage = default;
                Span<QueryValue> frame = storage;
                try
                {
                    FillD2Arguments(caller, term.Argument.Arguments, frame);
                    long checksum = 0;
                    for (var i = 0; i < program.Parameters.Length; i++)
                        checksum = unchecked(checksum * 31 + frame[i].Integer + (frame[i].Text?.Length ?? 0));
                    return checksum;
                }
                finally { frame.Clear(); }
            }

            internal long MeasureD1Arguments(Process caller, UserDefinedMethodTerm term)
            {
                term.Argument.SetTransporter(caller.exm);
                long checksum = 0;
                for (var i = 0; i < program.Parameters.Length; i++)
                {
                    registry.arguments[i] = program.Parameters[i].String
                        ? QueryValue.S(term.Argument.TransporterStr[i]) : QueryValue.I(term.Argument.TransporterInt[i]);
                    checksum = unchecked(checksum * 31 + registry.arguments[i].Integer + (registry.arguments[i].Text?.Length ?? 0));
                }
                return checksum;
            }
        }
    }
}
#endif

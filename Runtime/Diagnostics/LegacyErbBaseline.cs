#if LEGACY_ORACLE
namespace MinorShift.Emuera.Runtime.Diagnostics;

internal readonly record struct LegacyErbBaseline(
    double ElapsedMilliseconds,
    long AllocatedBytes,
    long ManagedBefore,
    long ManagedImmediatelyAfter,
    long ManagedAfterDiagnosticGc);
#endif

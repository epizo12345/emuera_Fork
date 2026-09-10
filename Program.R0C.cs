#if R0_C
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera;

static partial class Program
{
    internal static string R0CMode { get; private set; } = "LegacyControl";
    internal static string? R0CRegistryManifestPath { get; private set; }
    internal static bool R0CFlatCandidate => R0CMode == "FlatCandidate";
    internal static bool R0CDeterministic { get; private set; }
    internal const int R0CDeterministicSeed = 20260906;
    internal const string R0CDeterministicClockBase = "2026-09-06T12:00:00";
    internal const long R0CDeterministicClockStepMs = 7;

    private static string[] ConfigureR0C(string[] args)
    {
        var kept = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--R0CDeterministic")
            {
                R0CDeterministic = true;
                continue;
            }
            if (args[i] is not ("--R0CMode" or "--R0CRegistryManifest"))
            {
                kept.Add(args[i]);
                continue;
            }
            if (++i >= args.Length)
                throw new ArgumentException(args[i - 1] + " requires a value");
            if (args[i - 1] == "--R0CMode")
                R0CMode = args[i];
            else
                R0CRegistryManifestPath = Path.GetFullPath(args[i]);
        }
        if (R0CMode is not ("LegacyControl" or "FlatCandidate"))
            throw new ArgumentException("--R0CMode must be LegacyControl or FlatCandidate");
        if (R0CFlatCandidate && string.IsNullOrWhiteSpace(R0CRegistryManifestPath))
            throw new ArgumentException("FlatCandidate requires --R0CRegistryManifest");
        if (R0CFlatCandidate && kept.Exists(value => value == "--NextRuntime"))
            throw new ArgumentException("R0-C and production NextRuntime cannot be enabled together");
        if (R0CDeterministic && kept.Exists(value => value is "--NextRuntime" or "--NextRuntimeDifferentialCapture"))
            throw new ArgumentException("R0-C deterministic mode forbids NextRuntime and differential capture");
        return kept.ToArray();
    }

    internal static void ConfigureR0CHeadless(string root, string phase)
    {
        R0CMode = phase.StartsWith("r0cselftest-", StringComparison.Ordinal) || phase.StartsWith("r0c2benchmark-", StringComparison.Ordinal)
            ? "FlatCandidate" : "LegacyControl";
#if R0_D1
        if (phase.StartsWith("r0d1", StringComparison.Ordinal)) R0CMode = "FlatCandidate";
#endif
#if R0_D2
        if (phase.StartsWith("r0d2", StringComparison.Ordinal)) R0CMode = "FlatCandidate";
#endif
        R0CRegistryManifestPath = Path.Combine(root, "evidence", "r0c-registry.json");
        R0CDeterministic = phase.StartsWith("r0cdeterminism-", StringComparison.Ordinal);
        if (!R0CDeterministic) return;
        NextRuntimeDifferentialSeed = R0CDeterministicSeed;
        NextRuntimeDifferentialClockBase = R0CDeterministicClockBase;
        NextRuntimeDifferentialClockStepMs = R0CDeterministicClockStepMs;
        Runtime.Diagnostics.DifferentialDeterminism.Configure(true, NextRuntimeDifferentialSeed,
            NextRuntimeDifferentialClockBase, NextRuntimeDifferentialClockStepMs);
    }
}
#endif

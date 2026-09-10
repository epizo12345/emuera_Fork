#nullable enable
using System;
using System.Globalization;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal static class DifferentialDeterminism
{
    private static readonly object Gate = new();
    private static bool enabled;
    private static DateTime baseLocal;
    private static long stepMilliseconds;
    private static long observations;

    internal static bool Enabled => enabled;
#if R0_F1
    internal static long ObservationCount { get { lock (Gate) return observations; } }
#endif

    internal static bool Configure(bool captureEnabled, int? seed, string? baseInstant, long step)
    {
        lock (Gate)
        {
            enabled = false;
            baseLocal = default;
            stepMilliseconds = 0;
            observations = 0;
            if (!captureEnabled || seed is null || string.IsNullOrWhiteSpace(baseInstant)) return false;
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Differential clock step must be positive.");
            if (!DateTime.TryParse(baseInstant, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
                throw new ArgumentException("Invalid differential clock base; use an invariant date/time.", nameof(baseInstant));
            baseLocal = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            stepMilliseconds = step;
            enabled = true;
            return true;
        }
    }

    internal static int SelfTest()
    {
        static long[] Rng(int seed)
        {
            var random = new MTRandom(seed);
            return [random.NextInt64(1_000_000), random.NextInt64(1_000_000), random.NextInt64(1_000_000)];
        }
        static bool Same(long[] left, long[] right) => left.AsSpan().SequenceEqual(right);
        static DateTime[] Clock()
        {
            Configure(true, 7, "2026-01-01T00:00:00", 10);
            return [Now(), Now(), Now()];
        }

        var sameSeed = Same(Rng(7), Rng(7));
        var differentSeed = !Same(Rng(7), Rng(8));
        var extraRandomLeft = new MTRandom(7);
        var extraRandomRight = new MTRandom(7);
        _ = extraRandomLeft.NextInt64(1_000_000);
        _ = extraRandomLeft.NextInt64(1_000_000);
        _ = extraRandomRight.NextInt64(1_000_000);
        var extraRandomDiverges = extraRandomLeft.NextInt64(1_000_000) != extraRandomRight.NextInt64(1_000_000);
        var first = Clock();
        var second = Clock();
        var sameClock = first.AsSpan().SequenceEqual(second);
        var monotonic = first[0] < first[1] && first[1] < first[2];
        static (string time, string times, long millisecond, long second) Api(DateTime now) =>
            (now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
             now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
             now.Ticks / TimeSpan.TicksPerMillisecond,
             now.Ticks / TimeSpan.TicksPerSecond);
        var apiConsistency = Api(first[0]) == Api(first[0]);
        Configure(false, 7, "2026-01-01T00:00:00", 10);
        var disabled = !Enabled;
        Configure(true, 7, null, 10);
        var partial = !Enabled;
        Configure(true, 7, "2026-01-01T00:00:00", 10);
        var startup = Enabled;
        var extraClockA = Now();
        _ = Now();
        var extraClockB = Now();
        Configure(true, 7, "2026-01-01T00:00:00", 10);
        _ = Now();
        var alignedClock = Now();
        Configure(false, null, null, 1);
        var extraClockDiverges = extraClockA != extraClockB && alignedClock != extraClockB;
        Console.WriteLine($"DifferentialRngSameSeed={(sameSeed ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialRngDifferentSeed={(differentSeed ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialExtraRandomDivergence={(extraRandomDiverges ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialClockSameConfig={(sameClock ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialClockMonotonic={(monotonic ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialClockApiConsistency={(apiConsistency ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialDeterminismDisabledIsolation={(disabled && partial ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialStartupConfiguredBeforeScript={(startup ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialExtraClockDivergence={(extraClockDiverges ? "PASS" : "FAIL")}");
        Console.WriteLine($"DifferentialPartialConfigDoesNotSeedVariableEvaluator={(partial ? "PASS" : "FAIL")}");
        return sameSeed && differentSeed && extraRandomDiverges && sameClock && monotonic && apiConsistency && disabled && partial && startup && extraClockDiverges ? 0 : 1;
    }

    internal static DateTime Now()
    {
        if (!enabled) return DateTime.Now;
        lock (Gate) return baseLocal.AddMilliseconds(stepMilliseconds * observations++);
    }
}

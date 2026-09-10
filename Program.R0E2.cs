#if R0_E2
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;

namespace MinorShift.Emuera;

static partial class Program
{
    private static int RunR0E2(string root, string mode, string run)
    {
        root = Path.GetFullPath(root);
        var validMode = mode is "memory-common" or "memory-legacy" or "memory-graphfree"
            or "startup-legacy" or "startup-graphfree" or "execution-legacy" or "execution-graphfree";
        if (!(Path.GetFileName(root).StartsWith("CompactReplacementR0E2_", StringComparison.Ordinal)
                || Path.GetFileName(root).StartsWith("CompactReplacementR0E2S_", StringComparison.Ordinal))
            || !validMode || run.Length is < 1 or > 32 || run.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            return 64;
        Directory.CreateDirectory(Path.Combine(root, "raw"));
        var output = Path.Combine(root, "raw", mode + "-" + run);
        if (File.Exists(output + ".json") || File.Exists(output + ".failure.txt")) return 65;

        var candidate = mode is "memory-common" or "memory-graphfree" or "startup-graphfree" or "execution-graphfree";
        var legacy = !candidate;
        R0E1ACandidate = candidate;
        R0E1AControl = legacy;
        R0E1AProof.Reset(candidate);
        try
        {
            NextRuntimeMode = false;
            NextRuntimeDifferentialCapturePath = null;
            NextRuntimeDifferentialSeed = R0CDeterministicSeed;
            NextRuntimeDifferentialClockBase = R0CDeterministicClockBase;
            NextRuntimeDifferentialClockStepMs = R0CDeterministicClockStepMs;
            R0CDeterministic = true;
            if (!DifferentialDeterminism.Configure(true, NextRuntimeDifferentialSeed,
                    NextRuntimeDifferentialClockBase, NextRuntimeDifferentialClockStepMs))
                throw new InvalidOperationException("R0-E2 deterministic setup failed");

            SetDirPaths(Path.Combine(root, "Data"));
            ConfigData.Instance.LoadConfig();
            JSONConfig.Load();
            ApplicationConfiguration.Initialize();
            using var window = new Forms.MainWindow([]);
            SynchronizationContext.SetSynchronizationContext(null);
            if (candidate)
                Preload.Load(Config.GetFiles(ErbDir, "*.ERH").Select(path => path.Value)).GetAwaiter().GetResult();
            else
                Preload.Load(ErbDir).GetAwaiter().GetResult();
            Preload.Load(CsvDir).GetAwaiter().GetResult();
            FontFactory.LoadFontFolder();
            GlobalStatic.Console = window.R0B1Console;
            var process = new GameProc.Process(window.R0B1Console);
            GlobalStatic.Process = process;
            B1Proof.Active = legacy;
            using (var log = new StreamWriter(new FileStream(output + ".init.log", FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-E2 initialization failed");
            B1Proof.Active = false;
            var graphReadyMilliseconds = legacy ? R0E2Measurement.SinceB0Milliseconds : 0;
            Preload.Clear();

            object result;
            if (mode == "memory-common")
                result = process.RunR0E2CommonMemory(run);
            else
            {
                var saveStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var save = Path.Combine(root, "Data", "sav", "save219.sav");
                using (var stream = new FileStream(save, FileMode.Open, FileAccess.Read))
                using (var reader = EraBinaryDataReader.CreateReader(stream))
                {
                    if (reader is null) throw new InvalidOperationException("R0-E2 requires the binary save codec");
                    process.VEvaluator.LoadFromStreamBinary(reader);
                }
                process.SetR0E1ALoadNo(219);
                var saveMilliseconds = R0E2Measurement.Milliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - saveStart);
                result = mode switch
                {
                    "memory-legacy" => process.RunR0E2LegacyMemory(run, graphReadyMilliseconds, saveMilliseconds),
                    "memory-graphfree" => process.RunR0E2GraphFreeMemory(run, saveMilliseconds),
                    "startup-legacy" => process.RunR0E2LegacyStartup(run, graphReadyMilliseconds, saveMilliseconds),
                    "startup-graphfree" => process.RunR0E2GraphFreeStartup(run, saveMilliseconds),
                    "execution-legacy" => process.RunR0E2Execution(run, false),
                    "execution-graphfree" => process.RunR0E2Execution(run, true),
                    _ => throw new InvalidOperationException("R0-E2 mode")
                };
            }
            if (window.Visible || NextRuntimeMode || NextRuntimeDifferentialCapturePath is not null)
                throw new InvalidOperationException("R0-E2 GUI/Next isolation failed");
            B1Proof.WriteJson(output + ".json", result);
            return 0;
        }
        catch (Exception ex)
        {
            using var log = new StreamWriter(new FileStream(output + ".failure.txt", FileMode.CreateNew));
            log.Write(ex);
            return 1;
        }
        finally
        {
            Preload.Clear();
            B1Proof.Active = false;
            R0E1AProof.Active = false;
            R0E1ACandidate = false;
            R0E1AControl = false;
        }
    }
}
#endif

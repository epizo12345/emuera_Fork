#if R0_F1
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
    private static int RunR0F1(string root, string mode, string run)
    {
        root = Path.GetFullPath(root);
        if (!(Path.GetFileName(root).StartsWith("CompactReplacementR0F1_", StringComparison.Ordinal)
#if R0_F2
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F2_", StringComparison.Ordinal)
#if R0_F3
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F3_", StringComparison.Ordinal)
#if R0_F4A
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4A_", StringComparison.Ordinal)
#if R0_F4B
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4B_", StringComparison.Ordinal)
#if R0_F4C
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4C_", StringComparison.Ordinal)
#if R0_F4D1
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4D1_", StringComparison.Ordinal)
#if R0_F4D3
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4D3_", StringComparison.Ordinal)
#if R0_F4D4
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4D4_", StringComparison.Ordinal)
#if R0_F4D5
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4D5_", StringComparison.Ordinal)
#if R0_F4E1
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4E1_", StringComparison.Ordinal)
#if R0_F4E2
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4E2_", StringComparison.Ordinal)
#if R0_F4E3
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4E3_", StringComparison.Ordinal)
#if R0_F4F
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4F_", StringComparison.Ordinal)
#if R0_F4G1
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4G1_", StringComparison.Ordinal)
#if R0_F4G2
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4G2_", StringComparison.Ordinal)
#if R0_F4G3
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4G3_", StringComparison.Ordinal)
#if R0_F4G4
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F4G4_", StringComparison.Ordinal)
#if R0_F5A
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F5A_", StringComparison.Ordinal)
#if R0_F5A2
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F5A2_", StringComparison.Ordinal)
#if R0_F5B
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F5B_", StringComparison.Ordinal)
#if R0_F6
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6_", StringComparison.Ordinal)
#if R0_F6A
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6A_", StringComparison.Ordinal)
#if R0_F6B
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6B_", StringComparison.Ordinal)
#if R0_F6C
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6C_", StringComparison.Ordinal)
#if R0_F6D
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6D_", StringComparison.Ordinal)
#if R0_F6E
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6E_", StringComparison.Ordinal)
#if R0_F6F
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6F_", StringComparison.Ordinal)
#if R0_F6G3
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6G3_", StringComparison.Ordinal)
#endif
#if R0_F6G7R2
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6G7R2_", StringComparison.Ordinal)
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F6G8_", StringComparison.Ordinal)
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
#endif
            ) ||
            mode is not ("control" or "candidate" or "guard-negative") ||
            run.Length is < 1 or > 32 || run.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            return 64;
        Directory.CreateDirectory(Path.Combine(root, "raw"));
        var outputRun = R0F6CMode ? "f6c" : run;
#if R0_F6G3
        if (R0F6G3Mode) outputRun = "f6g3";
#endif
#if R0_F6G7R2
        if (R0F6G7R2Mode) outputRun = "f6g7r2";
#endif
#if R0_F6D
        if (R0F6DMode) outputRun = "f6d";
#if R0_F6E
        if (R0F6EMode) outputRun = "f6e";
#if R0_F6F
        if (R0F6FMode) outputRun = "f6f";
#endif
#endif
#endif
        var output = Path.Combine(root, "raw", $"{mode}-{outputRun}");
        if (File.Exists(output + ".json") || File.Exists(output + ".failure.txt")) return 65;
        var candidate = mode != "control";
        R0E1ACandidate = candidate;
        R0E1AControl = !candidate;
        R0E1AProof.Reset(candidate);
        try
        {
            if (mode == "guard-negative")
            {
                bool caught = false;
                try { _ = new Runtime.Script.Statements.FunctionLabelLine(null, "R0F1_NEGATIVE", new Runtime.Script.Parser.WordCollection()); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("FunctionLabelLineConstruction", StringComparison.Ordinal)) { caught = true; }
                B1Proof.WriteJson(output + ".json", new { Pass = caught, Counters = R0E1AProof.GuardSnapshot() });
                return caught ? 0 : 1;
            }

            NextRuntimeMode = false;
            NextRuntimeDifferentialCapturePath = null;
            NextRuntimeDifferentialSeed = R0CDeterministicSeed;
            NextRuntimeDifferentialClockBase = R0CDeterministicClockBase;
            NextRuntimeDifferentialClockStepMs = R0CDeterministicClockStepMs;
            R0CDeterministic = true;
            if (!DifferentialDeterminism.Configure(true, NextRuntimeDifferentialSeed,
                    NextRuntimeDifferentialClockBase, NextRuntimeDifferentialClockStepMs))
                throw new InvalidOperationException("R0-F1 deterministic setup failed");

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
            B1Proof.Active = !candidate;
            using (var log = new StreamWriter(new FileStream(output + ".init.log", FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-F1 initialization failed");
            B1Proof.Active = false;
            R0F6CDisplayCapture = R0F6CMode;
            if (R0F6CDisplayCapture) window.R0B1Console.PrintFlush(false);
            Preload.Clear();

            var sidecar = Path.Combine(root, "Data", "sav", "save219");
            global::Runtime.SQL.SQL.Load(sidecar);
            var save = Path.Combine(root, "Data", "sav", "save219.sav");
            using (var stream = new FileStream(save, FileMode.Open, FileAccess.Read))
            using (var reader = EraBinaryDataReader.CreateReader(stream))
            {
                if (reader is null) throw new InvalidOperationException("R0-F1 requires the binary save codec");
                process.VEvaluator.LoadFromStreamBinary(reader);
            }
            process.SetR0E1ALoadNo(219);
            var result =
#if R0_F4D4
                run == "f4d4" ? process.RunR0F4D4(candidate, root) :
#endif
                process.RunR0F1(candidate, run is "f4d5" or "f4e1" or "f4e2" or "f4e3" or "f4f" or "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d" ? "actual" : run,
                    run is "f4d5" or "f4e1" or "f4e2" or "f4e3" or "f4f" or "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4e1" or "f4e2" or "f4e3" or "f4f" or "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d",
                    run is "f4e2" or "f4e3" or "f4f" or "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4e3" or "f4f" or "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4f" or "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4g1" or "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4g2" or "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4g3" or "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f4g4" or "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f5a" or "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f5a2" or "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f5b" or "f6" or "f6a" or "f6b" or "f6d", run is "f6" or "f6a" or "f6b" or "f6d", run is "f6a" or "f6b" or "f6d", run is "f6b" or "f6d");
            if (window.Visible || NextRuntimeMode || NextRuntimeDifferentialCapturePath is not null)
                throw new InvalidOperationException("R0-F1 GUI/Next isolation failed");
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
            R0F6CDisplayCapture = false;
            Preload.Clear();
            B1Proof.Active = false;
            R0E1AProof.Active = false;
            R0E1ACandidate = false;
            R0E1AControl = false;
        }
    }
}
#endif

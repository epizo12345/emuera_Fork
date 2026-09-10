#if R0_E1A
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
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;

namespace MinorShift.Emuera;

static partial class Program
{
    internal static bool R0E1ACandidate { get; private set; }
    internal static bool R0E1AControl { get; private set; }

    private static int RunR0E1A(string root, string mode)
    {
        root = Path.GetFullPath(root);
        var validRoot = Path.GetFileName(root).StartsWith("CompactReplacementR0E1A_", StringComparison.Ordinal)
#if R0_E1B
            || Path.GetFileName(root).StartsWith("CompactReplacementR0E1B_", StringComparison.Ordinal)
#endif
#if R0_E2
            || Path.GetFileName(root).StartsWith("CompactReplacementR0E2_", StringComparison.Ordinal)
#if R0_E2S
            || Path.GetFileName(root).StartsWith("CompactReplacementR0E2S_", StringComparison.Ordinal)
#endif
#endif
#if R0_F1
            || Path.GetFileName(root).StartsWith("CompactReplacementR0F1_", StringComparison.Ordinal)
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
#endif
#endif
#endif
#endif
#endif
#endif
#endif
            ;
        if (!validRoot || mode is not ("candidate" or "control" or "guard-negative"
#if R0_E1B
            or "callsite-candidate" or "callsite-control" or "bind-negative"
#endif
            )) return 64;
        Directory.CreateDirectory(Path.Combine(root, "raw"));
        Directory.CreateDirectory(Path.Combine(root, "evidence"));
        var output = Path.Combine(root, "raw", mode);
        if (File.Exists(output + ".json") || File.Exists(output + ".failure.txt")) return 65;
        R0E1ACandidate = mode is "candidate" or "callsite-candidate" or "bind-negative";
        R0E1AControl = mode is "control" or "callsite-control";
        R0E1AProof.Reset(R0E1ACandidate);
        try
        {
            if (mode == "guard-negative")
            {
                R0E1AProof.Reset(true);
                bool caught = false;
                try { _ = new FunctionLabelLine(null, "R0E1A_NEGATIVE", new WordCollection()); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("FunctionLabelLineConstruction", StringComparison.Ordinal)) { caught = true; }
                var rows = R0E1AProof.GuardSnapshot();
                B1Proof.WriteJson(output + ".json", new { Pass = caught, ActualConstructorGuardCaught = caught, Counters = rows });
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
                throw new InvalidOperationException("R0-E1A deterministic setup failed");

            SetDirPaths(Path.Combine(root, "Data"));
            ConfigData.Instance.LoadConfig();
            JSONConfig.Load();
            ApplicationConfiguration.Initialize();
            using var window = new Forms.MainWindow([]);
            SynchronizationContext.SetSynchronizationContext(null);
            if (R0E1ACandidate)
            {
                var headers = Config.GetFiles(ErbDir, "*.ERH").Select(path => path.Value);
                Preload.Load(headers).GetAwaiter().GetResult();
            }
            else Preload.Load(ErbDir).GetAwaiter().GetResult();
            Preload.Load(CsvDir).GetAwaiter().GetResult();
            FontFactory.LoadFontFolder();
            GlobalStatic.Console = window.R0B1Console;
            var process = new GameProc.Process(window.R0B1Console);
            GlobalStatic.Process = process;
            B1Proof.Active = R0E1AControl;
            using (var log = new StreamWriter(new FileStream(output + ".init.log", FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-E1A initialization failed");
            B1Proof.Active = false;
            Preload.Clear();
            var save = Path.Combine(root, "Data", "sav", "save219.sav");
            using (var stream = new FileStream(save, FileMode.Open, FileAccess.Read))
            using (var reader = EraBinaryDataReader.CreateReader(stream))
            {
                if (reader is null) throw new InvalidOperationException("R0-E1A requires the binary save codec");
                process.VEvaluator.LoadFromStreamBinary(reader);
            }
            process.SetR0E1ALoadNo(219);
            object result = mode switch
            {
#if R0_E1B
                "callsite-candidate" => process.RunR0E1BMatrix(true),
                "callsite-control" => process.RunR0E1BMatrix(false),
                "bind-negative" => process.RunR0E1BBindNegatives(),
#endif
                _ => process.RunR0E1A(R0E1ACandidate)
            };
            if (window.Visible || NextRuntimeMode || NextRuntimeDifferentialCapturePath is not null)
                throw new InvalidOperationException("R0-E1A GUI/Next isolation failed");
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

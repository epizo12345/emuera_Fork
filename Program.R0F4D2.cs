#if R0_F4D2
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;

namespace MinorShift.Emuera;

static partial class Program
{
    private static int RunR0F4D2(string root, string mode)
    {
        root = Path.GetFullPath(root);
        if (!Path.GetFileName(root).StartsWith("CompactReplacementR0F4D2_", StringComparison.Ordinal)
            || mode is not ("control" or "candidate"))
            return 64;
        var output = Path.Combine(root, "result.json");
        var failure = Path.Combine(root, "failure.txt");
        if (File.Exists(output) || File.Exists(failure)) return 65;
        var candidate = mode == "candidate";
        R0E1ACandidate = candidate;
        R0E1AControl = !candidate;
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
                throw new InvalidOperationException("R0-F4D2 deterministic setup failed");

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
            using (var log = new StreamWriter(new FileStream(Path.Combine(root, "initialize.log"), FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-F4D2 initialization failed");
            B1Proof.Active = false;
            Preload.Clear();

            var result = process.RunR0F4D2(candidate, root);
            if (window.Visible || NextRuntimeMode || NextRuntimeDifferentialCapturePath is not null)
                throw new InvalidOperationException("R0-F4D2 GUI/Next isolation failed");
            B1Proof.WriteJson(output, result);
            return 0;
        }
        catch (Exception ex)
        {
            using var log = new StreamWriter(new FileStream(failure, FileMode.CreateNew));
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

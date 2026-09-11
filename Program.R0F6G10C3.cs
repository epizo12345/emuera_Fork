#if R0_F6G10C3
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;

namespace MinorShift.Emuera;

static partial class Program
{
    internal static bool R0F6G10C3PauseAfterIslandClear;
    internal static bool R0F6G10C3IslandClearObserved;

    private static int RunR0F6G10C3(string root, string outputPath, string mode)
    {
        root = Path.GetFullPath(root);
        outputPath = Path.GetFullPath(outputPath);
        var legacy = mode is "LegacyD9" or "LegacyNumSummoner" or "LegacyHtmlOracle" or "LegacyPostH";
        if (!IsCompactProductionProofRoot(root) || File.Exists(outputPath) ||
            mode is not ("LegacyD9" or "CompactD9" or "LegacyNumSummoner" or "CompactNumSummoner" or
                "LegacyHtmlOracle" or "CompactHtmlOracle" or "LegacyPostH" or "CompactPostH")) return 64;
        try
        {
            RuntimeMode = legacy ? ScriptRuntimeMode.Legacy : ScriptRuntimeMode.CompactStrict;
            R0F6G10BMode = !legacy;
            R0F6G10AHeadlessCapture = true;
            R0E1ACandidate = !legacy;
            NextRuntimeMode = false;
            DebugMode = false;
            AnalysisMode = false;
            NextRuntimeDifferentialSeed = R0CDeterministicSeed;
            NextRuntimeDifferentialClockBase = R0CDeterministicClockBase;
            NextRuntimeDifferentialClockStepMs = R0CDeterministicClockStepMs;
            if (!DifferentialDeterminism.Configure(true, NextRuntimeDifferentialSeed,
                    NextRuntimeDifferentialClockBase, NextRuntimeDifferentialClockStepMs))
                throw new InvalidOperationException("R0-F6G10C3 deterministic setup failed");
            SetDirPaths(Path.Combine(root, "Data"));
            ConfigData.Instance.LoadConfig();
            JSONConfig.Load();
            ApplicationConfiguration.Initialize();
            using var window = new Forms.MainWindow([]);
            _ = window.Handle;
            SynchronizationContext.SetSynchronizationContext(null);
            if (legacy)
                Preload.Load(ErbDir).GetAwaiter().GetResult();
            else
                Preload.Load(Config.GetFiles(ErbDir, "*.ERH").Select(path => path.Value)).GetAwaiter().GetResult();
            Preload.Load(CsvDir).GetAwaiter().GetResult();
            FontFactory.LoadFontFolder();
            GlobalStatic.Console = window.R0B1Console;
            var process = new GameProc.Process(window.R0B1Console);
            GlobalStatic.Process = process;
            window.R0B1Console.R0F6G10BAttachProcess(process);
            window.R0B1Console.R0F6G10ASetHeadlessPaintSuppressed(true);
            using (var log = new StreamWriter(new FileStream(Path.ChangeExtension(outputPath, ".init.log"), FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-F6G10C3 initialization failed");
            Preload.Clear();
            B1Proof.WriteJson(outputPath, process.R0F6G10C3Run(mode));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.ChangeExtension(outputPath, ".failure.txt"), ex.ToString());
            return 1;
        }
        finally
        {
            R0F6G10C3PauseAfterIslandClear = false;
            R0F6G10C3IslandClearObserved = false;
            DifferentialDeterminism.Configure(false, null, null, 1);
            R0F6G10BMode = false;
            R0F6G10AHeadlessCapture = false;
            R0E1ACandidate = false;
            B1Proof.Active = false;
            Preload.Clear();
        }
    }
}
#endif

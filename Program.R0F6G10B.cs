#if R0_F6G10B
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
    internal static bool R0F6G10BMode { get; private set; }

    private static int RunR0F6G10B(string root, string outputPath, string mode)
    {
        root = Path.GetFullPath(root);
        outputPath = Path.GetFullPath(outputPath);
        if (!IsCompactProductionProofRoot(root) ||
            File.Exists(outputPath) || mode is not ("D9" or "H" or "Invalid" or "WriteFail" or "Reset" or "Load" or "Reload" or "Close")) return 64;
        try
        {
            R0F6G10BMode = true;
            R0F6G10AHeadlessCapture = true;
            RuntimeMode = ScriptRuntimeMode.CompactStrict;
            R0E1ACandidate = true;
            NextRuntimeMode = false;
            DebugMode = false;
            AnalysisMode = false;
            NextRuntimeDifferentialSeed = R0CDeterministicSeed;
            NextRuntimeDifferentialClockBase = R0CDeterministicClockBase;
            NextRuntimeDifferentialClockStepMs = R0CDeterministicClockStepMs;
            if (!DifferentialDeterminism.Configure(true, NextRuntimeDifferentialSeed,
                    NextRuntimeDifferentialClockBase, NextRuntimeDifferentialClockStepMs))
                throw new InvalidOperationException("R0-F6G10B deterministic setup failed");
            SetDirPaths(Path.Combine(root, "Data"));
            ConfigData.Instance.LoadConfig();
            JSONConfig.Load();
            ApplicationConfiguration.Initialize();
            using var window = new Forms.MainWindow([]);
            window.R0B1Console.R0F6G10ASetHeadlessPaintSuppressed(true);
            SynchronizationContext.SetSynchronizationContext(null);
            Preload.Load(Config.GetFiles(ErbDir, "*.ERH").Select(path => path.Value)).GetAwaiter().GetResult();
            Preload.Load(CsvDir).GetAwaiter().GetResult();
            FontFactory.LoadFontFolder();
            GlobalStatic.Console = window.R0B1Console;
            var process = new GameProc.Process(window.R0B1Console);
            GlobalStatic.Process = process;
            window.R0B1Console.R0F6G10BAttachProcess(process);
            using (var log = new StreamWriter(new FileStream(Path.ChangeExtension(outputPath, ".init.log"), FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-F6G10B CompactStrict initialization failed");
            Preload.Clear();
            B1Proof.WriteJson(outputPath, process.R0F6G10BRun(mode));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.ChangeExtension(outputPath, ".failure.txt"), ex.ToString());
            return 1;
        }
        finally
        {
            DifferentialDeterminism.Configure(false, null, null, 1);
            R0F6G10BMode = false;
            R0F6G10AHeadlessCapture = false;
            R0E1ACandidate = false;
            Preload.Clear();
        }
    }
}
#endif

#if R0_F6G10A
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
    internal static bool R0F6G10AHeadlessCapture { get; private set; }

    private static bool IsCompactProductionProofRoot(string root)
    {
        var leaf = Path.GetFileName(root);
        if (leaf.StartsWith("CompactReplacementR0F6G10A_", StringComparison.Ordinal) ||
            leaf.StartsWith("CompactReplacementR0F6G10A1_", StringComparison.Ordinal) ||
            leaf.StartsWith("CompactReplacementR0F6G10B_", StringComparison.Ordinal) ||
            leaf.StartsWith("CompactReplacementR0F6G10C_", StringComparison.Ordinal) ||
            leaf.StartsWith("CompactReplacementR0F6G10C1_", StringComparison.Ordinal) ||
            leaf.StartsWith("CompactReplacementR0F6G10C2_", StringComparison.Ordinal) ||
            leaf.StartsWith("CompactReplacementR0F6G10C3_", StringComparison.Ordinal)) return true;
        return leaf == "USER_CANDIDATE" &&
            (Path.GetFileName(Path.GetDirectoryName(root)).StartsWith("CompactReplacementR0F6G10C_", StringComparison.Ordinal) ||
             Path.GetFileName(Path.GetDirectoryName(root)).StartsWith("CompactReplacementR0F6G10C1_", StringComparison.Ordinal) ||
             Path.GetFileName(Path.GetDirectoryName(root)).StartsWith("CompactReplacementR0F6G10C2_", StringComparison.Ordinal) ||
             Path.GetFileName(Path.GetDirectoryName(root)).StartsWith("CompactReplacementR0F6G10C3_", StringComparison.Ordinal));
    }

    private static int RunR0F6G10A(string root, string outputPath, string mode)
    {
        root = Path.GetFullPath(root);
        outputPath = Path.GetFullPath(outputPath);
        if (!IsCompactProductionProofRoot(root) || File.Exists(outputPath)) return 64;
        try
        {
            RuntimeMode = ScriptRuntimeMode.CompactStrict;
            R0E1ACandidate = true;
            NextRuntimeMode = false;
            DebugMode = false;
            AnalysisMode = false;
            SetDirPaths(Path.Combine(root, "Data"));
            ConfigData.Instance.LoadConfig();
            JSONConfig.Load();
            ApplicationConfiguration.Initialize();
            using var window = new Forms.MainWindow([]);
            _ = window.Handle;
            SynchronizationContext.SetSynchronizationContext(null);
            Preload.Load(Config.GetFiles(ErbDir, "*.ERH").Select(path => path.Value)).GetAwaiter().GetResult();
            Preload.Load(CsvDir).GetAwaiter().GetResult();
            FontFactory.LoadFontFolder();
            GlobalStatic.Console = window.R0B1Console;
            var process = new GameProc.Process(window.R0B1Console);
            GlobalStatic.Process = process;
            var initLog = Path.ChangeExtension(outputPath, ".init.log");
            using (var log = new StreamWriter(new FileStream(initLog, FileMode.CreateNew)))
                if (!process.Initialize(log).GetAwaiter().GetResult())
                    throw new InvalidOperationException("R0-F6G10A CompactStrict initialization failed");
            R0F6G10AHeadlessCapture = true;
            window.R0B1Console.R0F6G10ASetHeadlessPaintSuppressed(true);
            Preload.Clear();
            object result = mode.StartsWith("Admission:", StringComparison.Ordinal)
                ? new { Mode = mode, Admission = process.R0F6G10AProbeAdmission(mode[10..], false, true), Counters = process.R0F6G10AEvidence() }
                : mode switch
            {
                "Title" => new { Mode = mode, Path = process.R0F6G10ARunTitlePath(), Counters = process.R0F6G10AEvidence(),
                    HostWindowTitle = window.Text, ScriptWindowTitle = window.R0B1Console.GetWindowTitle(), GuiLaunched = false, UserInputSent = false },
                "EventFirst" => new { Mode = mode, Path = process.R0F6G10ARunEventFirstPath(), Counters = process.R0F6G10AEvidence(), GuiLaunched = false, UserInputSent = false },
                "LoadSuccess" => new { Mode = mode, Lifecycle = process.R0F6G10ARunLoadLifecycle(false), Counters = process.R0F6G10AEvidence(), GuiLaunched = false, UserInputSent = false },
                "LoadFailure" => new { Mode = mode, Lifecycle = process.R0F6G10ARunLoadLifecycle(true), Counters = process.R0F6G10AEvidence(), GuiLaunched = false, UserInputSent = false },
                "Probe" => new
                {
                    Status = "PARTIAL_PASS",
                    Oracles = process.R0F6G10ARunFocusedOracles(),
                    Title = process.R0F6G10AProbeAdmission("SYSTEM_TITLE", false, false),
                    NewGame = process.R0F6G10AProbeAdmission("EVENTFIRST", true, true),
                    SaveDecodeTransition = process.R0F6G10AProbeAdmission("SYSTEM_LOADEND", false, false),
                    Counters = process.R0F6G10AEvidence(),
                    GuiLaunched = false,
                    UserInputSent = false,
                    MacroExecuted = false,
                    PerformanceMeasured = false,
                },
                _ => throw new ArgumentException("unknown R0-F6G10A mode", nameof(mode)),
            };
            B1Proof.WriteJson(outputPath, result);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.ChangeExtension(outputPath, ".failure.txt"), ex.ToString());
            return 1;
        }
        finally { R0F6G10AHeadlessCapture = false; R0E1ACandidate = false; Preload.Clear(); }
    }
}
#endif

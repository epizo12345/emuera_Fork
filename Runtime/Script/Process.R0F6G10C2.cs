#if R0_F6G10C2
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object R0F6G10C2Run(string mode)
    {
        var runtime = compactProduction;
        if (mode == "MetadataCrossCheck")
            return runtime?.R0F6G10C2MetadataCrossCheck() ?? throw new InvalidOperationException("CompactStrict is not initialized");

        if (!console.R0F6G10C1StartProgram())
            throw new InvalidOperationException("initial production console run was rejected");
        var title = console.R0F6G10C1DisplayModel();
        if (mode == "CompactNewGame")
        {
            var before = runtime?.R0F6G10C2TransitionCounters() ?? default;
            var started = Stopwatch.GetTimestamp();
            if (!console.R0F6G10C1AcceptControlledInput("0"))
                throw new InvalidOperationException("controlled NEW GAME response was rejected");
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var after = runtime!.R0F6G10C2TransitionCounters();
            return new
            {
                Schema = "emuera-r0f6g10c2-new-game-transition-v1",
                Title = title,
                NewGame = console.R0F6G10C1DisplayModel(),
                NewGameInput = console.R0F6G10C2InputModel(),
                ElapsedMilliseconds = elapsed,
                DemandBodyCompileCountDelta = after.BodyCompiles - before.BodyCompiles,
                LinkFunctionCountDelta = after.Links - before.Links,
                SourceReadCountDelta = after.SourceReads - before.SourceReads,
                MaterializedFunctionDelta = after.MaterializedFunctions - before.MaterializedFunctions,
                Counters = runtime.Evidence(),
                GuiLaunched = false,
                SendKeysUsed = false,
            };
        }

        if (!console.R0F6G10C1AcceptControlledInput("1"))
            throw new InvalidOperationException("controlled LOAD GAME response was rejected");
        var loadMenu = console.R0F6G10C1DisplayModel();
        var loadInput = console.R0F6G10C2InputModel();
        if (mode is "LegacyLoadMenu" or "CompactLoadMenu")
            return new
            {
                Schema = "emuera-r0f6g10c2-load-menu-v1",
                RuntimeMode = Program.RuntimeMode.ToString(),
                Title = title,
                LoadMenu = loadMenu,
                LoadInput = loadInput,
                VisibleIntegerButtons = console.R0F6G10C2VisibleIntegerButtons(),
                CompactCounters = runtime?.Evidence(),
                GuiLaunched = false,
                SendKeysUsed = false,
            };

        if (runtime is null) throw new InvalidOperationException("full load path requires CompactStrict");
        var menuResponses = new List<long>();
        var values = console.R0F6G10C2VisibleIntegerButtons();
        if (!values.Contains(219L) && values.Contains(1005L))
        {
            if (!console.R0F6G10C1AcceptControlledInput("1005"))
                throw new InvalidOperationException("controlled dungeon save-area response was rejected");
            menuResponses.Add(1005);
            values = console.R0F6G10C2VisibleIntegerButtons();
        }
        for (var page = 0; !values.Contains(219L) && page < 20; page++)
        {
            if (!values.Contains(1002L) || !console.R0F6G10C1AcceptControlledInput("1002"))
                throw new InvalidOperationException("save219 is not reachable through the rendered load menu");
            menuResponses.Add(1002);
            values = console.R0F6G10C2VisibleIntegerButtons();
        }
        if (!values.Contains(219L)) throw new InvalidOperationException("save219 button was not rendered");
        if (!console.R0F6G10C1AcceptControlledInput("219"))
            throw new InvalidOperationException("controlled save219 response was rejected");
        menuResponses.Add(219);
        var d9 = runtime.G10BEvidence("production-title-load-save219-D9");
        var d9Display = console.R0F6G10C1DisplayModel();
        var controlledH = mode == "CompactLoadH";
        var hAccepted = controlledH && console.R0F6G10C1AcceptControlledInput("H");
        return new
        {
            Schema = "emuera-r0f6g10c2-title-load-save219-v1",
            Mode = controlledH ? "ControlledH" : "D9NoResponse",
            Title = title,
            LoadMenu = loadMenu,
            LoadInput = loadInput,
            MenuResponses = menuResponses,
            D9 = d9,
            D9Display = d9Display,
            ControlledHSubmitted = controlledH,
            ControlledHAccepted = hAccepted,
            AfterH = controlledH ? runtime.G10BEvidence("after-controlled-H") : null,
            Counters = runtime.Evidence(),
            GuiLaunched = false,
            SendKeysUsed = false,
        };
    }

    private sealed partial class CompactProductionRuntime
    {
        internal (int BodyCompiles, int Links, int SourceReads, int MaterializedFunctions) R0F6G10C2TransitionCounters() =>
            (bodyCompiles, links, sourceReads, registered.Count);

        internal object R0F6G10C2MetadataCrossCheck()
        {
            var inspected = 0;
            var fastSkipped = 0;
            var parserRequired = 0;
            var parserFailures = 0;
            var mismatches = new List<object>();
            var fileOpens = 0;
            foreach (var group in definitions.GroupBy(row => row.File.FileIdentity, StringComparer.OrdinalIgnoreCase))
            {
                using var source = FunctionSourceReader.OpenFile(group.First().File);
                fileOpens++;
                foreach (var row in group)
                {
                    inspected++;
                    var read = source.ReadStableBatch(row.Function);
                    if (read.Status != SourceReadStatus.Read)
                        throw new InvalidOperationException($"metadata cross-check source read failed: {row.Name}: {read.Reason}");
                    var prepared = row.Preprocessor.Apply(read.Source!.Value);
                    var valid = FunctionRuntimeMetadataParser.TryParse(prepared, environment.Compatibility,
                        out var parsed, out var detail, environment.Macros, allowUnresolvedDynamicInitializer: true);
                    if (!valid) parserFailures++;
                    if (NeedsRuntimeMetadata(row.Function)) { parserRequired++; continue; }
                    fastSkipped++;
                    var empty = valid && parsed.Parameters.IsEmpty && parsed.LocalSize == 0 && parsed.LocalsSize == 0 &&
                        parsed.PrivateVariables.IsEmpty && parsed.ReturnType is null;
                    if (valid && !empty && mismatches.Count < 20)
                        mismatches.Add(new { row.Id.Value, row.Name, row.RelativePath, row.Line, Valid = valid, Detail = detail });
                }
                if (!source.VerifyStableBatchSnapshot())
                    throw new InvalidOperationException($"metadata cross-check source changed: {group.Key}");
            }
            return new
            {
                Schema = "emuera-r0f6g10c2-metadata-fast-path-cross-check-v1",
                Status = inspected == 134651 && mismatches.Count == 0 ? "PASS" : "FAIL",
                ExpectedFunctions = 134651,
                InspectedFunctions = inspected,
                FullOldParserCalls = inspected,
                FastSkippedFunctions = fastSkipped,
                ParserRequiredFunctions = parserRequired,
                ParserFailures = parserFailures,
                FastSkipMismatches = mismatches,
                FileOpenCount = fileOpens,
                PerFunctionFileOpenCount = 0,
                RetainedBodyCache = false,
            };
        }
    }
}
#endif

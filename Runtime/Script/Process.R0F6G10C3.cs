#if R0_F6G10C3
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Next.Core;
using MinorShift.Emuera.Next.Vm;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object R0F6G10C3Run(string mode)
    {
        if (mode is "LegacyHtmlOracle" or "CompactHtmlOracle")
            return R0F6G10C3RunHtmlOracle(mode == "CompactHtmlOracle");
        if (mode is "LegacyNumSummoner" or "CompactNumSummoner")
        {
            if (!vEvaluator.LoadFrom(219)) throw new InvalidOperationException("save219 decode failed");
            if (mode == "LegacyNumSummoner")
            {
                long Run(string name, params AExpression[] arguments) =>
                    (idDic.GetFunctionMethod(labelDic, name, arguments.ToList(), true) as UserDefinedMethodTerm
                        ?? throw new InvalidOperationException("Legacy method missing: " + name)).GetIntValue(exm);
                var rows = Enumerable.Range(1, 6).Select(index =>
                {
                    var character = Run("POS", SingleLongTerm.FromValue(index));
                    return new { Index = index, Character = character,
                        Talent = character < 0 ? (long?)null : Run("GET_BTL_TALENT_LV", SingleStrTerm.FromValue("サマナー"), SingleLongTerm.FromValue(character), SingleStrTerm.FromValue("両形態")),
                        Inputable = character < 0 ? (long?)null : Run("INPUTABLE_CHARA_F", SingleLongTerm.FromValue(character)) };
                }).ToArray();
                var cstr = idDic.GetVariableToken("CSTR", null, false) ?? throw new InvalidOperationException("CSTR missing");
                var roleIndex = GlobalStatic.ConstantData.KeywordToInteger(cstr.Code, "ロール", 1);
                return new { Schema = "emuera-r0f6g10c3-num-summoner-probe-v1", Mode = "Legacy",
                    Value = Run("NUM_SUMMONER", SingleLongTerm.FromValue(1)), Rows = rows,
                    CharacterZeroRole = cstr.GetStrValue(exm, [0, roleIndex]),
                    SubRoleNum = Run("GET_SUB_ROLE_NUM", SingleStrTerm.FromValue("サマナー")),
                    RoleProp = Run("GET_ROLE_PROP", SingleStrTerm.FromValue("CAN_USE_サマナー"), SingleLongTerm.FromValue(0), SingleStrTerm.FromValue(""), SingleStrTerm.FromValue("両形態")) };
            }
            R0F6G10AApplyDataReset(discardExecution: true);
            return (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
                .R0F6G10C3NumSummonerProbe();
        }
        if (!console.R0F6G10C1StartProgram())
            throw new InvalidOperationException("initial production console run was rejected");
        var title = console.R0F6G10C1DisplayModel();
        if (!console.R0F6G10C1AcceptControlledInput("1"))
            throw new InvalidOperationException("controlled LOAD GAME response was rejected");
        var loadMenu = console.R0F6G10C1DisplayModel();
        var responses = new List<long>();
        var values = console.R0F6G10C2VisibleIntegerButtons();
        if (!values.Contains(219L) && values.Contains(1005L))
        {
            if (!console.R0F6G10C1AcceptControlledInput("1005"))
                throw new InvalidOperationException("controlled dungeon save-area response was rejected");
            responses.Add(1005);
            values = console.R0F6G10C2VisibleIntegerButtons();
        }
        for (var page = 0; !values.Contains(219L) && page < 20; page++)
        {
            if (!values.Contains(1002L) || !console.R0F6G10C1AcceptControlledInput("1002"))
                throw new InvalidOperationException("save219 is not reachable through the rendered load menu");
            responses.Add(1002);
            values = console.R0F6G10C2VisibleIntegerButtons();
        }
        if (!values.Contains(219L)) throw new InvalidOperationException("save219 button was not rendered");
        if (!console.R0F6G10C1AcceptControlledInput("219"))
            throw new InvalidOperationException("controlled save219 response was rejected");
        responses.Add(219);
        var runtime = compactProduction;
        if (mode is "CompactPostH" or "LegacyPostH")
        {
            var compact = runtime is not null;
            var d9 = console.R0F6G10C1DisplayModel();
            var flag = idDic.GetVariableToken("FLAG", null, true) ?? throw new InvalidOperationException("FLAG is unavailable");
            long ReadFlag(string name) => TryReadNextRuntimeHostValue(flag, [VmSemanticValue.From(name)], out var value) && value.TryGetInteger(out var number)
                ? number : long.MinValue;
            var fixtureApplied =
                TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("現M")], VmSemanticValue.From(12L)) &&
                TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("現X")], VmSemanticValue.From(10L)) &&
                TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("現Y")], VmSemanticValue.From(12L)) &&
                TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("未遭遇歩数")], VmSemanticValue.From(1000L)) &&
                TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("エンカウントしない")], VmSemanticValue.From(0L)) &&
                TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("呪いのレッドカーペット")], VmSemanticValue.From(0L));
            if (!fixtureApplied) throw new InvalidOperationException("encounter fixture state was not writable through host variables");
            if (!console.R0F6G10C1AcceptControlledInput("H"))
                throw new InvalidOperationException("controlled H response was rejected");
            var hAdmission = runtime?.G10BEvidence("post-H-WALK_DUNGEON-admitted");
            var approach = new List<object>();
            var fixtureRefreshes = 0;
            int PendingLine() => runtime?.R0F6G10C3PendingSourceLine ?? state.CurrentLine?.Position?.LineNo ?? -1;
            string? PendingOpcode() => runtime?.R0F6G10C3PendingInputOpcode ??
                (state.CurrentLine as InstructionLine)?.Function.Code.ToString();
            object? RuntimeCheckpoint() => runtime?.R0F6G10C3EncounterCounts();
            for (var guard = 0; PendingLine() != 2631 && guard < 64; guard++)
            {
                var opcode = PendingOpcode();
                var sourceLine = PendingLine();
                var input = "0";
                if (opcode == nameof(PrototypeOpcode.ONEINPUTS) && sourceLine == 1453)
                {
                    if (!TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("現M")], VmSemanticValue.From(12L)) ||
                        !TryWriteNextRuntimeHostValue(flag, [VmSemanticValue.From("未遭遇歩数")], VmSemanticValue.From(1000L)))
                        throw new InvalidOperationException("encounter fixture refresh failed");
                    fixtureRefreshes++;
                    input = "H";
                }
                var before = new { Opcode = opcode, SourceLine = sourceLine, Result = vEvaluator.RESULT,
                    EncounterRate = ReadFlag("エンカウント率"), UnencounteredSteps = ReadFlag("未遭遇歩数"), Runtime = RuntimeCheckpoint() };
                if (opcode is not (nameof(PrototypeOpcode.TWAIT) or nameof(PrototypeOpcode.WAIT) or nameof(PrototypeOpcode.FORCEWAIT) or nameof(PrototypeOpcode.WAITANYKEY) or nameof(PrototypeOpcode.INPUTMOUSEKEY)) &&
                    !(opcode == nameof(PrototypeOpcode.ONEINPUTS) && sourceLine == 1453) ||
                    !console.R0F6G10C1AcceptControlledInput(input))
                    throw new InvalidOperationException($"encounter approach stopped before source line 2631: {runtime?.ExecutionDiagnostic() ?? state.CurrentLine?.Position?.ToString()}");
                approach.Add(new { HuntIteration = guard + 1, Input = input, Before = before,
                    After = new { Opcode = PendingOpcode(), SourceLine = PendingLine(), Result = vEvaluator.RESULT,
                        EncounterRate = ReadFlag("エンカウント率"), UnencounteredSteps = ReadFlag("未遭遇歩数"), Runtime = RuntimeCheckpoint() } });
            }
            if (PendingLine() != 2631)
                return new { Schema = "emuera-r0f6g10c3r1-h-progression-v1", Mode = compact ? "CompactStrict" : "Legacy",
                    ReachedFirstIsland = false, MenuResponses = responses, FixtureApplied = fixtureApplied, HAdmission = hAdmission,
                    Approach = approach, Final = new { Opcode = PendingOpcode(), SourceLine = PendingLine(), Result = vEvaluator.RESULT,
                        Dungeon = ReadFlag("現ダンジョン"), Floor = ReadFlag("現M"), Steps = ReadFlag("未遭遇歩数"), Rate = ReadFlag("エンカウント率"),
                        NoEncounter = ReadFlag("エンカウントしない"), RedCarpet = ReadFlag("呪いのレッドカーペット"), Escape = ReadFlag("脱出"), Runtime = RuntimeCheckpoint() },
                    D9Display = d9, CompactCounters = runtime?.Evidence(), GuiLaunched = false, SendKeysUsed = false };
            var firstOpcode = PendingOpcode();
            var firstIsland = console.R0F6G10C1DisplayModel();
            if (firstOpcode != PrototypeOpcode.TWAIT.ToString() || !console.R0F6G10C1AcceptControlledInput("0"))
                throw new InvalidOperationException("encounter TWAIT boundary was not resumable");
            var secondOpcode = PendingOpcode();
            var secondIsland = console.R0F6G10C1DisplayModel();
            if (secondOpcode != PrototypeOpcode.WAIT.ToString())
                throw new InvalidOperationException($"encounter WAIT boundary was not reached: {secondOpcode ?? "<none>"}; {runtime?.ExecutionDiagnostic() ?? state.CurrentLine?.Position?.ToString()}");
            Program.R0F6G10C3PauseAfterIslandClear = true;
            if (!console.R0F6G10C1AcceptControlledInput("0"))
                throw new InvalidOperationException("encounter WAIT boundary rejected controlled input");
            if (!Program.R0F6G10C3IslandClearObserved)
                throw new InvalidOperationException("encounter HTML island clear boundary was not observed");
            return new
            {
                Schema = "emuera-r0f6g10c3r1-source-h-encounter-v1",
                Mode = compact ? "CompactStrict" : "Legacy",
                ReachedFirstIsland = true,
                MenuResponses = responses,
                Fixture = new { Applied = fixtureApplied, Kind = "GameVariableHostWrite", CurrentFloor = 12, CurrentX = 10, CurrentY = 12, Direction = 6, UnencounteredSteps = 1000, NoEncounter = 0, RedCarpet = 0, Refreshes = fixtureRefreshes },
                HAdmission = hAdmission,
                ApproachWaits = approach,
                D9Display = d9,
                FirstIsland = new { PendingOpcode = firstOpcode, Display = firstIsland },
                SecondIsland = new { PendingOpcode = secondOpcode, Display = secondIsland },
                AfterClear = new { Observed = Program.R0F6G10C3IslandClearObserved, PendingOpcode = PendingOpcode(), PendingSourceLine = PendingLine(), Display = console.R0F6G10C1DisplayModel() },
                CompactPath = runtime?.G10BEvidence("production-title-load-save219-H-encounter"),
                CompactCounters = runtime?.Evidence(),
                GuiLaunched = false,
                SendKeysUsed = false,
            };
        }
        return new
        {
            Schema = "emuera-r0f6g10c3-full-d9-parity-v1",
            RuntimeMode = Program.RuntimeMode.ToString(),
            Title = title,
            LoadMenu = loadMenu,
            MenuResponses = responses,
            D9Display = console.R0F6G10C1DisplayModel(),
            D9Input = console.R0F6G10C2InputModel(),
            CompactPath = runtime?.G10BEvidence("production-title-load-save219-D9"),
            CompactCounters = runtime?.Evidence(),
            GuiLaunched = false,
            SendKeysUsed = false,
        };
    }

    private object R0F6G10C3RunHtmlOracle(bool compact)
    {
        var steps = new List<object>();
        void Snapshot(string name) => steps.Add(new { Name = name, Display = console.R0F6G10C1DisplayModel() });
        VmHostEffectResult Apply(PrototypeOpcode opcode, params VmSemanticValue[] arguments)
        {
            if (compact)
                return (compactProduction ?? throw new InvalidOperationException("CompactStrict is not initialized"))
                    .R0F6G10C3ExecuteTypedHost(opcode, arguments);
            if (opcode == PrototypeOpcode.HTML_PRINT_ISLAND && arguments.Length == 2 &&
                arguments[0].TryGetString(out var html) && arguments[1].TryGetInteger(out var depth))
            {
                if (!skipPrint) console.PrintHTMLIsland(html, (int)depth);
                return VmHostEffectResult.Applied;
            }
            if (opcode == PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR && arguments.Length == 0)
            {
                console.ClearHTMLIsland();
                return VmHostEffectResult.Applied;
            }
            if (opcode == PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR && arguments.Length == 1 && arguments[0].TryGetInteger(out var clearDepth))
            {
                console.ClearHTMLIsland((int)clearDepth);
                return VmHostEffectResult.Applied;
            }
            return VmHostEffectResult.Fault;
        }

        const string a = "<div xpos='0' ypos='0'>A</div>";
        const string b = "<div xpos='100' ypos='100'>B</div>";
        const string c = "<div xpos='200' ypos='200'>C</div>";
        const string skipped = "<div xpos='300' ypos='300'>SKIPPED</div>";
        const string replacement = "<div xpos='400' ypos='400'>D</div>";
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR); Snapshot("clear-all-initial");
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND, VmSemanticValue.From(a), VmSemanticValue.From(0L)); Snapshot("default-depth-0");
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND, VmSemanticValue.From(b), VmSemanticValue.From(2L)); Snapshot("second-layer");
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND, VmSemanticValue.From(c), VmSemanticValue.From(2L)); Snapshot("same-layer-appends");
        skipPrint = true;
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND, VmSemanticValue.From(skipped), VmSemanticValue.From(0L));
        skipPrint = false;
        Snapshot("skip-print-no-effect");
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR, VmSemanticValue.From(2L)); Snapshot("clear-one-layer");
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND, VmSemanticValue.From(replacement), VmSemanticValue.From(2L)); Snapshot("add-after-clear");
        Apply(PrototypeOpcode.HTML_PRINT_ISLAND_CLEAR); Snapshot("clear-all-final");
        return new
        {
            Schema = "emuera-r0f6g10c3-html-island-oracle-v1",
            Mode = compact ? "CompactStrict" : "Legacy",
            Steps = steps,
            TypedPayload = compact,
            RawRuntimeParse = false,
            GuiLaunched = false,
            SendKeysUsed = false,
        };
    }

    private sealed partial class CompactProductionRuntime
    {
        internal VmHostEffectResult R0F6G10C3ExecuteTypedHost(PrototypeOpcode opcode, ReadOnlySpan<VmSemanticValue> arguments) =>
            owner.ContextNeutralEffects.ExecuteTypedHostStatement(opcode, arguments);

        internal string? R0F6G10C3PendingInputOpcode => inputBinding?.Opcode.ToString();

        internal int R0F6G10C3PendingSourceLine
        {
            get
            {
                var machine = owner.ContextNeutralMachine;
                return instructions.TryGetValue(machine.LastExecutedFunctionId, out var body) &&
                    (uint)machine.LastExecutedPc < (uint)body.Length ? body[machine.LastExecutedPc].SourceLine : -1;
            }
        }

        internal object R0F6G10C3EncounterCounts() => new
        {
            BodyCompiles = bodyCompiles,
            Invocations = new[] { "WALK_DUNGEON", "SET_ENCOUNT_RATE", "CHECK_ENCOUNT_105", "ENEMY_TABLE" }
                .Select(name =>
                {
                    var row = byName.GetValueOrDefault(name)?.SingleOrDefault(value => value.Kind == FunctionKind.Normal);
                    return new { Name = name, Count = row is null ? 0 : owner.R0F6G7R2InvocationCounts.GetValueOrDefault(row.Id.Value) };
                }).ToArray(),
            Last = ExecutionDiagnostic(),
        };

        internal object R0F6G10C3NumSummonerProbe()
        {
            var num = Run("NUM_SUMMONER", VmSemanticValue.From(1L));
            var rows = Enumerable.Range(1, 6).Select(index =>
            {
                var position = Run("POS", VmSemanticValue.From((long)index));
                if (!long.TryParse(position.Value, out var character)) throw new InvalidOperationException("POS did not return an integer");
                return new
                {
                    Index = index,
                    Character = character,
                    Talent = character < 0 ? null : Result(Run("GET_BTL_TALENT_LV", VmSemanticValue.From("サマナー"), VmSemanticValue.From(character), VmSemanticValue.From("両形態"))),
                    Inputable = character < 0 ? null : Result(Run("INPUTABLE_CHARA_F", VmSemanticValue.From(character))),
                };
            }).ToArray();
            var cstr = process.idDic.GetVariableToken("CSTR", null, false) ?? throw new InvalidOperationException("CSTR missing");
            var roleIndex = GlobalStatic.ConstantData.KeywordToInteger(cstr.Code, "ロール", 1);
            return new
            {
                Schema = "emuera-r0f6g10c3-method-probe-v1",
                Mode = "CompactStrict",
                Function = "NUM_SUMMONER",
                Stop = num.Stop,
                Value = num.Value,
                Rows = rows,
                CharacterZeroRole = cstr.GetStrValue(process.exm, [0, roleIndex]),
                SubRoleNum = Result(Run("GET_SUB_ROLE_NUM", VmSemanticValue.From("サマナー"))),
                RoleProp = Result(Run("GET_ROLE_PROP", VmSemanticValue.From("CAN_USE_サマナー"), VmSemanticValue.From(0L), VmSemanticValue.From(""), VmSemanticValue.From("両形態"))),
                OwnerCount = ownerCreations,
                VmMachineCount = machineCreations,
                LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO",
            };

            static object Result((string Stop, string Value) value) => new { value.Stop, value.Value };

            (string Stop, string Value) Run(string name, params VmSemanticValue[] arguments)
            {
                var row = byName[name].Single(value => value.Kind == FunctionKind.Method);
                Register(row.Id);
                var machine = owner.ContextNeutralMachine;
                var stop = machine.Start(row.Id, arguments, FunctionKind.Method);
                if (stop == VmStopReason.Returned) stop = machine.Continue(50_000_000);
                return (stop.ToString(), machine.LastReturnValue.ToString());
            }
        }
    }
}
#endif

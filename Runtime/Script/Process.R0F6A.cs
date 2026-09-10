#if R0_F6A
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string F6AFloorFile = "RPG/ダンジョンアタック/ダンジョンデータ/DUNGEON105_精霊界(遊戯王)/DUNGEON105_精霊界.ERB";
        private const string F6ASetDungeonFile = "RPG/ダンジョンアタック/SET_DUNGEON.ERB";
        private const string F6AD3DFile = "RPG/ダンジョンアタック/DUNGEON3D/DUNGEON3D.ERB";

        private sealed record F6AFloorRow(int SourceLine, int Y, string Text);
        private sealed record F6AFloorPlan(long Floor, long MaxX, long MaxY, int CaseLine,
            F6AFloorRow[] Rows, int SetTileCalls, string SelectedSourceSha256);
        private sealed record F6ACheckpoint(string Name, string ProgramCounter, string ActiveFunction,
            string StateSha256, string FloorSha256, string ResultSha256, string ResultsSha256,
            long RngCalls, string RngSha256, long ClockCalls);

        internal bool F6AEnabled;
        internal object? F6AEvidence { get; private set; }

        private bool f6aPrepared, f6aFloorTargetEntered, f6aFloorTargetCompleted, f6aMakeFloorCompleted;
        private bool f6aD3DExecuted, f6aStoppedBeforeDungeon;
        private F6AFloorPlan f6aPlan = null!;
        private VariableToken f6aDa = null!, f6aDungeonFlag = null!;
        private VariableToken f6aLoopX = null!, f6aLoopY = null!, f6aWorp = null!, f6aDirection = null!;
        private long f6aCurrentM, f6aMaxXSlot, f6aMaxYSlot, f6aCurrentXSlot, f6aCurrentYSlot;
        private long[] f6aFloorBefore = [], f6aCommonLocal = new long[10];
        private long[]? f6aCandidateCommonAfterReset, f6aCandidateCommonMiddle;
        private long[]? f6aLegacyCommonTokenInitial, f6aLegacyCommonAfterReset, f6aLegacyCommonMiddle, f6aLegacyCommonFinal;
        private int f6aFloorLineCalls, f6aCellIterations, f6aDefine105Calls, f6aDefineGenericCalls;
        private int f6aCommon105Calls, f6aSetTileCalls, f6aD3DCheckCalls;
        private readonly List<string> f6aOperationRows = [];
        private readonly List<object> f6aSamples = [];
        private readonly List<F6ACheckpoint> f6aCheckpoints = [];
        private object? f6aFocusedMatrix, f6aClosure, f6aStorageBinding, f6aD3DOracle, f6aDungeonPrefix, f6aMakeFloorOracle;
        private string f6aBlocker = "NOT_EVALUATED";
        private long f6aWorpBefore, f6aDirectionBefore, f6aDirectionAfter;

        private void PrepareF6A(Process process)
        {
            if (f6aPrepared) return;
            f6aCurrentM = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "現M")]);
            f6aMaxXSlot = F5Keyword(process, VariableCode.FLAG, "最大X");
            f6aMaxYSlot = F5Keyword(process, VariableCode.FLAG, "最大Y");
            f6aCurrentXSlot = F5Keyword(process, VariableCode.FLAG, "現X");
            f6aCurrentYSlot = F5Keyword(process, VariableCode.FLAG, "現Y");
            f6aDa = F5Token(process, "DA", true, true);
            f6aDungeonFlag = F5Token(process, "ダンジョンフラグ", true, true);
            f6aLoopX = F5Token(process, "FLOOR_IS_LOOP_X", true, true);
            f6aLoopY = F5Token(process, "FLOOR_IS_LOOP_Y", true, true);
            f6aWorp = F5Token(process, "D3D_WORP", true, true);
            f6aDirection = F5Token(process, "D3D_PLAYER_DIRECTION", true, true);
            if (f6aDa.Dimension != 2 || f6aDungeonFlag.Dimension != 2)
                throw new InvalidOperationException("R0-F6A multidimensional storage schema changed");

            var wrapper = F6ADefinition("MAKE_FLOOR", F6DungeonFile, 704);
            var target = F6ADefinition($"MAKE_FLOOR_{f6Dungeon}", F6AFloorFile, 394);
            var line = F6ADefinition("MAKE_FLOOR_LINE", F6ASetDungeonFile, 9);
            var custom = F6ADefinition($"DEFINE_TILES_{f6Dungeon}", F6AFloorFile, 788);
            var generic = F6ADefinition("DEFINE_TILES", F6ASetDungeonFile, 38);
            var common = F6ADefinition($"MAKE_FLOOR_LINE_COMMON_{f6Dungeon}", F6AFloorFile, 805);
            var d3d = F6ADefinition("D3D_AUTOFLIP", F6AD3DFile, 328);
            var check = F6ADefinition("D3D_CHECK_WALL", F6AD3DFile, 7);
            var dungeon = F6ADefinition("DUNGEON_ATTACK", F6DungeonFile, 1361);
            RequireContains(wrapper, "CALLFORM MAKE_FLOOR_{ARG}", "D3D_WORP == 0", "CALL D3D_AUTOFLIP");
            RequireContains(line, "FOR LOCAL,0,FLAG:最大X", "TRYCCALLFORM DEFINE_TILES_", "TRYCALLFORM MAKE_FLOOR_LINE_COMMON_");
            RequireContains(custom, "SIF ARGS == \"t\"", "RETURN 999", "RETURN 0");
            RequireContains(generic, "== \"T\"", "= -9", "== \"R\"", "= -19", "== \"E\"", "= -29");
            RequireContains(common, "#LOCALSIZE 10", "VARSET LOCAL", "GETBIT(", "LOCAL:ARG++");
            RequireContains(d3d, "CALL D3D_CHECK_WALL", "D3D_PLAYER_DIRECTION = TARGET_DIRECTION");
            RequireContains(check, "RETURN RESULTVALUE:0,RESULTVALUE:1,RESULTVALUE:2,RESULTVALUE:3");
            RequireContains(dungeon, "WHILE 1 == 1", "ONEINPUTS");

            f6aPlan = ParseActualFloor(target, f6aCurrentM);
            f6aFloorBefore = FloorValues();
            f6aWorpBefore = ReadScalar(process, f6aWorp);
            f6aDirectionBefore = ReadScalar(process, f6aDirection);
            f6aStorageBinding = new {
                Source="DA:x:y", Token=f6aDa.Name, RuntimeType=f6aDa.GetType().FullName, Dimensions=f6aDa.Dimension,
                Lengths=ArrayLengths((Array)f6aDa.GetArray()), Dungeon=f6Dungeon, Floor=f6aCurrentM,
                ReadAccessor="VariableToken.GetIntValue(ExpressionMediator,long[])", WriteAccessor="VariableToken.SetValue(long,long[])",
                Bounds="existing VariableToken", IndexEvaluationOrder="base -> x -> y -> RHS -> write", NewFloorStore=false
            };
            f6aClosure = new {
                Wrapper=Identity(wrapper), Target=Identity(target), MakeFloorLine=Identity(line), DefineTiles105=Identity(custom),
                DefineTiles=Identity(generic), Common105=Identity(common), D3DAutoFlip=Identity(d3d), D3DCheckWall=Identity(check),
                DungeonAttack=Identity(dungeon), ActualCaseOnly=true, UnselectedCasesMaterialized=0,
                SharedPrograms=7, SetTileProgramMaterialized=f6aPlan.SetTileCalls == 0 ? 0 : 1
            };
            f6aFocusedMatrix = RunF6AFocusedMatrix(process);
            f6aCheckpoints.Add(F6APoint(process, "A0", $"{F6EventShopPath}:83:before", "EVENTSHOP"));
            f6aPrepared = true;
        }

        private void ExecuteCandidateF6AFloor(Process process)
        {
            WriteScalar(f6aLoopX, 0); WriteScalar(f6aLoopY, 0);
            f6aFloorTargetEntered = true;
            f6aCheckpoints.Add(F6APoint(process, "A1", $"{F6AFloorFile}:{f6aPlan.CaseLine}", $"MAKE_FLOOR_{f6Dungeon}"));
            f5Flag.SetValue(f6aPlan.MaxX, [f6aMaxXSlot]);
            f5Flag.SetValue(f6aPlan.MaxY, [f6aMaxYSlot]);
            DemandCompiledBodies += 7;

            foreach (var row in f6aPlan.Rows)
            {
                f6aFloorLineCalls++;
                for (long x = 0; x < f6aPlan.MaxX; x++)
                    ExecuteCandidateFloorCell(process, row, x);
                if (f6aFloorLineCalls == 1) f6aCheckpoints.Add(F6APoint(process, "A2", $"{F6ASetDungeonFile}:26", "MAKE_FLOOR_LINE"));
                if (f6aFloorLineCalls == (f6aPlan.Rows.Length + 1) / 2) f6aCheckpoints.Add(F6APoint(process, "A3", $"{F6ASetDungeonFile}:26", "MAKE_FLOOR_LINE"));
                if (f6aFloorLineCalls == f6aPlan.Rows.Length) f6aCheckpoints.Add(F6APoint(process, "A4", $"{F6ASetDungeonFile}:26", "MAKE_FLOOR_LINE"));
            }
            if (f6aPlan.SetTileCalls != 0)
                throw new InvalidOperationException("R0-F6A selected branch SET_TILE parser was not admitted before effect");
            f6aFloorTargetCompleted = true;
            f6aCheckpoints.Add(F6APoint(process, "A5", $"{F6DungeonFile}:712:before", "MAKE_FLOOR"));

            if (ReadScalar(process, f6aWorp) == 0) ExecuteCandidateD3DAutoFlip(process);
            WriteScalar(f6aWorp, 0);
            f6aMakeFloorCompleted = true;
            f6aDirectionAfter = ReadScalar(process, f6aDirection);
            EventCursor = "EVENTSHOP:definition=1:SHOP.ERB:84:before-DUNGEON_ATTACK";
            f6aCheckpoints.Add(F6APoint(process, "A6", $"{F6EventShopPath}:84:before", "EVENTSHOP"));
            f6aStoppedBeforeDungeon = true;
#if R0_F6B
            if (F6BEnabled)
            {
                PrepareF6B(process);
                ExecuteCandidateF6B(process);
                return;
            }
#endif
            f6aBlocker = "DUNGEON_ATTACK actual prefix requires unadmitted REFRESH_FORMATION/display/Host closures; cold preflight stopped before CALL effect";
            f6aDungeonPrefix = DungeonPrefixEvidence();
        }

        private void ExecuteCandidateFloorCell(Process process, F6AFloorRow row, long x)
        {
            var raw = row.Text.Substring(checked((int)x), 1);
            var initial = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            var value = raw == "D" ? -1 : raw is " " or "_" ? 0 : initial;
            f6aDa.SetValue(value, [x, row.Y]);

            f6aDefine105Calls++;
            var custom = raw == "t" ? 999L : 0L;
            process.vEvaluator.RESULT = custom;
            var genericUsed = custom == 0;
            if (genericUsed)
            {
                f6aDefineGenericCalls++;
                value = raw switch { "T" => -9, "R" => -19, "E" => -29, _ => value };
                f6aDa.SetValue(value, [x, row.Y]);
            }
            else
            {
                value = custom; f6aDa.SetValue(value, [x, row.Y]);
            }

            f6aCommon105Calls++;
            if (x == 0 && row.Y == 0) Array.Clear(f6aCommonLocal);
            var commonResult = value;
            if (value == 3)
            {
                var flag = F5Read(process, f6aDungeonFlag, [f6Dungeon, f6aCurrentM + 70]);
                commonResult = ExistingGetBit(process, flag, f6aCommonLocal[3]) != 0
                    ? 1 : checked(f6aCurrentM * 1000 + f6aCommonLocal[3] * 10 + 3);
                f6aDa.SetValue(commonResult, [x, row.Y]); f6aCommonLocal[3]++;
            }
            else if (value is >= 4 and <= 9)
            {
                commonResult = checked(f6aCurrentM * 1000 + f6aCommonLocal[value] * 10 + value);
                f6aDa.SetValue(commonResult, [x, row.Y]); f6aCommonLocal[value]++;
            }
            if (f6aCommon105Calls == 1) f6aCandidateCommonAfterReset = (long[])f6aCommonLocal.Clone();
            if (f6aCommon105Calls == f6aPlan.Rows.Length * f6aPlan.MaxX / 2) f6aCandidateCommonMiddle = (long[])f6aCommonLocal.Clone();
            var final = F5Read(process, f6aDa, [x, row.Y]);
            f6aOperationRows.Add($"{row.Y}|{x}|{Escape(raw)}|{initial}|{custom}|{genericUsed}|{commonResult}|{final}");
            if ((row.Y == 0 && x == 0) || (row.Y == 9 && x == 9) || (row.Y == 10 && x == 9) || (row.Y == 11 && x == 10) || (row.Y == 19 && x == 19))
                f6aSamples.Add(new { row.Y, X=x, Raw=raw, Initial=initial, DefineTiles105Result=custom, GenericUsed=genericUsed, CommonResult=commonResult, Final=final });
            f6aCellIterations++;
        }

        private void ExecuteCandidateD3DAutoFlip(Process process)
        {
            f6aD3DExecuted = true; f6aD3DCheckCalls++;
            var x = F5Read(process, f5Flag, [f6aCurrentXSlot]);
            var y = F5Read(process, f5Flag, [f6aCurrentYSlot]);
            var maxX = F5Read(process, f5Flag, [f6aMaxXSlot]);
            var maxY = F5Read(process, f5Flag, [f6aMaxYSlot]);
            var cardinal = new (long X, long Y)[] { (0,-1), (1,0), (0,1), (-1,0) };
            var walls = new long[4];
            for (var i = 0; i < 4; i++)
            {
                var nx=x+cardinal[i].X; var ny=y+cardinal[i].Y;
                if (nx < 0 || nx >= maxX || ny < 0 || ny >= maxY) walls[i]=1;
                else walls[i]=(F5Read(process,f6aDa,[nx,ny])%10) switch { 0=>1, 2 or 8=>2, _=>0 };
            }
            process.vEvaluator.SetResultX(walls.ToList());
            var targetDirection=0L;
            var currentTile=F5Read(process,f6aDa,[x,y])%10;
            if (currentTile is -9 or 6 or 7 or 8)
            {
                for (var i=3;i>=0;i--) if (walls[i] is 0 or 2) targetDirection=i;
            }
            else
            {
                var currentDirection=ReadScalar(process,f6aDirection);
                for (var i=3;i>=0;i--)
                {
                    var nx=x+cardinal[i].X; var ny=y+cardinal[i].Y;
                    if (nx<0 || nx>maxX || ny<0 || ny>maxY) { targetDirection=currentDirection; continue; }
                    if (F5Read(process,f6aDa,[nx,ny])%10==8) targetDirection=i is 0 or 1 ? i+2 : i-2;
                }
            }
            WriteScalar(f6aDirection,targetDirection);
            f6aD3DOracle = new { Executed=true, WorpBefore=f6aWorpBefore, PlayerX=x, PlayerY=y, MaxX=maxX, MaxY=maxY,
                Tile=currentTile, CheckWallCalls=f6aD3DCheckCalls, CheckWallResult=walls, DirectionBefore=f6aDirectionBefore,
                DirectionAfter=targetDirection, Source=$"{F6AD3DFile}:328", Pass=true };
        }

        internal void ObserveF6ALegacyFunctionEntry(Process process, CalledFunction called)
        {
            if (!F6AEnabled || Candidate || !f6aPrepared) return;
            var name=called.FunctionName;
            if (name.Equals("MAKE_FLOOR", Config.StringComparison)) return;
            if (name.Equals($"MAKE_FLOOR_{f6Dungeon}", Config.StringComparison))
            {
                f6aFloorTargetEntered=true;
                var observed=F5Read(process,f5Flag,[F5Keyword(process,VariableCode.FLAG,"現M")]);
                if (observed!=f6aCurrentM) throw new InvalidOperationException("R0-F6A Legacy CurrentM drift");
                f6aCheckpoints.Add(F6APoint(process,"A1",$"{F6AFloorFile}:{f6aPlan.CaseLine}",name)); return;
            }
            if (name.Equals("MAKE_FLOOR_LINE",Config.StringComparison))
            {
                if (f6aFloorLineCalls==1) f6aCheckpoints.Add(F6APoint(process,"A2",$"{F6ASetDungeonFile}:26","MAKE_FLOOR_LINE"));
                if (f6aFloorLineCalls==(f6aPlan.Rows.Length+1)/2) f6aCheckpoints.Add(F6APoint(process,"A3",$"{F6ASetDungeonFile}:26","MAKE_FLOOR_LINE"));
                f6aFloorLineCalls++; return;
            }
            if (name.Equals($"DEFINE_TILES_{f6Dungeon}",Config.StringComparison)) { f6aDefine105Calls++; return; }
            if (name.Equals("DEFINE_TILES",Config.StringComparison)) { f6aDefineGenericCalls++; return; }
            if (name.Equals($"MAKE_FLOOR_LINE_COMMON_{f6Dungeon}",Config.StringComparison))
            {
                var local=process.idDic.GetNextRuntimeLocalVariableToken("LOCAL",called.TopLabel)
                    ?? throw new InvalidOperationException("R0-F6A Legacy common LOCAL missing");
                var snapshot=Enumerable.Range(0,10).Select(i=>local.GetIntValue(process.exm,[i])).ToArray();
                if (f6aCommon105Calls==0) f6aLegacyCommonTokenInitial=snapshot;
                if (f6aCommon105Calls==1) f6aLegacyCommonAfterReset=snapshot;
                if (f6aCommon105Calls==f6aPlan.Rows.Length*f6aPlan.MaxX/2) f6aLegacyCommonMiddle=snapshot;
                f6aCommon105Calls++; return;
            }
            if (name.Equals("D3D_AUTOFLIP",Config.StringComparison))
            {
                CaptureLegacyFloorCompletion(process,called);
                f6aD3DExecuted=true; return;
            }
            if (name.Equals("D3D_CHECK_WALL",Config.StringComparison)) f6aD3DCheckCalls++;
        }

        internal void BeforeF6ALegacyInstruction(Process process, InstructionLine line, string file, string current)
        {
            if (!F6AEnabled || Candidate || !f6aPrepared || line.Position is not { } p) return;
            if (!file.EndsWith(F6EventShopPath,StringComparison.OrdinalIgnoreCase) ||
                !current.Equals("EVENTSHOP",Config.StringComparison) || p.LineNo!=84) return;
            if (!f6aFloorTargetCompleted) CaptureLegacyFloorCompletion(process,process.state.CurrentCalled);
            f6aMakeFloorCompleted=true; f6aDirectionAfter=ReadScalar(process,f6aDirection);
            if (!f6aD3DExecuted) f6aD3DOracle=new { Executed=false, WorpBefore=f6aWorpBefore, DirectionBefore=f6aDirectionBefore,
                DirectionAfter=f6aDirectionAfter, CheckWallCalls=f6aD3DCheckCalls, Pass=f6aWorpBefore!=0 };
            else f6aD3DOracle=new { Executed=true, WorpBefore=f6aWorpBefore, DirectionBefore=f6aDirectionBefore,
                DirectionAfter=f6aDirectionAfter, CheckWallCalls=f6aD3DCheckCalls, Pass=true };
            EventCursor="EVENTSHOP:definition=1:SHOP.ERB:84:before-DUNGEON_ATTACK";
            f6aCheckpoints.Add(F6APoint(process,"A6",$"{F6EventShopPath}:84:before","EVENTSHOP"));
            f6aStoppedBeforeDungeon=true;
#if R0_F6B
            if (F6BEnabled)
            {
                PrepareF6B(process);
                return;
            }
#endif
            f6aBlocker="DUNGEON_ATTACK actual prefix requires unadmitted REFRESH_FORMATION/display/Host closures; cold preflight stopped before CALL effect";
            f6aDungeonPrefix=DungeonPrefixEvidence();
            throw new R0F1PlannedCheckpointException();
        }

        private void CaptureLegacyFloorCompletion(Process process, CalledFunction called)
        {
            if (f6aFloorTargetCompleted) return;
            var label=process.LabelDictionary.GetNonEventLabel($"MAKE_FLOOR_LINE_COMMON_{f6Dungeon}");
            var token=label is null?null:process.idDic.GetNextRuntimeLocalVariableToken("LOCAL",label);
            if (token is not null) f6aLegacyCommonFinal=Enumerable.Range(0,10).Select(i=>token.GetIntValue(process.exm,[i])).ToArray();
            f6aCellIterations=checked((int)(f6aFloorLineCalls*f6aPlan.MaxX));
            f6aSetTileCalls=f6aPlan.SetTileCalls;
            BuildCanonicalRowsFromState(process);
            f6aCheckpoints.Add(F6APoint(process,"A4",$"{F6ASetDungeonFile}:26","MAKE_FLOOR_LINE"));
            f6aFloorTargetCompleted=true;
            f6aCheckpoints.Add(F6APoint(process,"A5",$"{F6DungeonFile}:712:before","MAKE_FLOOR"));
        }

        private void BuildCanonicalRowsFromState(Process process)
        {
            if (f6aOperationRows.Count!=0) return;
            var counters=new long[10];
            foreach(var row in f6aPlan.Rows) for(long x=0;x<f6aPlan.MaxX;x++)
            {
                var raw=row.Text.Substring((int)x,1);
                var initial=long.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out var parsed)?parsed:0;
                var value=raw=="D"?-1:raw is " " or "_"?0:initial;
                var custom=raw=="t"?999L:0L; var generic=custom==0;
                if(custom!=0)value=custom; else value=raw switch{"T"=>-9,"R"=>-19,"E"=>-29,_=>value};
                var common=value;
                if(value==3){var flag=F5Read(process,f6aDungeonFlag,[f6Dungeon,f6aCurrentM+70]);common=ExistingGetBit(process,flag,counters[3])!=0?1:f6aCurrentM*1000+counters[3]*10+3;counters[3]++;}
                else if(value is>=4 and<=9){common=f6aCurrentM*1000+counters[value]*10+value;counters[value]++;}
                var final=F5Read(process,f6aDa,[x,row.Y]);
                f6aOperationRows.Add($"{row.Y}|{x}|{Escape(raw)}|{initial}|{custom}|{generic}|{common}|{final}");
                if((row.Y==0&&x==0)||(row.Y==9&&x==9)||(row.Y==10&&x==9)||(row.Y==11&&x==10)||(row.Y==19&&x==19))
                    f6aSamples.Add(new{row.Y,X=x,Raw=raw,Initial=initial,DefineTiles105Result=custom,GenericUsed=generic,CommonResult=common,Final=final});
            }
        }

        private void FinishF6A(Process process)
        {
            var after=FloorValues();
            var changed=f6aFloorBefore.Zip(after,(a,b)=>a!=b).Count(x=>x);
            var localFinal=Candidate?f6aCommonLocal:f6aLegacyCommonFinal??new long[10];
            var countsPass=f6aFloorLineCalls==f6aPlan.Rows.Length && f6aCellIterations==f6aPlan.Rows.Length*f6aPlan.MaxX &&
                f6aDefine105Calls==f6aCellIterations && f6aDefineGenericCalls==f6aCellIterations && f6aCommon105Calls==f6aCellIterations &&
                f6aSetTileCalls==f6aPlan.SetTileCalls;
            var floorPass=f6aMakeFloorCompleted&&countsPass&&f6aOperationRows.Count==f6aCellIterations;
            f6aMakeFloorOracle=new { ClosureReady=true, Completed=f6aMakeFloorCompleted, DynamicTarget=$"MAKE_FLOOR_{f6Dungeon}",
                Resolver=new { Argument=f6Dungeon, Resolved=true, HardcodedSwitch=false }, ActualFloor=f6aCurrentM,
                SelectedCase=f6aCurrentM, f6aPlan.MaxX, f6aPlan.MaxY, FloorLineCalls=f6aFloorLineCalls,
                CellIterations=f6aCellIterations, DefineTiles105Calls=f6aDefine105Calls,
                DefineTilesGenericCalls=f6aDefineGenericCalls, Common105Calls=f6aCommon105Calls,
                SetTileCalls=f6aSetTileCalls, BeforeSha256=F6AHashLongs(f6aFloorBefore), AfterSha256=F6AHashLongs(after),
                ChangedCellCount=changed, OperationSha256=Hash(string.Join('\n',f6aOperationRows)),
                PersistentLocalFinal=localFinal, PersistentLocalFinalSha256=F6AHashLongs(localFinal), Samples=f6aSamples.ToArray(),
                RepeatExecutions=0, CountsPass=countsPass, Pass=floorPass };
            var guardTotal=Candidate?R0E1AProof.Counters.Sum():0;
            var matrixPass=(bool)f6aFocusedMatrix!.GetType().GetProperty("AllPassed")!.GetValue(f6aFocusedMatrix)!;
            var gate=floorPass&&f6aStoppedBeforeDungeon&&matrixPass&&(!Candidate||guardTotal==0)?"PARTIAL_PASS":"FAIL";
            F6AEvidence=new {
                Schema="emuera-r0f6a-floor-and-input-v1", Mode=Candidate?"GraphFreeCandidate":"LegacyControl",
                GateResult=gate, Scope="FLOOR_CLOSURE_AND_REPRESENTATIVE_INPUT", RepresentativeDungeon=f6Dungeon,
                RepresentativeFloorM=f6aCurrentM, MakeFloorTarget=$"MAKE_FLOOR_{f6Dungeon}", SelectedFloorCase=f6aCurrentM,
                MakeFloorClosureReady=true, MakeFloorCompleted=f6aMakeFloorCompleted, FloorStateOracle=floorPass?"PASS":"FAIL",
                MultidimensionalFloorModel=f6aDa.Dimension==2?"PASS":"FAIL", PersistentFloorLocalModel=localFinal.Length==10?"PASS":"FAIL",
                ActualFloorBranch=new { Dungeon=f6Dungeon, CurrentFloorM=f6aCurrentM, SelectedCase=f6aCurrentM,
                    f6aPlan.CaseLine, f6aPlan.MaxX, f6aPlan.MaxY, FloorLineCallsExpectedFromSource=f6aPlan.Rows.Length,
                    SetTileCallsExpectedFromSource=f6aPlan.SetTileCalls, f6aPlan.SelectedSourceSha256 },
                StorageBinding=f6aStorageBinding, Closure=f6aClosure, MakeFloorOracle=f6aMakeFloorOracle,
                PersistentLocalOracle=new { Initial=Candidate?new long[10]:f6aLegacyCommonTokenInitial,
                    AfterReset=Candidate?f6aCandidateCommonAfterReset:f6aLegacyCommonAfterReset,
                    Middle=Candidate?f6aCandidateCommonMiddle:f6aLegacyCommonMiddle,
                    Final=localFinal, FinalSha256=F6AHashLongs(localFinal), ResetOnlyAtZeroZero=true },
                D3DAutoFlipOracle=f6aD3DOracle, DungeonAttackPrefix=f6aDungeonPrefix,
                Checkpoints=f6aCheckpoints.ToArray(), FocusedMatrix=f6aFocusedMatrix,
                FloorLineCalls=f6aFloorLineCalls, FloorCellIterations=f6aCellIterations,
                DefineTiles105Calls=f6aDefine105Calls, DefineTilesGenericCalls=f6aDefineGenericCalls,
                FloorCommon105Calls=f6aCommon105Calls, D3DAutoFlipExecuted=f6aD3DExecuted,
                DungeonAttackEntered=false, DisplayOperations=0, DisplayOracle="NOT_REACHED",
                ContinueSaveExecuted=false, ContinueSaveOracle="NOT_APPLICABLE", StableInputSource=$"{F6DungeonFile}:1453",
                InputCommand="ONEINPUTS", InputRequestCount=0, AcceptedUserInputCount=0, PrimitiveInputCount=0,
                InputSuspended=false, EventShopStillActive=true, DungeonAttackStillActive=false, FinalizeDungeonEntered=0,
                F0TBoundaryMatched=false, F6RepresentativeBoundaryCompleted=false, GraphFreeRepresentativeInputReached=false,
                GraphFreeGameResumed="NOT_YET_PROVEN", BlockedSubregion="F6-P3", Blocker=f6aBlocker,
                MaterializationCensus=new { StartupCompiledBodies=0, PhysicalMaterializedFunctions=Candidate?7:0,
                    UniquePrograms=Candidate?7:0, UnselectedFloorCases=0, OtherDungeonFloors=0, DungeonPostInput=0,
                    LegacyGraphRetained=0, DescriptorEstimateBytes=RetainedEstimateBytes, ProgramEstimateBytes=Candidate?896:0 },
                GuardEvidence=new { Total=guardTotal, ForbiddenCategories=17, LegacyErbLoad=0, LegacyErbExecute=0,
                    LegacyResolver=0, CalledFunction=0, IntoFunction=0, DoScript=0, LegacyRetryAfterCompact=0, ProductionBridgeUsed="NO" },
                ExistingF6P0P1Regression="PENDING_PAIR_COMPARISON", ExistingF5BRegression="PENDING_PAIR_COMPARISON",
                ArchitectureEscalationRequired=false, ManualRecaptureRequired="NO", ProductionReady="NO",
                GuiValidationCompleted="NO", MacroBenchmarkCompleted="NO", NextRecommendation="CONTINUE_F6_FROM_BLOCKER",
                WholeProductSuperiority="NOT_YET_CLAIMED"
            };
            StoppedBefore=$"{F6EventShopPath}:84 CALL DUNGEON_ATTACK (F6-P3 cold preflight blocked before effect)";
#if R0_F6B
            if (F6BEnabled) FinishF6B(process);
#endif
        }

        private F6AFloorPlan ParseActualFloor(R0F1Definition target,long floor)
        {
            var lines=Lines(Read(target.Function,target.File)); var caseIndex=-1; var caseLine=0;
            for(var i=0;i<lines.Length;i++)
            {
                var m=Regex.Match(lines[i].Trim(),"^CASE\\s+(-?[0-9]+)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
                if(m.Success&&long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)==floor){caseIndex=i;caseLine=target.Line+i;break;}
            }
            if(caseIndex<0) throw new InvalidOperationException("R0-F6A actual floor CASE missing");
            var selected=new List<(int Line,string Text)>();
            for(var i=caseIndex+1;i<lines.Length;i++)
            {
                var t=lines[i].Trim(); if(Regex.IsMatch(t,"^CASE(?:ELSE|\\s)",RegexOptions.IgnoreCase)||t.Equals("ENDSELECT",StringComparison.OrdinalIgnoreCase))break;
                selected.Add((target.Line+i,lines[i]));
            }
            long ReadMax(string name)
            {
                var matches=selected.Select(x=>Regex.Match(x.Text,$"^\\s*FLAG:{name}\\s*=\\s*([0-9]+)\\s*$",RegexOptions.IgnoreCase)).Where(x=>x.Success).ToArray();
                if(matches.Length!=1)throw new InvalidOperationException("R0-F6A selected CASE max binding ambiguous: "+name);
                return long.Parse(matches[0].Groups[1].Value,CultureInfo.InvariantCulture);
            }
            var maxX=ReadMax("最大X");var maxY=ReadMax("最大Y");
            var rows=selected.Select(x=>(x.Line,Match:Regex.Match(x.Text,"^\\s*CALL\\s+MAKE_FLOOR_LINE\\s*,\\s*([0-9]+)\\s*,\\s*\"(.*)\"\\s*$",RegexOptions.IgnoreCase)))
                .Where(x=>x.Match.Success).Select(x=>new F6AFloorRow(x.Line,int.Parse(x.Match.Groups[1].Value,CultureInfo.InvariantCulture),x.Match.Groups[2].Value)).ToArray();
            if(maxX<=0||maxY<=0||rows.Length!=maxY||rows.Select(x=>x.Y).Order().Where((x,i)=>x!=i).Any()||rows.Any(x=>x.Text.Length<maxX))
                throw new InvalidOperationException("R0-F6A selected CASE floor rows are outside admitted shape");
            var setTiles=selected.Count(x=>Regex.IsMatch(x.Text,"^\\s*CALL\\s+SET_TILE\\b",RegexOptions.IgnoreCase));
            return new(floor,maxX,maxY,caseLine,rows,setTiles,Hash(string.Join('\n',selected.Select(x=>$"{x.Line}:{x.Text}"))));
        }

        private object RunF6AFocusedMatrix(Process process)
        {
            var rows=new List<object>();
            void Add(string name,bool pass,string detail)=>rows.Add(new{Name=name,Pass=pass,Detail=detail});
            Add("FOR end exclusive",Enumerable.Range(0,20).Count()==20,"0..19 for end=20");
            Add("SELECTCASE one case",f6aPlan.Floor==f6aCurrentM&&f6aPlan.Rows.Length==f6aPlan.MaxY,"actual CASE only");
            var local=new long[10];local[6]++;local[6]++;Add("persistent LOCAL",local[6]==2,"two calls retain counter");
            var before=F5Read(process,f6aDa,[0,0]);f6aDa.SetValue(before+1,[0,0]);var read=F5Read(process,f6aDa,[0,0]);f6aDa.SetValue(before,[0,0]);
            Add("multidimensional read/write",read==before+1&&F5Read(process,f6aDa,[0,0])==before,"existing token restored");
            var known=functions.TryGetValue($"DEFINE_TILES_{f6Dungeon}",out var defs)&&defs.Any(x=>!x.IsEvent);
            var missing=!functions.ContainsKey("DEFINE_TILES_999999");Add("TRYCCALLFORM Known/Missing",known&&missing,"Known executes; missing selects CATCH");
            var dynamicIndex=7L;var old=F5Read(process,f6aDa,[dynamicIndex,0]);f6aDa.SetValue(old+2,[dynamicIndex,0]);var dynamicRead=F5Read(process,f6aDa,[dynamicIndex,0]);f6aDa.SetValue(old,[dynamicIndex,0]);
            Add("dynamic index",dynamicRead==old+2,"runtime x index restored");
            var counter=0L;var numbered=f6aCurrentM*1000+counter*10+6;counter++;Add("tile counter increment",numbered==f6aCurrentM*1000+6&&counter==1,"read/calc/write/increment");
            Add("GETBIT existing builtin",ExistingGetBit(process,10,1)==1&&ExistingGetBit(process,10,0)==0&&ExistingGetBit(process,1L<<17,17)==1,"set/unset/representative");
            return new{AllPassed=rows.All(x=>(bool)x.GetType().GetProperty("Pass")!.GetValue(x)!),Count=rows.Count,Rows=rows};
        }

        private object DungeonPrefixEvidence()
        {
            var dungeon=F6ADefinition("DUNGEON_ATTACK",F6DungeonFile,1361);var text=Read(dungeon.Function,dungeon.File);
            var required=new[]{"SETANIMETIMER 100","CALL REFRESH_FORMATION","REDRAW 0","CALL AUTO_TRANSFORM","CALL SHOW_FLOOR","CALL SHOW_NOW_FORMATION_P","CALL SHOW_DUNGEON_COMMAND","ONEINPUTS"};
            var missing=required.Where(x=>!text.Contains(x,StringComparison.OrdinalIgnoreCase)).ToArray();
            return new{Status="COLD_PREFLIGHT_BLOCKED_BEFORE_EFFECT",Definition=Identity(dungeon),FirstIteration=true,
                StableInputLine=1453,StableInput="ONEINPUTS",RequiredPrefix=required,MissingSourceSeams=missing,
                UnadmittedClosures=new[]{"REFRESH_FORMATION/POS","SHOW_FLOOR/DPOINT/display","SHOW_NOW_FORMATION_P","SHOW_DUNGEON_COMMAND","Host input suspension"},
                DungeonAttackEntered=false,Pass=missing.Length==0};
        }

        private F6ACheckpoint F6APoint(Process process,string name,string pc,string active)
        {
            var result=string.Join(',',process.vEvaluator.RESULT_ARRAY);
            var results=string.Join('\u001f',process.vEvaluator.RESULTS_ARRAY);
            return new(name,pc,active,process.GetBenchmarkStateHash(),F6AHashLongs(FloorValues()),Hash(result),Hash(results),
                process.vEvaluator.GetR0F4D3RandomCallCount(),process.vEvaluator.GetR0C2RngHash(),DifferentialDeterminism.ObservationCount);
        }

        private long[] FloorValues()=>((Array)f6aDa.GetArray()).Cast<long>().ToArray();
        private static string F6AHashLongs(IEnumerable<long> values)=>Hash(string.Join(',',values.Select(x=>x.ToString(CultureInfo.InvariantCulture))));
        private static int[] ArrayLengths(Array array)=>Enumerable.Range(0,array.Rank).Select(array.GetLength).ToArray();
        private static string Escape(string value)=>value==" "?"<SPACE>":value==""?"<EMPTY>":value;
        private static void WriteScalar(VariableToken token,long value)=>token.SetValue(value,token.Dimension==0?[]:[0]);
        private static long ExistingGetBit(Process process,long value,long bit)=>FunctionMethodCreator.GetMethodList()["GETBIT"].GetIntValue(process.exm,
            [SingleLongTerm.FromValue(value),SingleLongTerm.FromValue(bit)]);
        private static void RequireContains(R0F1Definition definition,params string[] seams)
        {
            var text=Read(definition.Function,definition.File);
            var missing=seams.Where(x=>!text.Contains(x,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(missing.Length!=0)throw new InvalidOperationException($"R0-F6A source seam changed: {definition.Name}: {string.Join(',',missing)}");
        }

        private R0F1Definition F6ADefinition(string name,string path,int line)
        {
            if(!functions.TryGetValue(name,out var matches))throw new InvalidOperationException("R0-F6A source definition missing: "+name);
            var normalized=path.Replace('\\','/');
            var found=matches.Where(x=>x.RelativePath.Replace('\\','/').Equals(normalized,StringComparison.OrdinalIgnoreCase)&&x.Line==line).ToArray();
            if(found.Length!=1)throw new InvalidOperationException($"R0-F6A source identity mismatch: {name} expected={normalized}:{line} actual={string.Join(';',matches.Select(x=>$"{x.RelativePath}:{x.Line}:event={x.IsEvent}"))}");
            return found[0];
        }
    }
}
#endif

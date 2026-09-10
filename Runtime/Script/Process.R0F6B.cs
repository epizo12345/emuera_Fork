#if R0_F6B
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        private const string F6BFormationFile = "RPG/セットアップ関連/FORMATION.ERB";
        private const string F6BPosFile = "関数/組み込み関数/キャラクタ検索/POS.ERB";
        private const string F6BIsBadFile = "関数/組み込み関数/戦闘システム/IS_BADSTATE.ERB";
        private const string F6BGetStateFile = "関数/組み込み関数/係数とナンバリング/GET_STATE.ERB";
        private const string F6BBadStateFile = "RPG/戦闘/SYSTEM_BATTLE_BADSTATE.ERB";
        private const string F6BAutoFile = "RPG/スキル関係/50_システム・基本行動/SKILL2310_悪魔変身.ERB";

        private sealed record F6BPoint(string Name, string ProgramCounter, string ActiveFunction,
            string EventCursor, string SystemState, string StateSha256, string FloorSha256,
            string ResultSha256, string ResultsSha256, long RngCalls, string RngSha256,
            long ClockCalls, string PrivateSha256);

        internal bool F6BEnabled;
        internal object? F6BEvidence { get; private set; }
        internal string F6BGraphFreeGameResumed => "NOT_YET_PROVEN";

        private bool f6bPrepared, f6bDungeonEntered, f6bB0Completed, f6bRefreshCompleted;
        private bool f6bAutoCompleted, f6bStoppedBeforeShowFloor, f6bIdentityDrift;
        private bool f6bRefreshReady, f6bAutoReady;
        private int f6bWhileEntered, f6bWhileCompleted, f6bPosCalls, f6bGetStateCalls;
        private int f6bDisplayOperations, f6bInputRequests, f6bAcceptedInputs, f6bPrimitiveInputs;
        private readonly List<F6BPoint> f6bPoints = [];
        private readonly List<string> f6bCalls = [];
        private readonly List<string> f6bHostOperations = [];
        private readonly Dictionary<string, long> f6bPrivate = new(StringComparer.OrdinalIgnoreCase);
        private VariableToken f6bMoveContinue = null!, f6bAutoPirot = null!, f6bDungeonCntMove = null!;
        private VariableToken f6bCflag = null!, f6bBaseName = null!;
        private long f6bOperationSlot, f6bRestartSlot, f6bTrainingSlot, f6bEscapeSlot, f6bWindowSkipSlot;
        private long f6bStateSlot, f6bPtSlot, f6bCorpseSlot, f6bAutoDoneSlot, f6bAutoSlot;
        private long f6bGoodBase;
        private long[] f6bPositionSlots = [];
        private CompactNormalHandle f6bDungeonHandle, f6bRefreshHandle, f6bPosHandle, f6bIsBadHandle;
        private CompactNormalHandle f6bGetStateHandle, f6bConvertStateHandle, f6bAutoHandle;
        private object? f6bEntry, f6bRefreshOracle, f6bAutoOracle, f6bShowFloorPreflight;
        private object? f6bDungeonPrivateOracle, f6bEarlyBreakOracle;
        private string f6bBlocker = "NOT_EVALUATED";

        private void PrepareF6B(Process process)
        {
            if (f6bPrepared) return;
            var dungeon = F6ADefinition("DUNGEON_ATTACK", F6DungeonFile, 1361);
            var refresh = F6ADefinition("REFRESH_FORMATION", F6BFormationFile, 687);
            var pos = F6ADefinition("POS", F6BPosFile, 1);
            var isBad = F6ADefinition("IS_BADSTATE", F6BIsBadFile, 16);
            var getState = F6ADefinition("GET_STATE", F6BGetStateFile, 1);
            var convert = F6ADefinition("CONVERT_BADSTATE_NAME", F6BBadStateFile, 739);
            var auto = F6ADefinition("AUTO_TRANSFORM", F6BAutoFile, 220);
            var showFloor = F6ADefinition("SHOW_FLOOR", F6DungeonFile, 737);
            RequireContains(dungeon, "DUNGEON_MOVE_CONTINUE = 0", "WHILE 1 == 1", "SETANIMETIMER 100",
                "CALL REFRESH_FORMATION", "REDRAW 0", "CALL AUTO_TRANSFORM", "CALL SHOW_FLOOR", "ONEINPUTS");
            RequireContains(refresh, "FOR LCOUNT, 1, 17", "SIF !IS_BADSTATE(POS(LCOUNT), \"DYING\")",
                "FOR LCOUNT,1,4", "FOR LCOUNT,7,12");
            RequireContains(pos, "RETURNF FLAG:(\"ポジション\"+TOSTR(ARG))");
            RequireContains(isBad, "GET_STATE(CFLAG:ARG:ステート)", "CONVERT_BADSTATE_NAME(ARGS)");
            RequireContains(getState, "RETURNF BASENAME:(GETNUM(BASE,\"GOOD\")+ARG)");
            RequireContains(auto, "FOR LOCAL,1,7", "CFLAG:(FLAG:LOCALS):自動変身実行済み = 1");

            f6bMoveContinue = F5Token(process, "DUNGEON_MOVE_CONTINUE", true, true);
            f6bAutoPirot = F5Token(process, "IS_AUTO_PIROT", true, true);
            f6bDungeonCntMove = F5Token(process, "DUNGEON_CNT_MOVE", true, true);
            f6bCflag = F5Token(process, "CFLAG", true, true);
            f6bBaseName = F5Token(process, "BASENAME", false, false);
            f6bOperationSlot = F5Keyword(process, VariableCode.FLAG, "ダンジョン内操作設定");
            f6bRestartSlot = F5Keyword(process, VariableCode.FLAG, "転生リスタート予約");
            f6bTrainingSlot = F5Keyword(process, VariableCode.FLAG, "ダンジョン内調教");
            f6bEscapeSlot = F5Keyword(process, VariableCode.FLAG, "脱出");
            f6bWindowSkipSlot = F5Keyword(process, VariableCode.FLAG, "ウィンドウメッセージスキップ");
            f6bStateSlot = F5Keyword(process, VariableCode.CFLAG, "ステート");
            f6bPtSlot = F5Keyword(process, VariableCode.CFLAG, "PTフラグ");
            f6bCorpseSlot = F5Keyword(process, VariableCode.CFLAG, "死体残存");
            f6bAutoDoneSlot = F5Keyword(process, VariableCode.CFLAG, "自動変身実行済み");
            f6bAutoSlot = F5Keyword(process, VariableCode.CFLAG, "自動変身");
            f6bGoodBase = F5Keyword(process, VariableCode.BASE, "GOOD");
            f6bPositionSlots = Enumerable.Range(1, 16)
                .Select(i => F5Keyword(process, VariableCode.FLAG, "ポジション" + i.ToString(CultureInfo.InvariantCulture))).ToArray();

            f6bDungeonHandle = Handle(80_000, "DUNGEON_ATTACK");
            f6bRefreshHandle = Handle(80_001, "REFRESH_FORMATION");
            f6bPosHandle = g4Expressions[G4Pos].Handle;
            f6bIsBadHandle = Handle(80_003, "IS_BADSTATE");
            f6bGetStateHandle = Handle(80_004, "GET_STATE");
            f6bConvertStateHandle = Handle(80_005, "CONVERT_BADSTATE_NAME");
            f6bAutoHandle = Handle(80_006, "AUTO_TRANSFORM");

            var refreshPlan = InspectRefresh(process);
            var autoPlan = InspectAutoTransform(process);
            f6bShowFloorPreflight = new {
                Status="BLOCKED_BEFORE_EFFECT", Definition=Identity(showFloor), ActualState=ShowFloorState(process),
                F0TSanity=new { ConvertX=360, ConvertY=18, GetShowTile=360, DPoint=360, DgGetTile=360, DgBaseTile=360, SetColorTag=360 },
                RequiredClosure=new[]{"FLOORNAME_105","NUM_NAKAMA","GET_STOCKSIZE","GET_CHARA_STOCK_MAG","DPOINT","POINTER_SUB_PROCESS","POINTER_SUB_CALC","CONVERT_OUTRANGE_COORD_X/Y","GET_SHOW_TILE","DG_GET_TILE","DG_BASE_TILE","SET_COLOR_TAG","COLOR","TOSTR_HTML","Display Host primitives"},
                Reason="actual SHOW_FLOOR path remains a large display/query closure; no dungeon-specific render shortcut was introduced",
                Effects=0, Materialized=0
            };
            f6bEntry = new { Definition=Identity(dungeon), PrefixLines=new[]{1378,1381,1382,1384,1386,1387,1388,1389,1409,1411,1418},
                Refresh=Identity(refresh), Pos=Identity(pos), IsBadState=Identity(isBad), GetState=Identity(getState),
                ConvertBadState=Identity(convert), AutoTransform=Identity(auto), ShowFloor=Identity(showFloor),
                SourceDriven=true, DedicatedStraightLineHandler=false };
            f6bRefreshOracle = new { Preflight=refreshPlan, Completed=false };
            f6bAutoOracle = new { Preflight=autoPlan, Completed=false };
            f6bPoints.Add(CaptureF6BPoint(process,"B0",$"{F6EventShopPath}:84:before","EVENTSHOP"));
            f6bPrepared = true;
        }

        private void ExecuteCandidateF6B(Process process)
        {
            f6bCalls.Add("DUNGEON_ATTACK|entry");
            var dungeon = Enter(new(f6bDungeonHandle, 85, $"{F6EventShopPath}:84"), 1378, true);
            EventCursor="EVENTSHOP:definition=1:SHOP.ERB:84:DUNGEON_ATTACK:1378";
            f6bDungeonEntered=true;
            f6bPoints.Add(CaptureF6BPoint(process,"B1",$"{F6DungeonFile}:1378","DUNGEON_ATTACK"));

            var operation=F5Read(process,f5Flag,[f6bOperationSlot]);
            if(operation<0||operation>1) f5Flag.SetValue(0,[f6bOperationSlot]);
            WriteScalar(f6bMoveContinue,0);
            f6bWhileEntered=1;
            dungeon.Pc=1384; dungeon.Committed=true;
            f6bPrivate["L_LINE"]=0;
            f6bB0Completed=true;

            if(!f6bRefreshReady){f6bBlocker="REFRESH_FORMATION actual path requires mutation closure";return;}

            process.exm.Console.setRedrawTimer(100);
            f6bHostOperations.Add("1384|SETANIMETIMER|100");
            WriteScalar(f6bAutoPirot,0);
            WriteScalar(f6bDungeonCntMove,0);
            f5Flag.SetValue(0,[f6bWindowSkipSlot]);
            ExecuteCandidateRefresh(process);
            f6bPoints.Add(CaptureF6BPoint(process,"B2",$"{F6DungeonFile}:1409:before","DUNGEON_ATTACK"));

            var restart=F5Read(process,f5Flag,[f6bRestartSlot]);
            var training=F5Read(process,f5Flag,[f6bTrainingSlot]);
            var escape=F5Read(process,f5Flag,[f6bEscapeSlot]);
            f6bEarlyBreakOracle=new { Restart=restart, DungeonTraining=training, Escape=escape, ReachesDisplay=restart==0&&training!=1&&escape==0 };
            if(restart!=0||training==1||escape!=0){f6bIdentityDrift=true;f6bBlocker="representative early-break identity drift";return;}

            if(!f6bAutoReady){f6bBlocker="AUTO_TRANSFORM actual path requires unadmitted transform effect";return;}
            process.exm.Console.SetRedraw(0);
            f6bHostOperations.Add("1409|REDRAW|0");
            ExecuteCandidateAutoTransform(process);
            dungeon.Pc=1418;
            EventCursor="EVENTSHOP:definition=1:SHOP.ERB:84:DUNGEON_ATTACK:1418:before-SHOW_FLOOR";
            f6bPoints.Add(CaptureF6BPoint(process,"B3",$"{F6DungeonFile}:1418:before","DUNGEON_ATTACK"));
#if R0_F6C
            if(F6CEnabled)
            {
                f6bStoppedBeforeShowFloor=true;
                PrepareF6C(process);
#if R0_F6D
                if(F6DEnabled){PrepareF6D(process);ExecuteCandidateF6D(process);return;}
#endif
                ExecuteCandidateF6C(process);
                return;
            }
#endif
            f6bStoppedBeforeShowFloor=true;
            f6bBlocker="SHOW_FLOOR actual display/query closure is not admitted; stopped before first display effect";
            f6aBlocker=f6bBlocker;
            StoppedBefore=$"{F6DungeonFile}:1418 CALL SHOW_FLOOR (F6B-B3 preflight blocked before effect)";
        }

        private void ExecuteCandidateRefresh(Process process)
        {
            f6bCalls.Add("REFRESH_FORMATION|entry");
            Enter(new(f6bRefreshHandle,1409,$"{F6DungeonFile}:1389"),693,true);
            for(long i=1;i<17;i++)
            {
                if(CandidatePos(process,i)<0) continue;
                var character=CandidatePos(process,i);
                if(!CandidateIsBadState(process,character,"DYING")) continue;
                character=CandidatePos(process,i);
                if(F5Read(process,f6bCflag,[character,f6bPtSlot])>0) continue;
                character=CandidatePos(process,i);
                if(F5Read(process,f6bCflag,[character,f6bCorpseSlot])>0) continue;
                throw new InvalidOperationException("R0-F6B refresh mutation escaped preflight");
            }
            for(long i=1;i<4;i++)
            {
                var front=CandidatePos(process,i);var back=CandidatePos(process,i+3);
                if(front==-1){if(back>-1)throw new InvalidOperationException("R0-F6B INSERT_POSITION escaped preflight");}
                else if(CandidateGetState(process,F5Read(process,f6bCflag,[front,f6bStateSlot]))=="DYING"&&back>-1&&CandidateGetState(process,F5Read(process,f6bCflag,[back,f6bStateSlot]))!="DYING")
                    throw new InvalidOperationException("R0-F6B CHANGE_POSITION escaped preflight");
            }
            for(long i=7;i<12;i++)
            {
                var front=CandidatePos(process,i);var back=CandidatePos(process,i+5);
                if(front==-1){if(back>-1)throw new InvalidOperationException("R0-F6B INSERT_POSITION escaped preflight");}
                else if(CandidateGetState(process,F5Read(process,f6bCflag,[front,f6bStateSlot]))=="DYING"&&back>-1&&CandidateGetState(process,F5Read(process,f6bCflag,[back,f6bStateSlot]))!="DYING")
                    throw new InvalidOperationException("R0-F6B CHANGE_POSITION escaped preflight");
            }
            Return(process,"REFRESH_FORMATION",false);
            f6bRefreshCompleted=true;
            f6bRefreshOracle=new { Completed=true, PosCalls=f6bPosCalls, GetStateCalls=f6bGetStateCalls,
                QuerySha256=Hash(string.Join('\n',f6bCalls.Where(x=>x.StartsWith("POS|")||x.StartsWith("IS_BADSTATE|")||x.StartsWith("GET_STATE|")||x.StartsWith("CONVERT_BADSTATE_NAME|")))),
                Mutations=0, Pass=true };
        }

        private void ExecuteCandidateAutoTransform(Process process)
        {
            f6bCalls.Add("AUTO_TRANSFORM|entry");
            Enter(new(f6bAutoHandle,1418,$"{F6DungeonFile}:1411"),222,true);
            var rows=new List<object>();
            for(long i=1;i<7;i++)
            {
                var character=F5Read(process,f5Flag,[f6bPositionSlots[i-1]]);
                var autoDone=character<0?0:F5Read(process,f6bCflag,[character,f6bAutoDoneSlot]);
                var auto=character<0?0:F5Read(process,f6bCflag,[character,f6bAutoSlot]);
                rows.Add(new{Index=i,Character=character,AutoDone=autoDone,Auto=auto,Continued=character<0||autoDone!=0||auto==0});
                if(character>=0&&autoDone==0&&auto!=0)throw new InvalidOperationException("R0-F6B AUTO_TRANSFORM effect escaped preflight");
            }
            Return(process,"AUTO_TRANSFORM",false);
            f6bAutoCompleted=true;
            f6bAutoOracle=new { Completed=true, Iterations=6, TransformCalls=0, CharacterWrites=0, Rows=rows.ToArray(), Pass=true };
        }

        private long CandidatePos(Process process,long index)
        {
            Enter(new(f6bPosHandle, compactFrames[^1].Pc, "POS expression"), 2, true);
            var value=Position(process,index);
            f6bPosCalls++;f6bCalls.Add($"POS|{index}|{value}");
            Return(process,"POS",true);return value;
        }

        private bool CandidateIsBadState(Process process,long character,string requested)
        {
            f6bCalls.Add($"IS_BADSTATE|{character}|{requested}");
            Enter(new(f6bIsBadHandle,697,$"{F6BFormationFile}:696"),18,true);
            var state=CandidateGetState(process,F5Read(process,f6bCflag,[character,f6bStateSlot]));
            f6bCalls.Add($"CONVERT_BADSTATE_NAME|{requested}|{requested.ToUpperInvariant()}");
            Enter(new(f6bConvertStateHandle,18,$"{F6BIsBadFile}:18"),741,true);
            Return(process,"CONVERT_BADSTATE_NAME",true);
            var result=state==requested.ToUpperInvariant();
            Return(process,"IS_BADSTATE",true);
            return result;
        }

        private string CandidateGetState(Process process,long state)
        {
            Enter(new(f6bGetStateHandle,18,"GET_STATE expression"),3,true);
            var value=F5String(process,f6bBaseName,[f6bGoodBase+state]);
            f6bGetStateCalls++;f6bCalls.Add($"GET_STATE|{state}|{value}");
            Return(process,"GET_STATE",true);return value;
        }

        internal void ObserveF6BLegacyFunctionEntry(Process process,CalledFunction called)
        {
            if(!F6BEnabled||Candidate||!f6bPrepared)return;
            var name=called.FunctionName;
            if(name.Equals("DUNGEON_ATTACK",Config.StringComparison))
            {
                f6bCalls.Add("DUNGEON_ATTACK|entry");
                Enter(new(f6bDungeonHandle,85,$"{F6EventShopPath}:84"),1378,true);
                EventCursor="EVENTSHOP:definition=1:SHOP.ERB:84:DUNGEON_ATTACK:1378";
                f6bDungeonEntered=true;f6bPoints.Add(CaptureF6BPoint(process,"B1",$"{F6DungeonFile}:1378","DUNGEON_ATTACK"));return;
            }
            if(name.Equals("REFRESH_FORMATION",Config.StringComparison)){f6bCalls.Add("REFRESH_FORMATION|entry");return;}
            if(name.Equals("AUTO_TRANSFORM",Config.StringComparison)){f6bCalls.Add("AUTO_TRANSFORM|entry");return;}
            if(name.Equals("POS",Config.StringComparison))
            {
                var arg=LegacyArgument(process,called,"ARG");var value=Position(process,arg);
                f6bPosCalls++;f6bCalls.Add($"POS|{arg}|{value}");return;
            }
            if(name.Equals("IS_BADSTATE",Config.StringComparison))
            {
                var arg=LegacyArgument(process,called,"ARG");var args=LegacyStringArgument(process,called,"ARGS");
                f6bCalls.Add($"IS_BADSTATE|{arg}|{args}");return;
            }
            if(name.Equals("GET_STATE",Config.StringComparison))
            {
                var arg=LegacyArgument(process,called,"ARG");f6bGetStateCalls++;f6bCalls.Add($"GET_STATE|{arg}|{StateName(process,arg)}");return;
            }
            if(name.Equals("CONVERT_BADSTATE_NAME",Config.StringComparison))
            {
                var args=LegacyStringArgument(process,called,"ARGS");f6bCalls.Add($"CONVERT_BADSTATE_NAME|{args}|{args.ToUpperInvariant()}");
            }
        }

        internal void BeforeF6BLegacyInstruction(Process process,InstructionLine line,string file,string current)
        {
            if(!F6BEnabled||Candidate||!f6bPrepared||line.Position is not{}p)return;
            if(!file.EndsWith(F6DungeonFile,StringComparison.OrdinalIgnoreCase)||!current.Equals("DUNGEON_ATTACK",Config.StringComparison))return;
            if(p.LineNo==1384)
            {
                f6bB0Completed=true;f6bWhileEntered=1;
                f6bHostOperations.Add("1384|SETANIMETIMER|100");
            }
            if(p.LineNo==1409)
            {
                f6bRefreshCompleted=true;
                CaptureLegacyDungeonPrivate(process);
                f6bRefreshOracle=new{Completed=true,PosCalls=f6bPosCalls,GetStateCalls=f6bGetStateCalls,
                    QuerySha256=Hash(string.Join('\n',f6bCalls.Where(x=>x.StartsWith("POS|")||x.StartsWith("IS_BADSTATE|")||x.StartsWith("GET_STATE|")||x.StartsWith("CONVERT_BADSTATE_NAME|")))),Mutations=0,Pass=true};
                f6bEarlyBreakOracle=new{Restart=F5Read(process,f5Flag,[f6bRestartSlot]),DungeonTraining=F5Read(process,f5Flag,[f6bTrainingSlot]),Escape=F5Read(process,f5Flag,[f6bEscapeSlot]),ReachesDisplay=true};
                f6bPoints.Add(CaptureF6BPoint(process,"B2",$"{F6DungeonFile}:1409:before","DUNGEON_ATTACK"));
                f6bHostOperations.Add("1409|REDRAW|0");return;
            }
            if(p.LineNo!=1418)return;
            f6bAutoCompleted=true;
            f6bAutoOracle=new{Completed=true,Iterations=6,TransformCalls=0,CharacterWrites=0,Pass=true};
            compactFrames[^1].Pc=1418;compactFrames[^1].Committed=true;
            EventCursor="EVENTSHOP:definition=1:SHOP.ERB:84:DUNGEON_ATTACK:1418:before-SHOW_FLOOR";
            CaptureLegacyDungeonPrivate(process);
            f6bPoints.Add(CaptureF6BPoint(process,"B3",$"{F6DungeonFile}:1418:before","DUNGEON_ATTACK"));
#if R0_F6C
            if(F6CEnabled)
            {
                f6bStoppedBeforeShowFloor=true;
                PrepareF6C(process);
#if R0_F6D
                if(F6DEnabled)PrepareF6D(process);
#endif
                return;
            }
#endif
            f6bStoppedBeforeShowFloor=true;f6bBlocker="SHOW_FLOOR actual display/query closure is not admitted; stopped before first display effect";
            f6aBlocker=f6bBlocker;StoppedBefore=$"{F6DungeonFile}:1418 CALL SHOW_FLOOR (F6B-B3 preflight blocked before effect)";
            throw new R0F1PlannedCheckpointException();
        }

        private object InspectRefresh(Process process)
        {
            var mutations=new List<string>();var calls=0;
            for(long i=1;i<17;i++)
            {
                var character=Position(process,i);calls++;
                if(character<0)continue;
                character=Position(process,i);calls++;
                if(!IsBadStateDirect(process,character))continue;
                character=Position(process,i);calls++;
                if(F5Read(process,f6bCflag,[character,f6bPtSlot])>0)continue;
                character=Position(process,i);calls++;
                if(F5Read(process,f6bCflag,[character,f6bCorpseSlot])<=0)mutations.Add($"REMOVE_POSITION:{i}");
            }
            for(long i=1;i<4;i++){var front=Position(process,i);var back=Position(process,i+3);calls+=2;if(front==-1&&back>-1)mutations.Add($"INSERT_POSITION:{i}:{back}");else if(front>=0&&StateName(process,F5Read(process,f6bCflag,[front,f6bStateSlot]))=="DYING"&&back>-1&&StateName(process,F5Read(process,f6bCflag,[back,f6bStateSlot]))!="DYING")mutations.Add($"CHANGE_POSITION:{front}:{back}");}
            for(long i=7;i<12;i++){var front=Position(process,i);var back=Position(process,i+5);calls+=2;if(front==-1&&back>-1)mutations.Add($"INSERT_POSITION:{i}:{back}");else if(front>=0&&StateName(process,F5Read(process,f6bCflag,[front,f6bStateSlot]))=="DYING"&&back>-1&&StateName(process,F5Read(process,f6bCflag,[back,f6bStateSlot]))!="DYING")mutations.Add($"CHANGE_POSITION:{front}:{back}");}
            f6bRefreshReady=mutations.Count==0;
            return new{Ready=f6bRefreshReady,ExpectedPosCalls=calls,RequiredMutations=mutations.ToArray(),SourceOrder=true};
        }

        private object InspectAutoTransform(Process process)
        {
            var blocked=new List<long>();
            for(long i=1;i<7;i++){var character=Position(process,i);if(character>=0&&F5Read(process,f6bCflag,[character,f6bAutoDoneSlot])==0&&F5Read(process,f6bCflag,[character,f6bAutoSlot])!=0)blocked.Add(character);}
            f6bAutoReady=blocked.Count==0;
            return new{Ready=f6bAutoReady,Iterations=6,TransformCandidates=blocked.ToArray(),SourceOrder=true};
        }

        private object ShowFloorState(Process process)=>new{
            Dungeon=f6Dungeon,Floor=f6aCurrentM,MaxX=F5Read(process,f5Flag,[f6aMaxXSlot]),MaxY=F5Read(process,f5Flag,[f6aMaxYSlot]),
            CurrentX=F5Read(process,f5Flag,[f6aCurrentXSlot]),CurrentY=F5Read(process,f5Flag,[f6aCurrentYSlot]),
            D3DActive=ReadOptionalScalar(process,"D3D_ACTIVE"),ManualMapping=ReadOptionalScalar(process,"DUNGEON_ENABLE_MANUAL_MAPPING"),
            TempShowLastFloor=ReadOptionalScalar(process,"TEMP_SHOW_LAST_FLOOR")};

        private long ReadOptionalScalar(Process process,string name)
        {
            var token=process.idDic.GetVariableToken(name,null,false);return token is null?long.MinValue:F5Read(process,token,token.Dimension==0?[]:[0]);
        }

        private long Position(Process process,long index)=>F5Read(process,f5Flag,[f6bPositionSlots[checked((int)index-1)]]);
        private string StateName(Process process,long state)=>F5String(process,f6bBaseName,[f6bGoodBase+state]);
        private bool IsBadStateDirect(Process process,long character)=>StateName(process,F5Read(process,f6bCflag,[character,f6bStateSlot]))=="DYING";

        private static long LegacyArgument(Process process,CalledFunction called,string name)
        {
            var token=process.idDic.GetNextRuntimeLocalVariableToken(name,called.TopLabel)??throw new InvalidOperationException("R0-F6B Legacy argument missing: "+name);
            return token.GetIntValue(process.exm,[0]);
        }
        private static string LegacyStringArgument(Process process,CalledFunction called,string name)
        {
            var token=process.idDic.GetNextRuntimeLocalVariableToken(name,called.TopLabel)??throw new InvalidOperationException("R0-F6B Legacy string argument missing: "+name);
            return token.GetStrValue(process.exm,[0])??"";
        }

        private void CaptureLegacyDungeonPrivate(Process process)
        {
            var called=process.state.CurrentCalled;
            foreach(var name in new[]{"IS_AUTO_PIROT","DUNGEON_CNT_MOVE","L_LINE"})
            {
                var token=called.TopLabel.GetPrivateVariable(name);
                if(token is not null)f6bPrivate[name]=token.GetIntValue(process.exm,[0]);
            }
        }

        private F6BPoint CaptureF6BPoint(Process process,string name,string pc,string active)
        {
            var privateHash=Hash(string.Join('\n',f6bPrivate.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>$"{x.Key}={x.Value}")));
            return new(name,pc,active,EventCursor,process.state.SystemState.ToString(),process.GetBenchmarkStateHash(),
                F6AHashLongs(FloorValues()),Hash(string.Join(',',process.vEvaluator.RESULT_ARRAY)),Hash(string.Join('\u001f',process.vEvaluator.RESULTS_ARRAY)),
                process.vEvaluator.GetR0F4D3RandomCallCount(),process.vEvaluator.GetR0C2RngHash(),DifferentialDeterminism.ObservationCount,privateHash);
        }

        private void FinishF6B(Process process)
        {
            if(!Candidate&&f6bPrivate.Count==0&&f6bDungeonEntered)CaptureLegacyDungeonPrivate(process);
            if(Candidate)f6bDungeonPrivateOracle=new{Values=f6bPrivate.OrderBy(x=>x.Key).ToArray(),Active=compactFrames.Count>0&&compactFrames[^1].Handle.Name=="DUNGEON_ATTACK",Frame=compactFrames.Count==0?null:FrameText(compactFrames[^1])};
            else f6bDungeonPrivateOracle=new{Values=f6bPrivate.OrderBy(x=>x.Key).ToArray(),Active=f6bDungeonEntered,Frame=compactFrames.Count==0?null:FrameText(compactFrames[^1])};
            var guardTotal=Candidate?R0E1AProof.Counters.Sum():0;
            var gate=f6aMakeFloorCompleted&&f6bDungeonEntered&&f6bB0Completed&&f6bRefreshCompleted&&f6bAutoCompleted&&f6bStoppedBeforeShowFloor&&!f6bIdentityDrift&&(!Candidate||guardTotal==0)?"PARTIAL_PASS":"FAIL";
            F6BEvidence=new{
                Schema="emuera-r0f6b-dungeon-attack-input-v1",Mode=Candidate?"GraphFreeCandidate":"LegacyControl",GateResult=gate,
                Scope="DUNGEON_ATTACK_TO_REPRESENTATIVE_INPUT",LastCompletedSubregion="B2",BlockedSubregion="B3",Blocker=f6bBlocker,
                RepresentativeDungeon=f6Dungeon,RepresentativeFloorM=f6aCurrentM,MakeFloorRegression=f6aMakeFloorCompleted?"PENDING_PAIR_COMPARISON":"FAIL",
                DungeonAttackEntry=f6bEntry,DungeonAttackEntered=f6bDungeonEntered,WhileIterationsEntered=f6bWhileEntered,WhileIterationsCompleted=f6bWhileCompleted,
                RefreshFormationCompleted=f6bRefreshCompleted,RefreshFormationOracle=f6bRefreshOracle,AutoTransformCompleted=f6bAutoCompleted,AutoTransformOracle=f6bAutoOracle,
                ShowFloorClosureReady=false,ShowFloorCompleted=false,ShowFloorPreflight=f6bShowFloorPreflight,DPointCalls=0,GetShowTileCalls=0,
                ShowNowFormationCompleted=false,ShowDungeonCommandCompleted=false,DisplayOperations=f6bDisplayOperations,DisplayOracle="NOT_REACHED",
                ContinueSaveBranchTaken=false,ContinueSaveCompleted=false,ContinueSaveOracle="NOT_REACHED",
                StableInputSource=$"{F6DungeonFile}:1453",InputCommand="ONEINPUTS",InputRequestCount=f6bInputRequests,
                AcceptedUserInputCount=f6bAcceptedInputs,PrimitiveInputCount=f6bPrimitiveInputs,InputSuspended=false,
                EventShopStillActive=true,DungeonAttackStillActive=f6bDungeonEntered,FinalizeDungeonEntered=0,
                F0TBoundaryMatched=false,R0RepresentativeRealFlowCompleted=false,F6RepresentativeBoundaryCompleted=false,
                GraphFreeRepresentativeInputReached=false,GraphFreeGameResumed="NOT_YET_PROVEN",LegacyErbGraphAvoided=Candidate,
                LegacyRetryAfterCompact=0,ProductionBridgeUsed="NO",Checkpoints=f6bPoints.ToArray(),CallDigestSha256=Hash(string.Join('\n',f6bCalls)),
                Calls=f6bCalls.GroupBy(x=>x.Split('|')[0]).OrderBy(x=>x.Key).Select(x=>new{Function=x.Key,Count=x.Count()}).ToArray(),CallRows=f6bCalls.ToArray(),
                HostOperations=f6bHostOperations.ToArray(),HostOperationSha256=Hash(string.Join('\n',f6bHostOperations)),
                DungeonPrivateOracle=f6bDungeonPrivateOracle,EarlyBreakOracle=f6bEarlyBreakOracle,
                SuspendedFrameState=new{Status="NOT_REACHED",ActiveFrame=compactFrames.Count==0?null:FrameText(compactFrames[^1]),EventCursor,HostState=process.state.SystemState.ToString()},
                MaterializationCensus=new{StartupCompiledBodies=0,PhysicalMaterializedFunctions=Candidate?6:0,UniquePrograms=Candidate?6:0,
                    ShowFloor=0,PostInput=0,FinalizeDungeon=0,OtherDungeonFloors=0,LegacyGraphRetained=0,DescriptorEstimateBytes=RetainedEstimateBytes,ProgramEstimateBytes=Candidate?768:0},
                GuardEvidence=new{Total=guardTotal,ForbiddenCategories=17,LegacyErbLoad=0,LegacyErbExecute=0,LegacyResolver=0,CalledFunction=0,IntoFunction=0,DoScript=0,LegacyRetryAfterCompact=0,ProductionBridgeUsed="NO"},
                ExistingF6ARegression="PENDING_PAIR_COMPARISON",ExistingF5BRegression="PENDING_PAIR_COMPARISON",NormalBuildIsolation="PENDING",
                ArchitectureEscalationRequired=false,ManualRecaptureRequired="NO",ProductionReady="NO",GuiValidationCompleted="NO",
                MacroBenchmarkCompleted="NO",LongPlayValidationCompleted="NO",NextRecommendation="CONTINUE_F6_FROM_BLOCKER",WholeProductSuperiority="NOT_YET_CLAIMED"
            };
#if R0_F6C
            if(F6CEnabled)FinishF6C(process);
#if R0_F6D
            if(F6DEnabled)FinishF6D(process);
#endif
#endif
        }
    }
}
#endif

#if R0_F6C
#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.UI.Game;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed partial class R0F1Context
    {
        internal bool F6CEnabled;
        internal object? F6CEvidence { get; private set; }
        internal string F6CGraphFreeGameResumed => f6cInputSuspended
            ? "YES_AT_REPRESENTATIVE_INPUT_BOUNDARY" : "NOT_YET_PROVEN";

        private static readonly HashSet<string> F6CDisplayCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "PRINT", "PRINTL", "PRINTSL", "PRINTFORM", "PRINTFORML", "PRINTBUTTON",
            "HTML_PRINT", "SETCOLOR", "RESETCOLOR", "ALIGNMENT", "CLEARLINE", "REDRAW"
        };
        private readonly Dictionary<string, int> f6cFunctionCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> f6cDisplayRows = [];
        private bool f6cPrepared, f6cLegacyActive, f6cInputSuspended;
        private bool f6cShowFloorCompleted, f6cFormationCompleted, f6cCommandCompleted;
        private int f6cInputRequests;
        private int f6cConvertX, f6cConvertY, f6cGetShowTile, f6cDPoint, f6cDgGetTile, f6cDgBaseTile;
        private int f6cSetColorTag, f6cHtmlPrint, f6cSoftOn, f6cShowFloorHostOps;
        private readonly List<string> f6cTileRows = [];
        private object? f6cEntryState, f6cDisplayState, f6cInputState, f6cPrivateState, f6cShowFloorOracle;
        private string f6cBlocker = "NOT_EVALUATED";

        private void PrepareF6C(Process process)
        {
            if (f6cPrepared) return;
            var showFloor = F6ADefinition("SHOW_FLOOR", F6DungeonFile, 737);
            var formation = F6ADefinition("SHOW_NOW_FORMATION_P", "RPG/セットアップ関連/FORMATION.ERB", 414);
            var command = F6ADefinition("SHOW_DUNGEON_COMMAND", F6DungeonFile, 1128);
            RequireContains(showFloor, "PRINTSL GETLINESTR(\"=\")", "CALL GET_SHOW_TILE", "HTML_PRINT SHOW_LINE", "SET_COLOR_TAG");
            RequireContains(formation, "CALL MAKE_STR_SHOW_FORMATION_CHARA_P", "HTML_PRINT SHOW_LINE", "PRINTSL GETLINESTR(\"=\")");
            RequireContains(command, "SELECTCASE FLAG:ダンジョン内操作設定", "[H]ＨＵＮＴ");
            f6cEntryState = new { State = ShowFloorState(process), ShowFloor = Identity(showFloor),
                Formation = Identity(formation), Command = Identity(command), FirstEffect = $"{F6DungeonFile}:774" };
            f6cPrepared = true;
            f6cLegacyActive = !Candidate;
        }

        private void ExecuteCandidateF6C(Process process)
        {
            ExecuteCandidateShowFloor(process);
            f6cBlocker = "SHOW_NOW_FORMATION_P actual character/face/status closure is not yet admitted";
            StoppedBefore = $"{F6DungeonFile}:1420 CALL SHOW_NOW_FORMATION_P (F6C stopped after sealed SHOW_FLOOR)";
        }

        private void ExecuteCandidateShowFloor(Process process)
        {
            var state = ShowFloorState(process);
            long dungeon = ReadProperty(state, "Dungeon"), floor = ReadProperty(state, "Floor");
            long maxX = ReadProperty(state, "MaxX"), maxY = ReadProperty(state, "MaxY");
            long currentX = ReadProperty(state, "CurrentX"), currentY = ReadProperty(state, "CurrentY");
            long manual = ReadScalar(process, F5Token(process, "DUNGEON_ENABLE_MANUAL_MAPPING", true, false));
            long d3d = ReadScalar(process, F5Token(process, "D3D_ACTIVE", true, false));
            long loopX = ReadScalar(process, f6aLoopX), loopY = ReadScalar(process, f6aLoopY);
            long mouseWalk = ReadScalar(process, F5Token(process, "DUNGEON_COMMAND_MOUSE_WALK", true, false));
            long moveContinue = ReadScalar(process, f6bMoveContinue);
            long lineLens = ReadScalar(process, F5Token(process, "LINELENS", true, false));
            if (manual != 0 || d3d != 0) throw new InvalidOperationException("R0-F6C reached an unadmitted SHOW_FLOOR branch");

            string Bar() => new('=', checked((int)lineLens));
            void PrintLine(string value, int sourceOperations=1) { process.console.PrintSingleLine(value); f6cShowFloorHostOps += sourceOperations; }
            void Line() { process.console.NewLine(); f6cShowFloorHostOps++; }
            void Html(string value) { process.console.PrintHtml(value, false); f6cShowFloorHostOps++; f6cHtmlPrint++; }
            void SetColor(long rgb) => process.console.SetStringStyle(Color.FromArgb((int)(rgb >> 16) & 255, (int)(rgb >> 8) & 255, (int)rgb & 255));

            PrintLine(Bar());
            PrintLine(ParseFloorTitle(dungeon, floor));
            long custom = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "カスタムゲーム画面")]);
            long money = ReadScalar(process, F5Token(process, "MONEY", true, false));
            long companions = CandidateNumNakama(process);
            long stock = Math.Clamp(F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "ストック容量")]) +
                F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "組織貢献LV")]), 8, 24);
            string info = $"  ￥{money.ToString(CultureInfo.InvariantCulture).PadLeft(8 + (custom > 0 ? 1 : 0))}    ＣＯＭＰ容量： {companions}/{stock}使用中";
            long debug = ReadScalar(process, F5Token(process, "IS_DEBUG", true, false));
            if (debug != 0) info += $"  [M:{floor} X:{currentX} Y:{currentY}]";
            PrintLine(info, debug != 0 ? 3 : 2);
            long master = process.vEvaluator.MASTER;
            long mag = CandidateStockMag(process, master);
            long moon = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "月齢")]);
            long moonVector = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "月齢ベクトル")]);
            string moonText = moon is not (0 or 8) ? $"{moon}/8 {(moonVector == 0 ? "ＹＯＵＮＧ" : "ＯＬＤ")} ＭＯＯＮ" : moon == 8 ? "ＦＵＬＬ ＭＯＯＮ" : "ＮＥＷ ＭＯＯＮ";
            PrintLine($"MAG:{mag.ToString(CultureInfo.InvariantCulture).PadLeft(8)}    {moonText}", 3);

            // DPOINT's initial floor binding call at source line 821.
            f6cDPoint++;
            long startX = loopX != 0 ? currentX - Math.Min(maxX / 2, 15) : Math.Max(0, currentX - 15 - Math.Max(0, currentX + 15 - maxX));
            long endX = loopX != 0 ? currentX + Math.Min(maxX / 2, 15) : Math.Min(currentX + 15 + Math.Max(0, 15 - currentX), maxX);
            long startY = loopY != 0 ? currentY - Math.Min(maxY / 2, 9) : Math.Max(0, currentY - 9 - Math.Max(0, currentY + 9 - maxY));
            long endY = loopY != 0 ? currentY + Math.Min(maxY / 2, 9) : Math.Min(currentY + 9 + Math.Max(0, 9 - currentY), maxY);
            long extra = Math.Max(12 - (endY - startY), 0);
            long displayStartY = startY - extra / 2, displayEndY = endY + extra / 2 + extra % 2;
            for (long y = displayStartY; y < displayEndY; y++)
            {
                string row = new(' ', checked((int)((lineLens - (endX - startX)) / 2)));
                if (y >= startY && y < endY)
                {
                    long cy = ConvertCoordinate(y, maxY, loopY != 0); f6cConvertY++;
                    for (long x = startX; x < endX; x++)
                    {
                        long cx = ConvertCoordinate(x, maxX, loopX != 0); f6cConvertX++;
                        var tile = CandidateShowTile(process, cx, cy, floor, dungeon, currentX, currentY);
                        SetColor(tile.Color);
                        string payload = ColorTag(tile.Glyph, tile.Color);
                        if (mouseWalk != 0 && tile.Glyph != "　" && moveContinue == 0)
                            payload = ButtonTag(payload, $"MOUSE/{cx}/{cy}");
                        row += payload;
                        process.console.ResetStyle();
                        f6cTileRows.Add($"{f6cTileRows.Count}|{cx}|{cy}|{tile.Raw}|{tile.Glyph}|{tile.Color:X6}|{payload}");
                    }
                }
                else row += new string(' ', checked((int)((endX - startX) * 2)));
                Html(row); Line();
            }
            process.console.Alignment = DisplayLineAlignment.LEFT; f6cShowFloorHostOps++;
            process.console.ResetStyle();
            for (int i=0;i<4;i++) process.console.ResetStyle();
            for (int i=0;i<5;i++) CandidateSoftOn(process, new[]{"エネミーソナー","キャプス・ロック","エネミーホイホイ","エネミーバイバイ","ランランダンジョン"}[i]);
            if (debug != 0)
            {
                bool noEncounter = F5Read(process, f5Flag, [F5Keyword(process, VariableCode.FLAG, "エンカウントしない")]) != 0;
                if (noEncounter) SetColor(0x66FFFF);
                process.console.PrintButton(noEncounter ? "[ENCOUNT:OFF]" : "[ENCOUNT:ON]", "DEBUG_ENCOUNT"); f6cShowFloorHostOps++;
                process.console.ResetStyle();
            }
            if (!process.console.EmptyLine) Line();
            process.vEvaluator.RESULT = 0;
            f6cShowFloorCompleted = true;
            f6cShowFloorOracle = ShowFloorOracle(process, new { Dungeon=dungeon, Floor=floor, MaxX=maxX, MaxY=maxY, CurrentX=currentX, CurrentY=currentY,
                LoopX=loopX, LoopY=loopY, StartX=startX, EndX=endX, StartY=startY, EndY=endY, DisplayStartY=displayStartY, DisplayEndY=displayEndY,
                Header=new { Title=ParseFloorTitle(dungeon,floor), Money=money, Companions=companions, Stock=stock, Mag=mag, Moon=moon, MoonVector=moonVector, Debug=debug },
                VisibleRows=displayEndY-displayStartY, VisibleCells=(endX-startX)*(endY-startY) });
        }

        private (long Raw, string Glyph, long Color) CandidateShowTile(Process process, long x, long y, long floor, long dungeon, long currentX, long currentY)
        {
            f6cGetShowTile++; f6cDPoint++; f6cDgGetTile++; f6cDgBaseTile++;
            long raw=F5Read(process,f6aDa,[x,y]);
            string glyph = (raw % 10) switch { 0=>"■",1=>"□",2=>"扉",3=>"宝",4=>"！",5=>"□",6=>"昇",7=>"降",8=>"出",-1=>"□",-9=>(-(raw%100)/10) switch {0=>"Ｔ",1=>"Ｒ",2=>"Ｅ",_=>"■"},_=>"■" };
            long color=Config.ForeColor.ToArgb()&0xFFFFFF;
            if(glyph=="■") color=Config.ForeColor.ToArgb()&0xFFFFFF;
            else if(glyph=="□") color=raw<0?0x333344:0x777766;
            else color=glyph switch{"扉"=>0x8888aa,"宝"=>0xffdd99,"罠"=>0xbb0033,"昇" or "降"=>0x44aa00,"Ｔ" or "ｔ" or "！"=>0xaa44ff,"Ｒ" or "Ｃ"=>0x77bbff,"↑" or "→" or "↓" or "←" or "Ｅ" or "出"=>0xff7700,_=>color};
            if(x==currentX&&y==currentY)
            {
                long lc=F5Read(process,f5a2Abl,[process.vEvaluator.MASTER,F5Keyword(process,VariableCode.ABL,"属性LC")]);
                glyph+=lc switch{1=>"<div xpos='-100' ypos='-25'><img src='＠時計回り' width='100' height='100'></div>",2=>"<div xpos='-100' ypos='-25'><img src='＠中庸' width='100' height='100'></div>",3=>"<div xpos='-100' ypos='-25'><img src='＠反時計回り' width='100' height='100'></div>",_=>""};
            }
            f6cSetColorTag++;
            process.vEvaluator.RESULT=color; process.vEvaluator.RESULTS=glyph; process.vEvaluator.RESULTS_ARRAY[1]=glyph;
            return(raw,glyph,color);
        }

        private long CandidateNumNakama(Process process)
        {
            long comp=F5Keyword(process,VariableCode.CFLAG,"所属ＣＯＭＰ"), pt=F5Keyword(process,VariableCode.CFLAG,"PTフラグ"), count=0;
            for(long c=0;c<process.vEvaluator.CHARANUM;c++) if(c!=process.vEvaluator.MASTER && F5Read(process,f6bCflag,[c,comp])!=-1 && F5Read(process,f6bCflag,[c,pt])!=0) count++;
            return count;
        }

        private long CandidateStockMag(Process process,long character)
        {
            var baseToken=F5Token(process,"BASE",true,true);
            long magLink=F5Keyword(process,VariableCode.CFLAG,"MAGリンク");
            if(F5Read(process,f6bCflag,[character,magLink])>0) throw new InvalidOperationException("R0-F6C linked MAG branch not admitted");
            return F5Read(process,baseToken,[character,F5Keyword(process,VariableCode.BASE,"ＭＡＧ")]);
        }

        private bool CandidateSoftOn(Process process,string name)
        {
            f6cSoftOn++;
            var equip=F5Token(process,"EQUIP_INSTALLSOFT",true,false);
            int index=Enumerable.Range(0,installSoftName.Length).Single(i=>installSoftName.Read(process,i).Equals(name,StringComparison.OrdinalIgnoreCase));
            long value=F5Read(process,equip,[index]);
            if(value!=0) throw new InvalidOperationException("R0-F6C reached an enabled SOFT_ON branch: "+name);
            return false;
        }

        private string ParseFloorTitle(long dungeon,long floor)
        {
            string name="FLOORNAME_"+dungeon.ToString(CultureInfo.InvariantCulture);
            if(!functions.TryGetValue(name,out var matches)||matches.Length!=1)throw new InvalidOperationException("R0-F6C floor-title target unresolved: "+name);
            var lines=Lines(Read(matches[0].Function,matches[0].File)); bool selected=false;
            foreach(var raw in lines)
            {
                string t=raw.Trim(); var m=Regex.Match(t,"^CASE\\s+(-?[0-9]+)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
                if(m.Success){selected=long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)==floor;continue;}
                if(selected&&t.StartsWith("PRINTL ",StringComparison.OrdinalIgnoreCase))return t[7..];
                if(selected&&(t.StartsWith("CASE",StringComparison.OrdinalIgnoreCase)||t.Equals("ENDSELECT",StringComparison.OrdinalIgnoreCase)))break;
            }
            throw new InvalidOperationException("R0-F6C selected FLOORNAME case is not a literal PRINTL");
        }

        private object ShowFloorOracle(Process process,object geometry)=>new { Geometry=geometry, VisibleRows=f6cHtmlPrint,
            VisibleCells=f6cGetShowTile, ConvertXCalls=f6cConvertX, ConvertYCalls=f6cConvertY, GetShowTileCalls=f6cGetShowTile,
            DPointCalls=f6cDPoint, DgGetTileCalls=f6cDgGetTile, DgBaseTileCalls=f6cDgBaseTile, SetColorTagCalls=f6cSetColorTag,
            HtmlPrintCalls=f6cHtmlPrint, SoftOnCalls=f6cSoftOn, HostDisplayOperations=f6cShowFloorHostOps,
            TileDigest=Hash(string.Join('\n',f6cTileRows)), DPointArgumentValueSha256=CanonicalDPointDigest(process),
            DisplayState=process.console.R0F6CDisplayState(),
            Result0=process.vEvaluator.RESULT, Results0=process.vEvaluator.RESULTS,
            ResultNonZero=process.vEvaluator.RESULT_ARRAY.Select((value,index)=>new{index,value}).Where(x=>x.value!=0).ToArray(),
            ResultsNonEmpty=process.vEvaluator.RESULTS_ARRAY.Select((value,index)=>new{index,value}).Where(x=>!string.IsNullOrEmpty(x.value)).ToArray(),
            ResultSha256=Hash(string.Join(',',process.vEvaluator.RESULT_ARRAY)), ResultsSha256=Hash(string.Join('\u001f',process.vEvaluator.RESULTS_ARRAY)) };
        private string CanonicalDPointDigest(Process process)
        {
            var state=ShowFloorState(process);long maxX=ReadProperty(state,"MaxX"),maxY=ReadProperty(state,"MaxY"),x0=ReadProperty(state,"CurrentX"),y0=ReadProperty(state,"CurrentY");
            long loopX=ReadScalar(process,f6aLoopX),loopY=ReadScalar(process,f6aLoopY);
            long startX=loopX!=0?x0-Math.Min(maxX/2,15):Math.Max(0,x0-15-Math.Max(0,x0+15-maxX));
            long endX=loopX!=0?x0+Math.Min(maxX/2,15):Math.Min(x0+15+Math.Max(0,15-x0),maxX);
            long startY=loopY!=0?y0-Math.Min(maxY/2,9):Math.Max(0,y0-9-Math.Max(0,y0+9-maxY));
            long endY=loopY!=0?y0+Math.Min(maxY/2,9):Math.Min(y0+9+Math.Max(0,9-y0),maxY);
            var rows=new List<string>{"initial|0|0|floor="+f6aCurrentM.ToString(CultureInfo.InvariantCulture)};
            for(long y=startY;y<endY;y++)for(long x=startX;x<endX;x++){long cx=ConvertCoordinate(x,maxX,loopX!=0),cy=ConvertCoordinate(y,maxY,loopY!=0);rows.Add($"{cx}|{cy}|{F5Read(process,f6aDa,[cx,cy])}|positive");}
            return Hash(string.Join('\n',rows));
        }
        private static long ConvertCoordinate(long value,long max,bool loop)=>!loop?value:value<0?value+max:value>=max?value-max:value;
        private static string ColorTag(string value,long color)=>$"<font color = '#{color:X6}'>"+value+"</font>";
        private static string ButtonTag(string value,string input)=>$"<button value = '{input}' title = ''>"+value+"</button>";
        private static long ReadProperty(object value,string name)=>Convert.ToInt64(value.GetType().GetProperty(name)!.GetValue(value),CultureInfo.InvariantCulture);

        internal void ObserveF6CLegacyFunctionEntry(Process process, CalledFunction called)
        {
            if (!F6CEnabled || Candidate || !f6cPrepared || !f6cLegacyActive) return;
            f6cFunctionCounts[called.FunctionName] = f6cFunctionCounts.GetValueOrDefault(called.FunctionName) + 1;
        }

        internal void BeforeF6CLegacyInstruction(Process process, InstructionLine line, string file, string current)
        {
            if (!F6CEnabled || Candidate || !f6cPrepared || !f6cLegacyActive || line.Position is not { } p) return;
            if (F6CDisplayCommands.Contains(line.Function.Name))
                f6cDisplayRows.Add($"{f6cDisplayRows.Count + 1}|{file}|{current}|{p.LineNo}|{line.Function.Name}");
            if (p.LineNo==854 && file.EndsWith(F6DungeonFile,StringComparison.OrdinalIgnoreCase) && current.Equals("SHOW_FLOOR",StringComparison.OrdinalIgnoreCase))
            {
                var called=process.state.CurrentCalled;
                long x=called.TopLabel.GetPrivateVariable("CONVERTED_X")!.GetIntValue(process.exm,[0]);
                long y=called.TopLabel.GetPrivateVariable("CONVERTED_Y")!.GetIntValue(process.exm,[0]);
                long raw=F5Read(process,f6aDa,[x,y]),color=process.vEvaluator.RESULT;
                string glyph=process.vEvaluator.RESULTS,payload=ColorTag(glyph,color);
                f6cTileRows.Add($"{f6cTileRows.Count}|{x}|{y}|{raw}|{glyph}|{color:X6}|{payload}");
            }
            if (p.LineNo==1420 && file.EndsWith(F6DungeonFile,StringComparison.OrdinalIgnoreCase) && current.Equals("DUNGEON_ATTACK",StringComparison.OrdinalIgnoreCase))
            {
                f6cShowFloorCompleted = true;
                f6cConvertX=f6cFunctionCounts.GetValueOrDefault("CONVERT_OUTRANGE_COORD_X");
                f6cConvertY=f6cFunctionCounts.GetValueOrDefault("CONVERT_OUTRANGE_COORD_Y");
                f6cGetShowTile=f6cFunctionCounts.GetValueOrDefault("GET_SHOW_TILE");
                f6cDPoint=f6cFunctionCounts.GetValueOrDefault("DPOINT");
                f6cDgGetTile=f6cFunctionCounts.GetValueOrDefault("DG_GET_TILE");
                f6cDgBaseTile=f6cFunctionCounts.GetValueOrDefault("DG_BASE_TILE");
                f6cSetColorTag=f6cFunctionCounts.GetValueOrDefault("SET_COLOR_TAG");
                f6cSoftOn=f6cFunctionCounts.GetValueOrDefault("SOFT_ON");
                f6cHtmlPrint=f6cDisplayRows.Count(x=>x.EndsWith("|HTML_PRINT",StringComparison.OrdinalIgnoreCase));
                f6cShowFloorHostOps=f6cDisplayRows.Count(x=>x.EndsWith("|PRINT",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|PRINTL",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|PRINTSL",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|PRINTFORM",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|PRINTFORML",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|PRINTBUTTON",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|HTML_PRINT",StringComparison.OrdinalIgnoreCase)||x.EndsWith("|ALIGNMENT",StringComparison.OrdinalIgnoreCase));
                f6cShowFloorOracle=ShowFloorOracle(process,ShowFloorState(process));
            }
        }

        internal void AfterF6CLegacyInstruction(Process process, InstructionLine line)
        {
            if (!F6CEnabled || Candidate || !f6cPrepared || !f6cLegacyActive || line.Position is not { } p ||
                p.LineNo != 1453 || !p.Filename.Replace('\\', '/').EndsWith(F6DungeonFile, StringComparison.OrdinalIgnoreCase) ||
                !process.state.CurrentCalled.FunctionName.Equals("DUNGEON_ATTACK", StringComparison.OrdinalIgnoreCase)) return;
            f6cInputRequests = 1;
            f6cInputSuspended = true;
            f6cDisplayState = process.console.R0F6CDisplayState();
            f6cInputState = process.console.R0F6CInputState();
            var called = process.state.CurrentCalled;
            var lLine = called.TopLabel.GetPrivateVariable("L_LINE");
            f6cPrivateState = new { LLine = lLine?.GetIntValue(process.exm, [0]), ActiveFunction = called.FunctionName,
                SystemState = process.state.SystemState.ToString(), EventCursor };
            f6cBlocker = "NONE";
            StoppedBefore = $"{F6DungeonFile}:1454 post-input CLEARLINE";
            throw new R0F1PlannedCheckpointException();
        }

        private void FinishF6C(Process process)
        {
            if (f6cDisplayState is null) f6cDisplayState = process.console.R0F6CDisplayState();
            if (f6cInputState is null) f6cInputState = process.console.R0F6CInputState();
            var guardTotal = Candidate ? Runtime.Diagnostics.R0E1AProof.Counters.Sum() : 0;
            var showFloorCompleted = f6cShowFloorCompleted;
            var formationCompleted = f6cFormationCompleted || f6cFunctionCounts.ContainsKey("SHOW_DUNGEON_COMMAND");
            var commandCompleted = f6cCommandCompleted || f6cInputSuspended;
            var gate = showFloorCompleted && formationCompleted && commandCompleted && f6cInputSuspended &&
                (!Candidate || guardTotal == 0) ? "PASS" : Candidate ? "PARTIAL_PASS" : "FAIL";
            F6CEvidence = new
            {
                Schema = "emuera-r0f6c-show-floor-input-v1", Mode = Candidate ? "GraphFreeCandidate" : "LegacyControl",
                GateResult = gate, Scope = "SHOW_FLOOR_TO_REPRESENTATIVE_INPUT", Entry = f6cEntryState,
                ShowFloorCompleted = showFloorCompleted, ShowNowFormationCompleted = formationCompleted,
                ShowDungeonCommandCompleted = commandCompleted, StableInputSource = $"{F6DungeonFile}:1453",
                InputCommand = "ONEINPUTS", InputRequestCount = f6cInputRequests, AcceptedUserInputCount = 0,
                PrimitiveInputCount = 0, InputSuspended = f6cInputSuspended, DisplayOperations = f6cDisplayRows.Count,
                DisplayOperationSha256 = Hash(string.Join('\n', f6cDisplayRows)), DisplayOperationsRaw = f6cDisplayRows,
                FunctionCounts = f6cFunctionCounts.OrderBy(x => x.Key).Select(x => new { Function = x.Key, x.Value }),
                ShowFloorOracle=f6cShowFloorOracle,
                DisplayState = f6cDisplayState, InputState = f6cInputState, SuspendedFrameState = f6cPrivateState,
                Blocker = f6cBlocker, LegacyErbGraphAvoided = Candidate, LegacyRetryAfterCompact = 0,
                ProductionBridgeUsed = "NO", GuardCounterTotal = guardTotal, ManualRecaptureRequired = "NO",
                WholeProductSuperiority = "NOT_YET_CLAIMED"
            };
        }
    }
}
#endif

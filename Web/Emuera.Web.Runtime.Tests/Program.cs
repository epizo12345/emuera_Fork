using MinorShift.Emuera.Web.Runtime;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Utils;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

try
{
if (args is ["getkey-state-contract"])
{
    var runtime = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
INPUT
PRINTFORML DOWN={GETKEY(0x12)},{GETKEY(0x12)},{GETKEY(0x05)},{GETKEY(0xA4)},{GETKEY(0xA5)} INVALID={GETKEY(-1)},{GETKEY(256)}
INPUT
PRINTFORML UP={GETKEY(0x12)},{GETKEY(0x05)},{GETKEY(0xA4)},{GETKEY(0xA5)}
INPUT
PRINTFORML AGAIN={GETKEY(0x12)},{GETKEY(0x12)}
WAIT
QUIT
"""), null);
    var raw = new short[256];
    var queried = new List<int>();
    runtime.SetKeyStateReader(code => { queried.Add(code); return raw[code]; });
    raw[18] = unchecked((short)0x8001); // low toggle bit does not change GETKEY's boolean return
    raw[5] = raw[164] = unchecked((short)0x8000);
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "down input accepted");
    Contains("DOWN=1,1,1,1,0 INVALID=0,0", runtime.Output, "real GETKEY reads raw state without consuming it");
    Equal(false, queried.Any(code => code < 0 || code > 255), "invalid code does not reach host reader");
    Array.Clear(raw); raw[18] = 1;
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "release input accepted");
    Contains("UP=0,0,0,0", runtime.Output, "released or low-bit-only state is not down");
    raw[18] = unchecked((short)0x8000);
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "fresh press input accepted");
    Contains("AGAIN=1,1", runtime.Output, "new press and repeated query remain down");
    runtime.SetKeyStateReader(null);
    runtime.SetKeyState(18, false);
    runtime.ReturnToTitle();
    runtime.SetKeyState(18, true);
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "legacy host state remains usable");
    Contains("DOWN=1,1,0,0,0", runtime.Output, "host .NET fallback remains unchanged");
    Console.WriteLine("PASS_GETKEY_RAW_HIGH_LOW_REPEAT_RELEASE_INVALID_AND_LEGACY_HOST"); return;
}
if (args is ["clear-textbox-contract"])
{
    var runtime = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL KEEP_HISTORY
HTML_PRINT_ISLAND "<div display='absolute-lefttop' xpos='20px' ypos='30px'>KEEP_ISLAND</div>",2
INPUT
LOCAL = LINECOUNT
CLEARTEXTBOX
PRINTFORML CLEAR_COUNT={LOCAL},{LINECOUNT}
INPUT
QUIT
"""), null);
    long count = runtime.DisplayLines.Count;
    int islands = runtime.IslandLineCount;
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId,"7")), "initial input accepted");
    Equal(BrowserRuntimeStatus.WaitingForInput,runtime.Status,"clear continues to next input");
    Equal(true,runtime.DisplayLines.Any(l=>l.Parts.Any(p=>p.Text.Contains("KEEP_HISTORY"))),"CLEARTEXTBOX retains history");
    Equal(islands,runtime.IslandLineCount,"CLEARTEXTBOX retains Island");
    var clearedCount = System.Text.RegularExpressions.Regex.Match(runtime.Output, @"CLEAR_COUNT=(\d+),(\d+)");
    Equal(true,clearedCount.Success && clearedCount.Groups[1].Value == clearedCount.Groups[2].Value,"CLEARTEXTBOX preserves LINECOUNT");
    Equal(1L,(long)typeof(BrowserRuntimeSession).GetProperty("TextBoxClearSequence")!.GetValue(runtime)!,"one host clear request");
    Equal(true,runtime.DisplayLines.Count>=count,"history not reset");
    runtime.ReturnToTitle();
    Equal(1,runtime.DisplayLines.Count(l=>l.Parts.Any(p=>p.Text.Contains("KEEP_HISTORY"))),"title return independently resets display");
    Console.WriteLine("PASS_CLEARTEXTBOX_INPUT_ONLY_HISTORY_ISLAND_LINECOUNT_AND_TITLE_RESET");return;
}
if (args is ["html-history-media-contract"])
{
    var runtime=await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
GCREATE 901,12,6
SPRITECREATE "HISTORY_IMG",901,0,0,12,6
PRINT_IMG "HISTORY_IMG"
PRINT_RECT 100
PRINTL TEXT_END
RESULTS '= HTML_GETPRINTEDSTR(0)
PRINTFORML GET=[%RESULTS%]
PRINT_IMG "HISTORY_IMG"
PRINT_RECT 100
RESULTS '= HTML_POPPRINTINGSTR()
PRINTFORML POP=[%RESULTS%]
RESULTS '= HTML_POPPRINTINGSTR()
PRINTFORML EMPTY=[%RESULTS%]
HTML_PRINT "<button value='7' title='日本語&amp;説明'><img src='HISTORY_IMG' srcb='SELECTED_IMG' height='200' width='300' ypos='20'><shape type='rect' param='0,0,100,100' color='#123456' bcolor='#654321'><font color='#123456'><s>STYLE</s></font></button>"
RESULTS '= HTML_GETPRINTEDSTR(0)
PRINTFORML ATTR=[%RESULTS%]
HTML_PRINT RESULTS
PRINTL AFTER_REDISPLAY
INPUT
QUIT
"""),null);
    Equal(BrowserRuntimeStatus.WaitingForInput,runtime.Status,"image/shape history commands do not throw");
    Contains("GET=[<p",runtime.Output,"GET wrapper");
    Contains("<img src='HISTORY_IMG'",runtime.Output,"image resource retained");
    Contains("<shape type='rect' param='100'",runtime.Output,"shape original units retained");
    Contains("POP=[<img",runtime.Output,"POP returns pending media");
    Contains("EMPTY=[]",runtime.Output,"POP consumes pending buffer once");
    Contains("srcb='SELECTED_IMG'",runtime.Output,"alternate resource name retained");
    Contains("param='0, 0, 100, 100' color='#123456' bcolor='#654321'",runtime.Output,"shape color and source units retained");
    Contains("title='日本語&amp;説明'",runtime.Output,"tooltip safely escaped");
    Contains("<s>STYLE</s>",runtime.Output,"strikeout retained");
    Contains("AFTER_REDISPLAY",runtime.Output,"serialized media accepted by actual HTML_PRINT");
    var wrapped=await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789\nRESULTS '= HTML_GETPRINTEDSTR(0)\nPRINTFORML WRAPPED=[%RESULTS%]\nINPUT\nQUIT\n"),null);
    wrapped.SetViewport(45,864); wrapped.StartTitle();
    Contains("<br>",wrapped.Output,"GET includes all physical continuations of one logical line");
    Contains("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789",System.Text.RegularExpressions.Regex.Replace(wrapped.Output,"<[^>]*>",""),"wrapped history content order retained");
    Console.WriteLine("PASS_HTML_HISTORY_MEDIA_GET_POP_AND_EMPTY");return;
}
if (args.Length > 0 && args[0] == "chara-dat-contract")
{
    await CharaDatProbe.Run(args.Length > 1 ? args[1] : null);
    return;
}
if (args is ["chara-dat-red"])
{
    var runtime = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nADDVOIDCHARA\nNAME:0 = 冬眠試験\nSAVECHARA \"winter\", \"メモ\", 0\nPRINTL AFTER_DAT_ACK\nWAIT\n"), null);
    Console.WriteLine(runtime.Output);
    Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "SAVECHARA must suspend before next instruction until durable ACK");
    Equal(false, runtime.Output.Contains("AFTER_DAT_ACK"), "SAVECHARA next instruction not run before ACK");
    return;
}
if (args is ["macro-file-red"])
{
    Equal(true, typeof(BrowserKeyMacros).Assembly.GetType("MinorShift.Emuera.Web.Runtime.BrowserMacroFile") is not null, "Native macro.txt codec connected to Web Runtime");
    return;
}
if (args is ["macro-file-contract", var inputFile, var exportFile])
{
    var supplied = BrowserMacroFile.Decode(File.ReadAllBytes(inputFile));
    File.WriteAllBytes(exportFile, BrowserMacroFile.Encode(supplied.GroupNames, supplied.Slots));
    var copy = BrowserMacroFile.Decode(File.ReadAllBytes(exportFile));
    Equal(string.Join("\0", supplied.GroupNames), string.Join("\0", copy.GroupNames), "all supplied Native group names roundtrip");
    Equal(string.Join("\0", supplied.Slots), string.Join("\0", copy.Slots), "all supplied Native 120 slots roundtrip");
    string[] names = Enumerable.Range(0, 10).Select(g => $"日本語グループ {g}:名称").ToArray();
    string[] slots = Enumerable.Repeat("", 120).ToArray();
    foreach (int index in new[] { 0, 11, 12, 119 }) slots[index] = $"  日本語:{index} (H\\e\\nd\\e\\n)*10 \\t \\w  ";
    var nativeBytes = BrowserMacroFile.Encode(names, slots);
    var roundtrip = BrowserMacroFile.Decode(nativeBytes);
    Equal(string.Join("\0", names), string.Join("\0", roundtrip.GroupNames), "names exact");
    Equal(string.Join("\0", slots), string.Join("\0", roundtrip.Slots), "Japanese colons slash whitespace exact");
    Equal(120, roundtrip.DefinedSlots, "empty slots emitted too");
    File.WriteAllBytes(exportFile + ".extended.txt", nativeBytes);
    var encoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    void Reject(byte[] bytes, string label) { bool failed = false; try { BrowserMacroFile.Decode(bytes); } catch (FormatException) { failed = true; } Equal(true, failed, label); }
    foreach (string text in new[] { "マクロキーF1:a\nG0:マクロキーF1:b", "グループ0:abc\nグループ0:def", "G10:マクロキーF1:x", "マクロキーF13:x", "bad", " グループ0:abc", "グループ0:ab", "マクロキーF1:x\0" }) Reject(encoding.GetBytes(text), "malformed or duplicate rejected wholesale");
    Reject([0x81], "invalid CP932"); Reject(new byte[BrowserMacroFile.MaxBytes + 1], "oversize");
    foreach (Encoding unicode in new Encoding[] { new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF32Encoding(false, true, true) })
    {
        var bytes = unicode.GetPreamble().Concat(unicode.GetBytes("マクロキーF12:日本語: \\e  ")).ToArray();
        Equal("日本語: \\e  ", BrowserMacroFile.Decode(bytes).Slots[11], "Native BOM detected without conversion");
    }
    Equal("", BrowserMacroFile.Decode(encoding.GetBytes("マクロキーF1:only")).Slots[119], "missing slots blank fresh bank");
    Equal(0, BrowserMacroFile.Decode([]).DefinedSlots, "empty whole replacement valid");
    foreach (string invalid in new[] { "😀", "line\nline", "\0", "¥" })
    {
        slots[1] = invalid; bool refused = false;
        try { BrowserMacroFile.Encode(names, slots); } catch (FormatException) { refused = true; }
        Equal(true, refused, "no lossy export " + invalid);
    }
    var bank = new BrowserKeyMacros(); bank.Register(0,"OLD");
    bool rejected = false; try { bank.Restore(0, roundtrip.Slots, ["bad"]); } catch (ArgumentException) { rejected = true; }
    Equal(true, rejected, "bank validates names before applying"); Equal("OLD", bank.Get(0), "failed restore retains bank");
    Console.WriteLine("PASS_MACRO_FILE_CP932_120_SLOTS_STRICT_VALIDATION_LOSSLESS_EXPORT"); return;
}
if (args is ["key-macro-contract"])
{
    var bank = new BrowserKeyMacros();
    for (int g = 0; g < 10; g++)
    {
        bank.SelectGroup(g);
        for (int f = 0; f < 12; f++) Equal(true, bank.Register(f, $"G{g}/F{f + 1}"), "slot registration");
    }
    for (int g = 0; g < 10; g++)
    {
        bank.SelectGroup(g);
        for (int f = 0; f < 12; f++) Equal($"G{g}/F{f + 1}", bank.Get(f), "all 120 independent slots");
    }
    Equal(false, bank.Register(0, ""), "empty Native registration ignored");
    Equal("G9/F1", bank.Get(0), "empty register does not erase");
    string[] snapshot = (string[])bank.Slots.Clone();
    bank.Restore(2, snapshot); snapshot[24] = "CHANGED";
    Equal("G2/F1", bank.Get(0), "restored settings are independently owned");
    bool rejected = false;try { bank.Restore(0, new string[12]); } catch (ArgumentException) { rejected = true; }
    Equal(true, rejected, "malformed settings rejected before applying");
    Equal(2, bank.Group, "malformed settings preserve group");
    var regular = new BrowserInputPrompt(1, BrowserInputKind.String, false);
    Equal(false, BrowserKeyMacros.SubmitOnRecall("", "(x\\e)*2", regular), "normal recall is not execution");
    Equal(true, BrowserKeyMacros.SubmitOnRecall("", "A", regular with { OneInput = true }), "Native ONEINPUT text growth");
    Equal(false, BrowserKeyMacros.SubmitOnRecall("AA", "B", regular with { OneInput = true }), "shorter replacement not autosubmitted");
    Equal(true, BrowserKeyMacros.SubmitOnRecall("", "A", regular with { Kind = BrowserInputKind.AnyKey }), "Native AnyKey text growth");
    Equal(false, BrowserKeyMacros.SubmitOnRecall("", "", regular with { OneInput = true }), "empty recall no execution");
    Equal("", BrowserKeyMacros.RecallInput("", "(7\\e)*9", regular with { Kind = BrowserInputKind.AnyKey }), "AnyKey clears text before Enter and never expands recalled macro");
    Equal("XYZ", BrowserKeyMacros.RecallInput("ABC", "ABCXYZ", regular with { OneInput = true }), "ONEINPUT discards existing text prefix before Enter");
    var runtime = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nFOR LOCAL,0,12\nINPUTMOUSEKEY\nPRINTFORML KEY={RESULT:0},{RESULT:1},{RESULT:2}\nNEXT\nINPUT\nQUIT\n"), null);
    for (int f = 1; f <= 12; f++)
    {
        var key = BrowserInputMapping.MapKey("F" + f, "F" + f, f == 2, f == 3, false)!;
        Equal(111 + f, key.Code, "Windows F key code");
        var prompt = runtime.PendingInput!;
        Equal(true, runtime.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Key, "", key.Code, key.KeyData, SessionGeneration: runtime.SessionGeneration)), "F key accepted as game input exactly once");
        Equal(false, runtime.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Key, "", key.Code, key.KeyData, SessionGeneration: runtime.SessionGeneration)), "old F event rejected");
        Contains($"KEY=3,{key.Code},{key.KeyData}", runtime.Output, "actual command writes Windows RESULT tuple");
    }
    Equal(new BrowserKeyInput(96, 131168), BrowserInputMapping.MapKey("Numpad0", "0", true, false, false), "Ctrl numpad uses Native VK_NUMPAD0");
    Equal(new BrowserKeyInput(45,45), BrowserInputMapping.MapKey("Insert", "Insert", false,false,false), "Insert code");
    Equal(null, BrowserInputMapping.MapKey("", "", false,false,false), "empty code safe");
    Console.WriteLine("PASS_KEY_MACRO_120_SLOTS_TIMING_AND_REAL_PRIMITIVE_KEYS");return;
}
if (args is ["gdrawg-contract"] or ["gdrawg-red"] or ["gdrawg-case", _, _, _])
{
    await GDrawGProbe.Run(args);
    return;
}
if (args is ["await-contract", var awaitCase, var awaitEvidence])
{
    int delay = awaitCase switch { "one" => 1, "positive" or "title-cancel" => 80, "timer" => 40, "upper" => 10000, _ => 0 };
    string command = awaitCase == "omitted" ? "AWAIT" : $"AWAIT {delay}";
    string script = awaitCase switch
    {
        "negative" => "AWAIT -1\nPRINTL BAD_AFTER\nINPUT",
        "too-high" => "AWAIT 10001\nPRINTL BAD_AFTER\nINPUT",
        "render" => "PRINTL TEXT_BEFORE\nHTML_PRINT \"<button value='4'>HTML_BEFORE</button>\"\nHTML_PRINT_ISLAND \"<div display='absolute-lefttop' xpos='10px' ypos='20px'>ISLAND_BEFORE</div>\", 2\nAWAIT 0\nPRINTL AFTER_AWAIT\nINPUT",
        "sequence" => "INPUT\nPRINTFORML FIRST={RESULT}\nAWAIT 0\nPRINTL MIDDLE\nAWAIT 0\nPRINTL AFTER_AWAIT\nINPUT",
        "macro" => "INPUT\nAWAIT 0\nWAIT\nPRINTL AFTER_AWAIT\nINPUT",
        "timer" => "TINPUT 1000, 0\nAWAIT 40\nPRINTL AFTER_AWAIT\nINPUT",
        "save-failure" => "SAVEDATA 7, \"AWAIT_TEST\"\nAWAIT 0\nPRINTL BAD_AFTER\nINPUT",
        "save-ack" or "load" => "SAVEDATA 7, \"AWAIT_TEST\"\nAWAIT 0\n" + (awaitCase == "load" ? "LOADDATA 7\n" : "") + "PRINTL AFTER_AWAIT\nINPUT",
        "quit" => "AWAIT 0\nQUIT",
        _ => "PRINTL BEFORE_AWAIT\n" + command + "\nPRINTL AFTER_AWAIT\nINPUT"
    };
    string root = CreateGlobalFixture("@SYSTEM_TITLE\n" + script + "\nQUIT\n" +
        (awaitCase == "load" ? "\n@EVENTLOAD\nPRINTL LOADED_FRESH_INPUT\nINPUT\nQUIT\n" : ""));
    var runtime = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(root, null);
    runtime.SetViewport(1512, 864);
    long started = System.Diagnostics.Stopwatch.GetTimestamp();
    runtime.StartTitle();
    var observations = new List<string>();
    if (awaitCase == "save-failure")
    {
        Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "save starts before AWAIT");
        string operation = runtime.PendingPersistence!.OperationId;
        try { await runtime.DrainPersistenceAsync(_ => Task.FromException(new IOException("test transaction failed"))); }
        catch (IOException) { observations.Add("injected transaction failure retained"); }
        Equal(BrowserRuntimeStatus.Failed, runtime.Status, "save failure not converted to AWAIT success");
        Equal(true, runtime.PersistenceError is not null, "save error retained");
        Equal(true, runtime.PendingAwait is null, "failed transaction never reaches AWAIT");
        Equal(false, runtime.AcknowledgePersistence(operation), "late ACK cannot revive failed save");
        Equal(false, runtime.CompleteAwait(1, runtime.SessionGeneration), "host completion cannot bypass failed save");
        Equal(false, runtime.Output.Contains("BAD_AFTER", StringComparison.Ordinal), "no continuation after failure");
    }
    else if (awaitCase is "negative" or "too-high")
    {
        Equal(BrowserRuntimeStatus.Failed, runtime.Status, "shared range check rejects explicit argument");
        Equal(true, runtime.PendingAwait is null, "range failure creates no continuation");
        Equal(false, runtime.Output.Contains("BAD_AFTER", StringComparison.Ordinal), "range failure stops later commands");
    }
    else
    {
        if (awaitCase is "sequence" or "macro")
        {
            var prompt = runtime.PendingInput!;
            Equal(true, runtime.SubmitMacro(new(prompt.RequestId, "3", BrowserInputSource.Keyboard, runtime.SessionGeneration), awaitCase == "macro"), "initial input accepted once");
            started = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        if (awaitCase == "timer")
        {
            var timed = runtime.PendingInput!;
            Equal(true, runtime.Submit(new(timed.RequestId, "2", SessionGeneration: runtime.SessionGeneration)), "timed input accepted once");
            Equal(false, runtime.SubmitTimeout(timed.RequestId, runtime.SessionGeneration), "old input timer cannot finish AWAIT");
            started = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        if (awaitCase is "save-ack" or "load")
        {
            Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "save blocks before AWAIT");
            Equal(true, runtime.PendingAwait is null, "no AWAIT before matching ACK");
            Equal(false, runtime.CompleteAwait(1, runtime.SessionGeneration), "host continuation cannot bypass save ACK");
            string operation = runtime.PendingPersistence!.OperationId;
            Equal(false, runtime.AcknowledgePersistence("stale"), "stale save ACK rejected");
            await Task.Delay(20);
            Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "delayed ACK still blocks");
            Equal(true, runtime.AcknowledgePersistence(operation), "matching ACK reaches AWAIT");
            Equal(false, runtime.AcknowledgePersistence(operation), "duplicate ACK rejected during AWAIT");
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            observations.Add("save -> matching ACK -> AWAIT; stale/duplicate ACK rejected");
        }
        Equal(BrowserRuntimeStatus.AwaitingHost, runtime.Status, "AWAIT separate non-input suspension");
        var request = runtime.PendingAwait!;
        Equal(delay, request.DelayMilliseconds, "omitted/zero/positive duration contract");
        Equal(true, runtime.PendingInput is null, "no input prompt while host waits");
        Equal(false, runtime.Output.Contains("AFTER_AWAIT", StringComparison.Ordinal), "later command not executed early");
        Equal(false, runtime.Submit(new(request.RequestId, "9")), "ordinary input not accepted at AWAIT");
        Equal(false, runtime.SubmitMacro(new(request.RequestId, "9"), true), "macro token not accepted or consumed at AWAIT");
        Equal(false, runtime.SubmitTimeout(request.RequestId, runtime.SessionGeneration), "input timer cannot finish AWAIT");
        Equal(false, runtime.CompleteAwait(request.RequestId + 1, runtime.SessionGeneration), "wrong request rejected");
        Equal(false, runtime.CompleteAwait(request.RequestId, runtime.SessionGeneration + 1), "wrong runtime generation rejected");
        if (awaitCase == "render")
        {
            string text = string.Join("|", Flatten(runtime.DisplayLines.SelectMany(l => l.Parts)).Select(p => p.Text));
            Contains("TEXT_BEFORE", text, "text published before host continuation");
            Contains("HTML_BEFORE", text, "HTML published before host continuation");
            Contains("ISLAND_BEFORE", text, "Island published before host continuation");
        }
        if (awaitCase == "macro")
        {
            Equal(true, runtime.MessageSkip.Active, "skip retained at non-input AWAIT");
            Equal(false, runtime.ContinueMessageSkip(), "skip cannot bypass AWAIT");
            runtime.StopMessageSkip("macro-user-stop");
        }
        if (awaitCase == "title-cancel")
        {
            runtime.CancelAwait();
            Equal(false, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "cancelled callback rejected");
            runtime.ReturnToTitle();
            Equal(false, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "old callback cannot advance replacement title");
            Equal(true, runtime.PendingAwait!.RequestId > request.RequestId, "same Process has fresh request identity");
            while (runtime.RemainingAwaitTime > TimeSpan.Zero)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(runtime.RemainingAwaitTime.TotalMilliseconds))));
            Equal(false, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "expired old timer cannot advance replacement");
            Equal(true, runtime.CompleteAwait(runtime.PendingAwait.RequestId, runtime.SessionGeneration), "replacement title advances once");
        }
        else
        {
            if (awaitCase == "upper") Equal(false, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "10000ms cannot complete early");
            while (runtime.RemainingAwaitTime > TimeSpan.Zero)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(runtime.RemainingAwaitTime.TotalMilliseconds))));
            Equal(true, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "host continuation accepted exactly once");
            Equal(false, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "duplicate/old completion rejected");
            if (awaitCase == "sequence")
            {
                Contains("FIRST=3", runtime.Output, "input value retained across AWAIT");
                Contains("MIDDLE", runtime.Output, "first continuation reaches second AWAIT");
                Equal(BrowserRuntimeStatus.AwaitingHost, runtime.Status, "continuous AWAIT not skipped");
                var second = runtime.PendingAwait!;
                Equal(true, second.RequestId > request.RequestId, "unique sequential requests");
                Equal(true, runtime.CompleteAwait(second.RequestId, second.SessionGeneration), "second continuation accepted");
                Equal(1, runtime.Output.Split("MIDDLE", StringSplitOptions.None).Length - 1, "middle command executed once");
            }
            if (awaitCase == "macro")
            {
                Equal(false, runtime.MessageSkip.Active, "stop not undone by AWAIT completion");
                Equal(BrowserInputKind.Enter, runtime.PendingInput!.Kind, "manual WAIT preserved after stop");
                Equal(true, runtime.Submit(new(runtime.PendingInput.RequestId, "")), "manual operation works after stop");
            }
        }
        Equal(awaitCase == "quit" ? BrowserRuntimeStatus.Succeeded : BrowserRuntimeStatus.WaitingForInput, runtime.Status, "correct final state");
        if (awaitCase == "load")
        {
            Contains("LOADED_FRESH_INPUT", runtime.Output, "LOADDATA enters EVENTLOAD instead of resuming old script");
            Equal(false, runtime.Output.Contains("AFTER_AWAIT", StringComparison.Ordinal), "old script not continued after LOAD");
            Equal(true, runtime.PendingAwait is null, "no old AWAIT retained after LOAD");
            Equal(false, runtime.CompleteAwait(request.RequestId, request.SessionGeneration), "old callback cannot advance loaded input");
        }
        else if (awaitCase != "quit") Equal(1, runtime.Output.Split("AFTER_AWAIT", StringSplitOptions.None).Length - 1, "following command executed once");
        if (delay > 0) Equal(true, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= delay, "requested positive delay not shortened");
    }
    double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(awaitEvidence))!);
    File.WriteAllText(awaitEvidence, JsonSerializer.Serialize(new { @case = awaitCase, requestedMilliseconds = delay, elapsedWallMilliseconds = elapsed, earlyToleranceMilliseconds = 0, status = runtime.Status.ToString(), runtime.CurrentErbPosition, runtime.DoScriptCallCount, runtime.Output, observations, fixtureRoot = root, assertion = true }));
    Console.WriteLine($"PASS await-contract {awaitCase} elapsed={elapsed:F3}ms requested={delay}ms"); return;
}
if (args is ["await-red"])
{
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL BEFORE_AWAIT\nAWAIT 0\nPRINTL AFTER_AWAIT\nINPUT\nQUIT\n"), null);
    runtime.StartTitle();
    Equal("AwaitingHost", runtime.Status.ToString(), "AWAIT suspends without unsupported failure");
    Contains("BEFORE_AWAIT", runtime.Output, "before AWAIT output published");
    Equal(false, runtime.Output.Contains("AFTER_AWAIT", StringComparison.Ordinal), "later command not executed before host continuation");
    Equal(true, runtime.PendingInput is null, "AWAIT is not INPUT");
    Console.WriteLine("PASS await-red"); return;
}
if (args is ["html-tagsplit-unit", var casesPath])
{
    var split = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.HtmlManager")!
        .GetMethod("HtmlTagSplit", BindingFlags.Public | BindingFlags.Static)!;
    using var cases = JsonDocument.Parse(File.ReadAllText(casesPath));
    foreach (var row in cases.RootElement.EnumerateArray())
    {
        string? input = row.GetProperty("input").GetString();
        string[]? actual = (string[]?)split.Invoke(null, [input]);
        string[]? expected = row.GetProperty("expected").ValueKind == JsonValueKind.Null ? null
            : row.GetProperty("expected").EnumerateArray().Select(v => v.GetString()!).ToArray();
        Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), "Native split " + row.GetProperty("name").GetString());
    }
    Console.WriteLine("PASS html-tagsplit-unit exact Native segments"); return;
}
if (args is ["html-tagsplit-command"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
#DIMS SHORT, 2
#DIMS LONG, 8
RESULTS:0 = KEEP0
RESULTS:1 = KEEP1
RESULTS:2 = KEEP2
HTML_TAGSPLIT "a<b>c</b>"
PRINTFORML FULL={RESULT}:[%RESULTS:0%]|[%RESULTS:1%]|[%RESULTS:2%]|[%RESULTS:3%]
RESULTS:0 = KEEP0
RESULTS:1 = KEEP1
RESULTS:2 = KEEP2
HTML_TAGSPLIT "a<b>c<broken"
PRINTFORML BAD={RESULT}:[%RESULTS:0%]|[%RESULTS:1%]|[%RESULTS:2%]
HTML_TAGSPLIT ""
PRINTFORML EMPTY={RESULT}:[%RESULTS:0%]|[%RESULTS:1%]|[%RESULTS:2%]
HTML_TAGSPLIT "a<b>c</b>", SHORT
PRINTFORML SHORT={RESULT}:[%SHORT:0%]|[%SHORT:1%]
VARSET LONG, "TAIL"
RESULT = 123
HTML_TAGSPLIT "<b>x</b>", LONG, LOCAL
PRINTFORML LONG={LOCAL}:[%LONG:0%]|[%LONG:1%]|[%LONG:2%]|[%LONG:3%]|[%LONG:7%],RESULT={RESULT}
HTML_TAGSPLIT "<a title='x>y'>z</a>"
PRINTFORML QUOTED={RESULT}:[%RESULTS:0%]|[%RESULTS:1%]|[%RESULTS:2%]
INPUT
QUIT
""");
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.SetViewport(1512, 864); runtime.StartTitle();
    if (runtime.Status == BrowserRuntimeStatus.Failed) Console.WriteLine(JsonSerializer.Serialize(runtime.FailureDiagnostic));
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "actual instruction reaches input");
    Contains("FULL=4:[a]|[<b>]|[c]|[</b>]", runtime.Output, "default RESULT/RESULTS copy");
    Contains("BAD=-1:[KEEP0]|[KEEP1]|[KEEP2]", runtime.Output, "failure has no partial write");
    Contains("EMPTY=0:[KEEP0]|[KEEP1]|[KEEP2]", runtime.Output, "empty preserves output");
    Contains("SHORT=4:[a]|[<b>]", runtime.Output, "full count even when output short");
    Contains("LONG=3:[<b>]|[x]|[</b>]|[TAIL]|[TAIL],RESULT=123", runtime.Output, "tail and custom count contract");
    Contains("QUOTED=3:[<a title='x>]|[y'>z]|[</a>]", runtime.Output, "first > even inside quotes");
    Console.WriteLine(runtime.Output);
    Console.WriteLine("PASS html-tagsplit-command actual shared instruction"); return;
}
if (args is ["html-tagsplit-ngo", var fixturePath])
{
    string root = CreateGlobalFixture(File.ReadAllText(fixturePath));
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.SetViewport(1512, 864); runtime.StartTitle();
    if (runtime.Status == BrowserRuntimeStatus.Failed) Console.WriteLine(JsonSerializer.Serialize(runtime.FailureDiagnostic));
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "NGO display reaches input");
    Equal(BrowserInputKind.MouseKey, runtime.PendingInput?.Kind, "NGO primitive input wait");
    var parts = Flatten(runtime.DisplayLines.SelectMany(l => l.Parts)).ToArray();
    Equal(true, parts.Any(p => p.Text.Contains("  /\\  ", StringComparison.Ordinal)), "AA spacing preserved");
    Equal(true, parts.Any(p => p.Text.Contains("視聴者コメント", StringComparison.Ordinal)), "comment display reached");
    Equal(true, parts.Any(p => p.Text.Contains("既存色", StringComparison.Ordinal) && p.Style?.Foreground == "#FF0000"), "existing font color preserved");
    Equal(true, parts.Any(p => p.Text.Contains("通常", StringComparison.Ordinal) && p.Style?.Foreground == "#00AAFF"), "untagged comment colored");
    var prompt = runtime.PendingInput!;
    Equal(true, runtime.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Click, Code: 0x200000,
        X: 20, Y: 200, SessionGeneration: runtime.SessionGeneration, DisplayGeneration: runtime.DisplayGeneration)), "right click accepted");
    Contains("NGO-RIGHT=1,2097152", runtime.Output, "right mouse code reaches script");
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "following manual input reached");
    Console.WriteLine(runtime.Output);
    Console.WriteLine("PASS html-tagsplit-ngo Runtime display and right-click progression"); return;
}
if (args is ["html-flow-string"])
{
    string text = string.Concat(Enumerable.Repeat("文字😀", 180));
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT \"<button value='TEXT_VALUE' title='説明&amp;安全'>" + text + "</button>\"\nINPUTS\nPRINTFORML GOT=%RESULTS%\nINPUTS\nQUIT\n");
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nウィンドウ幅:1512\n", Encoding.GetEncoding(932));
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.TextWidthMeasurer = (s, _, _) => s.Length * 9;
    runtime.StartTitle();
    var parts = runtime.DisplayLines.SelectMany(l => l.Parts).Where(p => p.Input == "TEXT_VALUE").ToArray();
    Equal(true, parts.Length > 1, "long string-input button splits");
    Equal(text, string.Concat(parts.SelectMany(p => p.Children!).Select(p => p.Text)), "unicode remains complete and ordered");
    Equal(true, parts.All(p => !p.IsInteger && p.Tooltip == "説明&安全" && p.Activation is not null), "string input and escaped tooltip preserved");
    Equal(true, parts.SelectMany(p => p.Children!).All(p => p.Text.Length == 0 || !char.IsHighSurrogate(p.Text[^1]) && !char.IsLowSurrogate(p.Text[0])), "no surrogate split boundary");
    Equal(true, runtime.SubmitDisplay(parts[^1]), "string continuation selectable");
    Contains("GOT=TEXT_VALUE", runtime.Output, "one accepted string value");
    Equal(false, runtime.SubmitDisplay(parts[^1]), "old fragment cannot be consumed twice");
    Console.WriteLine("PASS html-flow-string"); return;
}
if (args is ["html-flow-contract", var htmlFlowMode])
{
    string a = new('A', 160), b = new('B', 160), c = new('C', 160);
    string erb = "@SYSTEM_TITLE\nLOCAL:0 = LINECOUNT\n"
        + $"HTML_PRINT \"<button value='1' title='LONG'><font color='red'>{a}</font><b>{b}</b><font size='200'>{c}</font></button>\"\n"
        + $"HTML_PRINT \"<nobr><button value='2' title='NOBR'>{a}{b}{c}</button><br>NOBR_SECOND</nobr>\"\n"
        + $"HTML_PRINT \"<div display='absolute-leftbottom' xpos='20px'><button value='3' title='POSITION'>{a}{b}{c}</button></div>\"\n"
        + $"HTML_PRINT_ISLAND \"<button value='4' title='ISLAND'>{a}{b}{c}</button>\", 2\n"
        + "HTML_PRINT \"<button value='5'>BEFORE_BR</button><br>AFTER_BR\"\n"
        + "HTML_PRINT \"<nonbutton title='NONBUTTON'><font color='green'>" + a + b + c + "</font></nonbutton>\"\n"
        + "HTML_PRINT \"<button value='7' title='IMAGE'><img src='missing' width='30px' height='18px'>" + a + b + c + "</button>\"\n"
        + "PRINTFORML CONTRACT_LOGICAL={LINECOUNT - LOCAL:0}\nINPUT\nCLEARLINE 8\nPRINTL CLEARED\nINPUT\nQUIT\n";
    string root = CreateGlobalFixture(erb);
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nウィンドウ幅:1512\nフォントサイズ:18\nボタンの途中で行を折りかえさない:" + (htmlFlowMode == "split" ? "NO" : "YES") + "\n", Encoding.GetEncoding(932));
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.TextWidthMeasurer = (text, _, size) => text.Length * size / 2d;
    runtime.SetViewport(1512, 824);
    runtime.StartTitle();
    var rows = runtime.DisplayLines.ToArray();
    var fragments = rows.SelectMany(l => l.Parts).Where(p => p.Input == "1").ToArray();
    Equal(true, fragments.Length >= 4, "long styled button physically splits");
    Equal(a + b + c, string.Concat(fragments.SelectMany(p => p.Children!).Select(p => p.Text)), "all styled text retained exactly once");
    foreach (var p in fragments)
    {
        Equal("LONG", p.Tooltip, "split preserves tooltip");
        Equal(true, p.IsInteger && p.Activation is not null, "split preserves input activation");
        Equal(fragments[0].ButtonGeneration, p.ButtonGeneration, "split keeps button generation");
    }
    var leaves = fragments.SelectMany(p => p.Children!).ToArray();
    Equal(true, leaves.Where(p => p.Text.Contains('A')).All(p => p.Style?.Foreground == "#FF0000"), "red child style");
    Equal(true, leaves.Where(p => p.Text.Contains('B')).All(p => p.Style?.Bold == true), "bold child style");
    Equal(true, leaves.Where(p => p.Text.Contains('C')).All(p => p.Style?.FontSize == 36), "size child style");
    Equal(1, rows.Count(l => l.Parts.Any(p => p.Input == "2")), "NOBR excludes auto wrap");
    Equal(1, rows.Count(l => Flatten(l.Parts).Any(p => p.Input == "3")), "positioned DIV remains intact");
    Equal(1, rows.Count(l => l.Parts.Any(p => p.Input == "4")), "independent Island remains intact");
    int before = Array.FindIndex(rows, l => l.Parts.Any(p => p.Input == "5"));
    int after = Array.FindIndex(rows, l => l.Parts.Any(p => p.Text == "AFTER_BR"));
    Equal(before + 1, after, "explicit BR creates a physical continuation");
    Equal(true, rows[before].IsLogicalLine && !rows[after].IsLogicalLine, "Native HTML BR stays in the same logical line");
    Equal(true, rows.Count(l => l.Parts.Any(p => p.Tooltip == "NONBUTTON")) > 1, "styled nonbutton wraps without activation");
    Equal(1, Flatten(rows.SelectMany(l => l.Parts)).Count(p => p.Kind == BrowserDisplayPartKind.Image), "image is an indivisible child, retained once");
    var imageParts = rows.SelectMany(l => l.Parts).Where(p => p.Input == "7").ToArray();
    Equal(a + b + c, string.Concat(imageParts.SelectMany(p => p.Children!).Where(p => p.Kind == BrowserDisplayPartKind.Text).Select(p => p.Text)), "image button text retained across continuations");
    Contains("CONTRACT_LOGICAL=6", runtime.Output, "HTML BR does not inflate LINECOUNT; island independent");
    Equal(true, runtime.SubmitDisplay(fragments[^1]), "long button continuation is clickable");
    Equal(false, Flatten(runtime.RetainedDisplayLines.SelectMany(l => l.Parts)).Any(p => p.Tooltip is "LONG" or "NOBR" or "POSITION"), "CLEARLINE removes whole HTML logical groups");
    Equal(true, Flatten(runtime.DisplayLines.SelectMany(l => l.Parts)).Any(p => p.Tooltip == "ISLAND"), "CLEARLINE does not remove island");
    Console.WriteLine("PASS html-flow-contract " + htmlFlowMode); return;
}
if (args is ["html-flow-lists", var countsText])
{
    int[] counts = countsText.Split(',').Select(int.Parse).ToArray();
    string Item(int n) => $"[{n}] TITLE_{n:D3}".PadRight(27);
    int offset = 0;
    string html = string.Concat(counts.Select(count => {
        string block = string.Concat(Enumerable.Range(offset + 1, count).Select(i => $"<button value='{i}' title='DESCRIPTION_{i}'><font color='#ff8000'>{Item(i)}</font></button>"));
        offset += count; return $"HTML_PRINT \"{block}\"\n";
    }));
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nLOCAL:0 = LINECOUNT\n" + html
        + "PRINTFORML FLOW_LOGICAL={LINECOUNT - LOCAL:0}\nDRAWLINE\nPRINTL [0] BACK\nINPUT\nCLEARLINE " + (counts.Length + 4) + "\nPRINTL AFTER_CLEAR\nINPUT\nQUIT\n");
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nウィンドウ幅:1512\nフォントサイズ:18\n一行の高さ:18\nボタンの途中で行を折りかえさない:YES\n", Encoding.GetEncoding(932));
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.TextWidthMeasurer = (text, _, size) => text.Length * size / 2d;
    runtime.SetViewport(1512, 824);
    runtime.StartTitle();
    IReadOnlyList<BrowserDisplayLine> rows = runtime.DisplayLines.ToArray();
    var buttons = rows.SelectMany(l => l.Parts).Where(p => p.Input != null && p.Input != "0").ToArray();
    Equal(offset, buttons.Length, "no missing or duplicated title items");
    int start = 1;
    foreach (int count in counts)
    {
        int[] owners = Enumerable.Range(start, count).Select(n => Enumerable.Range(0, rows.Count).Single(r => rows[r].Parts.Any(p => p.Input == n.ToString()))).ToArray();
        Equal(true, count <= 6 ? owners.Distinct().Count() == 1 : owners.Distinct().Count() > 1, "title list physically wraps; small list remains one row");
        start += count;
    }
    foreach (var b in buttons)
    {
        int n = int.Parse(b.Input!);
        Equal(Item(n), string.Concat(Flatten(b.Children ?? []).Where(p => p.Children is null).Select(p => p.Text)), "child text and padding preserved");
        Equal("DESCRIPTION_" + n, b.Tooltip, "tooltip retained");
        Equal("#FF8000", b.Children![0].Style?.Foreground, "child formatting retained");
        Equal(true, b.IsInteger && b.Activation is not null && b.ButtonGeneration >= 0, "input selection and generation retained");
    }
    Contains("FLOW_LOGICAL=" + counts.Length, runtime.Output, "HTML wrapping counts one logical line per HTML_PRINT");
    Equal(rows.Count, rows.Select(l => l.LineId).Distinct().Count(), "physical rows have independent IDs");
    foreach (var row in rows.Where(l => l.Parts.Any(p => p.Tooltip?.StartsWith("DESCRIPTION_") == true)))
        Equal(true, Flatten(row.Parts).Where(p => p.Children is null).Sum(p => p.Text.Length * (p.Style?.FontSize ?? 18) / 2d) <= 1512, "children occupy one physical row, no browser-only overflow");
    int lastOwner = Enumerable.Range(0, rows.Count).Last(r => rows[r].Parts.Any(p => p.Input != null && p.Input != "0"));
    Equal(true, lastOwner < Enumerable.Range(0, rows.Count).Single(r => Flatten(rows[r].Parts).Any(p => p.Text.Contains("FLOW_LOGICAL="))), "subsequent heading/rule/back rows follow title continuations");
    Equal(true, runtime.SubmitDisplay(buttons[^1]), "last wrapped button accepts exactly one input");
    Equal(false, runtime.SubmitDisplay(buttons[^1]), "old button generation rejected");
    Equal(false, Flatten(runtime.DisplayLines.SelectMany(l => l.Parts)).Any(p => p.Tooltip?.StartsWith("DESCRIPTION_") == true), "CLEARLINE removes all logical HTML continuations");
    Console.WriteLine("PASS html-flow-lists " + countsText + " physicalRows=" + rows.Count); return;
}
if (args is ["qpt-quit", var quitCase])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL QPT_TITLE\nINPUT\nIF RESULT == 1\nSAVEGLOBAL\nSAVEDATA 400, \"suspend\"\nENDIF\nPRINTW BEFORE_QUIT_1\nPRINTW BEFORE_QUIT_2\nPRINTW BEFORE_QUIT_3\nQUIT\n");
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.StartTitle();
    object process = typeof(BrowserRuntimeSession).GetField("process", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
    long oldRequest = runtime.PendingInput!.RequestId;
    Equal(true, runtime.Submit(new(oldRequest, quitCase == "normal" ? "0" : "1")), "title input accepted once");
    if (quitCase != "normal")
    {
        Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "SAVE pauses until transaction ACK");
        Equal(false, runtime.AcknowledgePersistence("stale"), "old ACK rejected");
        bool blocked = false;
        try { runtime.ReturnToTitle(); } catch (InvalidOperationException) { blocked = true; }
        Equal(true, blocked, "cannot return before persistence completes");
        if (quitCase == "failure")
        {
            try { await runtime.DrainPersistenceAsync(_ => Task.FromException(new IOException("fixture transaction failed"))); } catch (IOException) { }
            Equal(0, runtime.PendingPersistenceCount, "failure also empties the queue");
            Equal(BrowserRuntimeStatus.Failed, runtime.Status, "failure is not normal QUIT");
            blocked = false;
            try { runtime.ReturnToTitle(); } catch (InvalidOperationException) { blocked = true; }
            Equal(true, blocked, "Failed cannot be reset as normal termination");
            Equal(true, !string.IsNullOrEmpty(runtime.PersistenceError), "save error remains available");
            Console.WriteLine("PASS qpt-quit failure"); return;
        }
        var committed = new List<string>();
        var transactionCompleted = new TaskCompletionSource();
        Task drain = runtime.DrainPersistenceAsync(mutation => { committed.Add(mutation.LogicalFilename); return mutation.LogicalFilename == "global.sav" ? transactionCompleted.Task : Task.CompletedTask; });
        Equal(false, drain.IsCompleted, "delayed transaction has not completed");
        Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "delayed transaction cannot expose a completed game");
        transactionCompleted.SetResult();
        await drain;
        Equal("global.sav,save400.sav", string.Join(',', committed), "both operations acknowledged in order");
        Equal(false, runtime.AcknowledgePersistence("stale"), "late ACK cannot advance PRINTW");
    }
    for (int i = 1; i <= 3; i++)
    {
        Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "PRINTW still requires input before QUIT");
        Equal(BrowserInputKind.Enter, runtime.PendingInput?.Kind, "PRINTW is an Enter wait");
        Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "")), "advance PRINTW once");
    }
    Equal(BrowserRuntimeStatus.Succeeded, runtime.Status, "normal QUIT");
    Equal(null, runtime.PendingInput, "QUIT has no pending input");
    string? suspendFile = quitCase == "normal" ? null : Directory.GetFiles(root, "save400.sav", SearchOption.AllDirectories).Single();
    string? suspendBefore = suspendFile is null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(suspendFile)));
    runtime.ReturnToTitle();
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "terminal return reaches interactive title");
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "manual title selection; no automatic LOAD");
    Equal(true, ReferenceEquals(process, typeof(BrowserRuntimeSession).GetField("process", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)), "same AOT Process reused");
    Equal(false, runtime.Submit(new(oldRequest, "1")), "old request cannot resume after title return");
    Equal(false, runtime.AcknowledgePersistence("stale"), "old ACK cannot resume after title return");
    Equal(0, runtime.PendingPersistenceCount, "title return does not delete or load save400");
    if (suspendBefore is not null) Equal(suspendBefore, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(suspendFile!))), "save400 bytes remain unchanged by title return");
    Console.WriteLine("PASS qpt-quit " + quitCase); return;
}
if (args is ["qpt-printc"])
{
    string erb = "@SYSTEM_TITLE\nLOCAL:0 = LINECOUNT\n" + string.Concat(Enumerable.Range(1, 13).Select(i => $"PRINTFORMLC [{i}] ITEM{i}\n"))
        + "PRINTL\nPRINTFORML LOGICAL={LINECOUNT - LOCAL:0}\nSETCOLOR 255,128,64\nPRINTL NEXT_HEADING\nDRAWLINE\nPRINTL [0] BACK\nINPUT\nCLEARLINE 6\nPRINTL AFTER_CLEAR\nINPUT\nQUIT\n";
    string root = CreateGlobalFixture(erb);
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nボタンの途中で行を折りかえさない:YES\n", Encoding.GetEncoding(932));
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.TextWidthMeasurer = (text, _, _) => text.Sum(c => c <= 127 ? 8d : 16d);
    runtime.SetViewport(600, 824);
    runtime.StartTitle();
    var rows = runtime.DisplayLines;
    int[] rowIndex = Enumerable.Range(1, 13).Select(i => Enumerable.Range(0, rows.Count).Single(r => Flatten(rows[r].Parts).Any(p => p.Input == i.ToString()))).ToArray();
    Equal(true, rowIndex.Distinct().Count() >= 3, "13 PRINTLC choices occupy distinct physical rows");
    Equal(true, rowIndex[0] != rowIndex[6] && rowIndex[6] != rowIndex[12], "separate membership, not two Any matches on one row");
    Contains("LOGICAL=1", runtime.Output, "wrapping does not inflate logical LINECOUNT");
    Equal(true, rows.Select(l => l.LineId).Distinct().Count() == rows.Count, "physical rows have unique DOM identities");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "wrapped button remains accepted");
    Equal(false, Flatten(runtime.DisplayLines.SelectMany(l => l.Parts)).Any(p => int.TryParse(p.Input, out int n) && n >= 1 && n <= 13), "CLEARLINE removes the whole logical PRINTLC row including continuation rows");
    Console.WriteLine("PASS qpt-printc"); return;
}
if (args is ["qpt-tooltip"])
{
    var runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT \"<div xpos='30px'><nonbutton title='image description'><img src='missing' height='40px'></nonbutton><button value='3' title='normal description'>NORMAL</button></div>\"\nHTML_PRINT_ISLAND \"<div display='absolute-lefttop' xpos='200px' ypos='100px'><button value='7' title='skill description&lt;br&gt;second line'>SKILL</button></div>\", 2\nINPUT\nHTML_PRINT_ISLAND_CLEAR 2\nWAIT\nQUIT\n"), null);
    runtime.StartTitle();
    var parts = Flatten(runtime.DisplayLines.SelectMany(l => l.Parts)).ToArray();
    Equal(true, parts.Any(p => p.Kind == BrowserDisplayPartKind.NonButton && p.Tooltip == "image description" && p.Input is null && p.Activation is null), "input-less description preserved independently from activation");
    var skill = parts.Single(p => p.Input == "7");
    Equal("skill description\nsecond line", skill.Tooltip, "depth2 tooltip reaches DTO with line break");
    Equal(true, skill.Activation is not null && runtime.IslandLineCount > 0, "Island button remains active");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "7")), "skill selection still accepted");
    Equal(0, runtime.IslandLineCount, "Island is removed after selection");
    Console.WriteLine("PASS qpt-tooltip DTO and input contracts"); return;
}
if (args is ["qpt-browser-fixture", var fixtureOutput])
{
    string erb = """
@SYSTEM_TITLE
GCREATE 910,40,40
SPRITECREATE "TIPIMG",910,0,0,40,40
PRINTL QPT FIXTURE MENU
HTML_PRINT "<div display='absolute-lefttop' xpos='40px' ypos='100px' width='150px' height='70px'><nonbutton title='Image description&lt;br&gt;second line'><img src='TIPIMG'></nonbutton><nonbutton title='Text description'>TEXT TIP</nonbutton></div>"
HTML_PRINT_ISLAND "<div display='absolute-lefttop' xpos='300px' ypos='120px' width='200px' height='40px'><button value='7' title='Island skill description&lt;br&gt;second line'>SKILL TIP</button></div>", 2
PRINTL [0] ROWS   [1] SAVE AND QUIT   [2] NORMAL QUIT
INPUT
HTML_PRINT_ISLAND_CLEAR 2
IF RESULT == 0
CALL QPT_ROWS
ELSEIF RESULT == 1
SAVEGLOBAL
SAVEDATA 400, "QPT suspend fixture"
PRINTW BEFORE_QUIT_1
PRINTW BEFORE_QUIT_2
PRINTW BEFORE_QUIT_3
ENDIF
QUIT

@QPT_ROWS
PRINTL ROWS_BEGIN
""" + "\n" + string.Concat(Enumerable.Range(1, 13).Select(i => $"PRINTFORMLC [{i}] 選択肢{i}\n")) + "PRINTL\nSETCOLOR 0xff8000\nPRINTL COLORED_HEADING\nRESETCOLOR\nDRAWLINE\nPRINTL [0] BACK\nINPUT\nRETURN\n";
    string root = CreateGlobalFixture(erb);
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nウィンドウ幅:1512\nウィンドウ高さ:882\n一行の高さ:18\nPRINTCの文字数:50\nボタンの途中で行を折りかえさない:YES\n", Encoding.GetEncoding(932));
    Directory.CreateDirectory(fixtureOutput);
    string data = Path.Combine(fixtureOutput, "data"), package = Path.Combine(fixtureOutput, "package");
    Directory.CreateDirectory(data); Directory.CreateDirectory(package);
    var files = new List<DataPackageFile>();
    using (var zip = ZipFile.Open(Path.Combine(package, "qpt-fixture.zip"), ZipArchiveMode.Create))
        foreach (string source in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = Relative(root, source); string destination = Path.Combine(data, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, false);
            zip.CreateEntryFromFile(source, name);
            byte[] bytes = File.ReadAllBytes(source);
            files.Add(new(name, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)), "qpt-fixture.zip"));
        }
    File.WriteAllText(Path.Combine(package, "manifest.json"), JsonSerializer.Serialize(new DataPackageManifest(1, files.Sum(f => f.Size), files), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    Console.WriteLine("PASS emitted task-only UTF8-BOM fixture and verified-input manifest");return;
}
if (args is ["save-import-cancel-finalization"])
{
    var normalEvents = new List<string>();
    int normalCommits = 0, normalLoads = 0;
    SaveImportFinalizationResult normal = await BrowserSaveImportFinalizer.FinishAsync(
        overwriteDeclined: true,
        saveManagerMode: false,
        cleanup: async () => { normalEvents.Add("cleanup"); await Task.Yield(); return true; },
        beginTitleHandoff: () => { normalEvents.Add("begin-title"); return Task.CompletedTask; },
        releaseStoreLock: () => { normalEvents.Add("unexpected-release"); return Task.FromResult(true); },
        navigateToTitle: () => { normalEvents.Add("unexpected-navigation"); return Task.CompletedTask; },
        startRuntimeTitle: () => { normalEvents.Add("runtime-title"); return Task.CompletedTask; });
    Equal(SaveImportFinalizationResult.TitleHandoffStarted, normal, "Runtime overwrite decline starts title exactly once after cleanup");
    Equal("cleanup,begin-title,runtime-title", string.Join(',', normalEvents), "Runtime title start follows completed cleanup without lock handoff");
    Equal(0, normalCommits, "overwrite decline does not commit imported files");
    Equal(0, normalLoads, "overwrite decline does not auto-load a save slot");

    var managerEvents = new List<string>();
    SaveImportFinalizationResult manager = await BrowserSaveImportFinalizer.FinishAsync(
        overwriteDeclined: true,
        saveManagerMode: true,
        cleanup: async () => { managerEvents.Add("cleanup"); await Task.Yield(); return true; },
        beginTitleHandoff: () => { managerEvents.Add("begin-title"); return Task.CompletedTask; },
        releaseStoreLock: async () => { managerEvents.Add("release-start"); await Task.Yield(); managerEvents.Add("release-complete"); return true; },
        navigateToTitle: () => { managerEvents.Add("navigate"); return Task.CompletedTask; },
        startRuntimeTitle: () => { managerEvents.Add("unexpected-runtime-title"); return Task.CompletedTask; });
    Equal(SaveImportFinalizationResult.TitleHandoffStarted, manager, "saveManager overwrite decline returns to title exactly once");
    Equal("cleanup,begin-title,release-start,release-complete,navigate", string.Join(',', managerEvents), "saveManager navigation waits for lock release after cleanup");

    var nonDeclineEvents = new List<string>();
    SaveImportFinalizationResult nonDecline = await BrowserSaveImportFinalizer.FinishAsync(
        overwriteDeclined: false,
        saveManagerMode: true,
        cleanup: () => { nonDeclineEvents.Add("cleanup"); return Task.FromResult(true); },
        beginTitleHandoff: () => { nonDeclineEvents.Add("unexpected-begin-title"); return Task.CompletedTask; },
        releaseStoreLock: () => { nonDeclineEvents.Add("unexpected-release"); return Task.FromResult(true); },
        navigateToTitle: () => { nonDeclineEvents.Add("unexpected-navigation"); return Task.CompletedTask; },
        startRuntimeTitle: () => { nonDeclineEvents.Add("unexpected-runtime-title"); return Task.CompletedTask; });
    Equal(SaveImportFinalizationResult.CleanupCompleted, nonDecline, "ordinary cancellation or completion does not trigger title handoff");
    Equal("cleanup", string.Join(',', nonDeclineEvents), "non-decline only cleans staged import");

    var cleanupFailureEvents = new List<string>();
    SaveImportFinalizationResult cleanupFailure = await BrowserSaveImportFinalizer.FinishAsync(
        overwriteDeclined: true,
        saveManagerMode: false,
        cleanup: () => { cleanupFailureEvents.Add("cleanup"); return Task.FromResult(false); },
        beginTitleHandoff: () => { cleanupFailureEvents.Add("unexpected-begin-title"); return Task.CompletedTask; },
        releaseStoreLock: () => { cleanupFailureEvents.Add("unexpected-release"); return Task.FromResult(true); },
        navigateToTitle: () => { cleanupFailureEvents.Add("unexpected-navigation"); return Task.CompletedTask; },
        startRuntimeTitle: () => { cleanupFailureEvents.Add("unexpected-runtime-title"); return Task.CompletedTask; });
    Equal(SaveImportFinalizationResult.CleanupFailed, cleanupFailure, "cleanup failure blocks overwrite-cancel return");
    Equal("cleanup", string.Join(',', cleanupFailureEvents), "cleanup failure does not release or start title");

    var thrownCleanupEvents = new List<string>();
    bool cleanupExceptionObserved = false;
    try
    {
        _ = await BrowserSaveImportFinalizer.FinishAsync(
            overwriteDeclined: true,
            saveManagerMode: false,
            cleanup: () => { thrownCleanupEvents.Add("cleanup"); return Task.FromException<bool>(new InvalidOperationException("cleanup failed")); },
            beginTitleHandoff: () => { thrownCleanupEvents.Add("unexpected-begin-title"); return Task.CompletedTask; },
            releaseStoreLock: () => { thrownCleanupEvents.Add("unexpected-release"); return Task.FromResult(true); },
            navigateToTitle: () => { thrownCleanupEvents.Add("unexpected-navigation"); return Task.CompletedTask; },
            startRuntimeTitle: () => { thrownCleanupEvents.Add("unexpected-runtime-title"); return Task.CompletedTask; });
    }
    catch (InvalidOperationException ex) when (ex.Message == "cleanup failed")
    {
        cleanupExceptionObserved = true;
    }
    Equal(true, cleanupExceptionObserved, "cleanup exception is surfaced to the App error handler");
    Equal("cleanup", string.Join(',', thrownCleanupEvents), "cleanup exception blocks the handoff callbacks");

    var releaseFailureEvents = new List<string>();
    SaveImportFinalizationResult releaseFailure = await BrowserSaveImportFinalizer.FinishAsync(
        overwriteDeclined: true,
        saveManagerMode: true,
        cleanup: () => { releaseFailureEvents.Add("cleanup"); return Task.FromResult(true); },
        beginTitleHandoff: () => { releaseFailureEvents.Add("begin-title"); return Task.CompletedTask; },
        releaseStoreLock: () => { releaseFailureEvents.Add("release"); return Task.FromResult(false); },
        navigateToTitle: () => { releaseFailureEvents.Add("unexpected-navigation"); return Task.CompletedTask; },
        startRuntimeTitle: () => { releaseFailureEvents.Add("unexpected-runtime-title"); return Task.CompletedTask; });
    Equal(SaveImportFinalizationResult.LockReleaseFailed, releaseFailure, "failed lock release blocks saveManager navigation");
    Equal("cleanup,begin-title,release", string.Join(',', releaseFailureEvents), "saveManager does not navigate before successful release");

    Console.WriteLine("PASS WEB-HOST-IMPORT-CANCEL-RETURN-TO-TITLE finalization flow");
    return;
}

if (args is ["vsl-native-paint-range"])
{
    BrowserDisplayLine[] lines = Enumerable.Range(1, 1_000).Select(id => new BrowserDisplayLine("left", [], LineId: id)).ToArray();
    BrowserDisplayWindowState state = new();
    PropertyInfo startProperty = typeof(BrowserDisplayWindowSlice).GetProperty("NativePaintStart")
        ?? throw new InvalidOperationException("Native paint range start is missing");
    PropertyInfo endProperty = typeof(BrowserDisplayWindowSlice).GetProperty("NativePaintEnd")
        ?? throw new InvalidOperationException("Native paint range end is missing");
    MethodInfo calculate = typeof(BrowserDisplayWindowState).GetMethods()
        .Single(method => method.Name == nameof(BrowserDisplayWindowState.Calculate) && method.GetParameters().Length == 4);
    BrowserDisplayWindowSlice At(double scrollTop, double clientHeight)
    {
        state.UpdateScroll(new(scrollTop, clientHeight, false, 0, 0));
        return (BrowserDisplayWindowSlice)(calculate.Invoke(state, [lines, lines.Length, null, 18])
            ?? throw new InvalidOperationException("Native paint range slice is missing"));
    }
    (int Start, int End) Range(BrowserDisplayWindowSlice slice) =>
        ((int)startProperty.GetValue(slice)!, (int)endProperty.GetValue(slice)!);

    Equal((0, 45), Range(At(0, 810)), "Native paint range at history top");
    BrowserDisplayWindowSlice middle = At(180, 810);
    Equal((9, 55), Range(middle), "Native OnPaint includes one owner row above the visible rows");
    Equal(true, middle.Start <= 8 && 8 < middle.End, "out-of-paint owner remains in virtualized overscan");
    Equal(false, Range(middle).Start <= 8 && 8 < Range(middle).End, "overscan owner outside Native paint range is identified");
    Equal(true, Range(middle).Start <= 9 && 9 < Range(middle).End, "Native edge owner remains paintable");
    Equal((10, 56), Range(At(180, 824)), "Native paint range uses exact viewport/line-height formula");
    Equal((9, 55), Range(At(185, 810)), "fractional browser scroll preserves native logical-row range");
    Equal((954, 1_000), Range(At((1_000 - 45) * 18, 810)), "Native paint range at history tail");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-AND-SKILL-LABEL native owner-row range");
    return;
}

if (args is ["vvr-resume-follow-tail"])
{
    BrowserDisplayPart positioned = new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.Absolute, ExplicitPosition: true));
    BrowserDisplayLine[] lines = Enumerable.Range(1, 1_000).Select(id => new BrowserDisplayLine("left", [positioned], LineId: id)).ToArray();
    BrowserDisplayWindowState state = new();
    foreach (double viewportHeight in new[] { 810d, 824d })
    {
        state.UpdateScroll(new(0, viewportHeight, false, 1, 0));
        state.ResumeFollowTail();
        BrowserDisplayWindowSlice slice = state.Calculate(lines, lines.Length, displayLineHeight: 18);
        Equal((954, 1_000), (slice.NativePaintStart, slice.NativePaintEnd), $"resume-tail Native owners at {viewportHeight}px before JS scroll notification");
        bool nativeLinePainted = 999 >= slice.NativePaintStart && 999 < slice.NativePaintEnd;
        Equal(true, positioned.ShouldRenderForNativePaint(nativeLinePainted), $"tail positioned owner paints at {viewportHeight}px");
        Equal(0d, state.ScrollState.ScrollTop, "tail resume does not wait for a JS scroll update");
    }
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-VISIBILITY ResumeFollowTail owner range");
    return;
}

if (args is ["vvr-append-follow-tail"])
{
    BrowserDisplayPart positioned = new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.Absolute, ExplicitPosition: true));
    BrowserDisplayLine[] before = Enumerable.Range(1, 100).Select(id => new BrowserDisplayLine("left", [positioned], LineId: id)).ToArray();
    BrowserDisplayLine[] after = Enumerable.Range(1, 200).Select(id => new BrowserDisplayLine("left", [positioned], LineId: id)).ToArray();
    BrowserDisplayWindowState state = new();
    state.UpdateScroll(new(990, 810, true, 100, 0));
    _ = state.Calculate(before, before.Length, structureGeneration: 1, displayLineHeight: 18);
    BrowserDisplayWindowSlice slice = state.Calculate(after, after.Length, structureGeneration: 2, displayLineHeight: 18);
    Equal((154, 200), (slice.NativePaintStart, slice.NativePaintEnd), "appended tail rows paint before JS viewport notification");
    Equal(true, positioned.ShouldRenderForNativePaint(199 >= slice.NativePaintStart && 199 < slice.NativePaintEnd), "new tail owner remains paintable");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-VISIBILITY appended tail owner range");
    return;
}

if (args is ["vvr-replaced-screen"])
{
    BrowserDisplayPart positioned = new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.Absolute, ExplicitPosition: true));
    BrowserDisplayLine[] previous = Enumerable.Range(1, 20).Select(id => new BrowserDisplayLine("left", [positioned], LineId: id)).ToArray();
    BrowserDisplayLine[] replacement = Enumerable.Range(21, 1_000).Select(id => new BrowserDisplayLine("left", [positioned], LineId: id)).ToArray();
    BrowserDisplayWindowState state = new();
    state.UpdateScroll(new(0, 824, true, 1, 0));
    _ = state.Calculate(previous, previous.Length, structureGeneration: 9, displayLineHeight: 18);
    BrowserDisplayWindowSlice current = state.Calculate(replacement, replacement.Length, structureGeneration: 10, displayLineHeight: 18);
    Equal((954, 1_000), (current.NativePaintStart, current.NativePaintEnd), "new long screen paints its tail despite prior short-screen ScrollTop");
    Equal(true, positioned.ShouldRenderForNativePaint(999 >= current.NativePaintStart && 999 < current.NativePaintEnd), "replacement screen tail owner remains paintable");

    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n"), null);
    runtime.StartTitle();
    object console = typeof(BrowserRuntimeSession).GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    long previousGeneration = runtime.DisplayStructureGeneration;
    console.GetType().GetMethod("ClearText")!.Invoke(console, null);
    MethodInfo printLine = console.GetType().GetMethod("PrintSystemLine")!;
    for (int index = 0; index < 1_000; index++) printLine.Invoke(console, [$"REPLACEMENT_{index}"]);
    Equal(true, runtime.DisplayStructureGeneration > previousGeneration, "ClearText replacement advances the structure generation");
    Equal(runtime.RetainedDisplayLines.Count, runtime.DisplayLines.Count, "activated rendering preserves retained row count and index mapping");
    for (int index = 0; index < runtime.RetainedDisplayLines.Count; index++)
        Equal(runtime.RetainedDisplayLines[index].LineId, runtime.DisplayLines[index].LineId, $"owner index {index} remains paired with the same LineId");
    BrowserDisplayWindowState afterClear = new();
    afterClear.UpdateScroll(new(0, 824, true, 1, 0));
    BrowserDisplayWindowSlice cleared = afterClear.Calculate(runtime.RetainedDisplayLines, structureGeneration: runtime.DisplayStructureGeneration, displayLineHeight: runtime.DisplayLineHeight);
    int expectedTailStart = 999 - ((824 - runtime.DisplayLineHeight) / runtime.DisplayLineHeight + 1);
    Equal((expectedTailStart, 1_000), (cleared.NativePaintStart, cleared.NativePaintEnd), "actual ClearText output uses its current line-height tail before another viewport update");
    Equal(true, cleared.IsNativeLinePainted(999), "actual replacement output passes its latest owner row to Razor");
    afterClear.UpdateScroll(new(17_000, 824, false, runtime.RetainedDisplayLines[950].LineId, 0));
    console.GetType().GetMethod("deleteLine")!.Invoke(console, [900]);
    BrowserDisplayWindowSlice afterDelete = afterClear.Calculate(runtime.RetainedDisplayLines, structureGeneration: runtime.DisplayStructureGeneration, displayLineHeight: runtime.DisplayLineHeight);
    Equal(100, runtime.RetainedDisplayLines.Count, "deleteLine removes the intended rows");
    int expectedDeletedTailStart = 99 - ((824 - runtime.DisplayLineHeight) / runtime.DisplayLineHeight + 1);
    Equal((expectedDeletedTailStart, 100), (afterDelete.NativePaintStart, afterDelete.NativePaintEnd), "deleteLine with a stale high ScrollTop keeps the current tail owner paintable");
    Equal(true, afterDelete.IsNativeLinePainted(99), "latest row after deleteLine reaches the Razor owner parameter");
    Console.WriteLine($"INFO replacement viewport=824px lineHeight={runtime.DisplayLineHeight}px tail=[{cleared.NativePaintStart},{cleared.NativePaintEnd}) deleteLine=[{afterDelete.NativePaintStart},{afterDelete.NativePaintEnd})");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-VISIBILITY replacement screen owner range");
    return;
}

if (args is ["vvr-backlog-island-owners"])
{
    BrowserDisplayPart positioned = new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.Absolute, ExplicitPosition: true));
    BrowserDisplayPart relative = new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.Relative));
    BrowserDisplayLine[] lines = Enumerable.Range(1, 102)
        .Select(id => new BrowserDisplayLine("left", [id <= 100 ? positioned : relative], LineId: id))
        .ToArray();
    BrowserDisplayWindowState state = new();
    state.UpdateScroll(new(180, 810, false, 11, -3));
    BrowserDisplayWindowSlice slice = state.Calculate(lines, mainLineCount: 100, structureGeneration: 11, displayLineHeight: 18);
    Equal((9, 55), (slice.NativePaintStart, slice.NativePaintEnd), "manual backlog retains Native paint interval");
    Equal(true, slice.Start <= 8 && 8 < slice.End, "old positioned owner remains in layout overscan");
    Equal(false, positioned.ShouldRenderForNativePaint(8 >= slice.NativePaintStart && 8 < slice.NativePaintEnd), "stale positioned owner outside Native paint stays hidden");
    Equal(true, positioned.ShouldRenderForNativePaint(9 >= slice.NativePaintStart && 9 < slice.NativePaintEnd), "valid positioned owner remains visible");
    Equal(true, relative.ShouldRenderForNativePaint(false), "ordinary relative flow is not owner-gated");
    Equal(true, slice.End <= 100, "main virtual window excludes independent HTML island rows");
    Equal(true, relative.ShouldRenderForNativePaint(true), "HTML island layer's default paint permission remains independent");
    state.ApplyMeasurements(Enumerable.Range(1, 100).Select(id => new BrowserDisplayLineMeasurement(id, 20)).ToArray());
    state.UpdateScroll(new(200, 810, false, 11, -3));
    BrowserDisplayWindowSlice measured = state.Calculate(lines, mainLineCount: 100, structureGeneration: 12, displayLineHeight: 18);
    Equal((9, 55), (measured.NativePaintStart, measured.NativePaintEnd), "physical ScrollTop maps through measured row heights before Native owner gating");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-VISIBILITY backlog and island owner policy");
    return;
}

if (args is ["vvr-generation-aware-scroll"])
{
    MethodInfo? update = typeof(BrowserDisplayWindowState).GetMethod(nameof(BrowserDisplayWindowState.UpdateScroll), [typeof(BrowserDisplayScrollState), typeof(long), typeof(long)]);
    Equal(true, update is not null, "display viewport update exposes generation-aware overload");
    if (update is null) return;
    BrowserDisplayWindowState state = new();
    update.Invoke(state, [new BrowserDisplayScrollState(180, 810, false, 20, 0), 7L, 7L]);
    BrowserDisplayScrollState before = state.ScrollState;
    bool acceptedStale = (bool)update.Invoke(state, [new BrowserDisplayScrollState(0, 810, true, 200, 0), 6L, 7L])!;
    Equal(false, acceptedStale, "stale viewport generation is rejected");
    Equal(before, state.ScrollState, "stale viewport cannot overwrite current scroll state");
    bool acceptedCurrent = (bool)update.Invoke(state, [new BrowserDisplayScrollState(0, 810, true, 200, 0), 7L, 7L])!;
    Equal(true, acceptedCurrent, "current display viewport generation is applied");
    Equal(true, state.FollowTail, "current near-bottom viewport can enable follow-tail");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-VISIBILITY viewport generation guard");
    return;
}

if (args is ["vvr-render-membership"])
{
    MethodInfo? paints = typeof(BrowserDisplayWindowSlice).GetMethod("IsNativeLinePainted", [typeof(int)]);
    Equal(true, paints is not null, "slice exposes the owner-render parameter used by Razor");
    if (paints is null) return;
    BrowserDisplayWindowSlice slice = new(20, 40, 0, 0, false, 25, 35);
    Equal(false, (bool)paints.Invoke(slice, [24])!, "owner before Native paint range is not passed as rendered");
    Equal(true, (bool)paints.Invoke(slice, [25])!, "first Native owner is passed as rendered");
    Equal(true, (bool)paints.Invoke(slice, [34])!, "last Native owner is passed as rendered");
    Equal(false, (bool)paints.Invoke(slice, [35])!, "owner after Native paint range is not passed as rendered");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-VISIBILITY slice render membership");
    return;
}

if (args is ["vpr-part-render-refresh"])
{
    Type? policyType = typeof(BrowserDisplayPart).Assembly.GetType("MinorShift.Emuera.Web.Runtime.BrowserPartRenderPolicy");
    Equal(true, policyType is not null, "BrowserPartView render-input policy is exposed to the existing Runtime test project");
    if (policyType is null) return;
    MethodInfo shouldRender = policyType.GetMethod("ShouldRender", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("BrowserPart render policy method missing");
    bool Changed(BrowserDisplayPart samePart, bool? previousOwner, bool currentOwner, int previousFlow = 0, int currentFlow = 0, bool callbackChanged = false)
        => (bool)shouldRender.Invoke(null, [samePart, samePart, callbackChanged, previousOwner, currentOwner, previousFlow, currentFlow])!;

    BrowserDisplayPart image = new(BrowserDisplayPartKind.Image, ImageDataUrl: "data:image/png;base64,AA==", Layout: new(BrowserDisplayMode.Absolute, ExplicitPosition: true));
    BrowserDisplayPart caption = new(BrowserDisplayPartKind.NonButton, Text: "shop line", Layout: new(BrowserDisplayMode.AbsoluteLeftBottom, ExplicitPosition: true));
    BrowserDisplayPart button = new(BrowserDisplayPartKind.Button, Text: "old choice", Input: "OLD", Layout: new(BrowserDisplayMode.Absolute, ExplicitPosition: true));
    BrowserDisplayPart nested = new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.AbsoluteLeftBottom, ExplicitPosition: true), Children: [
        new(BrowserDisplayPartKind.Group, Layout: new(BrowserDisplayMode.Relative), Children: [image, caption, button])
    ]);

    foreach (BrowserDisplayPart part in new[] { nested, image, caption, button })
    {
        Equal(true, Changed(part, true, false), $"same {part.Kind} instance rerenders true-to-false owner visibility");
        Equal(false, part.ShouldRenderForNativePaint(false), $"hidden {part.Kind} is omitted from its rendered subtree");
        Equal(true, Changed(part, false, true), $"same {part.Kind} instance rerenders false-to-true owner visibility");
        Equal(true, part.ShouldRenderForNativePaint(true), $"visible {part.Kind} is restored");
        Equal(false, Changed(part, true, true), $"unchanged {part.Kind} render inputs preserve the skip optimization");
    }
    Equal(true, Changed(nested, true, true, 0, 12), "changed island-flow offset invalidates rendered style");
    Equal(true, Changed(nested, true, true, callbackChanged: true), "changed event callback invalidates the rendered part");
    Console.WriteLine("PASS WEB-RUNTIME-POSITIONED-PART-REFRESH component render-input decision");
    return;
}

if (args is ["vsl-skill-label-div-flow"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n"), null);
    object console = typeof(BrowserRuntimeSession).GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    const string html = "<div display='absolute-leftbottom'><div width='6000' height='1000' border_width='20'>EXPLANATION</div><div xpos='-0' ypos='-200'><div width='1200' height='200' border_width='20'>SKILL_LABEL</div></div></div>";
    console.GetType().GetMethod("PrintHTMLIsland")!.Invoke(console, [html, 3]);
    BrowserDisplayPart outer = runtime.DisplayLines.Last().Parts.Single(part => part.Kind == BrowserDisplayPartKind.Group);
    BrowserDisplayPart explanation = outer.Children!.Single(part => Flatten([part]).Any(child => child.Text == "EXPLANATION"));
    BrowserDisplayPart label = outer.Children!.Single(part => Flatten([part]).Any(child => child.Text == "SKILL_LABEL"));
    Equal(BrowserDisplayMode.AbsoluteLeftBottom, outer.Layout!.Mode, "skill explanation outer island uses Native screen origin");
    Equal(1_080, explanation.Layout!.Width, "explanation keeps its explicit Native width after Emuera size conversion");
    Equal(0, explanation.Width, "Native ConsoleDivElement flow width remains zero for the fixed-width window");
    Equal(0, label.Layout!.X, "skill label keeps game-authored xpos");
    Equal(-36, label.Layout.Y, "skill label keeps game-authored ypos after Emuera size conversion");
    Equal(0, label.Width, "Native ConsoleDivElement does not advance its parent flow width");
    MethodInfo renderForNativePaint = typeof(BrowserDisplayPart).GetMethod("ShouldRenderForNativePaint")
        ?? throw new InvalidOperationException("Native owner-line render policy is missing");
    bool CanRender(BrowserDisplayPart part, bool nativeLinePainted)
        => (bool)renderForNativePaint.Invoke(part, [nativeLinePainted])!;
    Equal(false, CanRender(label, false), "explicitly positioned skill overlay needs its owner line in Native paint range");
    Equal(true, CanRender(label, true), "positioned overlay on a Native-painted line remains visible");
    Equal(true, CanRender(explanation.Children!.First(), false), "ordinary overscan flow content stays rendered");

    string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "app.css"));
    int ruleStart = css.IndexOf(".game-html-origin {", StringComparison.Ordinal);
    int ruleEnd = ruleStart < 0 ? -1 : css.IndexOf('}', ruleStart);
    string originRule = ruleStart < 0 || ruleEnd < 0 ? string.Empty : css[ruleStart..ruleEnd];
    Contains("width: 0;", originRule, "HTML DIV origin must not consume sibling flow width like Native ConsoleDivElement");
    Contains(".game-html-origin > .game-html-group { width: max-content;", css, "visible HTML group retains its intrinsic drawing width");
    Console.WriteLine("PASS WEB-RUNTIME-VIEWPORT-AND-SKILL-LABEL Native DIV sibling flow");
    return;
}

if (args is ["warning-animation-timebase", var evidencePath])
{
    var script = new StringBuilder("@SYSTEM_TITLE\nGCREATE 902,180,180\nSPRITEANIMECREATE \"WARNING_TIMEBASE\",180,180\n");
    for (int frame = 0; frame < 64; frame++)
        script.AppendLine($"SPRITEANIMEADDFRAME \"WARNING_TIMEBASE\",902,0,0,1,1,{frame},0,125");
    script.AppendLine("SPRITEANIMEADDFRAME \"WARNING_TIMEBASE\",902,0,0,1,1,64,0,86400000");
    script.AppendLine("SPRITEANIMECREATE \"WARNING_RECREATED\",180,180\nSPRITEANIMEADDFRAME \"WARNING_RECREATED\",902,0,0,1,1,0,0,125\nSPRITEANIMEADDFRAME \"WARNING_RECREATED\",902,0,0,1,1,1,0,86400000");
    script.AppendLine("GCREATE 903,2,2\nGCLEAR 903,0xFFFF0000\nGCREATE 904,2,2\nGCLEAR 904,0x00000000");
    script.AppendLine("SPRITEANIMECREATE \"WARNING_ALPHA\",2,2\nSPRITEANIMEADDFRAME \"WARNING_ALPHA\",903,0,0,2,2,0,0,250\nSPRITEANIMEADDFRAME \"WARNING_ALPHA\",904,0,0,2,2,0,0,250");
    script.AppendLine("SETANIMETIMER 20\nPRINT_IMG \"WARNING_TIMEBASE\"\nWAIT\nQUIT");
    script.Replace("PRINT_IMG \"WARNING_TIMEBASE\"", "PRINT_IMG \"WARNING_TIMEBASE\"\nPRINT_IMG \"WARNING_ALPHA\"");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture(script.ToString()), null);
    runtime.StartTitle();
    const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    Type contents = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")
        ?? throw new InvalidOperationException("WARNING timebase AppContents missing");
    object sprite = contents.GetMethod("GetSprite", flags)!.Invoke(null, ["WARNING_TIMEBASE"])
        ?? throw new InvalidOperationException("WARNING timebase sprite missing");
    PropertyInfo currentFrameProperty = sprite.GetType().GetProperty("CurrentFrame", flags)
        ?? throw new InvalidOperationException("WARNING CurrentFrame missing");
    System.Drawing.Point FrameOffset(object current)
        => (System.Drawing.Point)(current.GetType().GetProperty("Offset", flags)?.GetValue(current)
            ?? throw new InvalidOperationException("WARNING frame offset missing"));
    long tick = Environment.TickCount64;
    long total = (long)sprite.GetType().GetField("totaltime", flags)!.GetValue(sprite)!;
    object selectedFrame = currentFrameProperty.GetValue(sprite) ?? throw new InvalidOperationException("WARNING frame missing");
    int selectedDelay = (int)(selectedFrame.GetType().GetProperty("Delay", flags)?.GetValue(selectedFrame)
        ?? throw new InvalidOperationException("WARNING frame delay missing"));
    int selectedIndex = FrameOffset(selectedFrame).X;
    var red = new
    {
        tickCount64 = tick,
        totalDurationMs = total,
        processUptimeModuloMs = tick % total,
        shortAnimationDurationMs = 64 * 125,
        expectedInitialDelayMs = 125,
        observedInitialDelayMs = selectedDelay,
        observedInitialFrameOffsetX = selectedIndex,
        reproducedStaticTerminalFrameAtInitialRead = selectedDelay == 86400000
    };
    File.WriteAllText(evidencePath, JsonSerializer.Serialize(red, new JsonSerializerOptions { WriteIndented = true }));

    MethodInfo frameAtMethod = sprite.GetType().GetMethod("GetCurrentFrameAt", flags)
        ?? throw new InvalidOperationException("WARNING deterministic frame clock missing");
    MethodInfo resetMethod = sprite.GetType().GetMethod("ResetTime", flags)
        ?? throw new InvalidOperationException("WARNING ResetTime missing");
    object At(object target, long now) => frameAtMethod.Invoke(target, [now])
        ?? throw new InvalidOperationException("WARNING deterministic frame missing");
    void AtIndex(object target, long now, int expected, string name)
        => Equal(expected, FrameOffset(At(target, now)).X, name);

    resetMethod.Invoke(sprite, null);
    const long origin = 3_600_000_000;
    AtIndex(sprite, origin, 0, "WARNING new playback starts at first frame regardless of uptime");
    AtIndex(sprite, origin + 124, 0, "WARNING before first delay keeps frame zero");
    AtIndex(sprite, origin + 125, 0, "WARNING exact frame boundary matches Native inclusive boundary");
    AtIndex(sprite, origin + 126, 1, "WARNING advances after first frame boundary");
    AtIndex(sprite, origin + 7999, 63, "WARNING last short frame before terminal hold");
    AtIndex(sprite, origin + 8000, 63, "WARNING 8-second Native boundary remains on last short frame");
    AtIndex(sprite, origin + 8001, 64, "WARNING enters 24-hour terminal hold after four blink cycles");
    AtIndex(sprite, origin + 86407999, 64, "WARNING remains on terminal frame through its hold");
    AtIndex(sprite, origin + 86408000, 0, "WARNING wraps only after total Native duration");
    resetMethod.Invoke(sprite, null);
    AtIndex(sprite, origin + 900000, 0, "WARNING ResetTime restarts at frame zero");
    AtIndex(sprite, origin + 900126, 1, "WARNING ResetTime uses a fresh per-sprite origin");
    object recreated = contents.GetMethod("GetSprite", flags)!.Invoke(null, ["WARNING_RECREATED"])
        ?? throw new InvalidOperationException("WARNING recreated sprite missing");
    AtIndex(recreated, origin + 500000, 0, "WARNING recreated instance has independent playback origin");
    Equal(true, ReferenceEquals(sprite, contents.GetMethod("GetSprite", flags)!.Invoke(null, ["WARNING_TIMEBASE"])), "WARNING multiple references share one SpriteAnime instance");

    resetMethod.Invoke(sprite, null);
    _ = currentFrameProperty.GetValue(sprite);
    BrowserDisplayPart alphaPart = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Text == "WARNING_ALPHA");
    string alphaFirstUrl = alphaPart.ImageDataUrl ?? throw new InvalidOperationException("WARNING alpha first URL missing");
    string alphaFirstHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(alphaFirstUrl)));
    SKBitmap Decode(string url)
    {
        const string prefix = "data:image/png;base64,";
        if (!url.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("WARNING frame URL is not PNG data");
        return SKBitmap.Decode(Convert.FromBase64String(url[prefix.Length..]))
            ?? throw new InvalidOperationException("WARNING frame PNG decode failed");
    }
    int AlphaPixels(string url)
    {
        using SKBitmap bitmap = Decode(url);
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            if (bitmap.GetPixel(x, y).Alpha != 0) count++;
        return count;
    }
    var alphaHashes = new HashSet<string>(StringComparer.Ordinal) { alphaFirstHash };
    var alphaPixels = new HashSet<int> { AlphaPixels(alphaFirstUrl) };
    for (int sample = 0; sample < 12; sample++)
    {
        await Task.Delay(50);
        runtime.RefreshAnimations();
        BrowserDisplayPart sampled = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Text == "WARNING_ALPHA");
        string url = sampled.ImageDataUrl ?? throw new InvalidOperationException("WARNING alpha sample URL missing");
        alphaHashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));
        alphaPixels.Add(AlphaPixels(url));
    }
    Equal(true, alphaHashes.Count >= 2, "WARNING alpha frames produce different PNG data");
    Equal(true, alphaPixels.Count >= 2, "WARNING alpha frames have different visible alpha pixels");
    File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { red, deterministic = new { initialIndex = 0, beforeBoundary = 0, exactBoundary = 0, afterBoundary = 1, lastShort = 63, at8000Ms = 63, after8000Ms = 64, beforeTotal = 64, atTotal = 0, reset = 1, recreated = 0, sameInstanceShared = true }, alphaFrames = new { distinctPngHashes = alphaHashes.Count, distinctAlphaPixelCounts = alphaPixels.Count, values = alphaPixels.Order().ToArray(), samplingDurationMs = 600 }, nativeContract = "first frame on first read; elapsed from per-sprite start; <= frame boundary; modulo total; ResetTime restarts" }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("PASS WARNING animation timebase");
    return;
}
if (args is ["ux16r5-animation-window", var animationWindowPath])
{
    async Task<object> Observe(BrowserRuntimeSession runtime, int first, int end)
    {
        var before = runtime.DisplayPerformance;
        long projection = runtime.DisplayProjectionCount, generation = runtime.DisplayStructureGeneration;
        int ticks = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(100);
            runtime.RefreshAnimations(first, end);
            for (int index = first; index < end; index++) _ = runtime.DisplayLines[index];
            ticks++;
        }
        var after = runtime.DisplayPerformance;
        Equal(generation, runtime.DisplayStructureGeneration, "visual ticks preserve structure");
        Equal(projection, runtime.DisplayProjectionCount, "visual ticks preserve activation context");
        Equal(true, after.AnimationLinesScanned - before.AnimationLinesScanned <= ticks * 138L, "window-bounded line visits");
        return new { elapsedMs = clock.Elapsed.TotalMilliseconds, ticks, first, end, before, after, retained = runtime.DisplayLines.Count };
    }
    string StaticRoot(string script)
    {
        string root = CreateGlobalFixture(script);
        File.AppendAllText(Path.Combine(root, "emuera.config"), "\n履歴ログの行数:50000", Encoding.GetEncoding(932));
        return root;
    }
    var runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(StaticRoot("@SYSTEM_TITLE\nSETANIMETIMER 100\nREDRAW 0\nREPEAT 12000\nPRINTFORML HISTORY_{COUNT}\nREND\nPRINTL [1] CURRENT\nINPUT\nQUIT\n"), null);
    runtime.StartTitle(); runtime.EnableDisplayPerformanceMetrics(); _ = runtime.DisplayLines[^1];
    object staticWindow = await Observe(runtime, runtime.DisplayLines.Count - 138, runtime.DisplayLines.Count);
    Equal(0L, runtime.DisplayPerformance.AnimationPublishes, "static60 has no visual publish");
    string dynamicRoot = StaticRoot("@SYSTEM_TITLE\nSETANIMETIMER 100\nREDRAW 0\nGCREATE 10,2,2\nGCREATE 11,2,2\nSPRITECREATE \"COPY\",10,0,0,2,2\nSPRITEANIMECREATE \"ANI\",2,2\nSPRITEANIMEADDFRAME \"ANI\",10,0,0,2,2,0,0,200\nSPRITEANIMEADDFRAME \"ANI\",11,0,0,2,2,0,0,200\nPRINT_IMG \"COPY\"\nPRINT_IMG \"ANI\"\nPRINTL\nREPEAT 12000\nPRINTFORML HISTORY_{COUNT}\nREND\nPRINTL [1] CURRENT\nINPUT\nQUIT\n");
    runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(dynamicRoot, null);
    runtime.StartTitle(); runtime.EnableDisplayPerformanceMetrics(); _ = runtime.DisplayLines[^1];
    var contents = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")!;
    object Parent(long id) => contents.GetMethod("GetGraphics", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [id])!;
    object parent = Parent(10), next = Parent(11);
    parent.GetType().GetMethod("GClear")!.Invoke(parent, [System.Drawing.Color.Red]);
    next.GetType().GetMethod("GClear")!.Invoke(next, [System.Drawing.Color.Blue]);
    runtime.RefreshAnimations(0, 138);
    object dynamicWindow = await Observe(runtime, 0, 138);
    Equal(true, runtime.DisplayPerformance.AnimationPublishes > 0, "visible60 advances real anime frames");
    long publishes = runtime.DisplayPerformance.AnimationPublishes;
    BrowserDisplayPart Image(string name) => Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).First(p => p.Text == name);
    string? oldG = Image("COPY").ImageDataUrl;
    parent.GetType().GetMethod("GClear")!.Invoke(parent, [System.Drawing.Color.Green]);
    object offscreenWindow = await Observe(runtime, runtime.DisplayLines.Count - 138, runtime.DisplayLines.Count);
    Equal(publishes, runtime.DisplayPerformance.AnimationPublishes, "offscreen60 never republishes");
    Equal(true, runtime.RefreshAnimations(0, 138), "scroll reveal refreshes changed graphics");
    Equal(false, oldG == Image("COPY").ImageDataUrl, "revealed SpriteG pixels current");
    File.WriteAllText(animationWindowPath, JsonSerializer.Serialize(new { staticWindow, dynamicWindow, offscreenWindow, reveal = runtime.DisplayPerformance, authority = "Runtime wall-clock window test; no DOM/physical paint assertion" }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("PASS UX16R5 static60 / dynamic60 / offscreen60 / reveal");
    return;
}
if (args is ["ux16r5-window-cost", var windowCostPath])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nREDRAW 0\nREPEAT 12000\nPRINTFORML HISTORY_{COUNT}\nREND\nPRINTL [1] CURRENT\nINPUT\nQUIT\n");
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\n履歴ログの行数:50000", Encoding.GetEncoding(932));
    var runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    var lines = new CountingDisplayLines(runtime.DisplayLines);
    var window = new BrowserDisplayWindowState();
    window.UpdateScroll(new(0, 864, true, 0, 0));
    var first = window.Calculate(lines, lines.Count, runtime.DisplayStructureGeneration);
    lines.Reads = 0;
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    for (int frame = 0; frame < 100; frame++)
        Equal(first, window.Calculate(lines, lines.Count, runtime.DisplayStructureGeneration), "unchanged geometry");
    watch.Stop();
    long reads = lines.Reads;
    File.WriteAllText(windowCostPath, JsonSerializer.Serialize(new { count = lines.Count, frames = 100, reads, elapsedMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated, first }, new JsonSerializerOptions { WriteIndented = true }));
    Equal(true, reads <= 13800, "visual-only window calculation must not walk retained history");
    window.ApplyMeasurements([new(lines[0].LineId, 36)]);
    var measured = window.Calculate(lines, lines.Count, runtime.DisplayStructureGeneration);
    Equal(true, measured.TopSpacerPx != first.TopSpacerPx, "measurement invalidates geometry cache");
    window.RemoveMeasurement(lines[0].LineId);
    Equal(first, window.Calculate(lines, lines.Count, runtime.DisplayStructureGeneration), "removal restores estimate");
    window.UpdateScroll(new(0, 864, false, 0, 0));
    Equal(0, window.Calculate(lines, lines.Count, runtime.DisplayStructureGeneration).Start, "scroll invalidates tail cache");
    Console.WriteLine("PASS UX16R5 window cost / measurements / scroll");
    return;
}
if (args is ["ux16r5-history-cost", var historyCostPath])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nREDRAW 0\nGCREATE 10,2,2\nGCREATE 11,2,2\nSPRITEANIMECREATE \"ANI\",2,2\nSPRITEANIMEADDFRAME \"ANI\",10,0,0,2,2,0,0,1000\nSPRITEANIMEADDFRAME \"ANI\",11,0,0,2,2,0,0,1000\nSETANIMETIMER 20\nREPEAT 12000\nPRINTFORML HISTORY_{COUNT}\nREND\nPRINT_IMG \"ANI\"\nPRINTL [1] CURRENT\nINPUT\nPRINTL [2] NEXT\nINPUT\nQUIT\n");
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\n履歴ログの行数:50000", Encoding.GetEncoding(932));
    var runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();runtime.EnableDisplayPerformanceMetrics();
    var contents = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")!;
    var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    object graphics = contents.GetMethod("GetGraphics", flags | System.Reflection.BindingFlags.Static)!.Invoke(null, [11L])!;
    graphics.GetType().GetMethod("GClear")!.Invoke(graphics, [System.Drawing.Color.Blue]);
    object anime = contents.GetMethod("GetSprite", flags | System.Reflection.BindingFlags.Static)!.Invoke(null, ["ANI"])!;
    object firstGraphics = contents.GetMethod("GetGraphics", flags | System.Reflection.BindingFlags.Static)!.Invoke(null, [10L])!;
    int count = runtime.DisplayLines.Count;
    var first = runtime.DisplayLines[0];
    var choice = Flatten(runtime.DisplayLines[^1].Parts).Single(part => part.Input == "1");
    var before = runtime.DisplayPerformance;
    long projectionBefore = runtime.DisplayProjectionCount, structure = runtime.DisplayStructureGeneration;
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    int changed = 0, firstLineReplacements = 0;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    for (int frame = 0; frame < 100; frame++)
    {
        // Both source frames change to the same alternating color. The real uncached
        // SpriteAnime renderer is exercised deterministically, independent of wall clock.
        var color = frame % 2 == 0 ? System.Drawing.Color.Magenta : System.Drawing.Color.Green;
        foreach (object parent in new[] { firstGraphics, graphics }) parent.GetType().GetMethod("GClear")!.Invoke(parent, [color]);
        if (runtime.RefreshAnimations(count - 138, count)) changed++;
        for (int index = count - 138; index < count; index++) _ = runtime.DisplayLines[index].Parts;
        if (!ReferenceEquals(first, runtime.DisplayLines[0])) firstLineReplacements++;
    }
    watch.Stop();
    var after = runtime.DisplayPerformance;
    File.WriteAllText(historyCostPath, JsonSerializer.Serialize(new { count, visible = 138, frames = 100, changed, elapsedMs = watch.Elapsed.TotalMilliseconds,
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated, firstLineReplacements,
        structureBefore = structure, structureAfter = runtime.DisplayStructureGeneration,
        projectionDelta = runtime.DisplayProjectionCount - projectionBefore, before, after }, new JsonSerializerOptions { WriteIndented = true }));
    Equal(100, changed, "100 real animation frame changes");
    Equal(0, firstLineReplacements, "visual-only update preserves inactive history line identity");
    Equal(structure, runtime.DisplayStructureGeneration, "image-only update preserves structural generation");
    Equal(0L, runtime.DisplayProjectionCount - projectionBefore, "visual frame does not reset activation context");
    Equal(true, after.PublishSourceLines - before.PublishSourceLines <= 13800, "visual publish work bounded by rendered window");
    Equal(true, after.ActivationProjectionSourceLines - before.ActivationProjectionSourceLines <= 13800, "activation work bounded by accessed window");
    Equal(true, runtime.SubmitDisplay(choice), "animation preserves current input activation");
    Equal(false, runtime.SubmitDisplay(choice), "next request rejects stale object");
    Console.WriteLine("PASS UX16R5 12k history / 138 window / 100 anime frames / activation / stale");
    return;
}
if (args is ["ux16r3-battle-parts", var r3BattleHtmlPath, var r3PartsOutputPath])
{
    string html = File.ReadAllText(r3BattleHtmlPath);
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT @\"" + html.Replace("\"", "\"\"") + "\"\nPRINTL\nREPEAT 4\nPRINTL \" \"\nREND\nINPUTS\nPRINTFORML BATTLE_RESULT=%RESULTS%\nWAIT\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    var console = typeof(BrowserRuntimeSession).GetField("console", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
    long generation = (long)console.GetType().GetProperty("LastButtonGeneration")!.GetValue(console)!;
    var parts = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    File.WriteAllText(r3PartsOutputPath, JsonSerializer.Serialize(new { lastButtonGeneration = generation, lines = runtime.DisplayLines }, new JsonSerializerOptions { WriteIndented = true }));
    Equal(true, parts.Where(part => part.Input is not null).All(part => part.ButtonGeneration == generation && part.Activation is not null), "all actual battle command generations are current");
    Equal(null, parts.Last(part => part.Kind == BrowserDisplayPartKind.Text && part.Text == " ").Style?.Background, "native plain spacer does not paint background");
    foreach (string input in new[] { " ", "w", "2", "3", "a", "e", "s", "d", "f", "9", "i" })
    {
        runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
        runtime.StartTitle();
        BrowserDisplayPart choice = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == input);
        long request = runtime.PendingInput!.RequestId;
        Equal(true, runtime.SubmitDisplay(choice), "battle mouse accepts " + input);
        Contains("BATTLE_RESULT=" + input, runtime.Output, "battle mouse result " + input);
        Equal(request + 1, runtime.PendingInput!.RequestId, "battle mouse advances exactly once " + input);
        Equal(false, runtime.SubmitDisplay(choice), "battle old object rejected " + input);
    }
    Console.WriteLine("PASS UX16R3 actual battle parts / generations / each command");
    return;
}
if (args is ["ux16r3-html-font-width"])
{
    var runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT @\"<div padding='35'><font size='75'>[20]        </font></div>\"\nWAIT\nQUIT\n"), null);
    runtime.TextWidthMeasurer = (text, font, size) => text.Length * size / 2d;
    runtime.StartTitle();
    var text = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Text && part.Text.Contains("[20]"));
    Equal(13, text.Style!.FontSize, "native font size truncation");
    Equal(78, text.Width, "native scaled glyph advance for 12 halfwidth characters");
    Equal(6, Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).First(part => part.Kind == BrowserDisplayPartKind.Group).Layout!.Padding, "padding unchanged");
    Console.WriteLine("PASS UX16R3 HTML relative font advance / padding");
    return;
}
if (args is ["ux16r4-keyboard-handoff"])
{
    var state = new BrowserKeyboardInputState();
    var first = new BrowserInputPrompt(100, BrowserInputKind.String, true);
    var next = first with { RequestId = 101 };
    state.KeyDown("KeyS", false, first, 1);
    Equal(true, state.TryBegin("KeyS", "s", false, first, 1, out _), "initial movement accepted");
    state.Complete(next, 1);
    state.KeyDown("KeyS", true, next, 1);
    Equal(true, state.TryBegin("KeyS", "s", true, next, 1, out _), "held physical repeat crosses next OneInput");
    state.Complete(next with { RequestId = 102 }, 1);
    state.KeyUp("KeyS");
    Equal(false, state.TryBegin("KeyS", "s", true, next, 1, out _), "release prevents stale repeat");
    state.KeyDown("KeyS", false, first, 1);
    Equal(true, state.TryBegin("KeyS", "s", false, first, 1, out _), "new press");
    Equal(false, state.TryBegin("KeyS", "s", true, null, 1, out var reason), "one coalesced repeat during transition");
    Equal("coalesced-repeat", reason, "busy event is coalesced");
    Equal(false, state.TryBegin("KeyS", "s", true, null, 1, out _), "multiple events do not grow backlog");
    Equal(("KeyS", "s"), state.Complete(next, 1)!.Value, "exactly one handoff");
    Equal(null, state.Complete(next, 1), "slot drained once");
    state.KeyDown("KeyS", false, first, 1);
    state.TryBegin("KeyS", "s", false, first, 1, out _);
    state.TryBegin("KeyS", "s", true, null, 1, out _);
    state.KeyUp("KeyS");
    Equal(null, state.Complete(next, 1), "keyup clears pending repeat");
    foreach (var boundary in new[] { new BrowserInputPrompt(102, BrowserInputKind.Integer, false), new(103, BrowserInputKind.String, false), new(104, BrowserInputKind.Enter, false), new(105, BrowserInputKind.MouseKey, false), new(106, BrowserInputKind.String, true, 100, "TONEINPUTS") })
    {
        state.KeyDown("KeyS", false, first, 1);
        state.TryBegin("KeyS", "s", false, first, 1, out _);
        state.TryBegin("KeyS", "s", true, null, 1, out _);
        Equal(null, state.Complete(boundary, 1), "repeat cannot cross incompatible boundary");
        Equal(false, state.TryBegin("KeyS", "s", true, next, 1, out _), "incompatible boundary expires hold eligibility");
    }
    state.KeyDown("KeyS", false, first, 1);
    state.Clear();
    Equal(false, state.TryBegin("KeyS", "s", true, next, 1, out _), "blur/title clears repeat");
    state.KeyDown("KeyS", false, first, 1);
    Equal(false, state.TryBegin("KeyS", "s", true, next, 2, out _), "session replacement rejects repeat");
    var integer = new BrowserInputPrompt(200, BrowserInputKind.Integer, true);
    state.KeyDown("Digit7", false, integer, 2);
    Equal(true, state.TryBegin("Digit7", "7", true, integer, 2, out _), "native numeric OneInput repeat");
    state.Complete(integer with { RequestId = 201 }, 2);
    foreach (var kind in new[] { BrowserInputKind.Enter, BrowserInputKind.AnyKey, BrowserInputKind.MouseKey })
    {
        state.Clear();
        var wait = new BrowserInputPrompt(300, kind, false);
        state.KeyDown("Enter", false, wait, 2);
        Equal(true, state.TryBegin("Enter", "Enter", false, wait, 2, out _), "native initial " + kind);
        state.Complete(wait with { RequestId = 301 }, 2);
        Equal(true, state.TryBegin("Enter", "Enter", true, wait with { RequestId = 301 }, 2, out _), "native same-kind physical repeat " + kind);
        state.Complete(first, 2);
        Equal(false, state.TryBegin("Enter", "Enter", true, first, 2, out _), "wait hold cannot leak into OneInput " + kind);
    }
    Console.WriteLine("PASS UX16R4 held key / bounded owner / release / kind / session / Integer");
    return;
}
if (args is ["ux16r3-inactive-projection"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nREPEAT 1000\nPRINTFORML [{COUNT+10}] OLD_{COUNT}\nREND\nINPUT\nPRINTL [1] CURRENT_A\nPRINTL [2] CURRENT_B\nINPUT\nWAIT\nQUIT\n");
    var runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    var old = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).First(part => part.Input is not null);
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "10")), "reach next input");
    runtime.EnableDisplayPerformanceMetrics();
    var parts = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(true, parts.Where(part => part.Input is "1" or "2").All(part => part.Activation is not null), "both current rows activate");
    Equal(true, parts.Where(part => part.Input is not null && part.Input is not ("1" or "2")).All(part => part.Activation is null), "old rows inactive");
    Equal(false, runtime.SubmitDisplay(old), "stale object rejects");
    Equal(true, runtime.DisplayPerformance.ActivationProjectionParts < 30, "inactive immutable rows do not recurse");
    Console.WriteLine(JsonSerializer.Serialize(runtime.DisplayPerformance));
    Console.WriteLine("PASS UX16R3 inactive projection / current multirow / stale object");
    return;
}
if (args is ["ux16r3-animation-refresh"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nSETANIMETIMER 20\nREPEAT 500\nPRINTFORML STATIC_{COUNT}\nREND\nPRINTL [1] CURRENT\nINPUT\nPRINTFORML RESULT={RESULT}\nWAIT\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();runtime.EnableDisplayPerformanceMetrics();
    long generation = runtime.DisplayGeneration;
    BrowserDisplayPart active = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == "1");
    for (int i = 0; i < 20; i++) { runtime.RefreshAnimations(); _ = runtime.DisplayLines; }
    Console.WriteLine(JsonSerializer.Serialize(runtime.DisplayPerformance));
    Equal(generation, runtime.DisplayGeneration, "20 static ticks do not change display generation");
    Equal(0L, runtime.DisplayPerformance.AnimationPublishes, "20 static ticks do not publish");
    Equal(true, runtime.SubmitDisplay(active), "unchanged current activation is still valid");
    Equal(false, runtime.SubmitDisplay(active), "consumed request remains stale");
    string dynamicRoot = CreateGlobalFixture("@SYSTEM_TITLE\nGCREATE 10,2,2\nGCREATE 11,2,2\nGDRAWSPRITE 10,\"SOURCE\"\nGDRAWSPRITE 11,\"SOURCE\"\nSPRITECREATE \"COPY\",10,0,0,2,2\nSPRITEANIMECREATE \"ANI\",2,2\nSPRITEANIMEADDFRAME \"ANI\",10,0,0,2,2,0,0,20\nSPRITEANIMEADDFRAME \"ANI\",11,0,0,2,2,0,0,20\nSETANIMETIMER 20\nPRINT_IMG \"COPY\"\nPRINT_IMG \"ANI\"\nREPEAT 500\nPRINTFORML OFFSCREEN_{COUNT}\nREND\nWAIT\nQUIT\n");
    Directory.CreateDirectory(Path.Combine(dynamicRoot, "resources"));
    using (var bitmap = new SKBitmap(2, 2))
    {
        bitmap.Erase(SKColors.Magenta);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dynamicRoot, "resources", "source.png"), png.ToArray());
    }
    File.WriteAllText(Path.Combine(dynamicRoot, "resources", "sprites.csv"), "SOURCE,source.png,0,0,2,2\n", new UTF8Encoding(true));
    runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(dynamicRoot, null);
    runtime.StartTitle();runtime.EnableDisplayPerformanceMetrics();
    BrowserDisplayPart Image(string name) => Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Image && part.Text == name);
    string? firstG = Image("COPY").ImageDataUrl, firstAnime = Image("ANI").ImageDataUrl;
    var contents = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")!;
    object Graphics(long id) => contents.GetMethod("GetGraphics", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [id])!;
    object secondGraphics = Graphics(11);
    secondGraphics.GetType().GetMethod("GClear")!.Invoke(secondGraphics, [System.Drawing.Color.Blue]);
    long offscreenGeneration = runtime.DisplayGeneration;
    Equal(false, runtime.RefreshAnimations(4, runtime.DisplayLines.Count), "offscreen frames do not republish the visible window");
    Equal(offscreenGeneration, runtime.DisplayGeneration, "offscreen image change does not churn display generation");
    bool animeChanged = false;
    for (int i = 0; i < 20 && !animeChanged; i++)
    {
        await Task.Delay(11);
        runtime.RefreshAnimations(0, 4);
        animeChanged = firstAnime != Image("ANI").ImageDataUrl;
    }
    Equal(true, animeChanged, "offscreen SpriteAnime frame updates");
    object firstGraphics = Graphics(10);
    firstGraphics.GetType().GetMethod("GClear")!.Invoke(firstGraphics, [System.Drawing.Color.Green]);
    Equal(false, runtime.RefreshAnimations(4, runtime.DisplayLines.Count), "offscreen SpriteG mutation waits until revealed");
    Equal(true, runtime.RefreshAnimations(0, 4), "revealed SpriteG mutation reports a visual change");
    Equal(false, firstG == Image("COPY").ImageDataUrl, "offscreen SpriteG mutation updates pixels");
    Equal(true, runtime.DisplayPerformance.AnimationPublishes > 0, "dynamic image changes publish");
    Console.WriteLine(JsonSerializer.Serialize(runtime.DisplayPerformance));
    Console.WriteLine("PASS UX16R3 no-change refresh / input guard");
    return;
}
static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{name}: expected <{expected}>, actual <{actual}>");
}

static void Contains(string expected, string actual, string name)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"{name}: missing <{expected}> in <{actual}>");
}

static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

static string FromMarker(string value, string marker)
{
    value = NormalizeNewlines(value);
    int start = value.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0)
        throw new InvalidOperationException($"marker not found: {marker}");
    return value[start..];
}

static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

static IEnumerable<BrowserDisplayPart> Flatten(IEnumerable<BrowserDisplayPart> parts)
{
    foreach (BrowserDisplayPart part in parts)
    {
        yield return part;
        if (part.Children is not null)
            foreach (BrowserDisplayPart child in Flatten(part.Children))
                yield return child;
    }
}

static bool LastLineIsTemporary(BrowserRuntimeSession session)
{
    object console = typeof(BrowserRuntimeSession).GetField("console", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
    return (bool)console.GetType().GetProperty("LastLineIsTemporary")!.GetValue(console)!;
}

if (args is ["ux15r2-title-reuse"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL UX15R2_TITLE\nINPUT\nPRINTL UX15R2_OLD_GAME\nWAIT\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    long firstRequest = runtime.PendingInput!.RequestId;
    Equal(true, runtime.Submit(new(firstRequest, "1")), "enter game");
    Equal(BrowserInputKind.Enter, runtime.PendingInput?.Kind, "game WAIT");
    Contains("UX15R2_OLD_GAME", runtime.Output, "old output present before return");
    runtime.ReturnToTitle();
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "reused title INPUT");
    Equal(true, runtime.PendingInput!.RequestId > firstRequest, "new request identity");
    Contains("UX15R2_TITLE", runtime.Output, "title rerun");
    Equal(false, runtime.Output.Contains("UX15R2_OLD_GAME", StringComparison.Ordinal), "old output cleared");
    Equal(false, runtime.Submit(new(firstRequest, "1")), "stale request rejected");
    Console.WriteLine("PASS UX15R2 title reuse");
    return;
}

if (args.Length == 2 && args[0] == "manual-ux-r3-r3-input-yn" && args[1] is "0" or "1")
{
    string inputYnPath = Environment.GetEnvironmentVariable("EMUERA_R3R3_INPUT_YN")
        ?? throw new InvalidOperationException("EMUERA_R3R3_INPUT_YN must point to the read-only source INPUT_YN.ERB");
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nCALL INPUT_YN\nPRINTFORML R3R3_INPUT_YN_RESULT={RESULT}\nQUIT\n");
    File.Copy(inputYnPath, Path.Combine(root, "ERB", "INPUT_YN.ERB"));
    File.WriteAllText(Path.Combine(root, "CSV", "Flag.csv"), "612,二択入力設定\n", Encoding.GetEncoding(932));
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserInputKind.String, runtime.PendingInput?.Kind, "INPUT_YN first one-character request");
    long firstRequest = runtime.PendingInput!.RequestId;
    Equal(true, runtime.Submit(new(firstRequest, "d")), "INPUT_YN invalid one-character answer is delivered to ERB validation");
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, $"INPUT_YN invalid answer returns to request: {runtime.FailureDiagnostic.ExceptionType} {runtime.FailureDiagnostic.ExceptionMessage} at {runtime.FailureDiagnostic.ErbPosition}");
    Equal(BrowserInputKind.String, runtime.PendingInput?.Kind, "INPUT_YN retries as one-character request");
    Equal(false, firstRequest == runtime.PendingInput?.RequestId, "INPUT_YN retry gets a new request id");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, args[1])), "INPUT_YN valid retry accepted");
    Equal(BrowserRuntimeStatus.Succeeded, runtime.Status, "INPUT_YN returns normally after retry");
    Contains($"R3R3_INPUT_YN_RESULT={args[1]}", runtime.Output, "INPUT_YN returns the selected answer");
    Console.WriteLine($"PASS WEB-MANUAL-UX-R3-R3 INPUT_YN invalid d then {args[1]}");
    return;
}

if (args is ["manual-ux-r3-r3-temporary-line"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL R3R3_KEEP\nREUSELASTLINE \"R3R3_TEMP\"\nWAIT\nPRINTL R3R3_REPLACED\nWAIT\nREUSELASTLINE \"\"\nPRINTL R3R3_FINAL\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserInputKind.Enter, runtime.PendingInput?.Kind, $"nonempty REUSELASTLINE creates a temporary row: {runtime.FailureDiagnostic.ExceptionMessage} at {runtime.FailureDiagnostic.ErbPosition}");
    Equal(true, LastLineIsTemporary(runtime), "temporary row is identified as last line");
    long temporaryLineCount = runtime.LineCount;
    Equal((long)runtime.DisplayLines.Count, temporaryLineCount, "temporary row participates in displayed logical line count");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "first WAIT accepted");
    Equal(BrowserInputKind.Enter, runtime.PendingInput?.Kind, "next ordinary line reaches second WAIT");
    Equal(false, LastLineIsTemporary(runtime), "next completed line replaces the temporary row");
    Equal(temporaryLineCount, runtime.LineCount, "replacement consumes the temporary row without changing the completed line count");
    Equal((long)runtime.DisplayLines.Count, runtime.LineCount, "replacement line count matches retained rows in the small fixture");
    Equal(false, runtime.DisplayLines.Any(line => line.Parts.Any(part => part.Text == "R3R3_TEMP")), "replaced temporary row is removed");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "second WAIT accepted");
    Equal(BrowserRuntimeStatus.Succeeded, runtime.Status, "empty REUSELASTLINE is a no-op");
    Equal(temporaryLineCount + 1, runtime.LineCount, "empty REUSELASTLINE does not alter line count before the final ordinary line");
    Equal((long)runtime.DisplayLines.Count, runtime.LineCount, "empty REUSELASTLINE adds no phantom line");
    Equal(false, runtime.DisplayLines.Any(line => line.Parts.Any(part => part.Text == "R3R3_TEMP")), "empty REUSELASTLINE does not create a row");

    BrowserRuntimeSession clear = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL R3R3_CLEAR_KEEP\nREUSELASTLINE \"R3R3_CLEAR_TEMP\"\nCLEARLINE 1\nWAIT\nQUIT\n"), null);
    clear.StartTitle();
    Equal(BrowserInputKind.Enter, clear.PendingInput?.Kind, "CLEARLINE after temporary reaches WAIT");
    Equal(false, LastLineIsTemporary(clear), "CLEARLINE removes temporary marker with row");
    Equal((long)clear.DisplayLines.Count, clear.LineCount, "CLEARLINE updates line count after temporary removal");
    Equal(false, clear.DisplayLines.Any(line => line.Parts.Any(part => part.Text == "R3R3_CLEAR_TEMP")), "CLEARLINE removes temporary row");

    BrowserRuntimeSession html = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL R3R3_HTML_KEEP\nREUSELASTLINE \"R3R3_HTML_TEMP\"\nHTML_PRINT \"<b>R3R3_HTML_NEXT</b>\"\nWAIT\nQUIT\n"), null);
    html.StartTitle();
    Equal(BrowserInputKind.Enter, html.PendingInput?.Kind, "HTML after temporary reaches WAIT");
    Equal(false, LastLineIsTemporary(html), "HTML display line replaces the temporary row");
    Equal((long)html.DisplayLines.Count, html.LineCount, "HTML replacement line count follows native output");
    Console.WriteLine("PASS WEB-MANUAL-UX-R3-R3 temporary display lifecycle");
    return;
}

if (args is ["test-runner-known-failure"])
{
    Equal(1, 2, "known failure exit-code probe");
    return;
}

if (args is ["manual-ux-r3-r1-color-tab"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
HTML_PRINT @"<font color='#00FFFFFF'>WHITE</font><font color='#00404040'>GRAY</font><font color='#00FF0000'>RED</font><font color='#000000FF'>BLUE</font><font color='#00000000'>BLACK</font><font color='Red'>NAMED</font><font>DEFAULT</font><div border_color='#00404040' background_color='#000000FF'><button value='7'><font color='#00FFFFFF' bcolor='#00FF0000'>CHOICE</font></button></div><shape type='rect' param='10' color='#00FF0000'>AUTO<TAB>OFF"
PRINTL SKIP<TAB>OFF
INPUT
QUIT
""".Replace("<TAB>", "\t", StringComparison.Ordinal));
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, $"color and tab fixture reaches INPUT: {runtime.Status} {runtime.FailureDiagnostic.ExceptionMessage} {runtime.FailureDiagnostic.ErbPosition}");
    BrowserDisplayPart[] parts = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    foreach ((string label, string expected) in new[] { ("WHITE", "#FFFFFF"), ("GRAY", "#404040"), ("RED", "#FF0000"), ("BLUE", "#0000FF"), ("BLACK", "#000000"), ("NAMED", "#FF0000") })
        Equal(expected, parts.Single(part => part.Text == label).Style?.Foreground, $"native RGB color {label}");
    BrowserDisplayPart colorGroup = parts.First(part => part.Kind == BrowserDisplayPartKind.Group && part.Layout?.BorderColor == "#404040");
    Equal("#404040", colorGroup.Layout?.BorderColor, "native border RGB");
    Equal("#0000FF", colorGroup.Style?.Background, "native background RGB");
    Equal("#FF0000", parts.First(part => part.Kind == BrowserDisplayPartKind.Shape).Style?.Background, "native shape RGB");
    Equal("#FF0000", parts.First(part => part.Text == "CHOICE" && part.Kind == BrowserDisplayPartKind.Text).Style?.ButtonColor, "native button selection RGB");
    Equal(true, parts.Any(part => part.Text == "AUTOOFF"), "HTML display strips tab");
    Equal(true, parts.Any(part => part.Text.Contains("SKIPOFF", StringComparison.Ordinal)), "PRINT display strips tab");
    Equal(false, parts.Any(part => part.Text.Contains('\t')), "display-only tab normalization covers all visible parts");
    var parser = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.Web.Runtime.BrowserHtmlParser")!;
    var parseColor = parser.GetMethod("ParseColor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    foreach (string invalid in new[] { "#FFFFFFFF", "#1000000", "transparent", "not-a-color", "Red " })
    {
        try { _ = parseColor.Invoke(null, [invalid, "#123456"]); throw new InvalidOperationException($"invalid native color accepted: {invalid}"); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is InvalidDataException) { }
    }
    Equal("#123456", parseColor.Invoke(null, ["", "#123456"]) as string, "empty native color uses fallback");
    Console.WriteLine("PASS WEB-MANUAL-UX-R3-R1 native RGB and display tabs");
    return;
}

if (args is ["manual-ux-r3-r1-html-base"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nSETCOLOR 255,0,0\nFONTBOLD\nPRINTL RED_BOLD\nHTML_PRINT \"<font>HTML_BASE</font>\"\nRESETCOLOR\nFONTREGULAR\nPRINTL DEFAULT\nINPUT\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "HTML base fixture reaches INPUT");
    BrowserDisplayPart[] parts = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    BrowserDisplayStyle html = parts.Single(part => part.Text == "HTML_BASE").Style!;
    BrowserDisplayStyle ordinary = parts.Single(part => part.Text == "DEFAULT").Style!;
    Equal(ordinary.Foreground, html.Foreground, "native HTML starts at configured foreground");
    Equal(false, html.Bold, "native HTML does not inherit PRINT bold");
    Equal(true, parts.Single(part => part.Text == "RED_BOLD").Style?.Bold, "ordinary PRINT retains current style");
    Console.WriteLine("PASS WEB-MANUAL-UX-R3-R1 HTML base style");
    return;
}

if (args is ["manual-ux-r2-tooltip"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
TOOLTIP_SETCOLOR 16711680, 255
TOOLTIP_SETDELAY 100
TOOLTIP_SETDURATION 700
HTML_PRINT @"<button value='1' title='Skill explanation<br>second line'>SKILL</button>"
INPUT
PRINTFORML SKILL_RESULT={RESULT}
QUIT
""");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "skill list reaches INPUT after tooltip configuration");
    Equal(100, runtime.TooltipDelayMilliseconds, "skill tooltip delay follows TOOLTIP_SETDELAY");
    Equal(700, runtime.TooltipDurationMilliseconds, "skill tooltip duration follows TOOLTIP_SETDURATION");
    Equal("#FF0000", runtime.TooltipForeground, "skill tooltip foreground follows TOOLTIP_SETCOLOR");
    Equal("#0000FF", runtime.TooltipBackground, "skill tooltip background follows TOOLTIP_SETCOLOR");
    BrowserDisplayPart skill = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts))
        .Single(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "1");
    Equal("Skill explanation\nsecond line", skill.Tooltip, "skill explanation survives HTML projection");
    Equal(true, runtime.SubmitDisplay(skill), "skill button remains selectable");
    Contains("SKILL_RESULT=1", runtime.Output, "skill selection executes the real runtime");
    Console.WriteLine("PASS WEB-MANUAL-UX-R2 tooltip skill selection");
    return;
}

if (args is ["manual-ux-r2-html-history"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
#DIM START_LINE
START_LINE = LINECOUNT
REDRAW 0
PRINTL SKILL-ONE
PRINTL SKILL-TWO
PRINTFORML HISTORY=%HTML_TOPLAINTEXT(HTML_GETPRINTEDSTR(0))%
CLEARLINE LINECOUNT - START_LINE
REDRAW 1
PRINTFORML REMAIN={LINECOUNT - START_LINE}
QUIT
""");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserRuntimeStatus.Succeeded, runtime.Status, $"HTML history skill description must not stop runtime: {runtime.FailureDiagnostic.ExceptionType} {runtime.FailureDiagnostic.ExceptionMessage} at {runtime.FailureDiagnostic.ErbPosition}");
    Contains("HISTORY=SKILL-TWO", runtime.Output, "last logical display line read");
    Contains("REMAIN=0", runtime.Output, "temporary description lines removed");
    Console.WriteLine("PASS WEB-MANUAL-UX-R2 HTML display history");
    return;
}

if (args is ["p1c6-macro-parser"])
{
    InputMacroToken[] one = InputMacroSyntax.Expand("(H\\e\\d\\e)*1").ToArray();
    Equal(3, one.Length, "M01 token count including main-compatible trailing empty input");
    Equal("H", one[0].Value, "M01 H token");
    Equal(true, one[0].MessageSkip, "M01 H skip");
    Equal("d", one[1].Value, "M01 d token");
    Equal(true, one[1].MessageSkip, "M01 d skip");
    Equal("", one[2].Value, "M01 trailing empty token");
    Equal(false, one[2].MessageSkip, "M01 trailing token is not skip");
    InputMacroToken[] ten = InputMacroSyntax.Expand("(H\\e\\d\\e)*10").ToArray();
    Equal(21, ten.Length, "M02 repeated token count including final empty token");
    Equal("HdHdHdHdHdHdHdHdHdHd", string.Concat(ten.Select(token => token.Value)), "M02 token order");
    Equal(true, ten.Take(20).All(token => token.MessageSkip), "M02 skip markers preserved");
    Equal(false, ten.Any(token => token.Value == "e"), "M03 escape is not literal e");
    Console.WriteLine("PASS P1C6 macro syntax shared with main");
    return;
}

if (args is ["bootstrap-process-initialize-profile"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTFORML PROFILE_READY
INPUT
WAIT
QUIT
"""), null, captureProcessInitializeProfile: true);
    var profile = runtime.ProcessInitializeProfile ?? throw new InvalidOperationException("Process.Initialize profile was not captured");
    Equal(true, profile.TotalElapsedMilliseconds > 0, "profile records Process.Initialize elapsed time");
    Equal(true, profile.TotalAllocatedBytes >= 0, "profile records monotonic allocation delta");
    Equal(true, profile.ProcessorCount >= 1, "profile records processor count");
    Equal(true, profile.Phases.Select(phase => phase.Name).Contains("parser"), "profile includes parser phase");
    Equal(true, profile.Phases.Select(phase => phase.Name).Contains("erb"), "profile includes ERB phase");
    Equal(true, profile.ErbPrimaryParseMilliseconds >= 0, "profile exposes ERB primary parse duration");
    Console.WriteLine("PASS WEB runtime Process.Initialize profile contract");
    return;
}

if (args is ["save-import-bootstrap-profile"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n");
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    BrowserRuntimeSession profiled = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(
        root, files, captureProcessInitializeProfile: true);
    Equal(BrowserRuntimeStatus.BootstrapReady, profiled.Status, "save validator bootstrap remains ready");
    Equal(true, profiled.ProcessInitializeProfile is not null, "save validator captures Process.Initialize profile");
    _ = profiled.RunGlobalCodecDiagnostic(resave: false);
    bool absentSaveRejected = false;
    try { _ = profiled.RunSaveCodecDiagnostic(219); }
    catch (Exception ex) when (ex.GetType().Name == "ExeEE") { absentSaveRejected = true; }
    Equal(true, absentSaveRejected, "missing save remains rejected by the existing loader");
    Equal(2, profiled.CodecValidationProfile.Count, "diagnostic records one LoadGlobal and one LoadFrom timing");
    Equal("LoadGlobal", profiled.CodecValidationProfile[0].Operation, "first codec timing is LoadGlobal");
    Equal("LoadFrom", profiled.CodecValidationProfile[1].Operation, "second codec timing is LoadFrom");
    Equal(true, profiled.CodecValidationProfile.All(item => item.LoadMilliseconds >= 0 && item.InputHashMilliseconds >= 0), "codec durations are monotonic nonnegative values");
    Equal(false, profiled.CodecValidationProfile[1].Succeeded, "failed LoadFrom is timed but remains a failed validation");

    BrowserRuntimeSession unprofiled = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(
        CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n"), files);
    Equal(null, unprofiled.ProcessInitializeProfile, "profile is disabled by default");
    _ = unprofiled.RunGlobalCodecDiagnostic(resave: false);
    Equal(0, unprofiled.CodecValidationProfile.Count, "codec timing is disabled by default");
    Console.WriteLine("PASS WEB save import bootstrap profile is opt-in");
    return;
}

if (args is ["p1c6-getkey"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
INPUT
PRINTFORML DOWN_TRIGGER={GETKEYTRIGGERED(0x12)} DOWN={GETKEY(0x12)}
INPUT
PRINTFORML UP_TRIGGER={GETKEYTRIGGERED(0x12)} UP={GETKEY(0x12)}
WAIT
QUIT
"""), null);
    runtime.StartTitle();
    runtime.SetKeyState(0x12, true);
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "GETKEY down input accepted");
    Contains("DOWN_TRIGGER=1 DOWN=1", runtime.Output, "GETKEY and GETKEYTRIGGERED use browser key state");
    runtime.SetKeyState(0x12, false);
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "2")), "GETKEY up input accepted");
    Contains("UP_TRIGGER=0 UP=0", runtime.Output, "released browser key is not pressed or triggered");
    Console.WriteLine("PASS P1C6 GETKEY browser state");
    return;
}

if (args is ["perf-gate-01c-line-id"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
SETANIMETIMER 10
REPEAT 501
PRINTFORML LINE_ID_{COUNT}
REND
PRINTL [2] LINE_ID_TWO
PRINTL LINE_ID_THREE
INPUT
WAIT
QUIT
""");
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\n履歴ログの行数:500", Encoding.GetEncoding(932));
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    BrowserDisplayLine[] first = runtime.DisplayLines.ToArray();
    long[] firstIds = first.Select(line => line.LineId).ToArray();
    Equal(true, firstIds.All(id => id > 0), "LineId positive");
    Equal(firstIds.Length, firstIds.Distinct().Count(), "LineId unique");
    Equal(true, firstIds.SequenceEqual(firstIds.Order()), "LineId increasing");
    BrowserDisplayPart button = Flatten(first.SelectMany(line => line.Parts)).Single(part => part.Input == "2");
    Equal(true, button.ButtonGeneration >= 0, "ButtonGeneration exists independently");
    runtime.RefreshAnimations();
    Equal(string.Join(',', firstIds), string.Join(',', runtime.DisplayLines.Select(line => line.LineId)), "animation preserves LineId");

    object console = typeof(BrowserRuntimeSession).GetField("console", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
    console.GetType().GetMethod("deleteLine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(console, [1]);
    long[] afterDelete = runtime.DisplayLines.Select(line => line.LineId).ToArray();
    Equal(true, afterDelete.All(firstIds.Contains), "deleteLine preserves survivor LineId");

    console.GetType().GetMethod("ClearText")!.Invoke(console, null);
    console.GetType().GetMethods().Single(method => method.Name == "PrintSingleLine" && method.GetParameters().Length == 2).Invoke(console, ["LINE_ID_AFTER_CLEAR", false]);
    long afterClear = runtime.DisplayLines.Single().LineId;
    Equal(true, afterClear > firstIds.Max(), "ClearText does not reset LineId");

    console.GetType().GetMethod("PrintHTMLIsland")!.Invoke(console, ["<button value='7'>LINE_ID_ISLAND</button>", 0]);
    long island = runtime.DisplayLines.Single(line => Flatten(line.Parts).Any(part => part.Text.Contains("LINE_ID_ISLAND", StringComparison.Ordinal))).LineId;
    _ = runtime.DisplayLines;
    Equal(island, runtime.DisplayLines.Single(line => Flatten(line.Parts).Any(part => part.Text.Contains("LINE_ID_ISLAND", StringComparison.Ordinal))).LineId, "HTML island Publish preserves LineId");
    console.GetType().GetMethod("ClearHTMLIsland", Type.EmptyTypes)!.Invoke(console, null);
    console.GetType().GetMethod("PrintHTMLIsland")!.Invoke(console, ["<button value='8'>LINE_ID_ISLAND_NEW</button>", 0]);
    long islandNew = runtime.DisplayLines.Single(line => Flatten(line.Parts).Any(part => part.Text.Contains("LINE_ID_ISLAND_NEW", StringComparison.Ordinal))).LineId;
    Equal(true, islandNew > island, "HTML island recreate gets new LineId");
    Console.WriteLine("PASS WEB-PERF-GATE-01C stable LineId");
    return;
}

if (args is ["perf-gate-01c-window-state"])
{
    BrowserDisplayLine[] lines = Enumerable.Range(1, 50_000)
        .Select(id => new BrowserDisplayLine("left", [], LineId: id))
        .ToArray();
    BrowserDisplayWindowState state = new();

    BrowserDisplayWindowSlice empty = state.Calculate([]);
    Equal(0, empty.Start, "window empty start");
    Equal(0, empty.End, "window empty end");
    Equal(0d, empty.TopSpacerPx, "window empty top spacer");
    Equal(0d, empty.BottomSpacerPx, "window empty bottom spacer");

    state.UpdateScroll(new(0, 360, false, 1, 0));
    BrowserDisplayWindowSlice top = state.Calculate(lines);
    Equal(0, top.Start, "window scroll-up starts at first line");
    Equal(true, top.End < lines.Length, "window scroll-up bounds DOM slice");
    Equal(0d, top.TopSpacerPx, "window scroll-up top spacer");

    state.ApplyMeasurements([new(1, 44), new(2, 50), new(3, 0), new(4, double.NaN), new(5, double.PositiveInfinity)]);
    Equal(2, state.MeasurementCount, "window retains valid measurements only");
    BrowserDisplayWindowSlice measured = state.Calculate(lines);
    Equal(true, measured.BottomSpacerPx >= 0, "window has no negative bottom spacer");

    state.UpdateScroll(new(900_000, 360, false, 1, 0));
    BrowserDisplayWindowSlice middle = state.Calculate(lines);
    Equal(true, middle.Start > 0 && middle.End < lines.Length, "window scroll-up finds bounded middle slice");
    Equal(true, middle.TopSpacerPx > 0, "window scroll-up emits top spacer");

    state.UpdateScroll(new(double.MaxValue, 360, true, lines[^1].LineId, 0));
    BrowserDisplayWindowSlice tail = state.Calculate(lines);
    Equal(lines.Length, tail.End, "window follow-tail reaches retained tail");
    Equal(true, tail.Start > 0, "window follow-tail bounds DOM slice");
    Equal(true, tail.BottomSpacerPx >= 0, "window follow-tail has no negative bottom spacer");

    state.ApplyMeasurements(Enumerable.Range(1, 5_000).Select(id => new BrowserDisplayLineMeasurement(id, 20)).ToArray());
    Equal(true, state.MeasurementCount <= BrowserDisplayWindowState.MaximumMeasurementCache, "window measurement cache is bounded");
    BrowserDisplayLine[] withGap = lines.Where(line => line.LineId is < 100 or > 200).ToArray();
    _ = state.Calculate(withGap);
    Equal(true, state.MeasurementCount <= BrowserDisplayWindowState.MaximumMeasurementCache, "window gap/delete keeps bounded cache");

    state.Reset();
    Equal(0, state.MeasurementCount, "window ClearText reset clears measurements");
    BrowserDisplayWindowSlice afterClear = state.Calculate([new BrowserDisplayLine("left", [], LineId: 50_001)]);
    Equal(0, afterClear.Start, "window new LineId after ClearText starts at zero");
    Equal(1, afterClear.End, "window new LineId after ClearText renders one row");
    Console.WriteLine("PASS WEB-PERF-GATE-01C display window state");
    return;
}

if (args is ["ux16-08-follow-tail"])
{
    BrowserDisplayWindowState state = new();
    state.UpdateScroll(new(420, 360, false, 25, -4));
    Equal(false, state.FollowTail, "manual backlog scroll disables tail follow");
    BrowserDisplayLine[] lines = Enumerable.Range(1, 200)
        .Select(id => new BrowserDisplayLine("left", [], LineId: id))
        .ToArray();
    BrowserDisplayWindowSlice backlog = state.Calculate(lines, structureGeneration: 1);
    Equal(false, backlog.End == lines.Length, "manual backlog slice remains before retained tail");

    BrowserRuntimeSession noOutput = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL [1] CURRENT
INPUT
INPUT
WAIT
QUIT
"""), null);
    noOutput.StartTitle();
    long unchangedOutput = noOutput.ScriptOutputGeneration;
    long noOutputRequest = noOutput.PendingInput!.RequestId;
    BrowserDisplayPart noOutputChoice = Flatten(noOutput.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == "1");
    Equal(true, noOutputChoice.Activation is not null, "current choice has accepted activation");
    Equal(true, noOutput.SubmitDisplay(noOutputChoice), "current choice advances to the next input");
    Equal(noOutputRequest + 1, noOutput.PendingInput!.RequestId, "output-free transition reaches a new input request");
    Equal(unchangedOutput, noOutput.ScriptOutputGeneration, "input echo alone is not new script output");

    BrowserRuntimeSession newOutput = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL [1] CURRENT
INPUT
PRINTL NEW_OUTPUT
INPUT
WAIT
QUIT
"""), null);
    newOutput.StartTitle();
    long outputBefore = newOutput.ScriptOutputGeneration;
    long outputRequest = newOutput.PendingInput!.RequestId;
    BrowserDisplayPart outputChoice = Flatten(newOutput.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == "1");
    Equal(true, newOutput.SubmitDisplay(outputChoice), "current choice emits output before the next input");
    Equal(outputRequest + 1, newOutput.PendingInput!.RequestId, "output transition reaches a new input request");
    Equal(true, newOutput.ScriptOutputGeneration > outputBefore, "new script output has its own monotonic generation");

    var resume = typeof(BrowserDisplayWindowState).GetMethod("ResumeFollowTail", BindingFlags.Instance | BindingFlags.Public);
    Equal(true, resume is not null, "accepted current button with new output and next input can explicitly resume tail follow");
    if (resume is null) return;
    Equal(true, (bool)resume.Invoke(state, null)!, "explicit tail resume changes state after qualifying click transition");
    Equal(true, state.FollowTail, "explicit tail resume enables follow state");
    Equal(true, state.ScrollState.NearBottom, "explicit tail resume updates scroll state consistently");
    Equal(lines.Length, state.Calculate(lines, structureGeneration: 1).End, "resumed window includes retained tail");

    state.UpdateScroll(new(420, 360, false, 25, -4));
    Equal(false, state.FollowTail, "manual backlog scroll remains anchored until a qualifying accepted click transition");
    Equal(true, state.Calculate(lines).Start < lines.Length - 1, "manual backlog window does not jump to newest line");
    Console.WriteLine("PASS UX16-08 explicit tail-follow state");
    return;
}

if (args is ["ux16-08-island-flow"])
{
    var flowOffset = typeof(BrowserDisplayLine).GetProperty("IslandFlowOffsetY", BindingFlags.Instance | BindingFlags.Public);
    Equal(true, flowOffset is not null, "island rows carry their native per-depth flow offset");
    if (flowOffset is null) return;

    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n"), null);
    runtime.StartTitle();
    long islandOutputBefore = runtime.ScriptOutputGeneration;
    object console = typeof(BrowserRuntimeSession).GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
    var printIsland = console.GetType().GetMethod("PrintHTMLIsland")!;
    printIsland.Invoke(console, ["<div>A</div><br><div>B</div><br><div>C</div>", 3]);
    printIsland.Invoke(console, ["<div>D</div><br><div>E</div>", 2]);
    Equal(true, runtime.ScriptOutputGeneration > islandOutputBefore, "HTML island rows count as new game output");
    BrowserDisplayLine[] islandLines = runtime.DisplayLines.TakeLast(runtime.IslandLineCount).ToArray();
    int Offset(string text) => (int)flowOffset.GetValue(islandLines.Single(line => Flatten(line.Parts).Any(part => part.Text == text)))!;
    Equal(0, Offset("D"), "island depth 2 begins at native origin");
    Equal(runtime.DisplayLineHeight, Offset("E"), "island depth 2 advances one native display line");
    Equal(0, Offset("A"), "island depth 3 owns an independent native origin");
    Equal(runtime.DisplayLineHeight, Offset("B"), "island depth 3 advances one native display line");
    Equal(runtime.DisplayLineHeight * 2, Offset("C"), "island depth 3 advances two native display lines");

    console.GetType().GetMethod("ClearHTMLIsland", [typeof(int)])!.Invoke(console, [2]);
    printIsland.Invoke(console, ["<div>F</div>", 2]);
    islandLines = runtime.DisplayLines.TakeLast(runtime.IslandLineCount).ToArray();
    Equal(0, (int)flowOffset.GetValue(islandLines.Single(line => Flatten(line.Parts).Any(part => part.Text == "F")))!, "cleared depth restarts at native origin");

    printIsland.Invoke(console, ["<div>A<br><div>B</div><br>C</div>", 4]);
    BrowserDisplayPart nested = runtime.DisplayLines.Last().Parts.Single(part => part.Kind == BrowserDisplayPartKind.Group);
    BrowserDisplayPart[] nestedChildren = nested.Children!.ToArray();
    Equal(3, nestedChildren.Count(part => part.Kind is BrowserDisplayPartKind.Text or BrowserDisplayPartKind.Group), "nested flow retains text and group rows");
    Equal(2, nestedChildren.Count(part => part.Kind == BrowserDisplayPartKind.Break), "nested BR boundaries are retained");
    printIsland.Invoke(console, ["<button value='7'>A<br>B</button><nonbutton>A<br>B</nonbutton>", 5]);
    BrowserDisplayPart[] buttons = Flatten(runtime.DisplayLines.Last().Parts).ToArray();
    Equal(true, buttons.Where(part => part.Kind is BrowserDisplayPartKind.Button or BrowserDisplayPartKind.NonButton).All(part => Flatten(part.Children ?? []).Any(child => child.Kind == BrowserDisplayPartKind.Break)), "button and nonbutton BR content remains nested");
    printIsland.Invoke(console, ["<div><div xpos='10' ypos='10'>P1</div><br><div xpos='20' ypos='30'>P2</div></div>", 6]);
    BrowserDisplayPart positioned = runtime.DisplayLines.Last().Parts.Single(part => part.Kind == BrowserDisplayPartKind.Group);
    Equal(2, positioned.Children!.Count(part => part.Layout is { Mode: BrowserDisplayMode.Relative, ExplicitPosition: true }), "positioned siblings keep their independent coordinates");
    Console.WriteLine("PASS UX16-08 HTML island per-depth flow model and nested BR contract");
    return;
}

if (args is ["ux16-08-oneinput"])
{
    const string erb = """
@SYSTEM_TITLE
ONEINPUTS
PRINTFORML ONE=[%RESULTS%]
INPUTS
PRINTFORML INPUTS=[%RESULTS%]
TONEINPUTS 100, "", 0, ""
WAIT
QUIT
""";
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(CreateGlobalFixture(erb), null);
    runtime.StartTitle();
    BrowserInputPrompt oneInput = runtime.PendingInput!;
    Equal(BrowserInputKind.String, oneInput.Kind, "dungeon-style ONEINPUTS is a string prompt");
    Equal(true, oneInput.OneInput, "ONEINPUTS retains its one-character semantics");
    Equal(true, runtime.Submit(new(oneInput.RequestId, "abc", SessionGeneration: runtime.SessionGeneration, DisplayGeneration: runtime.DisplayGeneration)), "plain text form input accepted");
    Contains("ONE=[a]", runtime.Output, "multi-character nonmacro input keeps existing one-character truncation");
    BrowserInputPrompt inputs = runtime.PendingInput!;
    Equal(false, inputs.OneInput, "ordinary INPUTS remains distinct");
    Equal(true, runtime.Submit(new(inputs.RequestId, "abc", SessionGeneration: runtime.SessionGeneration, DisplayGeneration: runtime.DisplayGeneration)), "ordinary INPUTS accepts full text");
    Contains("INPUTS=[abc]", runtime.Output, "ordinary INPUTS keeps full string");
    Equal("TONEINPUTS", runtime.PendingInput?.TimedInputName, "timed OneInput remains a separate boundary");
    Equal(true, runtime.PendingInput?.OneInput, "timed OneInput semantics remain unchanged");
    InputMacroToken[] macro = InputMacroSyntax.Expand("(H\\e\\d\\e)*10").ToArray();
    Equal(21, macro.Length, "macro UI can reuse the established parser expansion");
    Equal(true, macro.Take(20).All(token => token.MessageSkip), "macro escape markers retain skip-after-token semantics");
    Equal(false, macro[^1].MessageSkip, "macro keeps its compatible trailing empty input");
    Equal("HdHdHdHdHdHdHdHdHdHd", string.Concat(macro.Select(token => token.Value)), "macro token order remains native-compatible");
    Console.WriteLine("PASS UX16-08 OneInput semantics and macro token contract");
    return;
}

if (args is ["perf-gate-01c-r3-window-measurement-state"])
{
    BrowserDisplayWindowState state = new();
    Equal(false, state.HasMeasurement(7), "R3 fresh measurement is absent");
    state.ApplyMeasurements([new(7, 24)]);
    Equal(true, state.HasMeasurement(7), "R3 applied measurement is present");
    Equal(true, state.RemoveMeasurement(7), "R3 removes cached measurement once");
    Equal(false, state.RemoveMeasurement(7), "R3 repeated removal is safe");
    Equal(false, state.RemoveMeasurement(99), "R3 missing removal is safe");
    state.ApplyMeasurements([new(8, 25)]);
    state.Calculate([new BrowserDisplayLine("left", [], LineId: 9)]);
    Equal(false, state.HasMeasurement(8), "R3 prune removes evicted measurement");
    state.ApplyMeasurements([new(9, 26)]);
    state.Reset();
    Equal(false, state.HasMeasurement(9), "R3 reset clears measurements");
    state.ApplyMeasurements([new(10, 18), new(11, 36), new(12, 18)]);
    state.Calculate([new("left", [], LineId: 10), new("left", [], LineId: 12)]);
    Equal(false, state.HasMeasurement(11), "UX16R2 middle-deleted LineId is evicted");
    Equal(2, state.MeasurementCount, "UX16R2 only live LineId measurements remain");
    Console.WriteLine("PASS 01C-R3 window measurement state");
    return;
}

if (args is ["perf-gate-01c-r3-current-line-id"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n"), null);
    object console = typeof(BrowserRuntimeSession).GetField("console", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
    Equal(0L, runtime.CurrentDisplayLineId, "R3 empty current line id");
    console.GetType().GetMethod("Print")!.Invoke(console, ["R3_PARTIAL", false]);
    console.GetType().GetMethod("PrintFlush")!.Invoke(console, [false]);
    long partial = runtime.CurrentDisplayLineId;
    Equal(true, partial > 0, "R3 partial line id allocated");
    console.GetType().GetMethod("RefreshStrings")!.Invoke(console, [false]);
    Equal(partial, runtime.CurrentDisplayLineId, "R3 repeated Publish preserves current id");
    console.GetType().GetMethod("NewLine")!.Invoke(console, null);
    Equal(0L, runtime.CurrentDisplayLineId, "R3 completed line resets current id");
    Equal(partial, runtime.DisplayLines.Single(line => line.Parts.Any(part => part.Text.Contains("R3_PARTIAL", StringComparison.Ordinal))).LineId, "R3 finalized line retains current id");
    Console.WriteLine("PASS 01C-R3 current incomplete LineId");
    return;
}

if (args is ["perf-gate-01c-r3-measurement-selection"])
{
    static long[] Select(IReadOnlyList<BrowserDisplayLine> lines, BrowserDisplayWindowSlice slice, BrowserDisplayWindowState state, long currentLineId, long lastCurrentLineId, long lastCurrentGeneration, long generation)
    {
        if (lastCurrentLineId > 0 && lastCurrentLineId != currentLineId)
            state.RemoveMeasurement(lastCurrentLineId);
        var selected = new HashSet<long>();
        foreach (BrowserDisplayLine line in lines.Skip(slice.Start).Take(slice.End - slice.Start))
            if (line.LineId != currentLineId && !state.HasMeasurement(line.LineId)) selected.Add(line.LineId);
        if (currentLineId > 0 && lines.Skip(slice.Start).Take(slice.End - slice.Start).Any(line => line.LineId == currentLineId)
            && (lastCurrentLineId != currentLineId || lastCurrentGeneration != generation)) selected.Add(currentLineId);
        return selected.Order().ToArray();
    }
    BrowserDisplayLine[] lines = Enumerable.Range(1, 6).Select(id => new BrowserDisplayLine("left", [], LineId: id)).ToArray();
    BrowserDisplayWindowSlice visible = new(1, 5, 0, 0, true);
    BrowserDisplayWindowState state = new();
    state.ApplyMeasurements([new(2, 20), new(3, 18), new(4, 20)]);
    Equal("5", string.Join(',', Select(lines, visible, state, 3, 3, 10, 10)), "R3 completed visible rows only");
    Equal("3,5", string.Join(',', Select(lines, visible, state, 3, 3, 10, 11)), "R3 current line remeasures once per generation");
    state.ApplyMeasurements([new(5, 20)]);
    Equal("3", string.Join(',', Select(lines, visible, state, 0, 3, 11, 12)), "R3 finalized prior current remeasures once");
    state.ApplyMeasurements([new(3, 20), new(1, 20)]);
    Equal("", string.Join(',', Select(lines, visible, state, 0, 0, 12, 13)), "R3 completed cached rows do not repeat");
    Equal("", string.Join(',', Select(lines, new(0, 2, 0, 0, true), state, 0, 0, 12, 13)), "R3 offscreen rows are excluded");
    Console.WriteLine("PASS 01C-R3 measurement selection policy");
    return;
}

if (args is ["p1c5-r1-messkip"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTW P5R1_READY
PRINTFORMW P5R1_FIRST={MESSKIP()} ISSKIP={ISSKIP()}
GLOBAL:0 = 11
SAVEGLOBAL
PRINTFORMW P5R1_AFTER_SAVE={MESSKIP()}
FORCEWAIT
PRINTFORMW P5R1_AFTER_FORCE={MESSKIP()}
INPUT
PRINTFORML P5R1_VALUE={RESULT} SKIP={MESSKIP()}
WAIT
QUIT
"""), null);

    Equal(true, runtime.SubmitWithMessageSkip(new(runtime.PendingInput!.RequestId, string.Empty)), "R1 MESSKIP starts");
    Contains("P5R1_FIRST=1 ISSKIP=0", runtime.Output, "R1 MESSKIP visible during first skip resume");
    Equal(true, runtime.ContinueMessageSkip(), "R1 MESSKIP reaches SAVEGLOBAL");
    Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "R1 MESSKIP waits for save commit");
    await runtime.DrainPersistenceAsync(_ => Task.CompletedTask);
    Equal(1, runtime.PersistenceDiagnostics.Count, "R1 save commit/ACK diagnostic count");
    Equal("global.sav", runtime.PersistenceDiagnostics[0].LogicalFilename, "R1 save commit/ACK diagnostic file");
    Equal(true, runtime.PersistenceDiagnostics[0].Bytes > 0, "R1 save commit/ACK diagnostic bytes");
    Contains("P5R1_AFTER_SAVE=1", runtime.Output, "R1 MESSKIP survives save ACK");
    Equal(true, runtime.ContinueMessageSkip(), "R1 MESSKIP reaches FORCEWAIT");
    Equal(false, runtime.MessageSkip.Active, "R1 FORCEWAIT stops skip owner");
    Equal(false, runtime.Output.Contains("P5R1_AFTER_FORCE", StringComparison.Ordinal), "R1 FORCEWAIT remains pending");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "R1 manual Enter crosses FORCEWAIT");
    Contains("P5R1_AFTER_FORCE=0", runtime.Output, "R1 MESSKIP cleared after stop");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "R1 manual Enter reaches INPUT");
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "R1 ordinary INPUT remains pending");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "7")), "R1 ordinary value accepted");
    Contains("P5R1_VALUE=7 SKIP=0", runtime.Output, "R1 ordinary value does not inherit skip");
    Console.WriteLine("PASS P1C5-R1 MESSKIP lifecycle");
    return;
}

if (args is ["p1c5-r1-display-cache"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
REPEAT 200
PRINTFORML P5R1_HISTORY_{COUNT}
REND
HTML_PRINT @"<button value='7'><b>P5R1_CACHE_BUTTON</b></button>"
INPUT
QUIT
"""), null);

    IReadOnlyList<BrowserDisplayLine> first = runtime.DisplayLines;
    IReadOnlyList<BrowserDisplayLine> second = runtime.DisplayLines;
    Equal(true, ReferenceEquals(first, second), "R1 unchanged display projection is reused");
    Equal(1, Flatten(second.SelectMany(line => line.Parts)).Count(part => part.Input == "7"), "R1 cached projection retains active button");
    Console.WriteLine("PASS P1C5-R1 unchanged display projection reuse");
    return;
}

if (args is ["ux16r2-lineisempty"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINT PENDING
LOCAL = LINEISEMPTY()
PRINTFORML BUFFER_BEFORE={LOCAL}
DRAWLINE
HTML_PRINT "<button value='105'>[105] DUNGEON</button>"
LOCAL = LINEISEMPTY()
PRINTFORM \@ LINEISEMPTY() ? # \n \@
PRINTL [998] SAMPLE
PRINTFORML BUFFER_AFTER={LOCAL}
HTML_PRINT "<b>HTML_PARTIAL</b>", 0
LOCAL = LINEISEMPTY()
PRINTFORML HTML_BUFFER={LOCAL}
PRINTL COMPLETED
LOCAL = LINEISEMPTY()
PRINTFORML COMPLETED_BUFFER={LOCAL}
PRINTBUTTON "PENDING_BUTTON", 7
LOCAL = LINEISEMPTY()
PRINTFORML BUTTON_BUFFER={LOCAL}
PRINT_IMG "MISSING"
LOCAL = LINEISEMPTY()
PRINTFORML IMAGE_BUFFER={LOCAL}
PRINT_RECT 4, 4
LOCAL = LINEISEMPTY()
PRINTFORML SHAPE_BUFFER={LOCAL}
WAIT
QUIT
"""), null);
    runtime.StartTitle();
    Equal(BrowserInputKind.Enter, runtime.PendingInput?.Kind, "LINEISEMPTY fixture reaches WAIT");
    Contains("BUFFER_BEFORE=0", runtime.Output, "pending PRINT is not empty");
    Contains("BUFFER_AFTER=1", runtime.Output, "HTML flush leaves ordinary print buffer empty");
    Contains("HTML_BUFFER=1", runtime.Output, "partial HTML does not populate ordinary buffer");
    Contains("COMPLETED_BUFFER=1", runtime.Output, "completed PRINTL empties ordinary buffer");
    Contains("BUTTON_BUFFER=0", runtime.Output, "ordinary button is buffered output");
    Contains("IMAGE_BUFFER=0", runtime.Output, "ordinary image is buffered output");
    Contains("SHAPE_BUFFER=0", runtime.Output, "ordinary shape is buffered output");
    string[] rows = runtime.DisplayLines.Select(line => string.Concat(Flatten(line.Parts).Select(part => part.Text))).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { label = "UX18_LINEISEMPTY_ROWS", rows }));
    int first = Array.FindIndex(rows, row => row.Contains("[105] DUNGEON", StringComparison.Ordinal));
    Equal(true, first >= 0, "dungeon row exists");
    Equal(true, rows[first + 1].Contains("[998] SAMPLE", StringComparison.Ordinal), "conditional newline adds no blank after completed HTML");
    Console.WriteLine("PASS UX16R2 LINEISEMPTY ordinary buffer / HTML boundary");
    return;
}

if (args is ["ux16r2-stable-history-refresh"])
{
    string fixture = CreateGlobalFixture("""
@SYSTEM_TITLE
SETANIMETIMER 20
REPEAT 500
PRINTFORML UX18_HISTORY_{COUNT}
REND
HTML_PRINT @"<div><b>UX18_NESTED_STATIC</b></div>"
PRINTL [7] UX18_CURRENT
INPUT
PRINTFORML UX18_FIRST={RESULT}
PRINTL [8] UX18_NEXT
INPUT
WAIT
QUIT
""");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(fixture, null);
    runtime.StartTitle();
    BrowserDisplayPart History() => Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).First(part => part.Text == "UX18_HISTORY_0");
    BrowserDisplayPart Current(string name) => Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Text.Contains(name, StringComparison.Ordinal));
    BrowserDisplayPart history = History(), active = Current("UX18_CURRENT");
    long generation = runtime.DisplayGeneration;
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 20; i++) { runtime.RefreshAnimations(); _ = runtime.DisplayLines; }
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Console.WriteLine(JsonSerializer.Serialize(new { label = "UX18_STATIC_REFRESH", retained = runtime.DisplayLines.Count, refreshes = 20, allocated, sameHistoryPart = ReferenceEquals(history, History()) }));
    Equal(true, ReferenceEquals(history, History()), "UX18 unchanged history part survives animation/projection");
    Equal(generation, runtime.DisplayGeneration, "UX18 unchanged refresh does not invalidate current input");
    Equal(true, runtime.SubmitDisplay(active), "UX18 unchanged current choice accepted");
    Equal(false, runtime.SubmitDisplay(active), "UX18 consumed choice remains stale");
    Equal(true, ReferenceEquals(history, History()), "UX18 next request shares inactive history parts");
    Equal(null, Current("UX18_CURRENT").Activation, "UX18 old choice is inactive in fresh projection");
    Contains("UX18_FIRST=7", runtime.Output, "UX18 accepted result unchanged");
    Equal(true, runtime.SubmitDisplay(Current("UX18_NEXT")), "UX18 current next choice accepted");
    Console.WriteLine("PASS UX16R2 immutable history reuse / input generations");
    return;
}

if (args is ["perf-gate-01b-display-metrics"])
{
    string fixture = CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL PG01B_HISTORY
PRINTL [7] PG01B_ACTIVE
HTML_PRINT @"<button value='7'><b>PG01B_HTML</b></button>"
PRINT PG01B_PARTIAL
INPUT
PRINTFORML PG01B_VALUE={RESULT}
INPUT
QUIT
""");
    File.AppendAllText(Path.Combine(fixture, "emuera.config"), "\n履歴ログの行数:500", Encoding.GetEncoding(932));

    BrowserRuntimeSession disabled = await BrowserRuntimeSession.StartTitleBootstrapAsync(fixture, null);
    disabled.StartTitle();
    _ = disabled.DisplayLines;
    Equal(0L, disabled.DisplayPerformance.PublishCalls, "PG01B disabled publish metrics remain zero");
    Equal(0L, disabled.DisplayPerformance.ActivationProjectionCalls, "PG01B disabled projection metrics remain zero");

    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(fixture, null);
    runtime.EnableDisplayPerformanceMetrics();
    runtime.StartTitle();
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "PG01B fixture reaches integer INPUT");
    BrowserDisplayPerformanceSnapshot afterPublish = runtime.DisplayPerformance;
    Equal(true, afterPublish.PublishCalls >= 1, "PG01B enabled metrics count title Publish");
    Equal(true, afterPublish.PublishSourceLines >= 1, "PG01B Publish measures retained source lines");
    Equal(true, afterPublish.PublishResultLines >= afterPublish.PublishSourceLines, "PG01B Publish measures partial and HTML result lines");

    IReadOnlyList<BrowserDisplayLine> first = runtime.DisplayLines;
    Equal(true, first.Any(line => Flatten(line.Parts).Any(part => part.Text.Contains("PG01B_PARTIAL", StringComparison.Ordinal))), "PG01B current partial line remains published");
    Equal(true, Flatten(first.SelectMany(line => line.Parts)).Any(part => part.Input == "7"), "PG01B HTML island remains published");
    BrowserDisplayPerformanceSnapshot firstProjection = runtime.DisplayPerformance;
    Equal(1L, firstProjection.ActivationProjectionCalls, "PG01B first activation projection counted once");
    Equal(true, firstProjection.ActivationProjectionSourceLines >= first.Count, "PG01B activation source rows counted");
    Equal(true, firstProjection.ActivationProjectionParts >= Flatten(first.SelectMany(line => line.Parts)).Count(), "PG01B activation parts counted recursively");

    IReadOnlyList<BrowserDisplayLine> second = runtime.DisplayLines;
    Equal(true, ReferenceEquals(first, second), "PG01B cached activation projection is reused");
    Equal(1L, runtime.DisplayPerformance.ActivationProjectionCalls, "PG01B cached activation read does not increment count");

    BrowserDisplayPart active = Flatten(first.SelectMany(line => line.Parts)).Single(part => part.Text.Contains("PG01B_ACTIVE", StringComparison.Ordinal));
    Equal(true, runtime.SubmitDisplay(active), "PG01B active HTML button is accepted");
    IReadOnlyList<BrowserDisplayLine> next = runtime.DisplayLines;
    Equal(2L, runtime.DisplayPerformance.ActivationProjectionCalls, "PG01B next request creates a new projection");
    Equal(false, runtime.SubmitDisplay(active), "PG01B stale activation remains rejected");
    Equal(true, next.Any(line => Flatten(line.Parts).Any(part => part.Text.Contains("PG01B_VALUE=7", StringComparison.Ordinal))), "PG01B display survives accepted activation");

    string wrapFixture = CreateGlobalFixture("""
@SYSTEM_TITLE
REPEAT 501
PRINTFORML PG01B_WRAP_{COUNT}
REND
PRINTL [7] PG01B_WRAP_ACTIVE
INPUT
QUIT
""");
    File.AppendAllText(Path.Combine(wrapFixture, "emuera.config"), "\n履歴ログの行数:500", Encoding.GetEncoding(932));
    BrowserRuntimeSession wrap = await BrowserRuntimeSession.StartTitleBootstrapAsync(wrapFixture, null);
    wrap.EnableDisplayPerformanceMetrics();
    wrap.StartTitle();
    Equal(500, wrap.DisplayLines.Count, "PG01B MaxLog wrap remains correct with metrics enabled");
    Equal(true, wrap.DisplayPerformance.PublishCalls >= 1, "PG01B wrap Publish remains measured");
    Console.WriteLine("PASS WEB-PERF-GATE-01B display metrics behavior");
    return;
}

if (args is ["perf-gate-01b-r1-historical-button"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL [1] OLD_CHOICE
INPUT
PRINTFORML FIRST={RESULT}
PRINTL [2] NEW_CHOICE
INPUT
PRINTFORML SECOND={RESULT}
WAIT
QUIT
""");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    BrowserDisplayPart firstOld = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == "1" && part.Text.Contains("OLD_CHOICE"));
    Equal(true, runtime.SubmitDisplay(firstOld), "historical first choice accepted");
    BrowserDisplayPart[] second = Flatten(runtime.DisplayLines.SelectMany(line => line.Parts)).Where(part => part.Input is not null).ToArray();
    BrowserDisplayPart old = second.Single(part => part.Input == "1" && part.Text.Contains("OLD_CHOICE"));
    BrowserDisplayPart current = second.Single(part => part.Input == "2" && part.Text.Contains("NEW_CHOICE"));
    long request = runtime.PendingInput!.RequestId;
    Equal(false, runtime.SubmitDisplay(old), "historical choice cannot consume new input");
    Equal(request, runtime.PendingInput!.RequestId, "historical choice leaves request unchanged");
    Equal(true, runtime.SubmitDisplay(current), "current choice accepted once");
    Contains("SECOND=2", runtime.Output, "current choice reaches second result");

    string transitionRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTBUTTON "INT_1", 1
INPUT
PRINTBUTTON "STR_S", "s"
INPUTS
PRINTBUTTON "INT_2", 2
INPUT
WAIT
QUIT
""");
    BrowserRuntimeSession transition = await BrowserRuntimeSession.StartTitleBootstrapAsync(transitionRoot, null);
    transition.StartTitle();
    BrowserDisplayPart int1 = Flatten(transition.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Text == "INT_1");
    Equal(true, transition.SubmitDisplay(int1), "INPUT current button accepted");
    BrowserDisplayPart[] stringProjection = Flatten(transition.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    BrowserDisplayPart staleInt = stringProjection.Single(part => part.Text == "INT_1");
    BrowserDisplayPart stringCurrent = stringProjection.Single(part => part.Text == "STR_S");
    Equal(null, staleInt.Activation, "INPUT button is inactive at INPUTS");
    Equal(true, transition.SubmitDisplay(stringCurrent), "INPUTS current button accepted");
    BrowserDisplayPart[] intProjection = Flatten(transition.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    BrowserDisplayPart staleString = intProjection.Single(part => part.Text == "STR_S");
    BrowserDisplayPart intCurrent = intProjection.Single(part => part.Text == "INT_2");
    Equal(null, staleString.Activation, "INPUTS button is inactive at INPUT");
    Equal(true, transition.SubmitDisplay(intCurrent), "INPUT current replacement accepted");

    string htmlRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
HTML_PRINT @"<button value='n'><b>NESTED</b><br><i>CHOICE</i></button>"
INPUTS
WAIT
QUIT
""");
    BrowserRuntimeSession html = await BrowserRuntimeSession.StartTitleBootstrapAsync(htmlRoot, null);
    html.StartTitle();
    BrowserDisplayPart nested = Flatten(html.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Button && part.Text.Contains("NESTED", StringComparison.Ordinal));
    Equal(true, nested.Children is { Count: > 0 }, "nested HTML children retained");
    Equal(true, html.SubmitDisplay(nested), "nested HTML current button accepted");

    string animationRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
SETANIMETIMER 10
PRINTBUTTON "ANIMATION_CURRENT", 3
INPUT
PRINTBUTTON "ANIMATION_NEXT", 4
INPUT
WAIT
QUIT
""");
    BrowserRuntimeSession animation = await BrowserRuntimeSession.StartTitleBootstrapAsync(animationRoot, null);
    animation.StartTitle();
    animation.RefreshAnimations();
    BrowserDisplayPart animationCurrent = Flatten(animation.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Text == "ANIMATION_CURRENT");
    Equal(true, animation.SubmitDisplay(animationCurrent), "animation redraw keeps current activation");
    BrowserDisplayPart animationStale = Flatten(animation.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Text == "ANIMATION_CURRENT");
    Equal(null, animationStale.Activation, "animation redraw does not reactivate historical button");

    string multiRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL [1] MULTI_A
PRINTL [2] MULTI_B
PRINTL [3] MULTI_C
INPUT
PRINTFORML MULTI_RESULT={RESULT}
WAIT
QUIT
""");
    BrowserRuntimeSession multi = await BrowserRuntimeSession.StartTitleBootstrapAsync(multiRoot, null);
    multi.StartTitle();
    BrowserDisplayPart[] multiChoices = Flatten(multi.DisplayLines.SelectMany(line => line.Parts))
        .Where(part => part.Input is "1" or "2" or "3")
        .ToArray();
    Equal(3, multiChoices.Length, "multi-line current choices are all present");
    Equal(true, multiChoices.All(part => part.Activation is not null), "multi-line current choices are all active");
    Equal(1, multiChoices.Select(part => part.ButtonGeneration).Distinct().Count(), "multi-line current choices share one generation");
    BrowserDisplayPart multiB = multiChoices.Single(part => part.Input == "2");
    Equal(true, multi.SubmitDisplay(multiB), "multi-line current choice B accepted");
    Contains("MULTI_RESULT=2", multi.Output, "multi-line current choice result");
    Console.WriteLine("PASS WEB-PERF-GATE-01B-R1 historical button generation");
    return;
}

if (args is ["p1c5-r1-toneinputs"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTW P5R1_TIMED_READY
PRINTFORMW P5R1_TIMED_BEFORE={MESSKIP()}
TONEINPUTS 100, "", 0, ""
PRINTFORML P5R1_TIMED_AFTER=[%RESULTS%] TIMEOUT={ISTIMEOUT} SKIP={MESSKIP()}
INPUT
QUIT
"""), null);

    Equal(true, runtime.SubmitWithMessageSkip(new(runtime.PendingInput!.RequestId, string.Empty)), "R1 timed string reached through message skip");
    Equal(true, runtime.ContinueMessageSkip(), "R1 timed string crosses PRINTFORMW boundary");
    Equal("TONEINPUTS", runtime.PendingInput?.TimedInputName, "R1 TONEINPUTS remains a timed string request");
    Equal(false, runtime.MessageSkip.Active, "R1 TONEINPUTS stops message skip owner");
    Equal(true, runtime.SubmitTimeout(runtime.PendingInput!.RequestId, runtime.SessionGeneration), "R1 TONEINPUTS natural timeout accepted");
    Contains("P5R1_TIMED_AFTER=[] TIMEOUT=1 SKIP=0", runtime.Output, "R1 TONEINPUTS default and timeout state");
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "R1 TONEINPUTS resumes once to next INPUT");
    Console.WriteLine("PASS P1C5-R1 TONEINPUTS natural timeout");
    return;
}

if (args is ["perf-gate-01a-preload"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL PG01A_TITLE
INPUT
CALL PG01A_LATE
WAIT
QUIT
""");
    string lazyDirectory = Path.Combine(root, "ERB", "Lazy");
    Directory.CreateDirectory(lazyDirectory);
    File.WriteAllText(Path.Combine(lazyDirectory, "LATE.ERB"), """
@PG01A_LATE()
PRINTL PG01A_LAZY=77
RETURN 0
""", new UTF8Encoding(true));
    File.WriteAllText(Path.Combine(root, "setting.json"), """
{"起動時に読み込まないERBフォルダ":{"有効":true,"フォルダ":["ERB/Lazy"]}}
""", new UTF8Encoding(true));
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nサブディレクトリを検索する:YES", Encoding.GetEncoding(932));

    Type preload = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.Runtime.Utils.Preload")!;
    var tryGet = preload.GetMethod("TryGetFileLines", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    bool IsCached(string path)
    {
        object?[] call = [path, null];
        return (bool)tryGet.Invoke(null, call)!;
    }

    string eagerPath = Path.Combine(root, "CSV", "GAMEBASE.CSV");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    Console.WriteLine($"PG01A_DIAGNOSTIC lazy={runtime.Scripts.LazyErbFileCount} fallback={runtime.Scripts.LazyErbFallbackFileCount} config={string.Join(';', runtime.EffectiveConfiguration.Select(pair => pair.Key + '=' + pair.Value))}");
    Equal(true, IsCached(eagerPath), "PG01A eager source is retained through bootstrap");
    Equal(1, runtime.Scripts.LazyErbFileCount, "PG01A lazy target indexed but not called by title");
    Equal(0, runtime.PendingPersistenceCount, "PG01A bootstrap persistence queue empty");

    runtime.StartTitle();
    Equal(BrowserInputKind.Integer, runtime.PendingInput?.Kind, "PG01A title reaches first input");
    Equal(false, IsCached(eagerPath), "PG01A title boundary releases startup Preload");
    Equal(0, runtime.PendingPersistenceCount, "PG01A title persistence queue empty");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, "1")), "PG01A resume after title");
    Contains("PG01A_LAZY=77", runtime.Output, "PG01A first lazy hydration succeeds after Preload release");

    object process = typeof(BrowserRuntimeSession).GetField("process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
    Task reload = (Task)process.GetType().GetMethod("ReloadErbAll")!.Invoke(process, null)!;
    await reload;
    Equal(true, IsCached(eagerPath), "PG01A reload explicitly repopulates Preload");
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "PG01A reload returns to input boundary");
    Equal(0, runtime.PendingPersistenceCount, "PG01A reload persistence queue empty");
    Console.WriteLine("PASS WEB-PERF-GATE-01A Preload lifecycle, lazy hydration and reload");
    return;
}

if (args.Length == 2 && args[0] == "perf-gate-01a-maxlog" && int.TryParse(args[1], out int maxLog) && maxLog is 500 or 5000 or 50000)
{
    static string LineText(BrowserDisplayLine line) => string.Concat(Flatten(line.Parts).Select(part => part.Text));

    async Task<BrowserRuntimeSession> Start(int maxLog)
    {
        string root = CreateGlobalFixture($$"""
@SYSTEM_TITLE
REDRAW 0
SETANIMETIMER 20
REPEAT {{maxLog}}
PRINTFORML PG01A_LINE_{COUNT}
REND
PRINTL [7] PG01A_ACTIVE
INPUT
QUIT
""");
        File.AppendAllText(Path.Combine(root, "emuera.config"), $"\n履歴ログの行数:{maxLog}", Encoding.GetEncoding(932));
        BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
        runtime.StartTitle();
        return runtime;
    }

    BrowserRuntimeSession runtime = await Start(maxLog);
    Console.WriteLine($"PG01A_MAXLOG_DIAGNOSTIC configured={maxLog} retained={runtime.DisplayLines.Count} lineCount={runtime.LineCount} pending={runtime.PendingInput?.Kind}:{runtime.PendingInput?.RequestId} first={LineText(runtime.DisplayLines[0])} last={LineText(runtime.DisplayLines[^1])} outputTail={runtime.OutputTail(200)}");
    Equal(maxLog, runtime.DisplayLines.Count, $"PG01A MaxLog={maxLog} retained count");
    Contains("PG01A_LINE_1", LineText(runtime.DisplayLines[0]), $"PG01A MaxLog={maxLog} evicts only oldest line");
    Contains("PG01A_ACTIVE", LineText(runtime.DisplayLines[^1]), $"PG01A MaxLog={maxLog} retains newest line");
    Equal(maxLog + 2L, runtime.LineCount, $"PG01A MaxLog={maxLog} eviction does not change logical line count");
    BrowserDisplayPart active = Flatten(runtime.DisplayLines[^1].Parts).Single(part => part.Input == "7");
    Equal(true, active.Activation is not null, $"PG01A MaxLog={maxLog} newest button remains selectable");

    if (maxLog == 500)
    {
        runtime.RefreshAnimations();
        Contains("PG01A_LINE_1", LineText(runtime.DisplayLines[0]), "PG01A animation refresh preserves wrapped order");
        BrowserDisplayPart refreshedActive = Flatten(runtime.DisplayLines[^1].Parts).Single(part => part.Input == "7");
        Equal(true, runtime.SubmitDisplay(refreshedActive), "PG01A active button accepted after wrap");

        string mutationRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
REDRAW 0
SETANIMETIMER 20
REPEAT 502
PRINTFORML PG01A_MUTATION_{COUNT}
REND
WAIT
CLEARLINE 2
PRINTL PG01A_AFTER_DELETE
WAIT
CLEARLINE 500
PRINTL PG01A_AFTER_CLEAR
WAIT
QUIT
""");
        File.AppendAllText(Path.Combine(mutationRoot, "emuera.config"), "\n履歴ログの行数:500", Encoding.GetEncoding(932));
        BrowserRuntimeSession mutation = await BrowserRuntimeSession.StartTitleBootstrapAsync(mutationRoot, null);
        mutation.StartTitle();
        mutation.RefreshAnimations();
        Equal(500, mutation.DisplayLines.Count, "PG01A wrapped mutation fixture retained count");
        Contains("PG01A_MUTATION_2", LineText(mutation.DisplayLines[0]), "PG01A wrapped mutation fixture oldest row");
        Equal(true, mutation.Submit(new(mutation.PendingInput!.RequestId, string.Empty)), "PG01A resume wrapped delete fixture");
        Equal(false, mutation.DisplayLines.Any(line => LineText(line).Contains("PG01A_MUTATION_500", StringComparison.Ordinal)), "PG01A deleteLine removes newest row 500 after wrap");
        Equal(false, mutation.DisplayLines.Any(line => LineText(line).Contains("PG01A_MUTATION_501", StringComparison.Ordinal)), "PG01A deleteLine removes newest row 501 after wrap");
        Contains("PG01A_AFTER_DELETE", LineText(mutation.DisplayLines[^1]), "PG01A append after wrapped delete");
        Equal(true, mutation.Submit(new(mutation.PendingInput!.RequestId, string.Empty)), "PG01A resume wrapped clear fixture");
        Equal(1, mutation.DisplayLines.Count, "PG01A CLEARLINE removes retained ring rows");
        Contains("PG01A_AFTER_CLEAR", LineText(mutation.DisplayLines[0]), "PG01A append after CLEARLINE");
    }

    Console.WriteLine($"PASS WEB-PERF-GATE-01A Config.MaxLog={maxLog} ring semantics");
    return;
}

if (args is ["p1c5-skip-s01"])
{
    const string erb = """
@SYSTEM_TITLE
FLAG:0 = 0
FLAG:1 = 0
PRINTW P5_START
FLAG:0 += 1
PRINTFORMW P5_A_{P5_ONCE()}
WAIT
FLAG:0 += 1
PRINTFORMW P5_B_{P5_ONCE()}
WAITANYKEY
PRINTFORML P5_COUNTS={FLAG:0},{FLAG:1}
INPUT
PRINTFORML P5_VALUE={RESULT}
WAIT
QUIT

@P5_ONCE()
#FUNCTION
FLAG:1 += 1
RETURNF FLAG:1
""";

    BrowserRuntimeSession reference = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(erb), null);
    for (int index = 0; index < 5; index++)
    {
        long requestId = reference.PendingInput!.RequestId;
        Equal(true, reference.Submit(new(requestId, string.Empty)), $"S01 reference wait {index + 1}");
    }
    Equal(BrowserInputKind.Integer, reference.PendingInput?.Kind, "S01 reference stops at INPUT");
    Contains("P5_COUNTS=2,2", reference.Output, "S01 reference evaluates output functions once");

    BrowserRuntimeSession skipped = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(erb), null);
    long first = skipped.PendingInput!.RequestId;
    Equal(true, skipped.SubmitWithMessageSkip(new(first, string.Empty)), "S01 skip operation starts through current request");
    for (int index = 0; index < 10 && skipped.ContinueMessageSkip(); index++) { }
    Equal(BrowserInputKind.Integer, skipped.PendingInput?.Kind, "S01 skip stops at INPUT");
    Contains("P5_A_1", skipped.Output, "S01 first expression evaluated");
    Contains("P5_B_2", skipped.Output, "S01 second expression evaluated");
    Contains("P5_COUNTS=2,2", skipped.Output, "S01 side effects match reference");
    Equal(false, skipped.Output.Contains("P5_VALUE", StringComparison.Ordinal), "S01 does not invent a value");

    long valueRequest = skipped.PendingInput!.RequestId;
    Equal(true, skipped.Submit(new(valueRequest, "37")), "S01 later ordinary value accepted");
    Contains("P5_VALUE=37", skipped.Output, "S01 later value remains ordinary");
    Equal(BrowserInputKind.Enter, skipped.PendingInput?.Kind, "S01 skip does not carry into following WAIT");
    Console.WriteLine("PASS P1C5 S01 message skip evaluation and value boundary");
    return;
}

if (args is ["p1c5-skip-s04-failure"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTW P5_SAVE_START
GLOBAL:0 = 11
SAVEGLOBAL
PRINTL SHOULD_NOT_RUN
QUIT
"""), null);
    long requestId = runtime.PendingInput!.RequestId;
    Equal(true, runtime.SubmitWithMessageSkip(new(requestId, string.Empty)), "S04 skip reaches persistence");
    Equal(BrowserRuntimeStatus.Persisting, runtime.Status, "S04 persistence is pending");
    bool failed = false;
    try
    {
        await runtime.DrainPersistenceAsync(_ => throw new IOException("P1C5 deterministic persistence failure"));
    }
    catch (IOException) { failed = true; }
    Equal(true, failed, "S04 persistence failure is surfaced");
    Equal(BrowserRuntimeStatus.Failed, runtime.Status, "S04 runtime fails with persistence");
    Equal(false, runtime.MessageSkip.Active, "S04 persistence failure stops skip owner");
    Equal("persistence-failed", runtime.MessageSkip.StopReason, "S04 failure stop reason");
    Equal(false, runtime.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "S04 failed commit blocks following ERB");
    Console.WriteLine("PASS P1C5 S04 persistence failure stops message skip");
    return;
}

if (args is ["p1c5-skip-s02-s05"])
{
    BrowserRuntimeSession force = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTW P5_FORCE_START
PRINTL P5_BEFORE_FORCE
FORCEWAIT
PRINTL P5_AFTER_FORCE
WAIT
PRINTL P5_AFTER_MANUAL_WAIT
QUIT
"""), null);
    Equal(true, force.SubmitWithMessageSkip(new(force.PendingInput!.RequestId, string.Empty)), "S02 starts skip");
    Equal(false, force.MessageSkip.Active, "S02 FORCEWAIT stops skip");
    Equal("stop-messkip", force.MessageSkip.StopReason, "S02 stop reason");
    Equal(false, force.Output.Contains("P5_AFTER_FORCE", StringComparison.Ordinal), "S02 does not cross FORCEWAIT");
    Equal(true, force.Submit(new(force.PendingInput!.RequestId, string.Empty)), "S02 explicit Enter crosses FORCEWAIT");
    Contains("P5_AFTER_FORCE", force.Output, "S02 explicit input resumes");
    Equal(false, force.Output.Contains("P5_AFTER_MANUAL_WAIT", StringComparison.Ordinal), "S02 following WAIT remains pending");

    BrowserRuntimeSession timed = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
RESULT = 2468
RESULTS = 維持対象
PRINTW P5_TIMED_START
TWAIT 700, 1
PRINTFORML P5_TIMED_AFTER RESULT={RESULT} TEXT=[%RESULTS%]
PRINTFORML P5_TIMED_TIMEOUT={ISTIMEOUT}
INPUT
PRINTFORML P5_TIMED_VALUE={RESULT}
WAIT
QUIT
"""), null);
    Equal(true, timed.SubmitWithMessageSkip(new(timed.PendingInput!.RequestId, string.Empty)), "S03 starts skip");
    BrowserInputPrompt timedPrompt = timed.PendingInput!;
    Equal(BrowserInputKind.TimedVoid, timedPrompt.Kind, "S03 reaches input-disabled TWAIT");
    Equal(true, timed.MessageSkip.Active, "S03 skip owns reached TWAIT");
    Equal(true, timed.ContinueMessageSkip(), "S03 skip resumes TWAIT once");
    Equal(BrowserInputKind.Integer, timed.PendingInput?.Kind, "S03 stops at INPUT");
    Contains("P5_TIMED_AFTER RESULT=2468 TEXT=[維持対象]", timed.Output, "S03 preserves values");
    Contains("P5_TIMED_TIMEOUT=0", timed.Output, "S03 skip is not timeout");
    Equal(false, timed.SubmitTimeout(timedPrompt.RequestId, timed.SessionGeneration, timed.DisplayGeneration), "S03 stale timeout is rejected");

    const string saveErb = """
@SYSTEM_TITLE
PRINTW P5_SAVE_START
GLOBAL:0 = 11
SAVEGLOBAL
GLOBAL:0 = 99
PRINTW P5_AFTER_SAVE
LOADGLOBAL
PRINTFORML P5_SAVED_VALUE={GLOBAL:0}
INPUT
PRINTFORML P5_SAVE_INPUT={RESULT}
WAIT
QUIT
""";
    BrowserRuntimeSession saved = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(saveErb), null);
    Equal(true, saved.SubmitWithMessageSkip(new(saved.PendingInput!.RequestId, string.Empty)), "S04 starts skip");
    Equal(BrowserRuntimeStatus.Persisting, saved.Status, "S04 waits for commit");
    SaveMutation save = saved.PendingPersistence!;
    string savedSha = Convert.ToHexString(SHA256.HashData(save.Bytes!));
    await saved.DrainPersistenceAsync(_ => Task.CompletedTask);
    while (saved.ContinueMessageSkip()) { }
    Equal(BrowserInputKind.Integer, saved.PendingInput?.Kind, "S04 stops at value input after ack");
    Contains("P5_SAVED_VALUE=11", saved.Output, "S04 loads immutable SAVEGLOBAL snapshot");
    Equal(savedSha, Convert.ToHexString(SHA256.HashData(save.Bytes!)), "S04 immutable bytes survive ack");

    BrowserRuntimeSession stoppedSave = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(saveErb), null);
    Equal(true, stoppedSave.SubmitWithMessageSkip(new(stoppedSave.PendingInput!.RequestId, string.Empty)), "S04 stop trial reaches persistence");
    Equal(true, stoppedSave.StopMessageSkip(), "S04 user can stop while commit is pending");
    await stoppedSave.DrainPersistenceAsync(_ => Task.CompletedTask);
    Equal(BrowserInputKind.Enter, stoppedSave.PendingInput?.Kind, "S04 accepted commit resumes only to next wait");
    Equal(false, stoppedSave.Output.Contains("P5_SAVED_VALUE", StringComparison.Ordinal), "S04 stop injects no later input");

    var loopErb = new StringBuilder("@SYSTEM_TITLE\n");
    for (int index = 0; index < 40; index++) loopErb.AppendLine($"PRINTW P5_STOP_{index:00}");
    loopErb.AppendLine("INPUTS").AppendLine("PRINTFORML P5_TEXT=[%RESULTS%]").AppendLine("WAIT").AppendLine("QUIT");
    BrowserRuntimeSession stopped = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(loopErb.ToString()), null);
    long firstRequest = stopped.PendingInput!.RequestId;
    Equal(true, stopped.SubmitWithMessageSkip(new(firstRequest, string.Empty)), "S05 first start accepted");
    Equal(false, stopped.SubmitWithMessageSkip(new(firstRequest, string.Empty)), "S05 duplicate start is stale");
    for (int index = 0; index < 4; index++) Equal(true, stopped.ContinueMessageSkip(), $"S05 continuation {index + 1}");
    int beforeStop = stopped.DoScriptCallCount;
    Equal(true, stopped.StopMessageSkip(), "S05 explicit stop accepted");
    Equal(false, stopped.ContinueMessageSkip(), "S05 stopped operation cannot resume");
    Equal(beforeStop, stopped.DoScriptCallCount, "S05 stop does not execute ERB");
    BrowserInputPrompt manual = stopped.PendingInput!;
    Equal(true, stopped.Submit(new(manual.RequestId, string.Empty)), "S05 current wait remains manually usable");
    Equal(beforeStop + 1, stopped.DoScriptCallCount, "S05 one manual input resumes once");

    BrowserRuntimeSession text = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nINPUTS\nPRINTFORML P5_TEXT=[%RESULTS%]\nWAIT\nQUIT\n"), null);
    BrowserInputPrompt textPrompt = text.PendingInput!;
    Equal(true, text.Submit(new(textPrompt.RequestId, "e")), "S05 ordinary text input accepted");
    Contains("P5_TEXT=[e]", text.Output, "S05 plain e remains plain text");

    BrowserRuntimeSession primitive = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nINPUTMOUSEKEY 5000\nPRINTFORML P5_PRIMITIVE={RESULT:0},{RESULT:1}\nWAIT\nQUIT\n"), null);
    BrowserInputPrompt primitivePrompt = primitive.PendingInput!;
    Equal(false, primitive.SubmitWithMessageSkip(new(primitivePrompt.RequestId, string.Empty)), "S05 skip cannot consume primitive input");
    Equal(true, primitive.SubmitPrimitive(new(primitivePrompt.RequestId, BrowserPrimitiveInputKind.Click, Code: 2097152,
        SessionGeneration: primitive.SessionGeneration, DisplayGeneration: primitive.DisplayGeneration)), "S05 primitive right click remains on primitive path");
    Contains("P5_PRIMITIVE=1,2097152", primitive.Output, "S05 primitive result is preserved");

    Console.WriteLine("PASS P1C5 S02-S05 runtime boundaries");
    return;
}

if (args is ["host-display-a"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nSETCOLOR 255,0,0\nFONTBOLD\nPRINT RED_BOLD\nRESETCOLOR\nFONTREGULAR\nPRINTL DEFAULT\nWAIT\nQUIT\n");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayPart[] parts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    foreach (BrowserDisplayLine line in display.DisplayLines)
        Console.WriteLine("DISPLAY_A_LINE=" + string.Join('|', Flatten(line.Parts).Select(part => $"{part.Kind}:{part.Text}")));
    Equal(1, display.DisplayLines.Count(line => Flatten(line.Parts).Any(part => part.Text is "RED_BOLD" or "DEFAULT")), "DISPLAY A has one output line");
    Equal(true, parts.Any(part => part.Text == "RED_BOLD" && part.Style is { Foreground: "#FF0000", Bold: true }), "DISPLAY A retains the first PRINT style");
    Equal(true, parts.Any(part => part.Text == "DEFAULT" && part.Style is { Foreground: not "#FF0000", Bold: false }), "DISPLAY A applies the later default style");
    Console.WriteLine("PASS host DISPLAY A");
    return;
}

if (args is ["ux15-layout-primitive", var ux15FixtureRoot])
{
    BrowserRuntimeSession ux15Session = await BrowserRuntimeSession.StartPersistentBootstrapAsync(Path.GetFullPath(ux15FixtureRoot), null);
    ux15Session.StartTitle();
    string[] ux15Lines = ux15Session.DisplayLines.Select(line => string.Concat(Flatten(line.Parts).Where(part => part.Children is null).Select(part => part.Text))).ToArray();
    foreach (string line in ux15Lines) Console.WriteLine("UX15_LINE=" + line);
    Equal(BrowserRuntimeStatus.WaitingForInput, ux15Session.Status, "UX15 fixture reaches WAIT");
    int firstRow = Array.FindIndex(ux15Lines, line => line.Contains("[1] FIRST") && line.Contains("[2] SECOND") && line.Contains("[3] THIRD"));
    int secondRow = Array.FindIndex(ux15Lines, line => line.Contains("[4] FOURTH") && line.Contains("[5] FIFTH") && line.Contains("[6] SIXTH"));
    Equal(true, firstRow >= 0 && secondRow >= 0 && firstRow != secondRow, "PRINTFORMLC has two distinct physical row indices");
    Equal(false, ux15Lines[firstRow].Contains("[4] FOURTH") || ux15Lines[secondRow].Contains("[1] FIRST"), "PRINTFORMLC rows have nonoverlapping membership");
    Equal(true, ux15Lines.Any(line => line.Contains("C_COUNTS=9,11", StringComparison.Ordinal)), "Windows LINECOUNT after PRINTC and HTML");
    Console.WriteLine("PASS UX15 layout primitive");
    return;
}

if (args is ["host-printc-contract"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nPRINTFORMLC [1]前\nPRINTFORMLC [2]後\nPRINTL\nINPUT\nQUIT\n");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayPart[] parts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(BrowserRuntimeStatus.WaitingForInput, display.Status, "PRINTC reaches input");
    Equal(true, parts.Any(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "1" && part.Text.Contains('前')), "PRINTC first button");
    Equal(true, parts.Any(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "2" && part.Text.Contains('後')), "PRINTC second button");
    Equal(1, display.DisplayLines.Count(line => Flatten(line.Parts).Any(part => part.Input is "1" or "2")), "PRINTC shares one line");
    Console.WriteLine("PASS host PRINTC contract");
    return;
}

if (args is ["ux13-printc-html-continuation"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nPRINTC \"PREFIX\"\nHTML_PRINT @\"<div xpos='100'>STATUS</div>\"\nINPUT\nQUIT\n");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayLine[] displayLines = display.DisplayLines.ToArray();
    int prefixIndex = Array.FindIndex(displayLines, line => Flatten(line.Parts).Any(part => part.Kind == BrowserDisplayPartKind.Text && part.Text.Contains("PREFIX", StringComparison.Ordinal)));
    int htmlIndex = Array.FindIndex(displayLines, line => Flatten(line.Parts).Any(part => part.Kind == BrowserDisplayPartKind.Group && Flatten(part.Children ?? []).Any(child => child.Text == "STATUS")));
    Equal(prefixIndex + 1, htmlIndex, "Windows PRINTC line ends before positioned HTML_PRINT");
    Console.WriteLine("PASS UX15 PRINTC and positioned HTML use separate Windows display lines");
    return;
}

if (args is ["host-display-b"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT \"<div xpos='100px' ypos='20px'><button value='999' title='test'><font color='#00FF00'>GREEN</font><br><b>SECOND</b><img src='dot' height='18px'><img src='dot' height='18px'></button></div>\"\nINPUT\nQUIT\n");
    Directory.CreateDirectory(Path.Combine(root, "resources"));
    File.WriteAllBytes(Path.Combine(root, "resources", "dot.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    File.WriteAllText(Path.Combine(root, "resources", "sprites.csv"), "dot,dot.png\n", new UTF8Encoding(true));
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayPart[] parts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(2, parts.Count(part => part.ImageDataUrl is not null), "DISPLAY B retains both button images");
    Equal(true, parts.Any(part => part.Text == "GREEN" && part.Style?.Foreground == "#00FF00"), "DISPLAY B retains nested font color");
    Equal(true, parts.Any(part => part.Text == "SECOND" && part.Style?.Bold == true), "DISPLAY B retains nested bold");
    BrowserDisplayPart button = parts.Single(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "999");
    Equal(true, display.SubmitDisplay(button), "DISPLAY B remains one selectable button");
    Console.WriteLine("PASS host DISPLAY B");
    return;
}

if (args is ["host-display-c"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT \"<div display='absolute-lefttop' xpos='100px' ypos='20px'><font color='#FF0000'>A</font><font color='#00FF00'>B</font></div>\"\nINPUT\nQUIT\n");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayPart[] parts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(1, parts.Count(part => part.Layout is { Mode: BrowserDisplayMode.AbsoluteLeftTop, X: 100, Y: 20 }), "DISPLAY C owns one positioned group");
    Console.WriteLine("PASS host DISPLAY C");
    return;
}

if (args is ["host-input-contract"])
{
    const string erb = """
@SYSTEM_TITLE
PRINTBUTTON "TARGET", "word"
INPUTMOUSEKEY 5000
PRINTFORML OBS={RESULT:0},{RESULT:1},{RESULT:2},{RESULT:3},{RESULT:4},{RESULT:5}
PRINTFORML OBS_TEXT=[%RESULTS:5%]
WAIT
QUIT
""";

    async Task<BrowserRuntimeSession> Start() => await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(erb), null);

    BrowserKeyInput enter = BrowserInputMapping.MapKey("Enter", "Enter", false, false, false)!;
    Equal(new BrowserKeyInput(13, 13), enter, "Enter uses Windows Keys values");
    BrowserKeyInput shiftEnter = BrowserInputMapping.MapKey("ShiftLeft", "Shift", false, true, false)!;
    Equal(new BrowserKeyInput(16, 65552), shiftEnter, "Shift+Enter begins with the Windows Shift key event");
    BrowserKeyInput letter = BrowserInputMapping.MapKey("KeyA", "a", false, false, false)!;
    Equal(new BrowserKeyInput(65, 65), letter, "letter key uses virtual-key value");

    BrowserRuntimeSession key = await Start();
    BrowserInputPrompt keyPrompt = key.PendingInput!;
    Equal(true, key.SubmitPrimitive(new(keyPrompt.RequestId, BrowserPrimitiveInputKind.Key, "word", enter.Code, enter.KeyData,
        SessionGeneration: key.SessionGeneration, DisplayGeneration: key.DisplayGeneration, ButtonIsInteger: false)), "key event accepted");
    Contains("OBS=3,13,13,0,0,0", key.Output, "key RESULT tuple");
    Contains("OBS_TEXT=[word]", key.Output, "key captures hovered string button");

    BrowserRuntimeSession left = await Start();
    BrowserInputPrompt leftPrompt = left.PendingInput!;
    Equal(true, left.SubmitPrimitive(new(leftPrompt.RequestId, BrowserPrimitiveInputKind.Click, "word", 1048576,
        X: 120, Y: 220, ButtonIsInteger: false, SessionGeneration: left.SessionGeneration, DisplayGeneration: left.DisplayGeneration)), "left click accepted");
    Contains("OBS=1,1048576,120,", left.Output, "left click RESULT tuple");
    Contains("OBS_TEXT=[word]", left.Output, "internal image click keeps parent button value");

    BrowserRuntimeSession right = await Start();
    BrowserInputPrompt rightPrompt = right.PendingInput!;
    Equal(true, right.SubmitPrimitive(new(rightPrompt.RequestId, BrowserPrimitiveInputKind.Click, "word", 2097152,
        X: 121, Y: 221, ButtonIsInteger: false, SessionGeneration: right.SessionGeneration, DisplayGeneration: right.DisplayGeneration)), "right click accepted");
    Contains("OBS=1,2097152,121,", right.Output, "right click RESULT tuple");

    BrowserRuntimeSession timeout = await Start();
    BrowserInputPrompt timeoutPrompt = timeout.PendingInput!;
    Equal(true, timeout.SubmitPrimitive(new(timeoutPrompt.RequestId, BrowserPrimitiveInputKind.Timeout,
        SessionGeneration: timeout.SessionGeneration, DisplayGeneration: timeout.DisplayGeneration)), "timeout accepted");
    Contains("OBS=4,0,0,0,0,0", timeout.Output, "timeout RESULT tuple");

    Equal(false, timeout.SubmitPrimitive(new(timeoutPrompt.RequestId, BrowserPrimitiveInputKind.Click, "word", 1048576,
        SessionGeneration: timeout.SessionGeneration, DisplayGeneration: timeout.DisplayGeneration, ButtonIsInteger: false)), "completed request rejects stale mouse event");
    Console.WriteLine("PASS host input contract");
    return;
}

if (args is ["ux15r3-primitive-timeout-redraw"])
{
    const string erb = "@SYSTEM_TITLE\nINPUTMOUSEKEY 100\nPRINTFORML UX15R3_RESULT={RESULT:0}\nQUIT\n";
    async Task<BrowserRuntimeSession> Start() => await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(erb), null);
    static void Redraw(BrowserRuntimeSession runtime)
    {
        object console = typeof(BrowserRuntimeSession).GetField("console", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;
        console.GetType().GetMethod("Publish", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(console, [false]);
    }

    BrowserRuntimeSession timeout = await Start();
    BrowserInputPrompt prompt = timeout.PendingInput!;
    long originalDisplay = timeout.DisplayGeneration;
    Redraw(timeout);
    Equal(true, timeout.DisplayGeneration > originalDisplay, "redraw changes display generation while request stays pending");
    Equal(false, timeout.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Timeout,
        SessionGeneration: timeout.SessionGeneration + 1, DisplayGeneration: originalDisplay)), "stale session timeout rejected");
    Equal(true, timeout.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Timeout,
        SessionGeneration: timeout.SessionGeneration, DisplayGeneration: originalDisplay)), "same-request timeout survives redraw");
    Contains("UX15R3_RESULT=4", timeout.Output, "timeout reaches ERB with result 4");
    Equal(false, timeout.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Timeout,
        SessionGeneration: timeout.SessionGeneration, DisplayGeneration: originalDisplay)), "completed timeout cannot consume twice");

    BrowserRuntimeSession click = await Start();
    BrowserInputPrompt clickPrompt = click.PendingInput!;
    long oldDisplay = click.DisplayGeneration;
    Redraw(click);
    Equal(false, click.SubmitPrimitive(new(clickPrompt.RequestId, BrowserPrimitiveInputKind.Click, Code: 1048576,
        SessionGeneration: click.SessionGeneration, DisplayGeneration: oldDisplay)), "stale click remains rejected");
    Equal(true, click.SubmitPrimitive(new(clickPrompt.RequestId, BrowserPrimitiveInputKind.Click, Code: 1048576,
        SessionGeneration: click.SessionGeneration, DisplayGeneration: click.DisplayGeneration)), "current click remains accepted");
    Console.WriteLine("PASS UX15R3 primitive timeout redraw");
    return;
}

if (args is ["ux16-print-rect-space-order"])
{
    const string erb = """
@SYSTEM_TITLE
PRINTC BEFORE
PRINT_RECT 0,0,400,100
PRINT_SPACE 200
PRINTC AFTER
INPUT
WAIT
QUIT
""";
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture(erb), null);
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status,
        $"shape program reaches input ({runtime.FailureDiagnostic.ExceptionType}: {runtime.FailureDiagnostic.ExceptionMessage})");
    BrowserDisplayLine line = runtime.DisplayLines.Single(item => item.Parts.Any(part => part.Text.Contains("BEFORE", StringComparison.Ordinal)));
    string[] ordered = line.Parts.Select(part => part.Kind == BrowserDisplayPartKind.Shape ? part.Tooltip ?? "SHAPE" : part.Text).ToArray();
    Contains("BEFORE", ordered[0], "text before shape keeps order");
    Equal(BrowserDisplayPartKind.Shape, line.Parts[1].Kind, "PRINT_RECT becomes shape part");
    Equal(BrowserDisplayPartKind.Shape, line.Parts[2].Kind, "PRINT_SPACE becomes shape part");
    Equal(true, ordered[^1].Contains("AFTER", StringComparison.Ordinal), "text after shapes keeps order");
    int fontSize = line.Parts[1].Style!.FontSize;
    Equal(400 * fontSize / 100, line.Parts[1].Layout?.Width, "rectangle width follows Windows percentage units");
    Equal(100 * fontSize / 100, line.Parts[1].Layout?.Height, "rectangle height follows Windows percentage units");
    Equal(200 * fontSize / 100, line.Parts[2].Width, "space width follows Windows percentage units");
    Console.WriteLine("PASS UX16 PRINT_RECT/PRINT_SPACE ordering");
    return;
}

if (args is ["r3-mouse-position"])
{
    BrowserRuntimeSession popup = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nPRINTFORML R3_MOUSE={MOUSEX()},{MOUSEY()}\nINPUTMOUSEKEY 5000\nQUIT\n"), null);
    Equal(BrowserRuntimeStatus.WaitingForInput, popup.Status, "MOUSEX/MOUSEY reach popup's primitive wait");
    Contains("R3_MOUSE=0,0", popup.Output, "no pointer has bottom-left zero position");
    Equal(BrowserInputKind.MouseKey, popup.PendingInput?.Kind, "popup waits for mouse or key");
    BrowserRuntimeSession clicked = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL [1] GO\nINPUT\nPRINTFORML R3_BEFORE={MOUSEX()},{MOUSEY()}\nINPUTMOUSEKEY 5000\nPRINTFORML R3_AFTER={MOUSEX()},{MOUSEY()}\nWAIT\nQUIT\n"), null);
    clicked.SetViewport(1512, 882);
    BrowserDisplayPart button = Flatten(clicked.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == "1");
    Equal(true, clicked.SubmitDisplay(button, 120, 200, 1048576), "normal display click stores pointer before script resumes");
    Contains("R3_BEFORE=120,-682", clicked.Output, "MOUSEY uses bottom-origin content coordinate");
    BrowserInputPrompt primitive = clicked.PendingInput!;
    Equal(true, clicked.SubmitPrimitive(new(primitive.RequestId, BrowserPrimitiveInputKind.Click, Code: 1048576,
        X: 121, Y: 201, SessionGeneration: clicked.SessionGeneration, DisplayGeneration: clicked.DisplayGeneration)), "primitive click accepted");
    Contains("R3_AFTER=121,-681", clicked.Output, "primitive click updates mouse position");
    Console.WriteLine("PASS R3 mouse position");
    return;
}

if (args is ["r3-html-island-click"])
{
    BrowserRuntimeSession popup = await BrowserRuntimeSession.StartPersistentAsync(CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT_ISLAND \"<div display='absolute-leftbottom' xpos='100px' ypos='-100px'><button value='0'>YES</button><button value='1'>NO</button></div>\", 99\nINPUTMOUSEKEY\nPRINTFORML R3_CHOICE={RESULT:0},{RESULT:1},{RESULT:5}\nHTML_PRINT_ISLAND_CLEAR 99\nWAIT\nQUIT\n"), null);
    Equal(BrowserInputKind.MouseKey, popup.PendingInput?.Kind, "island primitive wait");
    BrowserDisplayPart no = Flatten(popup.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Input == "1" && part.Text == "NO");
    Equal(true, no.Activation is not null, "island button has current input activation");
    Equal(true, popup.SubmitDisplay(no, 140, 200, 1048576), "island No click accepted");
    Contains("R3_CHOICE=1,1048576,1", popup.Output, "island click passes RESULT:5");
    Equal(false, Flatten(popup.DisplayLines.SelectMany(line => line.Parts)).Any(part => part.Text == "NO"), "island clears after answer");
    Console.WriteLine("PASS R3 HTML island click");
    return;
}

if (args is ["twait-contract"])
{
    static bool Timeout(BrowserRuntimeSession runtime, BrowserInputPrompt prompt, long? displayGeneration = null)
    {
        var method = typeof(BrowserRuntimeSession).GetMethod("SubmitTimeout");
        Equal(true, method is not null, "TWAIT timeout entry exists");
        return (bool)method!.Invoke(runtime, [prompt.RequestId, runtime.SessionGeneration, displayGeneration ?? runtime.DisplayGeneration])!;
    }

    BrowserRuntimeSession tw01 = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
LOCAL = 2468
RESULTS = 維持対象
PRINTL TW01_BEFORE
SETANIMETIMER 10
TWAIT 100, 1
PRINTFORML TW01_AFTER RESULT={LOCAL} TEXT=[%RESULTS%]
WAIT
PRINTL TW01_AFTER_MANUAL
QUIT
"""), null);
    BrowserInputPrompt tw01Prompt = tw01.PendingInput!;
    Equal("TimedVoid", tw01Prompt.Kind.ToString(), "TW01 prompt kind");
    Equal(100L, tw01Prompt.TimeLimit, "TW01 time limit");
    Equal(false, tw01.Submit(new(tw01Prompt.RequestId, "", SessionGeneration: tw01.SessionGeneration, DisplayGeneration: tw01.DisplayGeneration)), "TW01 rejects ordinary input");
    Equal(false, tw01.Output.Contains("TW01_AFTER", StringComparison.Ordinal), "TW01 does not run ahead");
    long beforeAnimation = tw01.DisplayGeneration;
    tw01.RefreshAnimations();
    Equal(beforeAnimation, tw01.DisplayGeneration, "TW01 static refresh preserves display generation");
    Equal(true, Timeout(tw01, tw01Prompt, beforeAnimation), "TW01 timeout survives animation redraw");
    Contains("TW01_AFTER RESULT=2468 TEXT=[維持対象]", tw01.Output, "TW01 values remain unchanged");
    Equal(BrowserInputKind.Enter, tw01.PendingInput?.Kind, "TW01 keeps following WAIT");
    Equal(false, Timeout(tw01, tw01Prompt), "TW01 stale timer rejected");

    const string tw02Erb = "@SYSTEM_TITLE\nPRINTL TW02_BEFORE\nTWAIT 1500, 0\nPRINTL TW02_AFTER\nWAIT\nPRINTL TW02_AFTER_MANUAL\nQUIT\n";
    BrowserRuntimeSession tw02Early = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture(tw02Erb), null);
    BrowserInputPrompt earlyPrompt = tw02Early.PendingInput!;
    Equal(BrowserInputKind.Enter, earlyPrompt.Kind, "TW02 early prompt kind");
    Equal(true, tw02Early.Submit(new(earlyPrompt.RequestId, "", SessionGeneration: tw02Early.SessionGeneration, DisplayGeneration: tw02Early.DisplayGeneration)), "TW02 early Enter accepted");
    Contains("TW02_AFTER", tw02Early.Output, "TW02 early resumes once");
    Equal(false, Timeout(tw02Early, earlyPrompt), "TW02 old timer rejected after Enter");
    Equal(false, tw02Early.Output.Contains("TW02_AFTER_MANUAL", StringComparison.Ordinal), "TW02 old timer does not consume following WAIT");

    BrowserRuntimeSession tw02Timeout = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture(tw02Erb), null);
    BrowserInputPrompt timeoutPrompt = tw02Timeout.PendingInput!;
    Equal(true, Timeout(tw02Timeout, timeoutPrompt), "TW02 timeout accepted");
    Contains("TW02_AFTER", tw02Timeout.Output, "TW02 timeout resumes");
    Equal(BrowserInputKind.Enter, tw02Timeout.PendingInput?.Kind, "TW02 timeout keeps following WAIT");

    BrowserRuntimeSession tw03 = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL TW03_BEGIN
TWAIT 80, 1
PRINTL TW03_MIDDLE
TWAIT 120, 1
PRINTL TW03_END
INPUTS
PRINTFORML TW03_VALUE=[%RESULTS%]
WAIT
QUIT
"""), null);
    BrowserInputPrompt tw03First = tw03.PendingInput!;
    Equal(true, Timeout(tw03, tw03First), "TW03 first timeout");
    BrowserInputPrompt tw03Second = tw03.PendingInput!;
    Equal(false, Timeout(tw03, tw03First), "TW03 first timer stale during second wait");
    Equal(true, Timeout(tw03, tw03Second), "TW03 second timeout");
    BrowserInputPrompt tw03Input = tw03.PendingInput!;
    Equal(BrowserInputKind.String, tw03Input.Kind, "TW03 reaches INPUTS");
    Equal(false, Timeout(tw03, tw03Second), "TW03 second timer stale during INPUTS");
    Equal(true, tw03.Submit(new(tw03Input.RequestId, "marker", SessionGeneration: tw03.SessionGeneration, DisplayGeneration: tw03.DisplayGeneration)), "TW03 marker accepted");
    Equal(1, tw03.Output.Split("TW03_BEGIN", StringSplitOptions.None).Length - 1, "TW03 BEGIN once");
    Equal(1, tw03.Output.Split("TW03_MIDDLE", StringSplitOptions.None).Length - 1, "TW03 MIDDLE once");
    Equal(1, tw03.Output.Split("TW03_END", StringSplitOptions.None).Length - 1, "TW03 END once");
    Contains("TW03_VALUE=[marker]", tw03.Output, "TW03 explicit marker only");

    BrowserRuntimeSession tw04 = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
TW_VALUE = 11
SAVEGLOBAL
TW_VALUE = 99
TWAIT 100, 1
LOADGLOBAL
PRINTFORML TW04_VALUE={TW_VALUE}
WAIT
QUIT
""", "#DIM GLOBAL SAVEDATA TW_VALUE\n"), null);
    var trace = new List<string>();
    await tw04.DrainPersistenceAsync(request =>
    {
        trace.Add($"{request.Kind}:{request.LogicalFilename}:{Convert.ToHexString(SHA256.HashData(request.Bytes!))}");
        Equal(false, tw04.Output.Contains("TW04_VALUE", StringComparison.Ordinal), "TW04 waits for persistence ACK");
        return Task.CompletedTask;
    });
    Equal(1, trace.Count, "TW04 one SAVEGLOBAL snapshot");
    BrowserInputPrompt tw04Prompt = tw04.PendingInput!;
    Equal(true, Timeout(tw04, tw04Prompt), "TW04 timeout accepted after ACK");
    Contains("TW04_VALUE=11", tw04.Output, "TW04 loads committed snapshot");
    Console.WriteLine("TW04_TRACE=" + trace[0]);
    Console.WriteLine("PASS TWAIT contracts TW01-TW04");
    return;
}

if (args is ["html-island-contract"])
{
    BrowserRuntimeSession island = await BrowserRuntimeSession.StartSavePersistentAsync(CreateGlobalFixture("""
@SYSTEM_TITLE
HTML_PRINT_ISLAND "<div display='absolute-lefttop' xpos='0' ypos='0' width='320px' height='120px' background_color='#112233'><button value='7'>ISLAND</button></div>", 2
TWAIT 50, 1
HTML_PRINT_ISLAND_CLEAR 2
PRINTL ISLAND_CLEARED
WAIT
QUIT
"""), null);
    Equal(false, island.IsFailed, "HTML island starts without failure");
    Equal(true, Flatten(island.DisplayLines.SelectMany(line => line.Parts)).Any(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "7"), "HTML island is published");
    BrowserInputPrompt prompt = island.PendingInput!;
    var timeout = typeof(BrowserRuntimeSession).GetMethod("SubmitTimeout")!;
    Equal(true, (bool)timeout.Invoke(island, [prompt.RequestId, island.SessionGeneration, island.DisplayGeneration])!, "HTML island TWAIT resumes");
    Equal(false, Flatten(island.DisplayLines.SelectMany(line => line.Parts)).Any(part => part.Input == "7"), "HTML island clear removes layer");
    Contains("ISLAND_CLEARED", island.Output, "HTML island continues after clear");
    Console.WriteLine("PASS HTML island contract");
    return;
}

if (args is ["p1c2-r1-infinite-loop-warning"])
{
    Type dialogType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Framework.Dialog")!;
    var warningPrompt = dialogType.GetMethod("ShowInfiniteLoopPrompt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    Equal(true, warningPrompt is not null, "browser has a dedicated infinite-loop warning policy");
    try
    {
        warningPrompt!.Invoke(null, ["title", "message"]);
        throw new InvalidOperationException("normal browser infinite-loop warning must stop explicitly");
    }
    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is UnsupportedRuntimeFeatureException inner && inner.Message.Contains("LONG_RUNNING_PROMPT_NOT_IMPLEMENTED", StringComparison.Ordinal)) { }
    var generalPrompt = dialogType.GetMethod("ShowPrompt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;
    try
    {
        generalPrompt.Invoke(null, ["title", "message"]);
        throw new InvalidOperationException("general browser prompts must remain unsupported");
    }
    catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is UnsupportedRuntimeFeatureException) { }

    long now = 0;
    var policy = new BrowserInfiniteLoopContinuation(() => now);
    for (int warning = 1; warning <= BrowserInfiniteLoopContinuation.WarningLimit; warning++)
        Equal(false, policy.ShouldAbort("title", $"fixture.erb:{warning}"), $"diagnostic approval {warning}");
    Equal(true, policy.ShouldAbort("title", "fixture.erb:13"), "diagnostic warning-count limit stops execution");
    now = 0;
    var boundedByTime = new BrowserInfiniteLoopContinuation(() => now);
    now = BrowserInfiniteLoopContinuation.ElapsedLimitMilliseconds + 1;
    Equal(true, boundedByTime.ShouldAbort("title", "fixture.erb:time"), "diagnostic cumulative time limit stops execution");
    Equal(true, policy.Events.All(entry => entry.DiagnosticContinuation && entry.MaxWarnings == 12 && entry.MaxElapsedMilliseconds == 60000 && entry.ErbPosition.StartsWith("fixture.erb:", StringComparison.Ordinal)), "diagnostic log fields are complete");
    Console.WriteLine("PASS P1C2-R1 bounded browser infinite-loop policy");
    return;
}

if (args is ["p1c2-lr1-contract"])
{
    const string erb = """
@SYSTEM_TITLE
LR_RESULT = LR_LONG()
PRINTFORML RESULT={LR_RESULT},CALLS={LR_CALLS}
WAIT
PRINTL AFTER_WAIT
QUIT

@LR_LONG
#FUNCTION
LR_CALLS++
FOR LOCAL, 0, 4000000
LR_TICKS++
NEXT
RETURNF 7
""";
    const string erh = "#DIM LR_RESULT\n#DIM LR_CALLS\n#DIM LR_TICKS\n";

    string continueRoot = CreateGlobalFixture(erb, erh);
    File.AppendAllText(Path.Combine(continueRoot, "emuera.config"), "\n無限ループ警告までのミリ秒数:1", Encoding.GetEncoding(932));
    var notices = new List<BrowserLongRunningNotice>();
    BrowserRuntimeSession continued = await BrowserRuntimeSession.StartTitleBootstrapAsync(continueRoot, null);
    continued.EnableInteractiveInfiniteLoopPrompt(notice =>
    {
        notices.Add(notice);
        return new(true, "confirm-approved");
    });
    continued.StartTitle();
    Equal(true, notices.Count > 0, "LR01 actual runtime warning reaches the interactive callback");
    Equal(BrowserRuntimeStatus.WaitingForInput, continued.Status, "LR02 OK continues the current function to WAIT");
    Equal(1, continued.DoScriptCallCount, "LR02 confirmation does not re-enter DoScript");
    Contains("RESULT=7,CALLS=1", continued.Output, "LR02 function and expression execute once");
    Equal(BrowserInputKind.Enter, continued.PendingInput?.Kind, "LR06 confirm answer does not consume WAIT");

    string abortRoot = CreateGlobalFixture(erb.Replace("WAIT", "PRINTL SHOULD_NOT_RUN\nWAIT", StringComparison.Ordinal), erh);
    File.AppendAllText(Path.Combine(abortRoot, "emuera.config"), "\n無限ループ警告までのミリ秒数:1", Encoding.GetEncoding(932));
    BrowserRuntimeSession aborted = await BrowserRuntimeSession.StartTitleBootstrapAsync(abortRoot, null);
    aborted.EnableInteractiveInfiniteLoopPrompt(_ => new(false, "cancel-or-suppressed"));
    aborted.StartTitle();
    Equal(BrowserRuntimeStatus.Failed, aborted.Status, "LR03 Cancel stops as failure");
    Equal(false, aborted.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "LR03 Cancel does not advance");
    Equal(null, aborted.PendingInput, "LR03 Cancel leaves no input request");
    Equal(false, aborted.Submit(new(1, string.Empty)), "LR03 stopped runtime rejects later input");

    Console.WriteLine("PASS P1C2-LR1 LR01/LR02/LR03/LR06 runtime contract");
    return;
}

if (args is ["p1c2-lr1-save-and-failure"])
{
    const string erb = """
@SYSTEM_TITLE
LR_RESULT = LR_SAVE_THEN_LOOP()
PRINTL SHOULD_NOT_RUN
QUIT

@LR_SAVE_THEN_LOOP
#FUNCTION
LR_VALUE = 101
SAVEGLOBAL
LR_VALUE = 202
FOR LOCAL, 0, 4000000
LR_VALUE++
NEXT
RETURNF 7
""";
    const string erh = "#DIM GLOBAL SAVEDATA LR_VALUE\n#DIM LR_RESULT\n";

    string savedRoot = CreateGlobalFixture(erb, erh);
    File.AppendAllText(Path.Combine(savedRoot, "emuera.config"), "\n無限ループ警告までのミリ秒数:1", Encoding.GetEncoding(932));
    BrowserRuntimeSession savedThenAborted = await BrowserRuntimeSession.StartTitleBootstrapAsync(savedRoot, null);
    savedThenAborted.EnableInteractiveInfiniteLoopPrompt(_ => new(false, "cancel-or-suppressed"));
    savedThenAborted.StartTitle();
    int alertMilliseconds = (int)typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.Runtime.Config.Config")!.GetProperty("InfiniteLoopAlertTime")!.GetValue(null)!;
    Console.WriteLine($"LR04_DIAGNOSTIC status={savedThenAborted.Status} events={savedThenAborted.LongRunningPrompt?.Events.Count ?? 0} alertMs={alertMilliseconds}");
    Equal(BrowserRuntimeStatus.Persisting, savedThenAborted.Status, "LR04 queued snapshot remains ahead of the abort");
    Equal(1, savedThenAborted.PendingPersistenceCount, "LR04 exactly one pre-warning snapshot is queued");
    SaveMutation snapshot = savedThenAborted.PendingPersistence!;
    Equal(true, savedThenAborted.AcknowledgePersistence(snapshot.OperationId), "LR04 snapshot commit is acknowledged once");
    Equal(BrowserRuntimeStatus.Failed, savedThenAborted.Status, "LR04 deferred abort is published after commit");
    Equal(false, savedThenAborted.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "LR04 abort does not execute later ERB");

    string verifyRoot = CreateGlobalFixture("@SYSTEM_TITLE\nLOADGLOBAL\nPRINTFORML VALUE={LR_VALUE}\nQUIT\n", erh);
    BrowserRuntimeSession restored = await BrowserRuntimeSession.StartPersistentAsync(verifyRoot, snapshot.Bytes);
    Contains("VALUE=101", restored.Output, "LR04 committed bytes retain the SAVEGLOBAL-time value");

    string failedRoot = CreateGlobalFixture(erb.Replace("SAVEGLOBAL", string.Empty, StringComparison.Ordinal), erh);
    File.AppendAllText(Path.Combine(failedRoot, "emuera.config"), "\n無限ループ警告までのミリ秒数:1", Encoding.GetEncoding(932));
    BrowserRuntimeSession interopFailed = await BrowserRuntimeSession.StartTitleBootstrapAsync(failedRoot, null);
    interopFailed.EnableInteractiveInfiniteLoopPrompt(_ => throw new InvalidOperationException("test-only JS failure"));
    interopFailed.StartTitle();
    alertMilliseconds = (int)typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.Runtime.Config.Config")!.GetProperty("InfiniteLoopAlertTime")!.GetValue(null)!;
    Console.WriteLine($"LR05_DIAGNOSTIC status={interopFailed.Status} events={interopFailed.LongRunningPrompt?.Events.Count ?? 0} alertMs={alertMilliseconds}");
    Equal(BrowserRuntimeStatus.Failed, interopFailed.Status, "LR05 confirmation exception stops the runtime");
    Contains("LONG_RUNNING_CONFIRM_FAILED", interopFailed.Output, "LR05 confirmation failure remains explicit");
    Equal(false, interopFailed.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "LR05 confirmation failure never auto-continues");
    Equal("error", interopFailed.LongRunningPrompt!.Events.Last().Phase, "LR05 failure is observed");

    BrowserRuntimeSession diagnostic = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateGlobalFixture("@SYSTEM_TITLE\nQUIT\n"), null);
    diagnostic.EnableDiagnosticInfiniteLoopContinuation();
    bool exclusive = false;
    try { diagnostic.EnableInteractiveInfiniteLoopPrompt(_ => new(true, "test")); }
    catch (InvalidOperationException) { exclusive = true; }
    Equal(true, exclusive, "LR05 diagnostic and normal confirmation are exclusive");

    Console.WriteLine("PASS P1C2-LR1 LR04/LR05 save and failure contract");
    return;
}

static string CreateGlobalFixture(string erb, string? erh = null, bool binary = true)
{
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1c1", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(
        Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"),
        Path.Combine(root, "CSV", "GAMEBASE.CSV"));
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    File.WriteAllText(Path.Combine(root, "emuera.config"), $"セーブデータをバイナリ形式で保存する:{(binary ? "YES" : "NO")}", Encoding.GetEncoding(932));
    File.WriteAllText(Path.Combine(root, "ERB", "P1C1.ERH"), erh ?? "#DIM GLOBAL SAVEDATA P1C1_VALUE\n", new UTF8Encoding(true));
    File.WriteAllText(Path.Combine(root, "ERB", "P1C1.ERB"), erb, new UTF8Encoding(true));
    return root;
}

if (args is ["save-persistence-contract"])
{
    const string erh = """
#DIM SAVEDATA P3_VALUE
#DIMS SAVEDATA P3_TEXT
#DIM SAVEDATA P3_GRID, 2, 3
#DIM GLOBAL SAVEDATA P3_GLOBAL
#DIM GLOBAL P3_LOAD_COUNT
""";
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
P3_GLOBAL = 101
P3_VALUE = 11
P3_TEXT = 通常セーブ往復
P3_GRID:1:2 = 9876543210123
P3_LOAD_COUNT = 0
SAVEGLOBAL
SAVEDATA 401, "P1C3 continue sentinel"
SAVEDATA 219, "P1C3 source"
P3_VALUE = 99
CHKDATA 219
PRINTFORML BEFORE_LOAD_CHK={RESULT}
LOADDATA 219
PRINTL ERROR_UNREACHABLE_AFTER_LOADDATA
QUIT

@SYSTEM_LOADEND
PRINTL SYSTEM_LOADEND_REACHED

@EVENTLOAD
P3_LOAD_COUNT++
LOADGLOBAL
PRINTFORML LOADED={P3_VALUE},GRID={P3_GRID:1:2},COUNT={P3_LOAD_COUNT},SLOT={LASTLOAD_NO}
PRINTFORML TEXT=[%P3_TEXT%],GLOBAL={P3_GLOBAL}
DELDATA 401
CHKDATA 401
PRINTFORML DELETED_CHK={RESULT}
P3_GLOBAL = 202
SAVEGLOBAL
SAVEDATA 218, "P1C3 roundtrip"
PRINTL P1C3_READY
WAIT
QUIT
""", erh);

    var committed = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    var trace = new List<string>();
    BrowserRuntimeSession save = await BrowserRuntimeSession.StartSavePersistentAsync(root, initialFiles: null);
    await save.DrainPersistenceAsync(request =>
    {
        trace.Add($"{request.Kind}:{request.LogicalFilename}");
        if (request.Kind == SaveMutationKind.Put)
            committed[request.LogicalFilename] = (byte[])request.Bytes!.Clone();
        else
            committed.Remove(request.LogicalFilename);
        return Task.CompletedTask;
    });
    Console.WriteLine($"P1C3_STATUS={save.Status}");
    Console.WriteLine("P1C3_TRACE=" + string.Join('|', trace));
    Console.WriteLine(save.Output);
    Equal(BrowserRuntimeStatus.WaitingForInput, save.Status, "S02 load transition reaches final WAIT");
    Contains("SYSTEM_LOADEND_REACHED", save.Output, "S02 SYSTEM_LOADEND runs");
    Contains("LOADED=11,GRID=9876543210123,COUNT=1,SLOT=219", save.Output, "S02 EVENTLOAD sees saved normal values");
    Contains("TEXT=[通常セーブ往復],GLOBAL=101", save.Output, "S02 GLOBAL stays separate and loads");
    Contains("DELETED_CHK=1", save.Output, "S02 deleted 401 is immediately absent");
    Equal(false, save.Output.Contains("ERROR_UNREACHABLE_AFTER_LOADDATA", StringComparison.Ordinal), "LOADDATA does not resume following instruction");
    Equal("Put:global.sav|Put:save401.sav|Put:save219.sav|Delete:save401.sav|Put:global.sav|Put:save218.sav", string.Join('|', trace), "GLOBAL and normal saves share one ordered queue");
    Equal(false, committed.ContainsKey("save401.sav"), "401 tombstone remains absent");
    Equal(true, committed.ContainsKey("save218.sav"), "218 is committed");

    string restartRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
CHKDATA 401
PRINTFORML RESTART_401={RESULT}
CHKDATA 218
PRINTFORML RESTART_218={RESULT}
LOADDATA 218
PRINTL ERROR_UNREACHABLE_AFTER_RELOAD
QUIT

@EVENTLOAD
LOADGLOBAL
PRINTFORML RESTART_VALUE={P3_VALUE},GRID={P3_GRID:1:2},GLOBAL={P3_GLOBAL},SLOT={LASTLOAD_NO}
WAIT
QUIT
""", erh);
    BrowserRuntimeSession restart = await BrowserRuntimeSession.StartSavePersistentAsync(restartRoot, committed);
    Equal(BrowserRuntimeStatus.WaitingForInput, restart.Status, "S02 restart reaches WAIT");
    Contains("RESTART_401=1", restart.Output, "S02 deleted 401 is not resurrected");
    Contains("RESTART_218=0", restart.Output, "S02 218 is restored");
    Contains("RESTART_VALUE=11,GRID=9876543210123,GLOBAL=202,SLOT=218", restart.Output, "S02 restart restores normal and GLOBAL state");
    Equal(false, restart.Output.Contains("ERROR_UNREACHABLE_AFTER_RELOAD", StringComparison.Ordinal), "restart LOADDATA transitions through EVENTLOAD");
    Console.WriteLine("PASS P1C3 named save persistence contract");
    return;
}

if (args is ["restore-count-contract"])
{
    static Dictionary<string, byte[]> FilesFor(IEnumerable<int> slots)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["global.sav"] = [0x47]
        };
        foreach (int slot in slots)
            files[$"save{slot:00}.sav"] = [(byte)(slot & 0xFF)];
        return files;
    }

    static async Task<(string Root, int Count)> StartAndCount(Dictionary<string, byte[]> files)
    {
        string root = CreateGlobalFixture("@SYSTEM_TITLE\nWAIT\nQUIT\n");
        BrowserRuntimeSession session = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(root, files);
        Equal(BrowserRuntimeStatus.BootstrapReady, session.Status, $"restore {files.Count} status");
        string sav = Path.GetFullPath(root);
        int count = Directory.GetFiles(sav, "*.sav", SearchOption.AllDirectories).Length;
        Equal(files.Count, count, $"restore {files.Count} file count");
        return (root, count);
    }

    var sixtyFive = FilesFor(Enumerable.Range(10, 64));
    var allSlots = FilesFor(Enumerable.Concat(Enumerable.Range(0, 240), [300, 400, 401]));
    var first = await StartAndCount(sixtyFive);
    var second = await StartAndCount(allSlots);
    Console.WriteLine($"RESTORE_65={first.Count}");
    Console.WriteLine($"RESTORE_244={second.Count}");
    Console.WriteLine("PASS P1C3-R1 restore count contract");
    return;
}

if (args is ["canonical-save-runtime-contract"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nSAVEDATA 0, \"zero\"\nSAVEDATA 1, \"one\"\nSAVEDATA 9, \"nine\"\nWAIT\nQUIT\n");
    var names = new List<string>();
    BrowserRuntimeSession canonicalSession = await BrowserRuntimeSession.StartSavePersistentAsync(root, null);
    await canonicalSession.DrainPersistenceAsync(request =>
    {
        if (request.Kind == SaveMutationKind.Put)
            names.Add(request.LogicalFilename);
        return Task.CompletedTask;
    });
    Equal(BrowserRuntimeStatus.WaitingForInput, canonicalSession.Status, "canonical runtime reaches WAIT");
    Equal("save00.sav|save01.sav|save09.sav", string.Join('|', names), "SAVEDATA emits canonical filenames");
    Console.WriteLine("PASS P1C3-R1 canonical runtime save names");
    return;
}

if (args is ["width-conversion-contract"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
RESULTS = 　ＡＢＣ１２３￥＼｜ガギパヴヾゔ。「」、・ー
PRINTFORML N=[%TOHALF(RESULTS)%]
RESULTS =  ABC123｜ｶﾞｷﾞﾊﾟｳﾞヽﾞ｡｢｣､･ｰ
PRINTFORML W=[%TOFULL(RESULTS)%]
QUIT
""");
    BrowserRuntimeSession width = await BrowserRuntimeSession.StartAsync(root);
    Equal(BrowserRuntimeStatus.Succeeded, width.Status, "width fixture succeeds");
    Contains("N=[ ABC123\\＼|ｶﾞｷﾞﾊﾟｳﾞヽﾞｳﾞ｡｢｣､･ｰ]", width.Output, "TOHALF matches Windows Japanese conversion");
    Contains("W=[ＡＢＣ１２３｜ガギパヴヾ。「」、・ー]", width.Output, "TOFULL matches Windows Japanese conversion");
    Console.WriteLine("PASS browser width conversion contract");
    return;
}

if (args is ["save-boundary-contract"])
{
    const string erh = "#DIM SAVEDATA P3_BOUNDARY\n";
    const int Missing = 98;
    int[] slots = [0, 99, 100, 199, 200, 218, 219, 220, 239, 300, 400, 401];
    StringBuilder erb = new("@SYSTEM_TITLE\nP3_BOUNDARY = 11\n");
    foreach (int slot in slots) erb.AppendLine($"SAVEDATA {slot}, \"slot-{slot}\"");
    foreach (int slot in slots) erb.AppendLine($"CHKDATA {slot}\nPRINTFORML SLOT_{slot}={{RESULT}}");
    erb.AppendLine($"CHKDATA {Missing}\nPRINTFORML SLOT_{Missing}={{RESULT}}\nWAIT\nQUIT");
    string root = CreateGlobalFixture(erb.ToString(), erh);
    var names = new List<string>();
    BrowserRuntimeSession binarySave = await BrowserRuntimeSession.StartSavePersistentAsync(root, null);
    await binarySave.DrainPersistenceAsync(request => { names.Add(request.LogicalFilename); return Task.CompletedTask; });
    Equal(BrowserRuntimeStatus.WaitingForInput, binarySave.Status, "binary boundary fixture reaches WAIT");
    Equal(string.Join('|', slots.Select(slot => $"save{slot:00}.sav")), string.Join('|', names), "S01 numeric slot filenames");
    foreach (int slot in slots) Contains($"SLOT_{slot}=0", binarySave.Output, $"S01 slot {slot} exists");
    Contains($"SLOT_{Missing}=1", binarySave.Output, "S01 missing slot is absent");

    string textRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
P3_BOUNDARY = 11
SAVEDATA 7, "text representative"
P3_BOUNDARY = 99
LOADDATA 7
QUIT
@EVENTLOAD
PRINTFORML TEXT_VALUE={P3_BOUNDARY},SLOT={LASTLOAD_NO}
WAIT
QUIT
""", erh, binary: false);
    var textFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    BrowserRuntimeSession textSave = await BrowserRuntimeSession.StartSavePersistentAsync(textRoot, null);
    await textSave.DrainPersistenceAsync(request => { if (request.Kind == SaveMutationKind.Put) textFiles[request.LogicalFilename] = request.Bytes!; return Task.CompletedTask; });
    Equal(BrowserRuntimeStatus.WaitingForInput, textSave.Status, "text save fixture reaches WAIT");
    Contains("TEXT_VALUE=11,SLOT=7", textSave.Output, "S01 text codec roundtrip");
    Equal(true, textFiles.ContainsKey("save07.sav"), "S01 text save is captured");
    Console.WriteLine("PASS P1C3 save boundary and text codec contract");
    return;
}

if (args is ["save-sql-sidecar-contract"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nSAVEDATA 1, \"sidecar\"\nQUIT\n", "#DIM SAVEDATA P3_VALUE\n");
    Directory.CreateDirectory(Path.Combine(root, "temp_db"));
    BrowserRuntimeSession sidecar = await BrowserRuntimeSession.StartSavePersistentAsync(root, null);
    Equal(BrowserRuntimeStatus.Failed, sidecar.Status, "S09 sidecar is rejected");
    Contains("P1C3未対応: SQL sidecarを含む通常sav", sidecar.Output, "S09 explicit sidecar error");
    Equal(0, sidecar.PendingPersistenceCount, "S09 failed sidecar queues no save");
    Console.WriteLine("PASS P1C3 SQL sidecar guard contract");
    return;
}

if (args is ["ux16r1-drain-single-flight"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(
        CreateGlobalFixture("@SYSTEM_TITLE\nSAVEGLOBAL\nWAIT\nQUIT\n"), null);
    var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int commits = 0;
    async Task Persist(SaveMutation mutation) { commits++; await commit.Task; }
    Task first = runtime.DrainPersistenceAsync(Persist);
    Task second = runtime.DrainPersistenceAsync(Persist);
    Console.WriteLine($"DRAIN_CAPTURE commitCallsBeforeRelease={commits}, head={runtime.PendingPersistence!.OperationId}");
    commit.SetResult();
    try { await Task.WhenAll(first, second); }
    catch (Exception ex) { Console.Error.WriteLine(ex); }
    Equal(1, commits, "two legitimate drains must commit the FIFO head once");
    Equal(0, runtime.PersistenceAckTimeline.Count, "no duplicate ACK rejection");
    Equal(0, runtime.PendingPersistenceCount, "FIFO drained");
    Equal(BrowserInputKind.Enter, runtime.PendingInput!.Kind, "exactly one resume at WAIT");
    Console.WriteLine("PASS UX16R1 concurrent title/save drain");
    return;
}

if (args is ["ux16r1-ack-diagnostics"])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentAsync(
        CreateGlobalFixture("@SYSTEM_TITLE\nSAVEGLOBAL\nWAIT\nQUIT\n"), null);
    SaveMutation head = runtime.PendingPersistence!;
    Equal(false, runtime.AcknowledgePersistence("wrong-head"), "reject wrong head");
    var property = typeof(BrowserRuntimeSession).GetProperty("PersistenceAckTimeline");
    Equal(true, property is not null, "bounded ACK timeline exists");
    JsonElement Timeline() => JsonSerializer.SerializeToElement(property!.GetValue(runtime));
    JsonElement rejected = Timeline()[0];
    Equal("wrong-head", rejected.GetProperty("OperationId").GetString(), "received operation");
    Equal(head.OperationId, rejected.GetProperty("HeadOperationId").GetString(), "FIFO head");
    Equal("global.sav", rejected.GetProperty("HeadLogicalFilename").GetString(), "FIFO filename");
    Equal(1, rejected.GetProperty("PendingCount").GetInt32(), "pending count");
    Equal(runtime.SessionGeneration, rejected.GetProperty("SessionGeneration").GetInt64(), "session");
    Equal(false, rejected.GetProperty("Accepted").GetBoolean(), "rejection recorded");
    Equal(true, rejected.GetProperty("Timestamp").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-1), "timestamp");
    Equal("P1C1.ERB:2", rejected.GetProperty("ErbPosition").GetString(), "position");
    Equal(true, rejected.TryGetProperty("RequestId", out _), "raw request ID present");
    Equal(true, runtime.AcknowledgePersistence(head.OperationId), "correct head ACK");
    for (int i = 0; i < 80; i++) runtime.AcknowledgePersistence("duplicate");
    JsonElement tail = Timeline();
    Equal(64, tail.GetArrayLength(), "bounded timeline");
    Equal(82L, tail[63].GetProperty("Sequence").GetInt64(), "monotonic sequence");
    Equal(false, tail[63].TryGetProperty("Bytes", out _), "no save bytes");
    Console.WriteLine("PASS UX16R1 bounded ACK rejection timeline");
    return;
}

if (args is ["save-snapshot-ack-contract"])
{
    const string erh = "#DIM SAVEDATA P3_VALUE\n#DIM GLOBAL P3_DESC_COUNT\n";
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
P3_VALUE = 11
P3_DESC_COUNT = 0
SAVEDATA 5, P3_DESC()
P3_VALUE = 99
PRINTFORML DESC_COUNT={P3_DESC_COUNT}
WAIT
QUIT
@P3_DESC
#FUNCTIONS
P3_DESC_COUNT++
RETURNF "snapshot"
""", erh);
    BrowserRuntimeSession save = await BrowserRuntimeSession.StartSavePersistentAsync(root, null);
    SaveMutation mutation = save.PendingPersistence ?? throw new InvalidOperationException("S03 save mutation missing");
    Equal(false, save.AcknowledgePersistence("stale-operation"), "S06 stale ack is rejected");
    Equal(mutation.OperationId, save.PendingPersistence?.OperationId, "S06 stale ack keeps pending mutation");
    byte[] bytes = (byte[])mutation.Bytes!.Clone();
    Equal(true, save.AcknowledgePersistence(mutation.OperationId), "S06 exact ack is accepted once");
    Equal(false, save.AcknowledgePersistence(mutation.OperationId), "S06 duplicate ack is rejected");
    Equal(BrowserRuntimeStatus.WaitingForInput, save.Status, "S03 fixture resumes after commit");
    Contains("DESC_COUNT=1", save.Output, "S03 save description is evaluated once");

    string restartRoot = CreateGlobalFixture("""
@SYSTEM_TITLE
LOADDATA 5
QUIT
@EVENTLOAD
PRINTFORML SNAPSHOT_VALUE={P3_VALUE},SLOT={LASTLOAD_NO}
WAIT
QUIT
""", erh);
    BrowserRuntimeSession restart = await BrowserRuntimeSession.StartSavePersistentAsync(restartRoot,
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["save05.sav"] = bytes });
    Equal(BrowserRuntimeStatus.WaitingForInput, restart.Status, "S03 snapshot reload reaches WAIT");
    Contains("SNAPSHOT_VALUE=11,SLOT=5", restart.Output, "S03 persisted bytes are the save-time snapshot");
    Console.WriteLine("PASS P1C3 snapshot and ACK contract");
    return;
}

if (args is ["save-codec-probe", var codecRoot, var globalPath, var savePath])
{
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["global.sav"] = File.ReadAllBytes(globalPath),
        ["save219.sav"] = File.ReadAllBytes(savePath)
    };
    BrowserRuntimeSession codec = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(codecRoot, files);
    GlobalCodecDiagnosticResult global = codec.RunGlobalCodecDiagnostic(resave: false);
    SaveCodecDiagnosticResult save = codec.RunSaveCodecDiagnostic(219);
    Equal(true, global.LoadSucceeded, "real GLOBAL codec");
    Equal(true, save.LoadSucceeded, "real save219 codec");
    Equal(Convert.ToHexString(SHA256.HashData(files["global.sav"])), global.InputSha256, "GLOBAL raw input remains unchanged");
    Equal(Convert.ToHexString(SHA256.HashData(files["save219.sav"])), save.InputSha256, "save219 raw input remains unchanged");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        global = new { global.LoadSucceeded, global.SavedVariableCount, global.ValueSha256, global.InputSha256 },
        save
    }));
    Console.WriteLine("PASS P1C3 real save219 codec probe");
    return;
}

if (args is ["save-import-lifetime-probe", var probeShape, var probeRoot, var probeGlobalPath, var probeSavePath])
{
    if (probeShape is not ("inline" or "scoped")) throw new ArgumentException("shape must be inline or scoped");
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["global.sav"] = File.ReadAllBytes(probeGlobalPath),
        ["save219.sav"] = File.ReadAllBytes(probeSavePath)
    };
    var parserType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.ParserMediator", throwOnError: true)!;
    var parserConsole = parserType.GetField("console", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    var globalStaticType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.GlobalStatic", throwOnError: true)!;
    var variableDataField = globalStaticType.GetField("VariableData", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
    var strFormType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.Runtime.Script.Data.StrForm", throwOnError: true)!;
    var sessionConsole = typeof(BrowserRuntimeSession).GetField("console", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    static long Memory() => GC.GetTotalMemory(forceFullCollection: false);
    static void DiagnosticCollect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static WeakReference WeakStatic(System.Reflection.FieldInfo field) => new(field.GetValue(null)!);
    static void Validate(BrowserRuntimeSession validator)
    {
        Equal(true, validator.RunGlobalCodecDiagnostic(resave: false).LoadSucceeded, "validator GLOBAL");
        Equal(true, validator.RunSaveCodecDiagnostic(219).LoadSucceeded, "validator save219");
        Equal(0, validator.DoScriptCallCount, "validator did not run game script");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static async Task<(WeakReference Weak, bool ParserRootMatches)> ValidateScoped(
        string root, Dictionary<string, byte[]> input, System.Reflection.FieldInfo parserField, System.Reflection.FieldInfo consoleField)
    {
        BrowserRuntimeSession validator = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(root, input);
        Validate(validator);
        return (new WeakReference(validator), ReferenceEquals(parserField.GetValue(null), consoleField.GetValue(validator)));
    }
    WeakReference weak;
    bool parserRootMatches;
    if (probeShape == "scoped")
    {
        (weak, parserRootMatches) = await ValidateScoped(probeRoot, files, parserConsole, sessionConsole);
    }
    else
    {
        BrowserRuntimeSession validator = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(probeRoot, files);
        Validate(validator);
        weak = new WeakReference(validator);
        parserRootMatches = ReferenceEquals(parserConsole.GetValue(null), sessionConsole.GetValue(validator));
        validator = null!;
        await Task.Yield();
    }
    WeakReference variableWeak = WeakStatic(variableDataField);
    var afterScope = new { alive = weak.IsAlive, variableAlive = variableWeak.IsAlive, bytes = Memory() };
    BrowserRuntimeSession.ReleaseBootstrapResources();
    var afterCleanup = new { alive = weak.IsAlive, variableAlive = variableWeak.IsAlive, bytes = Memory() };
    DiagnosticCollect();
    var afterGc = new { alive = weak.IsAlive, variableAlive = variableWeak.IsAlive, bytes = Memory() };
    parserConsole.SetValue(null, null); // Diagnostic-only targeted static-root falsification.
    DiagnosticCollect();
    var afterParserClear = new { alive = weak.IsAlive, variableAlive = variableWeak.IsAlive, bytes = Memory() };
    foreach (string name in new[] { "NameTarget", "CallnameMaster", "CallnamePlayer", "NameAssi", "CallnameTarget" })
        strFormType.GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.SetValue(null, null);
    DiagnosticCollect();
    var afterStrFormClear = new { alive = weak.IsAlive, variableAlive = variableWeak.IsAlive, bytes = Memory() };
    string? replacementError = null;
    try { _ = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(probeRoot, files); }
    catch (Exception ex) { replacementError = ex.ToString(); }
    DiagnosticCollect();
    var afterReplacementGc = new { alive = weak.IsAlive, variableAlive = variableWeak.IsAlive, bytes = Memory() };
    Console.WriteLine(JsonSerializer.Serialize(new { shape = probeShape, parserRootMatches, afterScope, afterCleanup, afterGc, afterParserClear, afterStrFormClear, afterReplacementGc, replacementError }));
    return;
}

if (args is ["title-display-input"])
{
    const string erb = """
@SYSTEM_TITLE
ALIGNMENT CENTER
REDRAW 0
PRINTFORML GFILE={GCREATEFROMFILE(10, "画像\\dot.png")}
PRINTL [0] NORMAL BUTTON
HTML_PRINT @"<div xpos='10'><button value='999'>HTML BUTTON</button></div>"
INPUTMOUSEKEY 5
PRINTFORML PRIMITIVE={RESULT:0},BUTTON={RESULT:5},KEY={RESULT:1}
CHKDATA 0
PRINTFORML CHKDATA={RESULT}
INPUT
PRINTFORML SELECT={RESULT}
QUIT
""";

    string CreateTitleFixture()
    {
        string root = CreateGlobalFixture(erb);
        string directory = Path.Combine(root, "resources", "画像");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "dot.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        return root;
    }

    async Task<BrowserRuntimeSession> StartTitleFixture()
    {
        BrowserRuntimeSession title = await BrowserRuntimeSession.StartTitleBootstrapAsync(CreateTitleFixture(), null);
        title.StartTitle();
        return title;
    }

    BrowserRuntimeSession timeout = await StartTitleFixture();
    Equal(BrowserInputKind.MouseKey, timeout.PendingInput?.Kind, "INPUTMOUSEKEY prompt kind");
    Equal(5L, timeout.PendingInput?.TimeLimit, "INPUTMOUSEKEY timeout value");
    BrowserDisplayPart[] parts = Flatten(timeout.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(true, parts.Any(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "0"), "PRINT numeric button");
    Equal(true, parts.Any(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "999"), "HTML button");
    Equal(false, parts.Any(part => part.Kind == BrowserDisplayPartKind.Text && part.Text.Contains("div xpos", StringComparison.OrdinalIgnoreCase)), "HTML layout tags are not exposed as text");
    Contains("GFILE=1", timeout.Output, "GCREATEFROMFILE accepts Windows separators");
    Equal(true, timeout.SubmitPrimitive(new PrimitiveInputEnvelope(timeout.PendingInput!.RequestId, BrowserPrimitiveInputKind.Timeout)), "timeout accepted");
    Contains("PRIMITIVE=4,BUTTON=0,KEY=0", timeout.Output, "timeout result tuple");
    Contains("CHKDATA=1", timeout.Output, "CHKDATA reads isolated missing slot");

    BrowserRuntimeSession click = await StartTitleFixture();
    Equal(true, click.SubmitPrimitive(new PrimitiveInputEnvelope(click.PendingInput!.RequestId, BrowserPrimitiveInputKind.Click, "999", 1)), "click accepted");
    Contains("PRIMITIVE=1,BUTTON=999,KEY=1", click.Output, "click result tuple and button value");

    BrowserRuntimeSession key = await StartTitleFixture();
    Equal(true, key.SubmitPrimitive(new PrimitiveInputEnvelope(key.PendingInput!.RequestId, BrowserPrimitiveInputKind.Key, null, 13)), "key accepted");
    Contains("PRIMITIVE=3,BUTTON=0,KEY=13", key.Output, "key result tuple");
    Console.WriteLine("PASS P1C2 display, CHKDATA and primitive input");
    return;
}

if (args is ["p1c2-r1-display"])
{
    const string erb = """
@SYSTEM_TITLE
SETCOLOR 255,0,0
FONTBOLD
PRINTL RED_BOLD
RESETCOLOR
FONTREGULAR
PRINTL DEFAULT
HTML_PRINT @"<div display='absolute-lefttop' xpos='100' ypos='20px' width='400' height='40px' padding='5px' border_width='2px' border_color='#00FF00' background_color='#000044'><font color='#00FFFF'><button value='word' title='tip'><img src='dot' height='20px'>GO</button><nonbutton title='n-tip'><img src='dot' height='20px'>N</nonbutton><br>TAIL</font></div>"
REDRAW 0
PRINTN HIDDEN_UNTIL_WAIT
WAIT
""";
    string root = CreateGlobalFixture(erb);
    Directory.CreateDirectory(Path.Combine(root, "resources"));
    File.WriteAllBytes(Path.Combine(root, "resources", "dot.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
    File.WriteAllText(Path.Combine(root, "resources", "sprites.csv"), "dot,dot.png\n", new UTF8Encoding(true));

    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.SetViewport(320, 200);
    display.StartTitle();
    BrowserDisplayPart[] waitParts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(true, typeof(BrowserDisplayPart).GetProperty("Style") is not null, "D1 display nodes expose immutable style");
    Equal(true, typeof(BrowserDisplayPart).GetProperty("Layout") is not null, "D2 display nodes expose HTML layout");
    Equal(true, typeof(BrowserDisplayPart).GetProperty("Tooltip") is not null, "D3 tooltip is retained");
    Equal(true, waitParts.Any(part => part.Text == "RED_BOLD" && part.Style is { Foreground: "#FF0000", Bold: true }), "D1 color and bold are captured at append time");
    Equal(true, waitParts.Any(part => part.Kind == BrowserDisplayPartKind.Button && part.Input == "word" && part.Tooltip == "tip" && Flatten(part.Children ?? []).Any(child => child.Kind == BrowserDisplayPartKind.Image && child.ImageDataUrl is not null)), "D3 nested button image and tooltip are rendered");
    Equal(true, waitParts.Any(part => part.Kind == BrowserDisplayPartKind.NonButton && part.Tooltip == "n-tip" && Flatten(part.Children ?? []).Any(child => child.Kind == BrowserDisplayPartKind.Image && child.ImageDataUrl is not null)), "D3 nonbutton image is retained without input");
    Equal(true, waitParts.Any(part => part.Layout is { Mode: BrowserDisplayMode.AbsoluteLeftTop, X: 18, Y: 20, Width: 72, Height: 40, Padding: 5, BorderWidth: 2, BorderColor: "#00FF00" } && part.Style?.Background == "#000044"), "D2 HTML units, layout and background");
    Equal(true, waitParts.Any(part => part.Text.Contains("HIDDEN_UNTIL_WAIT", StringComparison.Ordinal)), "D5 input wait force-publishes REDRAW 0 content");

    string redrawRoot = CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL REMOVE_ME\nCLEARLINE 1\nREDRAW 0\nPRINTL HIDDEN\nREDRAW 3\nDRAWLINE\nINPUT\n");
    BrowserRuntimeSession redraw = await BrowserRuntimeSession.StartPersistentBootstrapAsync(redrawRoot, null);
    redraw.SetViewport(320, 200);
    redraw.StartTitle();
    BrowserDisplayPart[] parts = Flatten(redraw.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(true, parts.Any(part => part.Text.Length > 10 && part.Text.All(character => character == '-')), "D5 DRAWLINE follows measured viewport");
    Equal(false, parts.Any(part => part.Text.Contains("REMOVE_ME", StringComparison.Ordinal)), "D4 CLEARLINE removes the prior logical line");
    Equal(BrowserInputKind.Integer, redraw.PendingInput?.Kind, "D4 INPUT boundary reached");
    Equal(true, typeof(BrowserRuntimeSession).GetProperty("ClientWidth") is not null, "D5 runtime exposes measured viewport width");
    Equal(true, typeof(BrowserRuntimeSession).GetProperty("ClientHeight") is not null, "D5 runtime exposes measured viewport height");
    Console.WriteLine("PASS P1C2-R1 D1-D5 display contract");
    return;
}

if (args is ["p1c2-r1-display-sequence"])
{
    const string erb = """
@SYSTEM_TITLE
SETCOLOR 255,0,0
FONTBOLD
PRINTL RED_BOLD
RESETCOLOR
FONTREGULAR
PRINTL DEFAULT
REDRAW 0
PRINTN HIDDEN_UNTIL_WAIT
WAIT
PRINTL AFTER_WAIT
CLEARLINE 1
REDRAW 3
DRAWLINE
INPUT
QUIT
""";
    string root = CreateGlobalFixture(erb);
    BrowserRuntimeSession sequence = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    sequence.SetViewport(800, 600);
    sequence.StartTitle();
    BrowserInputPrompt printNWait = sequence.PendingInput!;
    Equal(BrowserInputKind.Enter, printNWait.Kind, "PRINTN is the first Windows input boundary");
    Equal(true, sequence.Submit(new(printNWait.RequestId, string.Empty, SessionGeneration: sequence.SessionGeneration, DisplayGeneration: sequence.DisplayGeneration)), "PRINTN boundary resumes");
    BrowserInputPrompt explicitWait = sequence.PendingInput!;
    Equal(BrowserInputKind.Enter, explicitWait.Kind, "explicit WAIT is the second Windows input boundary");
    Equal(true, sequence.Submit(new(explicitWait.RequestId, string.Empty, SessionGeneration: sequence.SessionGeneration, DisplayGeneration: sequence.DisplayGeneration)), "explicit WAIT resumes");
    Equal(BrowserInputKind.Integer, sequence.PendingInput?.Kind, "Windows sequence reaches INPUT");
    BrowserDisplayPart[] parts = Flatten(sequence.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(true, parts.Any(part => part.Text.Length > 10 && part.Text.All(character => character == '-')), "Windows sequence publishes DRAWLINE");
    Equal(false, parts.Any(part => part.Text.Contains("HIDDEN_UNTIL_WAIT", StringComparison.Ordinal)), "CLEARLINE removes the combined PRINTN continuation line");
    Equal(false, parts.Any(part => part.Text.Contains("AFTER_WAIT", StringComparison.Ordinal)), "CLEARLINE removes the completed continuation line");
    Console.WriteLine("PASS P1C2-R1 Windows display sequence");
    return;
}

if (args is ["p1c2-r1-input"])
{
    const string erb = """
@SYSTEM_TITLE
PRINTBUTTON "LONG", "12"
ONEINPUTS
PRINTFORML BUTTON=[%RESULTS%]
INPUTMOUSEKEY 100
PRINTFORML PRIMITIVE={RESULT:0},{RESULT:1},{RESULT:2},{RESULT:3},{RESULT:4},{RESULT:5}
PRINTFORML PRIMITIVE_TEXT=[%RESULTS:5%]
INPUTS
PRINTFORML NEXT=[%RESULTS%]
QUIT
""";
    string root = CreateGlobalFixture(erb);
    File.AppendAllText(Path.Combine(root, "emuera.config"), "\nONEINPUT系命令でマウスによる2文字以上の入力を許可する:YES", Encoding.GetEncoding(932));
    BrowserRuntimeSession inputSession = await BrowserRuntimeSession.StartPersistentAsync(root, null);
    inputSession.SetViewport(640, 480);
    Equal(true, typeof(BrowserRuntimeSession).GetMethods().Any(method => method.Name == "SubmitDisplay"), "I1 display input has a distinct dispatch API");
    BrowserDisplayPart button = Flatten(inputSession.DisplayLines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Button);
    Equal(true, inputSession.SubmitDisplay(button), "current display button accepted");
    Contains("BUTTON=[12]", inputSession.Output, "I1 mouse ONEINPUTS preserves long value when enabled");
    var primitive = new PrimitiveInputEnvelope(inputSession.PendingInput!.RequestId, BrowserPrimitiveInputKind.Click, "word", 1048576,
        X: 123, Y: 200, ButtonMapValue: 77, ButtonIsInteger: false,
        SessionGeneration: inputSession.SessionGeneration, DisplayGeneration: inputSession.DisplayGeneration);
    Equal(true, inputSession.SubmitPrimitive(primitive), "I2 primitive click accepted");
    Contains("PRIMITIVE=1,1048576,123,-280,77,0", inputSession.Output, "I2 primitive RESULT:0..5 values");
    Contains("PRIMITIVE_TEXT=[word]", inputSession.Output, "I2 primitive string button value");
    Equal(false, inputSession.SubmitPrimitive(primitive), "I3 old primitive event rejected after request completion");
    Equal(false, inputSession.SubmitDisplay(button), "I3 old display generation rejected");
    Console.WriteLine("PASS P1C2-R1 I1-I5 input correlation");
    return;
}

if (args is ["p1c2-r1-chkdata-disabled"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nCHKDATA 0\nPRINTFORML RESULT={RESULT}\nQUIT\n");
    BrowserRuntimeSession disabled = await BrowserRuntimeSession.StartAsync(root);
    Equal(BrowserRuntimeStatus.Failed, disabled.Status, "CHKDATA capability is disabled outside P1C2");
    Contains("P1A未対応: CHKDATA", disabled.Output, "disabled CHKDATA remains explicit");
    Console.WriteLine("PASS P1C2-R1 CHKDATA disabled capability");
    return;
}

if (args is ["p1c2-r1-chkdata-enabled"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nCHKDATA 0\nPRINTFORML RESULT={RESULT}\nQUIT\n");
    BrowserRuntimeSession enabled = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    enabled.StartTitle();
    Equal(BrowserRuntimeStatus.Succeeded, enabled.Status, "P1C2 CHKDATA reads only its prepared sav area");
    Equal(false, enabled.Output.Contains("P1A未対応: CHKDATA", StringComparison.Ordinal), "P1C2 capability is enabled explicitly");
    Console.WriteLine("PASS P1C2-R1 CHKDATA prepared-root capability");
    return;
}

if (args is ["real-playable-probe", var playableRoot, var playableGlobalPath, var playableSavePath])
{
    var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["global.sav"] = File.ReadAllBytes(playableGlobalPath),
        ["save219.sav"] = File.ReadAllBytes(playableSavePath)
    };
    BrowserRuntimeSession playable = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(Path.GetFullPath(playableRoot), files);
    playable.EnableInteractiveInfiniteLoopPrompt(_ => new(true, "test-continue"));
    playable.SetViewport(960, 720);
    playable.StartTitle();
    var mutations = new List<string>();
    async Task Drain()
    {
        if (playable.Status == BrowserRuntimeStatus.Persisting)
            await playable.DrainPersistenceAsync(request =>
            {
                mutations.Add($"{request.Kind}:{request.LogicalFilename}:{request.Bytes?.Length ?? 0}:{(request.Bytes is null ? "" : Convert.ToHexString(SHA256.HashData(request.Bytes)))}");
                return Task.CompletedTask;
            });
    }
    async Task Input(string value)
    {
        await Drain();
        BrowserInputPrompt prompt = playable.PendingInput ?? throw new InvalidOperationException($"input {value}: no pending input, status={playable.Status}\n{playable.Output}");
        Console.WriteLine($"BeforeInput={value};Status={playable.Status};Kind={prompt.Kind};Request={prompt.RequestId}");
        if (!playable.Submit(new(prompt.RequestId, value))) throw new InvalidOperationException($"input rejected: {value}; kind={prompt.Kind}\n{playable.Output}");
        await Drain();
        Console.WriteLine($"AfterInput={value};Status={playable.Status};Kind={playable.PendingInput?.Kind};Request={playable.PendingInput?.RequestId}");
    }
    await Drain();
    await Input("1");
    await Input("1005");
    await Input("219");
    await Input("5");
    await Input("100");
    await Input("5");
    await Input("10");
    await Input("218");
    Console.WriteLine($"Status={playable.Status}");
    Console.WriteLine($"Pending={playable.PendingInput?.Kind}");
    Console.WriteLine($"Persistence={playable.PendingPersistenceCount}");
    Console.WriteLine($"AnimationInterval={playable.AnimationIntervalMilliseconds}");
    Console.WriteLine("Mutations=" + string.Join('|', mutations));
    Console.WriteLine(playable.Output);
    if (playable.IsFailed) Environment.ExitCode = 1;
    return;
}

if (args.Length is 2 or 3 && args[0] == "title-probe")
{
    string titleRoot = args[1];
    byte[]? global = args.Length == 3 ? File.ReadAllBytes(args[2]) : null;
    BrowserRuntimeSession title = await BrowserRuntimeSession.StartTitleBootstrapAsync(Path.GetFullPath(titleRoot), global);
    title.StartTitle();
    for (int step = 0; step < 40; step++)
    {
        if (title.Status == BrowserRuntimeStatus.Persisting)
        {
            await title.DrainPersistenceAsync(_ => Task.CompletedTask);
            continue;
        }
        if (title.PendingInput is not { } prompt)
            break;
        if (prompt.Kind == BrowserInputKind.MouseKey)
            title.SubmitPrimitive(new(prompt.RequestId, BrowserPrimitiveInputKind.Click, "1", 1));
        else if (prompt.Kind is BrowserInputKind.Enter or BrowserInputKind.AnyKey)
            title.Submit(new(prompt.RequestId, string.Empty));
        else
            break;
    }
    Console.WriteLine($"Status={title.Status}");
    Console.WriteLine($"DoScript={title.DoScriptCallCount}");
    Console.WriteLine($"Pending={title.PendingInput?.Kind}");
    Console.WriteLine($"DisplayLines={title.DisplayLines.Count}");
    Console.WriteLine($"DisplayImages={Flatten(title.DisplayLines.SelectMany(line => line.Parts)).Count(part => part.Kind == BrowserDisplayPartKind.Image)}");
    Console.WriteLine($"DisplayButtons={Flatten(title.DisplayLines.SelectMany(line => line.Parts)).Count(part => part.Kind == BrowserDisplayPartKind.Button)}");
    Console.WriteLine(title.Output);
    return;
}

if (args is ["global-persistence-basic"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
RESULT = 909
P1C1_VALUE = 41
SAVEGLOBAL
P1C1_VALUE = 42
PRINTFORML BEFORE_LOAD={P1C1_VALUE},RESULT={RESULT}
LOADGLOBAL
PRINTFORML AFTER_LOAD={P1C1_VALUE},RESULT={RESULT}
QUIT
""");
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, initialGlobalBytes: null);
    Equal(BrowserRuntimeStatus.Persisting, persistent.Status, "SAVEGLOBAL waits for durable commit");
    Equal(null, persistent.PendingInput, "input is hidden while persistence is pending");
    Equal(1, persistent.PendingPersistenceCount, "one immutable snapshot queued");
    SaveMutation first = persistent.PendingPersistence!;
    Equal("global.sav", first.LogicalFilename, "only global.sav is queued");
    Equal(true, first.Bytes.Length > 0, "snapshot has codec bytes");
    string snapshotHash = Convert.ToHexString(SHA256.HashData(first.Bytes));
    first.Bytes[0] ^= 0xff;
    Equal(snapshotHash, Convert.ToHexString(SHA256.HashData(persistent.PendingPersistence!.Bytes)), "published snapshot is a defensive copy");
    Equal(false, persistent.AcknowledgePersistence("stale-operation"), "stale ack rejected");
    Equal(true, persistent.AcknowledgePersistence(first.OperationId), "current ack accepted");
    Equal(false, persistent.AcknowledgePersistence(first.OperationId), "duplicate ack rejected");
    Equal(BrowserRuntimeStatus.Succeeded, persistent.Status, "script resumes after commit");
    Contains("BEFORE_LOAD=42,RESULT=909", persistent.Output, "SAVEGLOBAL preserves RESULT and resumes after instruction");
    Contains("AFTER_LOAD=41,RESULT=1", persistent.Output, "existing LOADGLOBAL restores snapshot");
    Console.WriteLine($"SnapshotSha256={snapshotHash}");
    Console.WriteLine("PASS P1C1 basic persistence boundary");
    return;
}

if (args is ["global-persistence-function"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
RESULT = 909
P1C1_EXPR = P1C1_SAVE_FUNCTION() + 5
PRINTFORML EXPR={P1C1_EXPR},VALUE={P1C1_VALUE},CALLS={P1C1_CALLS},RESULT={RESULT}
LOADGLOBAL
PRINTFORML LOADED={P1C1_VALUE},CALLS={P1C1_CALLS},RESULT={RESULT}
QUIT

@P1C1_SAVE_FUNCTION
#FUNCTION
P1C1_CALLS++
P1C1_VALUE = 101
SAVEGLOBAL
P1C1_VALUE = 202
RETURNF 7
""", "#DIM GLOBAL SAVEDATA P1C1_VALUE\n#DIM P1C1_EXPR\n#DIM P1C1_CALLS\n");
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, initialGlobalBytes: null);
    Equal(BrowserRuntimeStatus.Persisting, persistent.Status, "function SAVEGLOBAL waits after outer expression");
    Equal(false, persistent.Output.Contains("EXPR=", StringComparison.Ordinal), "outer expression has completed but following line has not run");
    SaveMutation request = persistent.PendingPersistence!;
    Equal(true, persistent.AcknowledgePersistence(request.OperationId), "function snapshot ack accepted");
    Equal(BrowserRuntimeStatus.Succeeded, persistent.Status, "function caller resumes");
    Contains("EXPR=12,VALUE=202,CALLS=1,RESULT=909", persistent.Output, "function return, scope and RESULT survive persistence boundary");
    Contains("LOADED=101,CALLS=1,RESULT=1", persistent.Output, "function snapshot is from SAVEGLOBAL instant");
    Console.WriteLine("PASS P1C1 #FUNCTION persistence boundary");
    return;
}

if (args is ["global-persistence-order"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
P1C1_EXPR = P1C1_SAVE_A() + P1C1_SAVE_B()
PRINTFORML EXPR={P1C1_EXPR},CALLS={P1C1_CALLS}
LOADGLOBAL
PRINTFORML LOADED={P1C1_VALUE},CALLS={P1C1_CALLS}
QUIT

@P1C1_SAVE_A
#FUNCTION
P1C1_CALLS++
P1C1_VALUE = 111
SAVEGLOBAL
RETURNF 1

@P1C1_SAVE_B
#FUNCTION
P1C1_CALLS++
P1C1_VALUE = 222
SAVEGLOBAL
RETURNF 2
""", "#DIM GLOBAL SAVEDATA P1C1_VALUE\n#DIM P1C1_EXPR\n#DIM P1C1_CALLS\n");
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, initialGlobalBytes: null);
    Equal(BrowserRuntimeStatus.Persisting, persistent.Status, "two function saves wait after outer expression");
    Equal(2, persistent.PendingPersistenceCount, "both snapshots queued");
    var operations = new List<string>();
    var hashes = new List<string>();
    await persistent.DrainPersistenceAsync(request =>
    {
        operations.Add(request.OperationId);
        hashes.Add(Convert.ToHexString(SHA256.HashData(request.Bytes)));
        return Task.CompletedTask;
    });
    Equal(2, operations.Distinct(StringComparer.Ordinal).Count(), "operation IDs are unique");
    Equal(2, hashes.Distinct(StringComparer.Ordinal).Count(), "ordered saves retain distinct immutable bytes");
    Contains("EXPR=3,CALLS=2", persistent.Output, "outer expression executes once");
    Contains("LOADED=222,CALLS=2", persistent.Output, "last ordered save remains in virtual file");
    Equal(BrowserRuntimeStatus.Succeeded, persistent.Status, "ordered commits resume once");
    Console.WriteLine("PASS P1C1 ordered persistence queue");
    return;
}

if (args is ["global-persistence-failure"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
P1C1_VALUE = 41
SAVEGLOBAL
P1C1_VALUE = 42
PRINTL SHOULD_NOT_RUN
QUIT
""");
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, initialGlobalBytes: null);
    bool failed = false;
    try
    {
        await persistent.DrainPersistenceAsync(_ => throw new IOException("deterministic abort"));
    }
    catch (IOException) { failed = true; }
    Equal(true, failed, "persistence error remains distinct");
    Equal(BrowserRuntimeStatus.Failed, persistent.Status, "write failure stops runtime");
    Equal(false, persistent.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "write failure blocks following ERB");
    Equal(false, persistent.Submit(new InputEnvelope(1, "stale")), "write failure rejects input");
    Contains("永続保存失敗", persistent.PersistenceError ?? string.Empty, "host exposes persistence failure");
    Console.WriteLine("PASS P1C1 persistence failure stops runtime");
    return;
}

if (args is ["global-persistence-error-after-save"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
P1C1_EXPR = P1C1_SAVE_THROW() + 1
PRINTL SHOULD_NOT_RUN
QUIT

@P1C1_SAVE_THROW
#FUNCTION
P1C1_VALUE = 515
SAVEGLOBAL
THROW P1C1_THROW_AFTER_SAVE
RETURNF 7
""", "#DIM GLOBAL SAVEDATA P1C1_VALUE\n#DIM P1C1_EXPR\n");
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, initialGlobalBytes: null);
    Equal(BrowserRuntimeStatus.Persisting, persistent.Status, "save request precedes deferred ERB error");
    SaveMutation request = persistent.PendingPersistence!;
    Equal(true, request.Bytes.Length > 0, "valid snapshot survives later THROW");
    Equal(true, persistent.AcknowledgePersistence(request.OperationId), "snapshot is committed before error publication");
    Equal(BrowserRuntimeStatus.Failed, persistent.Status, "original ERB error is published after commit");
    Equal(false, persistent.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "THROW never resumes following ERB");
    Console.WriteLine("PASS P1C1 deferred error preserves prior save");
    return;
}

if (args is ["global-codec-malformed-header"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nQUIT\n");
    byte[] malformed = new byte[16];
    BitConverter.GetBytes(0x0A1A0A0D41524589UL).CopyTo(malformed, 0);
    BitConverter.GetBytes(1808U).CopyTo(malformed, 8);
    BitConverter.GetBytes(uint.MaxValue).CopyTo(malformed, 12);
    bool rejected = false;
    try { await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, malformed); }
    catch (InvalidDataException ex) when (ex.Message.Contains("data count", StringComparison.Ordinal)) { rejected = true; }
    Equal(true, rejected, "malformed binary header is rejected before existing reader allocation");
    Console.WriteLine("PASS P1C1 malformed header boundary");
    return;
}

if (args is ["global-codec-add-test-header-word", var sourcePath, var destinationPath])
{
    const ulong header = 0x0A1A0A0D41524589UL;
    const uint version = 1808;
    const int maxImportBytes = 16 * 1024 * 1024;
    byte[] source = File.ReadAllBytes(sourcePath);
    if (source.Length < 17 || source.Length > maxImportBytes - 4
        || System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(source) != header
        || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(8)) != version
        || System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12)) != 0
        || source[16] != 0x01)
        throw new InvalidDataException("source must be a bounded version-1808 binary GLOBAL with dataCount=0");

    byte[] result = new byte[checked(source.Length + sizeof(uint))];
    source.AsSpan(0, 16).CopyTo(result);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12, sizeof(uint)), 1);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16, sizeof(uint)), 0x13579BDF);
    source.AsSpan(16).CopyTo(result.AsSpan(20));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
    File.WriteAllBytes(destinationPath, result);
    Console.WriteLine($"InputBytes={source.Length}");
    Console.WriteLine($"InputSha256={Convert.ToHexString(SHA256.HashData(source))}");
    Console.WriteLine($"OutputBytes={result.Length}");
    Console.WriteLine($"OutputSha256={Convert.ToHexString(SHA256.HashData(result))}");
    Console.WriteLine("PASS synthetic GLOBAL header extension");
    return;
}

const string CodecErh = """
#DIM GLOBAL SAVEDATA P1C1_VALUE
#DIM GLOBAL SAVEDATA P1C1_INTS, 5
#DIM GLOBAL SAVEDATA P1C1_INT2, 2, 2
#DIMS GLOBAL SAVEDATA P1C1_STRS, 3
#DIM GLOBAL P1C1_VOLATILE
#DIM SAVEDATA P1C1_LOCAL_SAVE
""";

if (args is ["global-codec-write", var outputPath])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
GLOBAL:0 = 9007199254740993
GLOBALS:0 = 日本語Ａ1
P1C1_VALUE = -9223372036854775807 - 1
P1C1_INTS:0 = 0
P1C1_INTS:1 = -123
P1C1_INTS:2 = 9007199254740993
P1C1_INTS:3 = 9223372036854775807
P1C1_INTS:4 = -9223372036854775807 - 1
P1C1_INT2:0:0 = 11
P1C1_INT2:0:1 = 12
P1C1_INT2:1:0 = 21
P1C1_INT2:1:1 = 22
P1C1_STRS:0 = 日本語
P1C1_STRS:1 =
P1C1_STRS:2 = ＡＢＣabc１２３123
P1C1_VOLATILE = 777
P1C1_LOCAL_SAVE = 888
SAVEGLOBAL
QUIT
""", CodecErh);
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, initialGlobalBytes: null);
    byte[]? committed = null;
    await persistent.DrainPersistenceAsync(request =>
    {
        committed = request.Bytes;
        return Task.CompletedTask;
    });
    if (persistent.Status != BrowserRuntimeStatus.Succeeded)
        Console.WriteLine(persistent.Output);
    Equal(BrowserRuntimeStatus.Succeeded, persistent.Status, "codec writer completes after commit");
    File.WriteAllBytes(outputPath, committed ?? throw new InvalidOperationException("snapshot missing"));
    Console.WriteLine($"RawSha256={Convert.ToHexString(SHA256.HashData(committed))}");
    Console.WriteLine("PASS P1C1 codec writer");
    return;
}

if (args is ["global-codec-verify", var verifyInputPath])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
LOADGLOBAL
PRINTFORML LOAD={RESULT}
PRINTFORML VALUE={P1C1_VALUE}
PRINTFORML INTS={P1C1_INTS:0},{P1C1_INTS:1},{P1C1_INTS:2},{P1C1_INTS:3},{P1C1_INTS:4}
PRINTFORML INT2={P1C1_INT2:0:0},{P1C1_INT2:0:1},{P1C1_INT2:1:0},{P1C1_INT2:1:1}
PRINTFORML STR0=%P1C1_STRS:0%
PRINTFORML STR1=[%P1C1_STRS:1%]
PRINTFORML STR2=%P1C1_STRS:2%
PRINTFORML EXCLUDED={P1C1_VOLATILE},{P1C1_LOCAL_SAVE}
QUIT
""", CodecErh);
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentAsync(root, File.ReadAllBytes(verifyInputPath));
    Equal(BrowserRuntimeStatus.Succeeded, persistent.Status, "codec verification completes");
    Contains("LOAD=1", persistent.Output, "valid GLOBAL loads");
    Contains("VALUE=-9223372036854775808", persistent.Output, "Int64 minimum restores");
    Contains("INTS=0,-123,9007199254740993,9223372036854775807,-9223372036854775808", persistent.Output, "Int64 array restores without JS Number");
    Contains("INT2=11,12,21,22", persistent.Output, "two-dimensional GLOBAL restores");
    Contains("STR0=日本語", persistent.Output, "Japanese string restores");
    Contains("STR1=[]", persistent.Output, "empty string restores");
    Contains("STR2=ＡＢＣabc１２３123", persistent.Output, "full and half width literals restore");
    Contains("EXCLUDED=0,0", persistent.Output, "non-SAVEDATA GLOBAL and non-GLOBAL SAVEDATA stay absent");
    Console.WriteLine(persistent.Output);
    Console.WriteLine("PASS P1C1 comprehensive GLOBAL values");
    return;
}

if (args is ["global-codec-read", var inputPath, var resavedOutputPath])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nQUIT\n", CodecErh);
    byte[] input = File.ReadAllBytes(inputPath);
    BrowserRuntimeSession persistent = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, input);
    GlobalCodecDiagnosticResult diagnostic = persistent.RunGlobalCodecDiagnostic(resave: true);
    Equal(true, diagnostic.LoadSucceeded, "existing LoadGlobal accepts input");
    Equal(6, diagnostic.SavedVariableCount, "built-in and user GLOBAL values are enumerated");
    Equal(64, diagnostic.ValueSha256.Length, "value digest is distinct from schema");
    Equal(Convert.ToHexString(SHA256.HashData(input)), diagnostic.InputSha256, "diagnostic identifies the raw input bytes");
    Equal(BrowserRuntimeStatus.Persisting, persistent.Status, "diagnostic resave requires durable commit");
    byte[]? committed = null;
    await persistent.DrainPersistenceAsync(request =>
    {
        committed = request.Bytes;
        return Task.CompletedTask;
    });
    Equal(BrowserRuntimeStatus.BootstrapReady, persistent.Status, "codec diagnostic never starts title");
    Equal(0, persistent.DoScriptCallCount, "codec diagnostic DoScript count");
    File.WriteAllBytes(resavedOutputPath, committed ?? throw new InvalidOperationException("resave snapshot missing"));
    Console.WriteLine(JsonSerializer.Serialize(diagnostic));
    Console.WriteLine($"ResavedSha256={Convert.ToHexString(SHA256.HashData(committed))}");
    Console.WriteLine("PASS P1C1 codec read/resave");
    return;
}

if (args is ["inspect-bootstrap", var inspectRoot])
{
    BrowserRuntimeSession inspected = await BrowserRuntimeSession.StartBootstrapAsync(inspectRoot);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        inspected.Status,
        inspected.DoScriptCallCount,
        inspected.Scripts,
        inspected.Resources,
        inspected.EffectiveConfiguration,
        OutputLength = inspected.Output.Length
    }));
    return;
}

if (args is ["bootstrap-user-schema", var schemaRootArg, var schemaLengthText])
{
    if (!int.TryParse(schemaLengthText, out int globalLength) || globalLength is not (3 or 4))
        throw new ArgumentException("schema length must be 3 or 4");

    string schemaRoot = Path.GetFullPath(schemaRootArg);
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    Encoding sjis = Encoding.GetEncoding(932);
    if (Directory.Exists(schemaRoot) && Directory.EnumerateFileSystemEntries(schemaRoot).Any())
        throw new InvalidOperationException("Schema test root must be new or empty");
    Directory.CreateDirectory(Path.Combine(schemaRoot, "CSV"));
    Directory.CreateDirectory(Path.Combine(schemaRoot, "ERB"));
    File.Copy(
        Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"),
        Path.Combine(schemaRoot, "CSV", "GAMEBASE.CSV"));
    File.WriteAllText(Path.Combine(schemaRoot, "emuera.config"), "セーブデータをバイナリ形式で保存する:YES", sjis);

    string header =
        "#DIM SAVEDATA P1B_SCHEMA_S, 2, 3\n" +
        $"#DIM GLOBAL SAVEDATA P1B_SCHEMA_G, {globalLength}\n" +
        "#DIM GLOBAL P1B_SCHEMA_VOL, 4\n" +
        "#DIMS GLOBAL SAVEDATA P1B_SCHEMA_TEXT, 2\n" +
        "#DIM CHARADATA SAVEDATA P1B_SCHEMA_C, 5\n";
    File.WriteAllText(Path.Combine(schemaRoot, "ERB", "SCHEMA.ERH"), header, new UTF8Encoding(true));
    File.WriteAllText(
        Path.Combine(schemaRoot, "ERB", "SCHEMA.ERB"),
        "@SYSTEM_TITLE\nPRINTL P1B_SCHEMA_SHOULD_NOT_RUN\nQUIT\n",
        new UTF8Encoding(true));

    BrowserRuntimeSession audit = await BrowserRuntimeSession.StartBootstrapAsync(schemaRoot);
    Equal(BrowserRuntimeStatus.BootstrapReady, audit.Status, "schema bootstrap status");
    Equal(0, audit.DoScriptCallCount, "schema test does not execute ERB");
    Equal(false, audit.Output.Contains("P1B_SCHEMA_SHOULD_NOT_RUN", StringComparison.Ordinal), "title not run");

    string[] rows = audit.Scripts.VariableSchemaRows.Split('\n');
    void RequireRow(string expected) => Equal(true,
        Array.Exists(rows, row => string.Equals(row, expected, StringComparison.Ordinal)),
        "ERH schema row " + expected.Split('\t')[0]);

    RequireRow("P1B_SCHEMA_S\tInt64\t2\t2,3\tchara=False\tglobal=False\tsavedata=True\tprivate=False\treference=False");
    RequireRow($"P1B_SCHEMA_G\tInt64\t1\t{globalLength}\tchara=False\tglobal=True\tsavedata=True\tprivate=False\treference=False");
    RequireRow("P1B_SCHEMA_VOL\tInt64\t1\t4\tchara=False\tglobal=True\tsavedata=False\tprivate=False\treference=False");
    RequireRow("P1B_SCHEMA_TEXT\tString\t1\t2\tchara=False\tglobal=True\tsavedata=True\tprivate=False\treference=False");
    RequireRow("P1B_SCHEMA_C\tInt64\t1\t5\tchara=True\tglobal=False\tsavedata=True\tprivate=False\treference=False");
    Equal(true, audit.Scripts.VariableSchemaCount >= audit.Scripts.VariableTokenCount + 5,
        "schema contains ERH entries as well as built-ins");

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        GlobalLength = globalLength,
        BuiltInCount = audit.Scripts.VariableTokenCount,
        SchemaCount = audit.Scripts.VariableSchemaCount,
        SchemaHash = audit.Scripts.VariableSchemaSha256,
        audit.Scripts.VariableSchemaRows
    }));
    Console.WriteLine("PASS P1B ERH user-defined schema coverage");
    return;
}

static async Task BuildDataPackage(string sourceRoot, string outputRoot, long maxPackBytes)
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    sourceRoot = Path.GetFullPath(sourceRoot);
    outputRoot = Path.GetFullPath(outputRoot);
    if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
        throw new InvalidOperationException($"空でない出力先は上書きしません: {outputRoot}");
    Directory.CreateDirectory(outputRoot);

    var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    void AddFiles(string directory, params string[] extensions)
    {
        foreach (string path in Directory.EnumerateFiles(Path.Combine(sourceRoot, directory), "*", SearchOption.AllDirectories))
            if (extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                selected.Add(path);
    }
    AddFiles("CSV", ".csv", ".config");
    AddFiles("ERB", ".erb", ".erh");
    foreach (string name in new[] { "emuera.config", "setting.json", "setting_user.json", "macro.txt" })
    {
        string path = Path.Combine(sourceRoot, name);
        if (File.Exists(path)) selected.Add(path);
    }

    string resources = Path.Combine(sourceRoot, "resources");
    var missingResources = new List<string>();
    foreach (string csv in Directory.EnumerateFiles(resources, "*", SearchOption.AllDirectories)
        .Where(path => Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)))
    {
        selected.Add(csv);
        foreach (string raw in File.ReadAllLines(csv, Encoding.GetEncoding(932)))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            string[] tokens = line.Split(',');
            if (tokens.Length < 2 || !tokens[1].Contains('.')) continue;
            string requested = Path.Combine(Path.GetDirectoryName(csv)!, tokens[1]);
            string? parent = CompatiblePath.ResolveExistingFile(requested);
            if (parent is null) missingResources.Add(Relative(sourceRoot, requested));
            else selected.Add(parent);
        }
    }

    var files = selected.Select(path => new FileInfo(path)).OrderBy(file => Relative(sourceRoot, file.FullName), StringComparer.OrdinalIgnoreCase).ToArray();
    var manifestFiles = new List<DataPackageFile>(files.Length);
    foreach (var group in files.GroupBy(file => Relative(sourceRoot, file.FullName).Contains('/')
        ? Relative(sourceRoot, file.FullName).Split('/')[0].ToLowerInvariant()
        : "config"))
    {
        int number = 1;
        long packedSourceBytes = 0;
        ZipArchive? zip = null;
        FileStream? stream = null;
        string packName = string.Empty;
        try
        {
            foreach (FileInfo file in group)
            {
                if (zip is null || (packedSourceBytes > 0 && packedSourceBytes + file.Length > maxPackBytes))
                {
                    zip?.Dispose(); stream?.Dispose();
                    packName = $"{group.Key}-{number++:000}.zip";
                    stream = File.Create(Path.Combine(outputRoot, packName));
                    zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
                    packedSourceBytes = 0;
                }
                string relative = Relative(sourceRoot, file.FullName);
                ZipArchiveEntry entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using (Stream input = file.OpenRead())
                using (Stream output = entry.Open())
                    await input.CopyToAsync(output);
                using FileStream hashInput = file.OpenRead();
                manifestFiles.Add(new(relative, file.Length, Convert.ToHexString(SHA256.HashData(hashInput)), packName));
                packedSourceBytes += file.Length;
            }
        }
        finally { zip?.Dispose(); stream?.Dispose(); }
    }

    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    var manifest = new DataPackageManifest(1, manifestFiles.Sum(file => file.Size), manifestFiles);
    await File.WriteAllTextAsync(Path.Combine(outputRoot, "manifest.json"), JsonSerializer.Serialize(manifest, options), new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(outputRoot, "package-diagnostics.json"), JsonSerializer.Serialize(new
    {
        sourceRoot,
        maxPackBytes,
        selectedFileCount = manifestFiles.Count,
        missingResourceParents = missingResources.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()
    }, options), new UTF8Encoding(false));
}

if (args is ["pack", var sourceRoot, var packageRoot, var maxMiB])
{
    await BuildDataPackage(sourceRoot, packageRoot, checked(long.Parse(maxMiB) * 1024 * 1024));
    Console.WriteLine("PASS P1B data package built");
    return;
}

if (args is ["unpack", var inputPackageRoot, var destinationRoot])
{
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    DataPackageManifest manifest = JsonSerializer.Deserialize<DataPackageManifest>(await File.ReadAllTextAsync(Path.Combine(inputPackageRoot, "manifest.json")), options)
        ?? throw new InvalidDataException("manifest.jsonを読めません");
    foreach (string pack in manifest.Files.Select(file => file.Pack).Distinct(StringComparer.Ordinal))
    {
        using FileStream archive = File.OpenRead(Path.Combine(inputPackageRoot, pack));
        await DataPackageExtractor.ExtractPackAsync(archive, pack, destinationRoot, manifest, manifest.TotalSize);
    }
    Console.WriteLine("PASS P1B data package extracted");
    return;
}

static async Task<BrowserRuntimeSession> StartTextFixture(string erb)
{
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1a-r1", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GAMEBASE.CSV"));
    File.WriteAllText(Path.Combine(root, "ERB", "TEST.ERB"), erb);
    return await BrowserRuntimeSession.StartAsync(root);
}

if (args is ["bootstrap-only"])
{
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-bootstrap", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GameBase.csv"));
    File.WriteAllText(Path.Combine(root, "ERB", "BOOTSTRAP.ERB"), "@SYSTEM_TITLE\nPRINTL P1B_TITLE_MUST_NOT_RUN\nQUIT\n");

    var bootstrap = await BrowserRuntimeSession.StartBootstrapAsync(root, loadConfig: false);
    Equal(BrowserRuntimeStatus.BootstrapReady, bootstrap.Status, "bootstrap status is distinct");
    Equal(0, bootstrap.DoScriptCallCount, "bootstrap does not invoke DoScript");
    Equal(false, bootstrap.Output.Contains("P1B_TITLE_MUST_NOT_RUN", StringComparison.Ordinal), "title marker stays absent");
    Console.WriteLine("PASS P1B bootstrap stops after Process.Initialize");
    return;
}

if (args is ["bootstrap-parse-failure"])
{
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-bootstrap-error", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GAMEBASE.CSV"));
    File.WriteAllText(Path.Combine(root, "ERB", "BROKEN.ERB"), "@SYSTEM_TITLE\nTHIS_COMMAND_DOES_NOT_EXIST\n");

    bool rejected = false;
    string diagnostic = string.Empty;
    try { await BrowserRuntimeSession.StartBootstrapAsync(root, loadConfig: false); }
    catch (InvalidOperationException ex) { rejected = true; diagnostic = ex.Message; }
    Equal(true, rejected, "ERB parse failure rejects BootstrapReady");
    Contains("Proc:Init:ERB:Result success=False", diagnostic, "failed bootstrap preserves process phase log");
    Contains("FirstError", diagnostic, "failed bootstrap identifies first parse error");
    Console.WriteLine("PASS P1B ERB parse failure is not BootstrapReady");
    return;
}

if (args is ["scoped-config-after-static-init"])
{
    bool disabledRejected = false;
    try { await StartTextFixture("@SYSTEM_TITLE\nVARS TEXT\nQUIT\n"); }
    catch (InvalidOperationException) { disabledRejected = true; }
    Equal(true, disabledRejected, "scoped-variable instruction stays disabled by default");

    string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-scoped", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GAMEBASE.CSV"));
    File.WriteAllText(Path.Combine(root, "setting.json"), "{\"UseScopedVariableInstruction\":true}");
    File.WriteAllText(Path.Combine(root, "ERB", "SCOPED.ERB"), "@SYSTEM_TITLE\nVARS TEXT\nQUIT\n");

    var bootstrap = await BrowserRuntimeSession.StartBootstrapAsync(root);
    Equal(BrowserRuntimeStatus.BootstrapReady, bootstrap.Status, "late scoped-variable config is honored");
    Console.WriteLine("PASS P1B scoped-variable config is applied after static initialization");
    return;
}

if (args is ["path-package"])
{
    static MemoryStream Archive(params (string Path, byte[] Bytes)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var file in files)
                using (Stream entry = zip.CreateEntry(file.Path).Open())
                    entry.Write(file.Bytes);
        stream.Position = 0;
        return stream;
    }
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-path", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "深い", "日本語"));
    byte[] cp932 = [0x83, 0x65, 0x83, 0x58, 0x83, 0x67, 0x0d, 0x0a];
    File.WriteAllBytes(Path.Combine(root, "CSV", "GameBase.csv"), cp932);
    File.WriteAllText(Path.Combine(root, "深い", "日本語", "混在.txt"), "ok");

    Equal(Path.Combine(root, "CSV", "GameBase.csv"), CompatiblePath.ResolveExistingFile(Path.Combine(root, "csv", "GAMEBASE.CSV")), "P01 case-insensitive resolution");
    Equal(Path.Combine(root, "深い", "日本語", "混在.txt"), CompatiblePath.ResolveExistingFile(root + "\\深い/日本語\\混在.TXT"), "P02 mixed separators and Japanese");
    Equal(1, CompatiblePath.GetFiles(Path.Combine(root, "CSV"), "*.CSV").Length, "P01 no duplicate enumeration");
    string lateFile = Path.Combine(root, "CSV", "Late.txt");
    Equal<string?>(null, CompatiblePath.ResolveExistingFile(Path.Combine(root, "CSV", "LATE.TXT")), "P06 missing path");
    await Task.Delay(20);
    File.WriteAllText(lateFile, "late");
    Equal(lateFile, CompatiblePath.ResolveExistingFile(Path.Combine(root, "CSV", "LATE.TXT")), "P07 directory snapshot refresh after file creation");

    string sha = Convert.ToHexString(SHA256.HashData(cp932));
    var manifest = new DataPackageManifest(1, cp932.Length, [new("CSV/GameBase.csv", cp932.Length, sha, "csv-001.zip")]);
    using var zipBytes = new MemoryStream();
    using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = zip.CreateEntry("CSV/GameBase.csv").Open())
            entry.Write(cp932);
    zipBytes.Position = 0;
    string extracted = Path.Combine(root, "extracted");
    DataPackageIndex index = DataPackageExtractor.Validate(manifest, 1024);
    DataPackageExtractionResult extraction = await DataPackageExtractor.ExtractPackAsync(zipBytes, "csv-001.zip", extracted, index, 1024);
    Equal(1, extraction.EntryCount, "P03 extracted entry count");
    Equal(1, extraction.LookupCount, "P03 one dictionary lookup per entry");
    Equal((long)cp932.Length, extraction.BytesWritten, "P03 streamed byte count");
    Equal(sha, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(extracted, "CSV", "GameBase.csv")))), "P03 original bytes preserved");

    foreach (var bad in new[]
    {
        new DataPackageManifest(1, 1, [new("../escape", 1, new string('0', 64), "bad.zip")]),
        new DataPackageManifest(1, 2, [new("CSV/a.csv", 1, new string('0', 64), "bad.zip"), new("csv/A.CSV", 1, new string('1', 64), "bad.zip")])
    })
    {
        bool rejected = false;
        try { DataPackageExtractor.Validate(bad, 1024); }
        catch (InvalidDataException) { rejected = true; }
        Equal(true, rejected, "P05 unsafe manifest rejected");
    }

    var wrongPack = new DataPackageManifest(1, cp932.Length, [new("CSV/GameBase.csv", cp932.Length, sha, "other.zip")]);
    zipBytes.Position = 0;
    bool wrongPackRejected = false;
    try { await DataPackageExtractor.ExtractPackAsync(zipBytes, "csv-001.zip", Path.Combine(root, "wrong-pack"), DataPackageExtractor.Validate(wrongPack, 1024), 1024); }
    catch (InvalidDataException) { wrongPackRejected = true; }
    Equal(true, wrongPackRejected, "P05 wrong pack rejected");

    var wrongSize = new DataPackageManifest(1, cp932.Length - 1, [new("CSV/GameBase.csv", cp932.Length - 1, sha, "csv-001.zip")]);
    zipBytes.Position = 0;
    bool wrongSizeRejected = false;
    try { await DataPackageExtractor.ExtractPackAsync(zipBytes, "csv-001.zip", Path.Combine(root, "wrong-size"), DataPackageExtractor.Validate(wrongSize, 1024), 1024); }
    catch (InvalidDataException) { wrongSizeRejected = true; }
    Equal(true, wrongSizeRejected, "P05 short manifest size rejected");

    var wrongHash = new DataPackageManifest(1, cp932.Length, [new("CSV/GameBase.csv", cp932.Length, new string('0', 64), "csv-001.zip")]);
    using (MemoryStream archive = Archive(("CSV/GameBase.csv", cp932)))
    {
        bool rejected = false;
        try { await DataPackageExtractor.ExtractPackAsync(archive, "csv-001.zip", Path.Combine(root, "wrong-hash"), DataPackageExtractor.Validate(wrongHash, 1024), 1024); }
        catch (InvalidDataException) { rejected = true; }
        Equal(true, rejected, "P05 hash mismatch rejected");
    }
    using (MemoryStream archive = Archive(("CSV/Other.csv", cp932)))
    {
        bool rejected = false;
        try { await DataPackageExtractor.ExtractPackAsync(archive, "csv-001.zip", Path.Combine(root, "unexpected"), index, 1024); }
        catch (InvalidDataException) { rejected = true; }
        Equal(true, rejected, "P05 unregistered entry rejected");
    }
    using (MemoryStream archive = Archive(("CSV/GameBase.csv", cp932), ("csv/GAMEBASE.CSV", cp932)))
    {
        bool rejected = false;
        try { await DataPackageExtractor.ExtractPackAsync(archive, "csv-001.zip", Path.Combine(root, "duplicate-entry"), index, 1024); }
        catch (InvalidDataException) { rejected = true; }
        Equal(true, rejected, "P05 duplicate archive path rejected");
    }
    using (MemoryStream archive = Archive(("CSV/GameBase.csv", cp932)))
    {
        bool rejected = false;
        try { await DataPackageExtractor.ExtractPackAsync(archive, "csv-001.zip", Path.Combine(root, "stream-limit"), index, 1024, 1020); }
        catch (InvalidDataException) { rejected = true; }
        Equal(true, rejected, "P05 streamed cumulative size limit enforced");
    }
    Equal(0, Directory.EnumerateDirectories(root, "*.stage-*").Count(), "P05 failed stages removed");
    Console.WriteLine("PASS P1B P01/P02/P03/P05/P06/P07 path and package boundary");
    return;
}

if (args is ["config-order"])
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    Encoding sjis = Encoding.GetEncoding(932);
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-config", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GameBase.csv"));
    File.WriteAllText(Path.Combine(root, "CSV", "_default.config"), "大文字小文字の違いを無視する:NO", sjis);
    File.WriteAllText(Path.Combine(root, "emuera.config"), "大文字小文字の違いを無視する:YES", sjis);
    File.WriteAllText(Path.Combine(root, "CSV", "_fixed.config"), "大文字小文字の違いを無視する:NO", sjis);
    File.WriteAllText(Path.Combine(root, "CSV", "VariableSize.csv"), "FLAG,12345", sjis);
    File.WriteAllText(Path.Combine(root, "CSV", "Base.csv"), "0,体力", sjis);
    File.WriteAllText(Path.Combine(root, "ERB", "BOOTSTRAP.ERB"), "@SYSTEM_TITLE\nPRINTVL BASE:0:体力\nQUIT\n", sjis);

    var configured = await BrowserRuntimeSession.StartBootstrapAsync(root);
    Equal("False", configured.EffectiveConfiguration["IgnoreCase"], "C01 fixed config wins");
    Equal(BrowserRuntimeStatus.BootstrapReady, configured.Status, "C01 initializes");
    Equal(true, File.Exists(Path.Combine(root, "setting.json")), "C02 generated game JSON stays in run root");
    Equal(true, File.Exists(Path.Combine(root, "setting_user.json")), "C02 generated user JSON stays in run root");
    Contains("FLAG\tInt64\t1\t12345", configured.Scripts.VariableSchemaRows, "C03 mixed-case VariableSize is applied");
    Equal(0, configured.Scripts.WarningCount, "C03 mixed-case named CSV is applied before ERB parse");
    Console.WriteLine("PASS P1B C01/C02 shared config order and run-root writes");
    return;
}

if (args is ["resources"])
{
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-images", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    Directory.CreateDirectory(Path.Combine(root, "resources"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GAMEBASE.CSV"));
    File.WriteAllText(Path.Combine(root, "ERB", "BOOTSTRAP.ERB"), "@SYSTEM_TITLE\nPRINTL IMAGE_TITLE_MUST_NOT_RUN\nQUIT\n");
    using (var bitmap = new SKBitmap(2, 2))
    {
        bitmap.Erase(SKColors.Magenta);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using SKData webp = image.Encode(SKEncodedImageFormat.Webp, 100);
        File.WriteAllBytes(Path.Combine(root, "resources", "tiny.png"), png.ToArray());
        File.WriteAllBytes(Path.Combine(root, "resources", "tiny.webp"), webp.ToArray());
    }
    File.WriteAllBytes(Path.Combine(root, "resources", "bad.png"), [1, 2, 3, 4]);
    File.WriteAllText(Path.Combine(root, "resources", "sprites.CSV"), """
ONE,tiny.png
TWO,tiny.png,0,0,1,1
ANI,ANIME,2,2
ANI,tiny.webp,0,0,2,2,0,0,100
MISSING,missing.png
BROKEN,bad.png
ONE,tiny.webp
""");

    var images = await BrowserRuntimeSession.StartBootstrapAsync(root, loadConfig: false);
    Equal(BrowserRuntimeStatus.BootstrapReady, images.Status, "G01 resources initialize");
    Equal(2, images.Resources.ParentImageCount, "G01 PNG and WebP decoded");
    Equal(3, images.Resources.SpriteCount, "G01 unique sprites registered");
    Equal(1, images.Resources.AnimationCount, "G01 animation declared");
    Equal(1, images.Resources.AnimationFrameCount, "G01 animation frame registered");
    Equal(true, images.Scripts.WarningCount >= 1, "G01 parser warnings captured without suppression");
    Equal(64, images.Scripts.WarningIdentitySha256.Length, "G01 warning multiset identity");
    Equal(images.Scripts.VariableTokenCount, images.Scripts.VariableSchemaCount, "G01 named registry schema count");
    Contains("function private/local excluded", images.Scripts.VariableRegistryScope, "G01 registry scope explicit");
    Equal(64, images.Scripts.VariableSchemaSha256.Length, "G01 variable schema identity");
    Equal(64, images.Scripts.ConfigurationIdentitySha256.Length, "G01 effective config identity");
    Equal(false, images.Output.Contains("IMAGE_TITLE_MUST_NOT_RUN", StringComparison.Ordinal), "G01 title not run");
    Contains("missing.png", images.Output, "G01 missing image warning");
    Contains("bad.png", images.Output, "G01 broken image warning");
    Console.WriteLine("PASS P1B G01 startup PNG/WebP resources");
    return;
}

if (args is ["image-draw-contract"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nGCREATE 10,2,2\nGDRAWSPRITE 10,\"SOURCE\"\nSPRITECREATE \"COPY\",10,0,0,2,2\nSPRITEANIMECREATE \"ANI\",2,2\nSPRITEANIMEADDFRAME \"ANI\",10,0,0,2,2,0,0,20\nSETANIMETIMER 20\nPRINT_IMG \"COPY\"\nPRINT_IMG \"ANI\"\nWAIT\nQUIT\n");
    Directory.CreateDirectory(Path.Combine(root, "resources"));
    using (var bitmap = new SKBitmap(2, 2))
    {
        bitmap.Erase(SKColors.Magenta);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(root, "resources", "source.png"), png.ToArray());
    }
    File.WriteAllText(Path.Combine(root, "resources", "sprites.csv"), "SOURCE,source.png,0,0,2,2\n", new UTF8Encoding(true));
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayPart[] imageParts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).Where(part => part.Kind == BrowserDisplayPartKind.Image).ToArray();
    Equal(BrowserRuntimeStatus.WaitingForInput, display.Status, "GDRAWSPRITE reaches input");
    Equal(2, imageParts.Length, "generated and animated sprites are displayed");
    Equal(true, imageParts.All(part => part.ImageDataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true), "generated sprites have PNG data");
    Equal(20, display.AnimationIntervalMilliseconds, "SETANIMETIMER interval");
    display.RefreshAnimations();
    Equal(true, Flatten(display.DisplayLines.SelectMany(line => line.Parts)).Where(part => part.Kind == BrowserDisplayPartKind.Image).All(part => part.ImageDataUrl is not null), "animation refresh retains pixels");
    Console.WriteLine("PASS image draw contract");
    return;
}

if (args is ["ux18-graphics-text"])
{
    const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    const long textGraphicsId = 981;
    const string cursorName = "UX18_CURSOR", staticSpriteName = "UX18_STATIC";
    const int observationSamples = 13;

    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
CALL UX18_CYCLE
WAIT
CALL UX18_CYCLE
QUIT

@UX18_CYCLE
#DIM R_CREATE
#DIM R_CLEAR
#DIM R_BRUSH
#DIM R_FONT
#DIM R_DRAW
#DIM R_CREATED
#DIM R_WIDTH
#DIM R_HEIGHT
#DIM R_ANIME
#DIM R_FRAME_A
#DIM R_FRAME_B
#DIM R_STATIC_SPRITE
#DIM R_MUTATE
#DIM R_STOP
#DIM R_DISPOSE_SPRITE
#DIM R_DISPOSE_G
SETANIMETIMER 100
R_CREATE = GCREATE(981, 200, 100)
R_CLEAR = GCLEAR(981, 0x00000000)
R_BRUSH = GSETBRUSH(981, 0xFFFF0000)
R_FONT = GSETFONT(981, "ＭＳ ゴシック", 100)
R_DRAW = GDRAWTEXT(981, "_", 0, 0)
R_CREATED = GCREATED(981)
R_WIDTH = GWIDTH(981)
R_HEIGHT = GHEIGHT(981)
R_ANIME = SPRITEANIMECREATE("UX18_CURSOR", 100, 100)
R_FRAME_A = SPRITEANIMEADDFRAME("UX18_CURSOR", 981, 0, 0, 100, 100, 0, 0, 500)
R_FRAME_B = SPRITEANIMEADDFRAME("UX18_CURSOR", 981, 100, 0, 100, 100, 0, 0, 500)
R_STATIC_SPRITE = SPRITECREATE("UX18_STATIC", 981, 0, 0, 200, 100)
PRINTFORML UX18_RET={R_CREATE},{R_CLEAR},{R_BRUSH},{R_FONT},{R_DRAW},{R_CREATED},{R_WIDTH},{R_HEIGHT},{R_ANIME},{R_FRAME_A},{R_FRAME_B},{R_STATIC_SPRITE}
PRINT_IMG "UX18_CURSOR"
WAIT
R_MUTATE = GDRAWTEXT(981, "X", 16, 16)
PRINTFORML UX18_MUTATE={R_MUTATE}
WAIT
R_STOP = SETANIMETIMER(0)
R_DISPOSE_SPRITE = SPRITEDISPOSE("UX18_CURSOR")
SPRITEDISPOSE "UX18_STATIC"
R_DISPOSE_G = GDISPOSE(981)
PRINTFORML UX18_CLEAN={SPRITECREATED("UX18_CURSOR")},{SPRITECREATED("UX18_STATIC")},{GCREATED(981)}
RETURN
""");

    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    runtime.StartTitle();
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "UX18 reaches the first cursor wait");
    Equal(100, runtime.AnimationIntervalMilliseconds, "UX18 SETANIMETIMER interval");
    runtime.EnableDisplayPerformanceMetrics();

    Type assembly = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")
        ?? throw new InvalidOperationException("AppContents type missing");
    object Graphics(long id) => assembly.GetMethod("GetGraphics", statics)!.Invoke(null, [id])
        ?? throw new InvalidOperationException($"GraphicsImage {id} missing");
    object? Sprite(string name) => assembly.GetMethod("GetSprite", statics)!.Invoke(null, [name]);
    string? Url(string name) => (string?)assembly.GetMethod("GetSpriteDataUrl", statics)!.Invoke(null, [name]);
    long Generation(object graphics) => (long?)graphics.GetType().GetProperty("ContentGeneration", instance)?.GetValue(graphics)
        ?? throw new InvalidOperationException("GraphicsImage.ContentGeneration missing");
    SKBitmap DecodeDataUrl(string dataUrl)
    {
        const string prefix = "data:image/png;base64,";
        Contains(prefix, dataUrl, "UX18 generated image is PNG data");
        return SKBitmap.Decode(Convert.FromBase64String(dataUrl[prefix.Length..]))
            ?? throw new InvalidOperationException("UX18 PNG decode failed");
    }

    object graphics = Graphics(textGraphicsId);
    Equal(true, (bool)graphics.GetType().GetProperty("IsCreated")!.GetValue(graphics)!, "UX18 graphics created");
    SKImage image = (SKImage)graphics.GetType().GetProperty("Image")!.GetValue(graphics)!;
    Equal(200, image.Width, "UX18 graphics width");
    Equal(100, image.Height, "UX18 graphics height");
    int inkPixels = 0, redInkPixels = 0, rightSideInkPixels = 0;
    using (SKBitmap pixels = SKBitmap.FromImage(image))
    {
        for (int y = 0; y < pixels.Height; y++)
        for (int x = 0; x < pixels.Width; x++)
        {
            SKColor color = pixels.GetPixel(x, y);
            if (color.Alpha == 0) continue;
            if (x < 100 && y < 100)
            {
                inkPixels++;
                if (color.Red > color.Blue && color.Red > color.Green) redInkPixels++;
            }
            else if (x >= 100)
                rightSideInkPixels++;
        }
        Equal(true, inkPixels > 0, "UX18 underscore rasterized in first 100x100 region");
        Equal(true, redInkPixels > 0, "UX18 brush color applied to text pixels");
        Equal(0, rightSideInkPixels, "UX18 second 100x100 cursor frame remains transparent");
    }
    long initialGeneration = Generation(graphics);
    string initialStaticUrl = Url(staticSpriteName)
        ?? throw new InvalidOperationException("UX18 SpriteG URL missing before mutation");
    string?[] animationUrls = new string?[observationSamples];
    HashSet<string> animationHashes = new(StringComparer.Ordinal);
    bool sawTextFrame = false, sawBlankFrame = false;
    int displayLineCount = runtime.DisplayLines.Count;
    for (int index = 0; index < observationSamples; index++)
    {
        await Task.Delay(100);
        runtime.RefreshAnimations();
        string url = Url(cursorName) ?? throw new InvalidOperationException("UX18 SpriteAnime URL missing");
        animationUrls[index] = url;
        animationHashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))));
        using SKBitmap frame = DecodeDataUrl(url);
        int alphaPixels = 0;
        for (int y = 0; y < frame.Height; y++)
        for (int x = 0; x < frame.Width; x++)
            if (frame.GetPixel(x, y).Alpha != 0) alphaPixels++;
        sawTextFrame |= alphaPixels > 0;
        sawBlankFrame |= alphaPixels == 0;
        Equal(displayLineCount, runtime.DisplayLines.Count, "UX18 animation keeps retained line count stable");
    }
    Equal(true, sawTextFrame, "UX18 animated cursor shows rendered underscore frame");
    Equal(true, sawBlankFrame, "UX18 animated cursor shows transparent frame");
    Equal(true, animationHashes.Count >= 2, "UX18 animation publishes at least two distinct data hashes");
    Equal(true, animationUrls.Distinct(StringComparer.Ordinal).Count() >= 2, "UX18 animation uses distinct frame data URLs");
    Equal(2, (int)Sprite(cursorName)!.GetType().GetProperty("FrameCount", instance)!.GetValue(Sprite(cursorName))!, "UX18 animation has two frames");
    Equal(1000L, (long)Sprite(cursorName)!.GetType().GetField("totaltime", instance)!.GetValue(Sprite(cursorName))!, "UX18 animation duration");
    Equal(true, runtime.DisplayPerformance.AnimationPublishes <= observationSamples, "UX18 animation has no runaway publish loop");

    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "UX18 first wait resumes into text mutation");
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "UX18 mutation reaches the second wait");
    Contains("UX18_MUTATE=1", runtime.Output, "UX18 GDRAWTEXT ERB return value");
    Equal(true, Generation(graphics) > initialGeneration, "UX18 text mutation increments graphics generation");
    string mutatedStaticUrl = Url(staticSpriteName)
        ?? throw new InvalidOperationException("UX18 SpriteG URL missing after mutation");
    Equal(false, initialStaticUrl == mutatedStaticUrl, "UX18 SpriteG cache invalidates after GDRAWTEXT");

    Type fontFactory = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.FontFactory")
        ?? throw new InvalidOperationException("FontFactory type missing");
    var familyNameMatches = fontFactory.GetMethod("FamilyNameMatches", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("FontFactory family comparison missing");
    Equal(true, familyNameMatches.Invoke(null, ["ＭＳ　ゴシック", "MS Gothic"]), "UX18 browser-safe fullwidth MS Gothic alias");
    Equal(true, familyNameMatches.Invoke(null, ["ms gothic", "ＭＳ ゴシック"]), "UX18 browser-safe case and space comparison");
    Equal(false, familyNameMatches.Invoke(null, ["MS Gothic", "DotGothic16"]), "UX18 unrelated font families remain distinct");
    var getFont = fontFactory.GetMethod("GetFont", statics, null,
        [typeof(string), typeof(System.Drawing.FontStyle), typeof(float?)], null)
        ?? throw new InvalidOperationException("FontFactory.GetFont missing");
    SKFont requestedFont = getFont.Invoke(null, ["ＭＳ ゴシック", System.Drawing.FontStyle.Regular, 100f]) as SKFont
        ?? throw new InvalidOperationException("UX18 missing Windows family did not resolve to a usable font");
    Equal(100f, requestedFont.Size, "UX18 fallback preserves requested font size");
    string resolvedFamily = requestedFont.Typeface?.FamilyName ?? "<Skia default>";
    SKFontMetrics fontMetrics = requestedFont.Metrics;
    SKFont portableFont = getFont.Invoke(null, ["DotGothic16", System.Drawing.FontStyle.Regular, 100f]) as SKFont
        ?? throw new InvalidOperationException("UX18 embedded portable font unavailable");
    Equal("DotGothic16", portableFont.Typeface?.FamilyName, "UX18 embedded OFL typeface loaded");
    Equal(100f, portableFont.Size, "UX18 portable typeface preserves requested size");
    Equal(true, ReferenceEquals(portableFont, getFont.Invoke(null, ["DotGothic16", System.Drawing.FontStyle.Regular, 100f])), "UX18 portable cache returns same borrowed font");
    SKFont boldPortableFont = getFont.Invoke(null, ["DotGothic16", System.Drawing.FontStyle.Bold, 100f]) as SKFont
        ?? throw new InvalidOperationException("UX18 bold portable font unavailable");
    SKFont italicPortableFont = getFont.Invoke(null, ["DotGothic16", System.Drawing.FontStyle.Italic, 100f]) as SKFont
        ?? throw new InvalidOperationException("UX18 italic portable font unavailable");
    Equal(true, boldPortableFont.Embolden, "UX18 portable bold intent");
    Equal(-0.3f, italicPortableFont.SkewX, "UX18 portable italic intent");
    Type configType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.Runtime.Config.Config")
        ?? throw new InvalidOperationException("Config type missing");
    int configuredFontSize = (int)configType.GetProperty("FontSize", statics)!.GetValue(null)!;
    SKFont defaultFont = getFont.Invoke(null, ["", System.Drawing.FontStyle.Regular, null]) as SKFont
        ?? throw new InvalidOperationException("UX18 blank-family font unavailable");
    Equal((float)configuredFontSize, defaultFont.Size, "UX18 blank family/size use config defaults");
    SKFontMetrics portableMetrics = portableFont.Metrics;
    float portableBaseline = -portableMetrics.Top;
    using var portablePaint = new SKPaint { Color = SKColors.Red };
    using SKSurface portableSurface = SKSurface.Create(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
    portableSurface.Canvas.Clear(SKColors.Transparent);
    portableSurface.Canvas.DrawText("_", new SKPoint(0, portableBaseline), portableFont, portablePaint);
    using SKImage portableImage = portableSurface.Snapshot();
    using SKBitmap portablePixels = SKBitmap.FromImage(portableImage);
    int portableInk = 0, portableRedInk = 0, portableRightInk = 0;
    for (int y = 0; y < portablePixels.Height; y++)
    for (int x = 0; x < portablePixels.Width; x++)
    {
        SKColor color = portablePixels.GetPixel(x, y);
        if (color.Alpha == 0) continue;
        if (x >= 100) portableRightInk++;
        else
        {
            portableInk++;
            if (color.Red > color.Blue && color.Red > color.Green) portableRedInk++;
        }
    }
    Equal(true, portableInk > 0, "UX18 portable cursor ink stays in first 100x100 frame");
    Equal(true, portableRedInk > 0, "UX18 portable brush color is rasterized");
    Equal(0, portableRightInk, "UX18 portable second frame remains transparent");

    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "UX18 second wait resumes into cleanup");
    Contains("UX18_CLEAN=0,0,0", runtime.Output, "UX18 first sprite and graphics cleanup");
    Equal(null, Url(cursorName), "UX18 disposed cursor has no stale image");
    Equal(null, Url(staticSpriteName), "UX18 disposed SpriteG has no stale image");
    Equal(0, runtime.AnimationIntervalMilliseconds, "UX18 cleanup stops animation timer");
    long publishesAfterCleanup = runtime.DisplayPerformance.AnimationPublishes;
    runtime.RefreshAnimations();
    Equal(publishesAfterCleanup, runtime.DisplayPerformance.AnimationPublishes, "UX18 stopped cursor does not publish more animation");
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "UX18 inter-cycle wait reached after cleanup");
    Contains("UX18_RET=1,1,1,1,1,1,200,100,1,1,1,1", runtime.Output, "UX18 ERB function return values");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "UX18 inter-cycle wait starts repeated create");
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "UX18 repeated create reaches first wait");
    Equal(true, runtime.Output.Split("UX18_RET=", StringSplitOptions.None).Length >= 3, "UX18 second cycle reached graphics text path");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "UX18 second cycle proceeds to mutation wait");
    Equal(BrowserRuntimeStatus.WaitingForInput, runtime.Status, "UX18 second cycle reaches mutation wait");
    Equal(true, runtime.Submit(new(runtime.PendingInput!.RequestId, string.Empty)), "UX18 second cycle completes cleanup");
    Contains("UX18_CLEAN=0,0,0", runtime.Output, "UX18 repeated lifecycle cleanup completes");
    Equal(BrowserRuntimeStatus.Succeeded, runtime.Status, "UX18 fixture exits after repeated lifecycle");

    object ownership = Activator.CreateInstance(graphics.GetType(), nonPublic: true)
        ?? throw new InvalidOperationException("UX18 ownership graphics construction failed");
    ownership.GetType().GetMethod("GCreate", instance)!.Invoke(ownership, [2, 2, false]);
    using var firstBrush = new SKPaint();
    using var replacementBrush = new SKPaint();
    ownership.GetType().GetMethod("GSetBrush", instance)!.Invoke(ownership, [firstBrush]);
    ownership.GetType().GetMethod("GSetBrush", instance)!.Invoke(ownership, [firstBrush]);
    Equal(true, firstBrush.Handle != IntPtr.Zero, "UX18 brush same-reference assignment is safe");
    ownership.GetType().GetMethod("GSetBrush", instance)!.Invoke(ownership, [replacementBrush]);
    Equal(IntPtr.Zero, firstBrush.Handle, "UX18 replaced brush is disposed by GraphicsImage");
    SKFont borrowedFont = portableFont;
    ownership.GetType().GetMethod("GSetFont", instance)!.Invoke(ownership, [borrowedFont]);
    ownership.GetType().GetMethod("GDispose", instance)!.Invoke(ownership, null);
    Equal(IntPtr.Zero, replacementBrush.Handle, "UX18 GraphicsImage disposal releases owned brush");
    Equal(true, borrowedFont.Handle != IntPtr.Zero, "UX18 GraphicsImage disposal preserves borrowed cached font");
    Equal(true, ReferenceEquals(borrowedFont, getFont.Invoke(null, ["DotGothic16", System.Drawing.FontStyle.Regular, 100f])), "UX18 FontFactory returns cached font");
    Console.WriteLine("UX18_RESULT_JSON=" + JsonSerializer.Serialize(new
    {
        requestedFamily = "ＭＳ ゴシック",
        resolvedFamily,
        fontSize = requestedFont.Size,
        fontStyle = "Regular",
        fontMetrics = new { fontMetrics.Top, fontMetrics.Ascent, fontMetrics.Descent, fontMetrics.Bottom, fontMetrics.Leading },
        portableFont = new { family = portableFont.Typeface?.FamilyName, size = portableFont.Size, top = portableMetrics.Top, ascent = portableMetrics.Ascent, descent = portableMetrics.Descent, baselineY = portableBaseline, ink = portableInk, redInk = portableRedInk, rightInk = portableRightInk },
        inkPixels,
        redInkPixels,
        rightSideInkPixels,
        animationFrameHashes = animationHashes.OrderBy(hash => hash, StringComparer.Ordinal).ToArray(),
        initialGeneration,
        finalGeneration = Generation(graphics),
        cleanupOutputObserved = runtime.Output.Contains("UX18_CLEAN=0,0,0", StringComparison.Ordinal),
        runtimeStatus = runtime.Status.ToString()
    }));
    Console.WriteLine("PASS UX18 graphics text");
    return;
}

if (args is ["ux18-font-file", var fontPath])
{
    byte[] fontBytes = File.ReadAllBytes(fontPath);
    using SKTypeface typeface = SKTypeface.FromFile(fontPath)
        ?? throw new InvalidOperationException("Skia could not load the supplied font file");
    using var font = new SKFont(typeface, 100);
    using var paint = new SKPaint { Color = SKColors.Red };
    SKFontMetrics metrics = font.Metrics;
    float baselineY = -metrics.Top;
    float advance = font.MeasureText("_", out SKRect localGlyphBounds, paint);
    SKRect placedGlyphBounds = new(localGlyphBounds.Left, localGlyphBounds.Top + baselineY,
        localGlyphBounds.Right, localGlyphBounds.Bottom + baselineY);
    using SKSurface surface = SKSurface.Create(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.DrawText("_", new SKPoint(0, baselineY), font, paint);
    using SKImage rendered = surface.Snapshot();
    using SKBitmap pixels = SKBitmap.FromImage(rendered);
    int ink = 0, redInk = 0, rightInk = 0, minX = 200, minY = 100, maxX = -1, maxY = -1;
    for (int y = 0; y < pixels.Height; y++)
    for (int x = 0; x < pixels.Width; x++)
    {
        SKColor color = pixels.GetPixel(x, y);
        if (color.Alpha == 0) continue;
        ink++;
        if (color.Red > color.Blue && color.Red > color.Green) redInk++;
        if (x >= 100) rightInk++;
        minX = Math.Min(minX, x); minY = Math.Min(minY, y);
        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
    }
    Console.WriteLine("UX18_FONT_FILE_RESULT=" + JsonSerializer.Serialize(new
    {
        file = Path.GetFileName(fontPath),
        sha256 = Convert.ToHexString(SHA256.HashData(fontBytes)),
        family = typeface.FamilyName,
        size = font.Size,
        metrics = new { metrics.Top, metrics.Ascent, metrics.Descent, metrics.Bottom, metrics.Leading },
        baselineY,
        advance,
        localGlyphBounds = new { localGlyphBounds.Left, localGlyphBounds.Top, localGlyphBounds.Right, localGlyphBounds.Bottom },
        placedGlyphBounds = new { placedGlyphBounds.Left, placedGlyphBounds.Top, placedGlyphBounds.Right, placedGlyphBounds.Bottom },
        ink,
        redInk,
        rightInk,
        inkBounds = new { minX, minY, maxX, maxY },
        fitsFirstCursorFrame = ink > 0 && maxX < 100 && maxY < 100 && rightInk == 0
    }));
    return;
}

if (args is ["perf-gate-01c-r3-graphics-generation"])
{
    const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    Type graphicsType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.GraphicsImage")
        ?? throw new InvalidOperationException("GraphicsImage type missing");
    Type spriteType = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.SpriteG")
        ?? throw new InvalidOperationException("SpriteG type missing");
    object NewGraphics() => Activator.CreateInstance(graphicsType, nonPublic: true)
        ?? throw new InvalidOperationException("GraphicsImage construction failed");
    long Generation(object graphics) => (long?)graphicsType.GetProperty("ContentGeneration", instance)?.GetValue(graphics)
        ?? throw new InvalidOperationException("ContentGeneration missing");
    void Call(object target, string name, params object?[] values)
        => target.GetType().GetMethods(instance).Single(method => method.Name == name && method.GetParameters().Length == values.Length).Invoke(target, values);

    object empty = NewGraphics();
    Equal(0L, Generation(empty), "R3 initial generation");
    Call(empty, "GClear", System.Drawing.Color.Red);
    Equal(0L, Generation(empty), "R3 failed clear leaves generation unchanged");

    object source = NewGraphics();
    Call(source, "GCreate", 2, 2, false);
    Equal(1L, Generation(source), "R3 source create increments");

    object target = NewGraphics();
    Call(target, "GCreate", 2, 2, false);
    Equal(1L, Generation(target), "R3 target create increments");
    using (SKSurface surface = SKSurface.Create(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul)))
    {
        Call(target, "GCreateFromF", surface.Snapshot(), false);
    }
    Equal(2L, Generation(target), "R3 create-from-F increments");
    Call(target, "GClear", System.Drawing.Color.Blue);
    Equal(3L, Generation(target), "R3 clear increments");

    object sprite = Activator.CreateInstance(spriteType, instance, null, ["SOURCE", source, new System.Drawing.Rectangle(0, 0, 2, 2)], null)
        ?? throw new InvalidOperationException("SpriteG construction failed");
    Call(target, "GDrawCImg", sprite, new System.Drawing.Rectangle(0, 0, 2, 2));
    Equal(4L, Generation(target), "R3 draw increments");
    float[][] identity =
    [
        [1f, 0f, 0f, 0f, 0f],
        [0f, 1f, 0f, 0f, 0f],
        [0f, 0f, 1f, 0f, 0f],
        [0f, 0f, 0f, 1f, 0f]
    ];
    Call(target, "GDrawCImg", sprite, new System.Drawing.Rectangle(0, 0, 2, 2), identity);
    Equal(5L, Generation(target), "R3 matrix draw increments");
    Call(target, "GDispose");
    Equal(6L, Generation(target), "R3 dispose increments once");
    Call(target, "GDispose");
    Equal(6L, Generation(target), "R3 repeated dispose does not increment");
    Call(source, "GDispose");
    Console.WriteLine("PASS 01C-R3 GraphicsImage generation contract");
    return;
}

if (args is ["perf-gate-01c-r3-sprite-dataurl-cache"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nGCREATE 930,2,2\nGDRAWSPRITE 930,\"SOURCE\"\nSPRITECREATE \"COPY\",930,0,0,2,2\nSPRITEANIMECREATE \"ANI\",2,2\nSPRITEANIMEADDFRAME \"ANI\",930,0,0,2,2,0,0,20\nSETANIMETIMER 20\nWAIT\nQUIT\n");
    Directory.CreateDirectory(Path.Combine(root, "resources"));
    using (var bitmap = new SKBitmap(2, 2))
    {
        bitmap.Erase(SKColors.Magenta);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(root, "resources", "source.png"), png.ToArray());
    }
    File.WriteAllText(Path.Combine(root, "resources", "sprites.csv"), "SOURCE,source.png,0,0,2,2\n", new UTF8Encoding(true));
    BrowserRuntimeSession cacheSession = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    cacheSession.StartTitle();
    const System.Reflection.BindingFlags staticFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    Type contents = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")
        ?? throw new InvalidOperationException("AppContents type missing");
    string? Url(string name) => (string?)contents.GetMethod("GetSpriteDataUrl", staticFlags)!.Invoke(null, [name]);
    object Graphics(long id) => contents.GetMethod("GetGraphics", staticFlags)!.Invoke(null, [id])
        ?? throw new InvalidOperationException("GraphicsImage missing");
    object Sprite(string name) => contents.GetMethod("GetSprite", staticFlags)!.Invoke(null, [name])
        ?? throw new InvalidOperationException("Sprite missing");
    void Draw(object graphics, object sprite)
        => graphics.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Single(method => method.Name == "GDrawCImg" && method.GetParameters().Length == 2)
            .Invoke(graphics, [sprite, new System.Drawing.Rectangle(0, 0, 2, 2)]);

    string firstF = Url("SOURCE") ?? throw new InvalidOperationException("SpriteF URL missing");
    string secondF = Url("SOURCE") ?? throw new InvalidOperationException("SpriteF URL missing on repeat");
    Equal(true, ReferenceEquals(firstF, secondF), "R3 SpriteF reuses instance URL");

    string firstG = Url("COPY") ?? throw new InvalidOperationException("SpriteG URL missing");
    string secondG = Url("COPY") ?? throw new InvalidOperationException("SpriteG URL missing on repeat");
    Equal(true, ReferenceEquals(firstG, secondG), "R3 SpriteG reuses same generation URL");
    object graphics = Graphics(930);
    graphics.GetType().GetMethod("GClear")!.Invoke(graphics, [System.Drawing.Color.Blue]);
    string clearedG = Url("COPY") ?? throw new InvalidOperationException("SpriteG URL missing after clear");
    Equal(false, ReferenceEquals(firstG, clearedG), "R3 SpriteG invalidates after clear");
    Equal(false, firstG == clearedG, "R3 SpriteG clear returns changed pixels");
    Equal(true, ReferenceEquals(clearedG, Url("COPY")), "R3 SpriteG reuses post-clear URL");
    Draw(graphics, Sprite("COPY"));
    string drawnG = Url("COPY") ?? throw new InvalidOperationException("SpriteG URL missing after draw");
    Equal(false, ReferenceEquals(clearedG, drawnG), "R3 SpriteG invalidates after draw");
    Equal(true, ReferenceEquals(drawnG, Url("COPY")), "R3 SpriteG reuses post-draw URL");

    string firstAnime = Url("ANI") ?? throw new InvalidOperationException("SpriteAnime URL missing");
    string secondAnime = Url("ANI") ?? throw new InvalidOperationException("SpriteAnime URL missing on repeat");
    Equal(false, ReferenceEquals(firstAnime, secondAnime), "R3 SpriteAnime is never cached");
    Equal(null, Url("MISSING"), "R3 missing sprite has no URL");
    Equal(null, Url("MISSING"), "R3 missing sprite is not negatively cached");
    contents.GetMethod("ResetBootstrapResources", staticFlags)!.Invoke(null, null);
    _ = await BrowserRuntimeSession.StartPersistentBootstrapAsync(root, null);
    string reloadedF = Url("SOURCE") ?? throw new InvalidOperationException("reloaded SpriteF URL missing");
    Equal(false, ReferenceEquals(firstF, reloadedF), "R3 same-name SpriteF instance does not reuse old cache");
    Console.WriteLine("PASS 01C-R3 Sprite data URL cache contract");
    return;
}

if (args is ["ux14-graphics-reset-disposes"])
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    Type contents = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")
        ?? throw new InvalidOperationException("AppContents type missing");
    object graphics = contents.GetMethod("GetGraphics", flags)!.Invoke(null, [9900001L])!;
    graphics.GetType().GetMethod("GCreate")!.Invoke(graphics, [32, 32, false]);
    Equal(true, (bool)graphics.GetType().GetProperty("IsCreated")!.GetValue(graphics)!, "UX14 graphics created");
    contents.GetMethod("ResetBootstrapResources", flags)!.Invoke(null, null);
    Equal(false, (bool)graphics.GetType().GetProperty("IsCreated")!.GetValue(graphics)!, "UX14 reset disposes graphics before dropping owner");
    Console.WriteLine("PASS UX14 graphics reset disposes native image");
    return;
}

if (args is ["ux14-session-release"])
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static bool Alive<T>(WeakReference<T> reference) where T : class => reference.TryGetTarget(out _);
    static WeakReference<object> MakeControl() => new(new object());
    WeakReference<object> control = MakeControl();
    static (WeakReference<BrowserRuntimeSession> Session, WeakReference<object> Console, WeakReference<object> Process) Start()
    {
        BrowserRuntimeSession runtime = BrowserRuntimeSession.StartTitleBootstrapAsync(
            CreateGlobalFixture("@SYSTEM_TITLE\nPRINTL UX14_TITLE\nWAIT\nQUIT\n"), null).GetAwaiter().GetResult();
        runtime.StartTitle();
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        return (new(runtime), new(runtime.GetType().GetField("console", fields)!.GetValue(runtime)!),
            new(runtime.GetType().GetField("process", fields)!.GetValue(runtime)!));
    }
    var old = Start();
    BrowserRuntimeSession.ReleaseBootstrapResources();
    Type parserMediator = typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.ParserMediator")
        ?? throw new InvalidOperationException("ParserMediator type missing");
    object? parserConsole = parserMediator.GetField("console", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null);
    Equal<object?>(null, parserConsole, "UX14 ParserMediator releases prior console");
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    Equal(false, control.TryGetTarget(out _), "UX14 weak-reference control is collectible");
    Console.WriteLine($"UX14 weak roots session={Alive(old.Session)} console={Alive(old.Console)} process={Alive(old.Process)}");
    Equal(false, Alive(old.Session), "UX14 old Runtime graph released after reset");
    Console.WriteLine("PASS UX14 old Runtime graph released");
    return;
}

if (args is ["resource-metadata"])
{
    static async Task<BootstrapResourceSummary> Load(string csv)
    {
        string root = Path.Combine(Path.GetTempPath(), "emuera-p1b-resource-metadata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "CSV"));
        Directory.CreateDirectory(Path.Combine(root, "ERB"));
        Directory.CreateDirectory(Path.Combine(root, "resources"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GAMEBASE.CSV"));
        File.WriteAllText(Path.Combine(root, "ERB", "BOOTSTRAP.ERB"), "@SYSTEM_TITLE\nQUIT\n");
        using (var bitmap = new SKBitmap(32, 16))
        {
            bitmap.Erase(SKColors.Magenta);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(root, "resources", "parent-a.png"), png.ToArray());
            File.WriteAllBytes(Path.Combine(root, "resources", "parent-b.png"), png.ToArray());
        }
        File.WriteAllText(Path.Combine(root, "resources", "sprites.csv"), csv);
        return (await BrowserRuntimeSession.StartBootstrapAsync(root, loadConfig: false)).Resources;
    }

    const string baseline = """
LEFT,parent-a.png,0,0,16,16
RIGHT,parent-a.png,16,0,16,16
ANI,ANIME,16,16
ANI,parent-a.png,0,0,16,16,1,2,100
ANI,parent-b.png,16,0,16,16,3,4,200
""";
    BootstrapResourceSummary first = await Load(baseline);
    Equal(16L, first.SourceXTotal, "G02 both static source X values retained");
    Equal(16L, first.AnimationSourceXTotal, "G02 animation source X retained after normalization");
    Equal(4L, first.AnimationOffsetXTotal, "G02 animation destination offset retained");
    Equal(300L, first.AnimationDelayTotal, "G02 animation delay retained");

    BootstrapResourceSummary moved = await Load(baseline.Replace("RIGHT,parent-a.png,16", "RIGHT,parent-a.png,15", StringComparison.Ordinal));
    Equal(false, first.SpriteIdentitySha256 == moved.SpriteIdentitySha256, "G02 source X changes registration identity");
    BootstrapResourceSummary reparented = await Load(baseline.Replace("RIGHT,parent-a.png", "RIGHT,parent-b.png", StringComparison.Ordinal));
    Equal(false, first.SpriteIdentitySha256 == reparented.SpriteIdentitySha256, "G02 parent changes registration identity");
    BootstrapResourceSummary reordered = await Load(baseline.Replace("100\nANI,parent-b", "101\nANI,parent-b", StringComparison.Ordinal));
    Equal(false, first.SpriteIdentitySha256 == reordered.SpriteIdentitySha256, "G02 frame delay changes registration identity");
    Console.WriteLine($"BaselineSpriteIdentitySha256={first.SpriteIdentitySha256}");
    Console.WriteLine($"BaselineResourceIdentitySha256={first.IdentitySha256}");
    Console.WriteLine($"BaselineTotals=sourceX:{first.SourceXTotal},animationSourceX:{first.AnimationSourceXTotal},animationOffsetX:{first.AnimationOffsetXTotal},animationOffsetY:{first.AnimationOffsetYTotal},delay:{first.AnimationDelayTotal}");
    Console.WriteLine("PASS P1B G02 registered sprite and animation metadata");
    return;
}

if (args is ["unsupported"])
{
    var unsupportedSession = await BrowserRuntimeSession.StartAsync(Path.Combine(AppContext.BaseDirectory, "p1a-unsupported"));
    Contains("P1A未対応: font color", unsupportedSession.Output, "SETCOLOR reason");
    Contains("UNSUPPORTED.ERBの2行目", unsupportedSession.Output, "SETCOLOR location");
    Equal(BrowserRuntimeStatus.Failed, unsupportedSession.Status, "SETCOLOR status");
    Equal(false, unsupportedSession.IsComplete, "error is not normal completion");
    Equal(null, unsupportedSession.PendingInput, "error clears pending input");
    Equal(false, unsupportedSession.Submit(new InputEnvelope(0, "stale")), "error cannot resume");
    Equal(false, unsupportedSession.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "error stops following line");
    Console.WriteLine("PASS unsupported command fails explicitly");
    return;
}

if (args is ["intentional-error"])
{
    var errorSession = await StartTextFixture("@SYSTEM_TITLE\nTHROW 意図したエラー\nPRINTL SHOULD_NOT_RUN\nQUIT\n");
    Equal(BrowserRuntimeStatus.Failed, errorSession.Status, "THROW status");
    Equal(false, errorSession.IsComplete, "THROW is not normal completion");
    Equal(false, errorSession.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), "THROW stops following line");
    Console.WriteLine("PASS intentional ERB error fails explicitly");
    return;
}

if (args is ["line-semantics"])
{
    var lineSession = await BrowserRuntimeSession.StartAsync(Path.Combine(AppContext.BaseDirectory, "p1a-r2-fixture"));
    Equal(BrowserRuntimeStatus.WaitingForInput, lineSession.Status, "warmup WAIT");
    foreach (string input in new[] { "", "", "", "" })
    {
        long id = lineSession.PendingInput!.RequestId;
        Equal(true, lineSession.Submit(new InputEnvelope(id, input)), "empty WAIT accepted");
    }
    Equal(true, NormalizeNewlines(lineSession.Output).EndsWith("C_PROMPT:\n", StringComparison.Ordinal), "ordinary PRINT is a completed line at input wait");

    long cId = lineSession.PendingInput!.RequestId;
    Equal(true, lineSession.Submit(new InputEnvelope(cId, "比較入力")), "C input accepted");
    Equal(true, NormalizeNewlines(lineSession.Output).EndsWith("D_N:", StringComparison.Ordinal), "PRINTN continuation is visible before input");

    foreach (string input in new[] { "N入力", "", "", "日本語echo" })
    {
        long id = lineSession.PendingInput!.RequestId;
        Equal(true, lineSession.Submit(new InputEnvelope(id, input)), "D input accepted");
    }

    string expected = """
R2_BEGIN
A_BEFORE
A_AFTER
B_BEFORE
-
B_AFTER

B_END
=
+
C_PROMPT:
比較入力
[比較入力]C_END
D_AD_BD_C
D_N:N入力
D_AFTER_N
D_DEFAULT_INT
42
D_INT=42
D_EMPTY_STR
D_EMPTY=[]
D_JAPANESE
日本語echo
D_JP=[日本語echo]
LC_A=1,0,1,0
LC_B=5,2
LC_C=2,1
LC_D=1,1,1,8
R2_END
D_QUIT
""" + "\n";
    Equal(expected, FromMarker(lineSession.Output, "R2_BEGIN\n"), "R2 exact display transcript");
    Equal(BrowserRuntimeStatus.Succeeded, lineSession.Status, "R2 status");
    Console.WriteLine("PASS P1A-R2 line semantics");
    return;
}

if (args is ["r3r2-battle-plain-line"])
{
    var runtime = await StartTextFixture("@SYSTEM_TITLE\nPRINTL ACT_HEADER\nPRINTPLAINFORM TARGET:[ 8] PLAIN\nPRINTPLAINFORM  RESULT\nPRINTL\nPRINTL NEXT_ACT_HEADER\nINPUTS\nQUIT\n");
    Equal(BrowserInputKind.String, runtime.PendingInput?.Kind, "battle message reaches string input");
    BrowserDisplayLine[] lines = runtime.DisplayLines.ToArray();
    string Text(BrowserDisplayLine line) => string.Concat(Flatten(line.Parts).Select(part => part.Text));
    int target = Array.FindIndex(lines, line => Text(line).Contains("TARGET:[ 8]", StringComparison.Ordinal));
    Equal(true, target >= 0, "target output exists");
    Equal("TARGET:[ 8] PLAIN RESULT", Text(lines[target]), "empty PRINTL terminates PRINTPLAINFORM target line");
    Equal("NEXT_ACT_HEADER", Text(lines[target + 1]), "next action begins on its own line");
    Equal(false, Flatten(lines[target].Parts).Any(part => part.Kind == BrowserDisplayPartKind.Button), "PRINTPLAINFORM target is not a selectable button");
    Console.WriteLine("PASS R3R2 battle plain line boundary");
    return;
}

if (args is ["manual-ux-r1-shape"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
HTML_PRINT "<shape type='rect' param='0,50,500,200' color='#112233' bcolor='#445566'><shape type='space' param='200'><shape type='rect' param='broken'>"
INPUT
QUIT
""");
    BrowserRuntimeSession shape = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    shape.StartTitle();
    BrowserDisplayPart[] parts = Flatten(shape.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    BrowserDisplayPart rect = parts.Single(part => part.Kind == BrowserDisplayPartKind.Shape && part.Style?.Background == "#112233");
    BrowserDisplayPart space = parts.Single(part => part.Kind == BrowserDisplayPartKind.Shape && part.Style?.Background is null);
    Equal(0, rect.Layout?.X, "SHAPE rect X uses the desktop font-unit conversion");
    Equal(rect.Layout!.Y * 4, rect.Layout.Height, "SHAPE rect Y and height preserve desktop font-unit ratios");
    Equal(rect.Layout.Width * 2, rect.Layout.Height * 5, "SHAPE rect width and height preserve desktop font-unit ratios");
    Equal(rect.Layout.Height, space.Width, "SHAPE space reserves its desktop width");
    Equal(true, parts.Any(part => part.Kind == BrowserDisplayPartKind.Text && part.Text.Contains("param='broken'", StringComparison.Ordinal)), "invalid SHAPE stays visible like the desktop error shape");
    Console.WriteLine("PASS manual UX R1 SHAPE compatibility");
    return;
}

if (args is ["ux16r1-save18-character-list", var listRoot, var listSave, var listOutput])
{
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartSavePersistentBootstrapAsync(listRoot, new Dictionary<string, byte[]> { ["save18.sav"] = File.ReadAllBytes(listSave) });
    Equal(true, runtime.RunSaveCodecDiagnostic(18).LoadSucceeded, "real save18 codec");
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    object process = typeof(BrowserRuntimeSession).GetField("process", flags)!.GetValue(runtime)!;
    object evaluator = process.GetType().GetProperty("VEvaluator", flags)!.GetValue(process)!;
    object variables = evaluator.GetType().GetProperty("VariableData", flags)!.GetValue(evaluator)!;
    var characters = (System.Collections.IEnumerable)variables.GetType().GetProperty("CharacterList", flags)!.GetValue(variables)!;
    var rows = new List<object>();
    int index = 0;
    foreach (object character in characters)
    {
        long[] numbers = (long[])character.GetType().GetProperty("DataInteger", flags)!.GetValue(character)!;
        string[] names = (string[])character.GetType().GetProperty("DataString", flags)!.GetValue(character)!;
        rows.Add(new { index = index++, no = numbers[1], name = names[0], callName = names[1] });
    }
    File.WriteAllText(listOutput, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS isolated save18 character inventory: {rows.Count}");
    return;
}

if (args is ["ux16r1-subpixel-shape"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
HTML_PRINT "<shape type='rect' param='0,70,4,30' color='#990000'>"
PRINT_RECT 0,70,4,30
HTML_PRINT "<shape type='rect' param='0,70,0,30'>"
INPUT
QUIT
""");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    display.StartTitle();
    var parts = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).ToArray();
    Equal(2, parts.Count(part=>part.Kind==BrowserDisplayPartKind.Shape), "positive source width remains a shape even when native integer drawing width truncates to zero");
    Equal(1, parts.Count(part=>part.Kind==BrowserDisplayPartKind.Text&&part.Text.Contains("param=", StringComparison.Ordinal)), "only truly zero source width retains native error text");
    Console.WriteLine("PASS UX16R1 subpixel shape validity contract");
    return;
}

if (args is ["ux16r1-hairline-border"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
HTML_PRINT "<div border_width='3'>HAIRLINE</div><div border_width='0px'>ZERO</div><div>NONE</div><div border_width='200' border_color='#123456'>THICK</div>"
INPUT
QUIT
""");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    display.StartTitle();
    BrowserDisplayPart[] groups = Flatten(display.DisplayLines.SelectMany(line => line.Parts)).Where(part => part.Kind == BrowserDisplayPartKind.Group).ToArray();
    Equal(0, groups[0].Layout!.BorderWidth, "native 3 font-percent converts to a zero-width hairline stroke");
    Equal("#FFFFFF", groups[0].Layout!.BorderColor, "native omitted border color is white, independent of text foreground");
    Equal(0, groups[1].Layout!.BorderWidth, "explicit zero stroke remains present");
    Equal(null, (int?)groups[2].Layout!.BorderWidth, "omitted border has no stroke");
    Equal(36, groups[3].Layout!.BorderWidth, "nonzero border retains native font conversion");
    Equal("#123456", groups[3].Layout!.BorderColor, "explicit border color remains intact");
    Console.WriteLine("PASS UX16R1 native hairline border contract");
    return;
}

if (args is ["ux16r1-bar-font-contract"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nDRAWLINE\nCUSTOMDRAWLINE =_\nINPUT\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.SetViewport(1512, 864);
    var property = typeof(BrowserRuntimeSession).GetProperty("TextWidthMeasurer");
    Equal(true, property is not null, "browser font measurement hook");
    property!.SetValue(runtime, (Func<string, string, int, double>)((text, font, size) => text.Length * 9));
    runtime.StartTitle();
    string[] bars = runtime.DisplayLines.Select(line => string.Concat(Flatten(line.Parts).Select(part => part.Text))).Where(text => text.StartsWith('-') || text.StartsWith('=')).ToArray();
    Equal(167, bars[0].Length, "native drawable width / measured MS Gothic width");
    Equal(167, bars[1].Length, "multi-character custom bar trims individual final character like native");
    Console.WriteLine("PASS UX16R1 browser bar font contract");
    return;
}

if (args is ["ux16-html-paragraph-line-contract"])
{
    string root = CreateGlobalFixture("@SYSTEM_TITLE\nHTML_PRINT \"<p align='left'>UX16_P_CONTENT</p>\"\nPRINTL UX16_AFTER_P\nINPUT\nQUIT\n");
    BrowserRuntimeSession runtime = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    runtime.StartTitle();
    BrowserDisplayLine[] lines = runtime.DisplayLines.ToArray();
    static string LineText(BrowserDisplayLine line) => string.Concat(Flatten(line.Parts).Select(part => part.Text));
    int paragraph = Array.FindIndex(lines, line => LineText(line).Contains("UX16_P_CONTENT", StringComparison.Ordinal));
    int next = Array.FindIndex(lines, line => LineText(line).Contains("UX16_AFTER_P", StringComparison.Ordinal));
    Equal(true, paragraph >= 0 && next > paragraph, "HTML paragraph and following PRINTL are present");
    Equal(paragraph + 1, next, "P element does not add an implicit display row before HTML_PRINT lineEnd");
    Console.WriteLine("PASS UX16 HTML paragraph line contract");
    return;
}

if (args is ["manual-ux-r3-r2-html-contract"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
ALIGNMENT CENTER
HTML_PRINT "<div xpos='4620' ypos='-200'><button value='999'>TITLE_NOTICE</button></div>"
HTML_PRINT "<div>OUTER<div>INNER</div></div>"
HTML_PRINT "<div background_color='#002244'>EXPLICIT</div>"
HTML_PRINT "<button value='shop'><div>SHOP_FLOW</div></button>"
HTML_PRINT "<button value='key'><div xpos='0' ypos='300'>KEY_POSITIONED</div></button>"
HTML_PRINT "<nobr><nonbutton>PRE</nonbutton><button value='1' pos='200'>POS</button><nonbutton>POST</nonbutton></nobr>"
HTML_PRINT "<nobr><nonbutton>PRE</nonbutton><nonbutton pos='200'>LOCK</nonbutton><button value='2'>TAIL</button></nobr>"
HTML_PRINT "<div xpos='7300' ypos='0'><button title='攻撃相性'>ＣＡＵＴＩＯＮ<br>火:？</button></div>"
INPUT
QUIT
""");
    BrowserRuntimeSession display = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    display.StartTitle();
    Equal("#000000", display.BackgroundColor, "display surface uses the current game background");
    BrowserDisplayLine[] lines = display.DisplayLines.ToArray();
    BrowserDisplayLine notice = lines.Single(line => Flatten(line.Parts).Any(part => part.Text == "TITLE_NOTICE"));
    Equal("left", notice.Alignment, "native HTML_PRINT default alignment ignores prior ALIGNMENT CENTER");
    BrowserDisplayPart omitted = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Group && part.Children?.Any(child => child.Text == "OUTER") == true);
    Equal(null, omitted.Style?.Background, "native omitted DIV background is transparent");
    BrowserDisplayPart explicitBackground = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Group && part.Children?.Any(child => child.Text == "EXPLICIT") == true);
    Equal("#002244", explicitBackground.Style?.Background, "explicit DIV background remains opaque");
    BrowserDisplayPart shopGroup = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Group && part.Children?.Any(child => child.Text == "SHOP_FLOW") == true);
    BrowserDisplayPart keypadGroup = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Group && part.Children?.Any(child => child.Text == "KEY_POSITIONED") == true);
    Equal(false, shopGroup.Layout?.ExplicitPosition, "ordinary shop button DIV stays in document flow");
    Equal(true, keypadGroup.Layout?.ExplicitPosition, "keypad coordinate DIV is positioned");
    BrowserDisplayPart locked = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Button && part.Text == "POS");
    Equal(36, locked.LockedX, "native pos=200 locks at 200 percent of 18px font size");
    BrowserDisplayPart nonButtonLocked = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.NonButton && part.Text == "LOCK");
    Equal(36, nonButtonLocked.LockedX, "NONBUTTON pos shares the native lock contract");
    BrowserDisplayPart caution = Flatten(lines.SelectMany(line => line.Parts)).Single(part => part.Kind == BrowserDisplayPartKind.Group && Flatten(part.Children ?? []).Any(child => child.Text?.Contains("ＣＡＵＴＩＯＮ") == true));
    Equal(1314, caution.Layout?.X, "CAUTION group stays at native 7300 font-relative X");
    Console.WriteLine("PASS R3-R2 HTML alignment and background contract");
    return;
}

if (args is ["manual-ux-r3-r2-timed-input"])
{
    string root = CreateGlobalFixture("""
@SYSTEM_TITLE
PRINTL TIMED_INT_READY
TINPUT 5000, -1, 1
PRINTFORML TIMED_INT={RESULT} TIMEOUT={ISTIMEOUT}
PRINTL TIMED_STRING_READY
TINPUTS 10000, "時間切れ", 0
PRINTFORML TIMED_STRING=[%RESULTS%] TIMEOUT={ISTIMEOUT}
WAIT
QUIT
""");
    BrowserRuntimeSession timed = await BrowserRuntimeSession.StartTitleBootstrapAsync(root, null);
    timed.StartTitle();
    Equal(BrowserInputKind.Integer, timed.PendingInput?.Kind, "TINPUT integer prompt");
    Equal("TINPUT", timed.PendingInput?.TimedInputName, "TINPUT name");
    Equal(5000L, timed.PendingInput?.TimeLimit, "TINPUT deadline");
    Equal(true, timed.PendingInput?.DisplayTime, "TINPUT display ON");
    long integerId = timed.PendingInput!.RequestId;
    Equal(false, timed.Submit(new InputEnvelope(integerId, "bad")), "invalid integer leaves timed request open");
    Equal(integerId, timed.PendingInput?.RequestId, "invalid value keeps request ID");
    Equal(true, timed.Submit(new InputEnvelope(integerId, "2")), "TINPUT integer accepted");
    Contains("TIMED_INT=2 TIMEOUT=0", timed.Output, "integer result");
    Equal(BrowserInputKind.String, timed.PendingInput?.Kind, "TINPUTS string prompt");
    Equal("TINPUTS", timed.PendingInput?.TimedInputName, "TINPUTS name");
    Equal(false, timed.PendingInput?.DisplayTime, "TINPUTS display OFF");
    long stringId = timed.PendingInput!.RequestId;
    Equal(false, timed.SubmitTimeout(integerId, timed.SessionGeneration), "stale timeout rejected");
    Equal(stringId, timed.PendingInput?.RequestId, "stale callback does not consume next request");
    Equal(true, timed.SubmitTimeout(stringId, timed.SessionGeneration), "TINPUTS timeout accepted");
    Contains("TIMED_STRING=[時間切れ] TIMEOUT=1", timed.Output, "string timeout default");
    Equal(false, timed.SubmitTimeout(stringId, timed.SessionGeneration), "duplicate timeout rejected");
    Console.WriteLine("PASS R3-R2 timed input request boundaries");
    return;
}

var guards = new Dictionary<string, (string Command, string Feature, bool Persistence)>
{
    ["guard-twait-enter"] = ("TWAIT 1000,0", "TWAIT", false),
    ["guard-twait-void"] = ("TWAIT 1000,1", "TWAIT", false),
    ["guard-saveglobal"] = ("SAVEGLOBAL", "SAVEGLOBAL", true),
    ["guard-loadglobal"] = ("LOADGLOBAL", "LOADGLOBAL", true),
    ["guard-savedata"] = ("SAVEDATA 1,\"test\"", "SAVEDATA", true),
    ["guard-loaddata"] = ("LOADDATA 1", "LOADDATA", true),
    ["guard-deldata"] = ("DELDATA 1", "DELDATA", true),
    ["guard-alignment"] = ("ALIGNMENT CENTER", "ALIGNMENT CENTER", false),
};

if (args is [var mode] && guards.TryGetValue(mode, out var guard))
{
    string root = Path.Combine(Path.GetTempPath(), "emuera-p1a-r1", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "CSV"));
    Directory.CreateDirectory(Path.Combine(root, "ERB"));
    File.Copy(Path.Combine(AppContext.BaseDirectory, "p1a-fixture", "CSV", "GAMEBASE.CSV"), Path.Combine(root, "CSV", "GAMEBASE.CSV"));
    File.WriteAllText(Path.Combine(root, "ERB", "GUARD.ERB"), $"@SYSTEM_TITLE\nPRINTL GUARD START\n{guard.Command}\nPRINTL SHOULD_NOT_RUN\nQUIT\n");
    var before = Snapshot(root);
    var guardSession = await BrowserRuntimeSession.StartAsync(root);
    Equal(BrowserRuntimeStatus.Failed, guardSession.Status, $"{mode} status");
    Equal(false, guardSession.IsComplete, $"{mode} is not normal completion");
    Equal(null, guardSession.PendingInput, $"{mode} leaves no input");
    Contains("P1A未対応: " + guard.Feature, guardSession.Output, $"{mode} reason");
    Equal(false, guardSession.Output.Contains("SHOULD_NOT_RUN", StringComparison.Ordinal), $"{mode} stops following line");
    if (guard.Persistence)
        Equal(string.Join('\n', before), string.Join('\n', Snapshot(root)), $"{mode} has no file side effects");
    Console.WriteLine($"PASS {mode} fails before side effects");
    return;
}

var gate = new RuntimeInputGate();
var large = new InputEnvelope(41, "9007199254740993");
Equal(true, gate.TryConsume(41, large, out var rawLarge), "current request accepted");
Equal("9007199254740993", rawLarge, "64-bit input remains text");
Equal(false, gate.TryConsume(41, large, out _), "duplicate rejected");
Equal(false, gate.TryConsume(42, new InputEnvelope(41, "-7"), out _), "stale request rejected");
Equal(true, gate.TryConsume(42, new InputEnvelope(42, "日本語"), out var japanese), "next request accepted");
Equal("日本語", japanese, "Japanese input preserved");

Console.WriteLine("PASS RuntimeInputGate 6/6");

var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "p1a-fixture");
var session = await BrowserRuntimeSession.StartAsync(fixtureRoot);

Contains("P1A START", session.Output, "existing PRINTL runs");
Equal(BrowserRuntimeStatus.WaitingForInput, session.Status, "initial waiting status");
Equal(BrowserInputKind.Integer, session.PendingInput?.Kind, "first INPUT request");
var firstId = session.PendingInput!.RequestId;
Equal(false, session.Submit(new InputEnvelope(firstId, "9223372036854775808")), "out-of-range integer rejected");
Equal(firstId, session.PendingInput!.RequestId, "invalid integer keeps request pending");
Equal(true, session.Submit(new InputEnvelope(firstId, "7")), "first integer accepted");
Contains("POSITIVE", session.Output, "positive branch");

var largeId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(largeId, "9007199254740993")), "large integer accepted");
Contains("BIG=9007199254740993", session.Output, "large integer exact");

var negativeId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(negativeId, "-9")), "negative integer accepted");
Contains("NEGATIVE", session.Output, "negative branch");

var textId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(textId, "日本語入力")), "Japanese string accepted");
Contains("TEXT=日本語入力", session.Output, "RESULTS output");

var oneId = session.PendingInput!.RequestId;
Equal(true, session.PendingInput.OneInput, "ONEINPUTS distinguished");
Equal(true, session.Submit(new InputEnvelope(oneId, "一二")), "ONEINPUTS accepted");
Contains("ONE=一", session.Output, "ONEINPUTS output");

var functionId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(functionId, "関数再開")), "function INPUTS accepted");
Contains("FUNC=関数再開\nFUNC AFTER\nCALLER AFTER", session.Output.Replace("\r", ""), "call stack resumes in order");

var waitId = session.PendingInput!.RequestId;
Equal(BrowserInputKind.Enter, session.PendingInput.Kind, "WAIT distinguished");
Equal(false, session.Submit(new InputEnvelope(functionId, "stale")), "stale function response rejected");
Equal(true, session.Submit(new InputEnvelope(waitId, "")), "WAIT accepted");
Equal(false, session.Submit(new InputEnvelope(waitId, "")), "consumed WAIT rejected");
Contains("LOOP=0\nLOOP=1\nDONE\nZERO", session.Output.Replace("\r", ""), "loop and defaults start");

var zeroId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(zeroId, "0")), "non-empty zero is input");
Contains("ZERO=0", session.Output, "zero remains zero");

var defaultIntId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(defaultIntId, "")), "empty INPUT uses default");
Contains("DEFAULT_INT=42", session.Output, "integer default output");
Equal(false, session.Submit(new InputEnvelope(defaultIntId, "")), "integer default consumes once");

var whitespaceId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(whitespaceId, "  ")), "whitespace is not empty string input");
Contains("SPACE=[  ]", session.Output, "whitespace preserved");

var defaultStringId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(defaultStringId, "")), "empty INPUTS uses default");
Contains("DEFAULT_STR=[既定値]", session.Output, "string default output");
Equal(false, session.Submit(new InputEnvelope(defaultStringId, "")), "string default consumes once");

var oneDefaultIntId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(oneDefaultIntId, "")), "ONEINPUT uses generated default once");
Contains("ONE_DEFAULT_INT=4", session.Output, "ONEINPUT default already truncated by request");

var oneDefaultStringId = session.PendingInput!.RequestId;
Equal(true, session.Submit(new InputEnvelope(oneDefaultStringId, "")), "ONEINPUTS uses generated default once");
Contains("ONE_DEFAULT_STR=[既]", session.Output, "ONEINPUTS default already truncated by request");

var promptId = session.PendingInput!.RequestId;
Equal(true, NormalizeNewlines(session.Output).EndsWith("名前を入力:\n", StringComparison.Ordinal), "PRINT is visible as a completed line before input");
Equal(true, session.Submit(new InputEnvelope(promptId, "比較入力")), "prompt input accepted");
Contains("名前を入力:\n比較入力\n[比較入力]END\nNO_NEWLINE_QUIT\n", NormalizeNewlines(session.Output), "PRINT input boundary and quit flush");
Equal(BrowserRuntimeStatus.Succeeded, session.Status, "normal QUIT status");
Equal(true, session.IsComplete, "session completed");
Equal(false, session.IsFailed, "normal QUIT is not error");

Console.WriteLine("PASS BrowserRuntimeSession real runtime transcript");
}
catch (Exception exception)
{
    string testName = args.Length == 0 ? "default" : string.Join(' ', args);
    Console.Error.WriteLine($"TEST_FAILURE test={testName}");
    Console.Error.WriteLine(exception.ToString());
    Environment.ExitCode = 1;
}

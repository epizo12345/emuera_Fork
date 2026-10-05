using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.Web.Runtime;

internal static class CharaDatProbe
{
    const string Erh = "#DIM CHARADATA SAVEDATA WINTER_VALUE, 3\n";
    static int checks;
    static readonly List<object> cases = [];
    static void Check(bool value, string name)
    {
        cases.Add(new { name, passed = value });
        if (!value) throw new InvalidOperationException(name);
        checks++;
    }
    internal static string Fixture(string erb)
    {
        string root = Path.Combine(Path.GetTempPath(), "emuera-chara-dat", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "CSV")); Directory.CreateDirectory(Path.Combine(root, "ERB"));
        File.WriteAllText(Path.Combine(root, "CSV/GAMEBASE.CSV"), "コード,1\nバージョン,1000\nバージョン違い認める,1000\nタイトル,CHARA-DAT\n", new UTF8Encoding(true));
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(Path.Combine(root, "emuera.config"), "セーブデータをバイナリ形式で保存する:YES", Encoding.GetEncoding(932));
        File.WriteAllText(Path.Combine(root, "ERB/TEST.ERB"), erb, new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(root, "ERB/TEST.ERH"), Erh, new UTF8Encoding(true));
        return root;
    }
    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static async Task Run(string? evidence)
    {
        string root = Fixture("""
@SYSTEM_TITLE
ADDVOIDCHARA
ADDVOIDCHARA
NAME:0 = 冬眠A
NAME:1 = 冬眠B
CFLAG:0:3 = 7654321
CFLAG:1:3 = 9876543
CSTR:0:1 = 日本語 空白 : \\e
CSTR:1:1 = 二人目
WINTER_VALUE:0:2 = 321
WINTER_VALUE:1:2 = 654
RESULT = 77
SAVECHARA "Winter日本語", "冬眠メモ：二人", 0, 1
PRINTFORML ACK_AFTER_SAVE={RESULT},{CHARANUM}
DELCHARA 1, 0
RESULTS:9 = TAIL
FIND_CHARADATA "Winter*"
PRINTFORML FOUND={RESULT},NAME=[%RESULTS:0%],TAIL=[%RESULTS:9%]
CHKCHARADATA "Winter日本語"
PRINTFORML CHECK={RESULT},MEMO=[%RESULTS%]
LOADCHARA "winter日本語"
PRINTFORML LOADED={RESULT},{CHARANUM},N0=[%NAME:0%],N1=[%NAME:1%],A={CFLAG:0:3},{CFLAG:1:3},U={WINTER_VALUE:0:2},{WINTER_VALUE:1:2}
PRINTFORML STR=[%CSTR:0:1%],[%CSTR:1:1%]
INPUT
QUIT
""");
        var runtime = await BrowserRuntimeSession.StartSavePersistentAsync(root, null);
        Check(runtime.Status == BrowserRuntimeStatus.Persisting, "save waits for ACK");
        Check(!runtime.Output.Contains("ACK_AFTER_SAVE"), "following DELCHARA cannot execute before ACK");
        var put = runtime.PendingPersistence!;
        Check(put.LogicalFilename == "dat/chara_Winter日本語.dat", "separate DAT namespace and Native filename case");
        Check(!File.Exists(Path.Combine(root, "DAT/chara_Winter日本語.dat")), "uncommitted dat is not exposed in MEMFS");
        var info = runtime.InspectCharacterFile("chara_Winter日本語.dat", put.Bytes!);
        Check(info.State == 0 && info.CharacterCount == 2 && info.Memo == "冬眠メモ：二人", "full payload and memo validation without adding characters");
        Check(!runtime.AcknowledgePersistence("old"), "stale ACK rejected");
        Check(runtime.AcknowledgePersistence(put.OperationId), "matching ACK accepted");
        Check(!runtime.AcknowledgePersistence(put.OperationId), "duplicate ACK rejected");
        Check(runtime.Status == BrowserRuntimeStatus.WaitingForInput, "restore finishes at INPUT");
        Check(runtime.Output.Contains("ACK_AFTER_SAVE=77,2"), "SAVECHARA preserves RESULT");
        Check(runtime.Output.Contains("FOUND=1,NAME=[Winter日本語],TAIL=[TAIL]"), "FIND names count and unused RESULTS preserved");
        Check(runtime.Output.Contains("CHECK=0,MEMO=[冬眠メモ：二人]"), "CHKCHARADATA returns state and memo");
        Check(runtime.Output.Contains("LOADED=1,2,N0=[冬眠A],N1=[冬眠B],A=7654321,9876543,U=321,654"), "multiple characters integer schema restored");
        Check(runtime.Output.Contains("STR=[日本語 空白 : \\e],[二人目]"), "strings restored verbatim");
        byte[] bytes = put.Bytes!;
        Check(Sha(File.ReadAllBytes(Path.Combine(root, "DAT/chara_Winter日本語.dat"))) == Sha(bytes), "ACK-installed bytes equal writer snapshot");
        if (evidence is not null)
        {
            Directory.CreateDirectory(evidence);
            File.WriteAllBytes(Path.Combine(evidence, "chara_Web.dat"), bytes);
            File.Copy(Path.Combine(root, "ERB/TEST.ERB"), Path.Combine(evidence, "roundtrip.ERB"), true);
        }
        foreach (var corruption in Corruptions(bytes))
        {
            bool rejected = false;
            try { _ = runtime.InspectCharacterFile("chara_test.dat", corruption.Value); }
            catch { rejected = true; }
            Check(rejected, "preflight rejects " + corruption.Key);
            Check(Sha(File.ReadAllBytes(Path.Combine(root, "DAT/chara_Winter日本語.dat"))) == Sha(bytes), "preflight preserves old file " + corruption.Key);
        }
        foreach (int offset in new[] { 17, 25 })
        {
            byte[] incompatible = (byte[])bytes.Clone();
            BitConverter.GetBytes(offset == 17 ? 999L : -1L).CopyTo(incompatible, offset);
            Check(runtime.InspectCharacterFile("chara_test.dat", incompatible).State == (offset == 17 ? 2 : 3), "game/version state " + offset);
        }
        string reload = Fixture("""
@SYSTEM_TITLE
RESULTS:8 = TAIL
FIND_CHARADATA
PRINTFORML RELOAD_FIND={RESULT},NAME=[%RESULTS%],TAIL=[%RESULTS:8%]
CHKCHARADATA "Winter日本語"
PRINTFORML RELOAD_CHECK={RESULT},MEMO=[%RESULTS%]
LOADCHARA "Winter日本語"
PRINTFORML RELOAD={RESULT},{CHARANUM},N0=[%NAME:0%],N1=[%NAME:1%],A={CFLAG:0:3},{CFLAG:1:3},U={WINTER_VALUE:0:2},{WINTER_VALUE:1:2}
CHKCHARADATA "missing"
PRINTFORML MISSING={RESULT},MEMO=[%RESULTS%]
LOADCHARA "missing"
PRINTFORML LOAD_MISSING={RESULT},{CHARANUM}
INPUT
QUIT
""");
        var restored = await BrowserRuntimeSession.StartSavePersistentAsync(reload, null, new Dictionary<string, byte[]> { ["chara_Winter日本語.dat"] = bytes });
        Console.WriteLine(restored.Output);
        Check(restored.Output.Contains("RELOAD_FIND=1,NAME=[Winter日本語],TAIL=[TAIL]"), "fresh bootstrap list retains native names");
        Check(restored.Output.Contains("RELOAD_CHECK=0,MEMO=[冬眠メモ：二人]"), "fresh bootstrap memo");
        Check(restored.Output.Contains("RELOAD=1,2,N0=[冬眠A],N1=[冬眠B],A=7654321,9876543,U=321,654"), "fresh bootstrap actual LOADCHARA");
        Check(restored.Output.Contains("MISSING=1,MEMO=[----]") && restored.Output.Contains("LOAD_MISSING=0,2"), "Native missing states and no partial append");
        string overwriteRoot = Fixture("@SYSTEM_TITLE\nADDVOIDCHARA\nNAME:0 = 新キャラ\nSAVECHARA \"winter日本語\", \"置換\", 0\nPRINTL MUST_NOT_RUN_AFTER_FAILED_SAVE\nDELCHARA 0\nINPUT\n");
        var overwrite = await BrowserRuntimeSession.StartSavePersistentAsync(overwriteRoot, null, new Dictionary<string, byte[]> { ["chara_Winter日本語.dat"] = bytes });
        Check(overwrite.Status == BrowserRuntimeStatus.Persisting, "same name overwrite suspends");
        Check(Sha(File.ReadAllBytes(Path.Combine(overwriteRoot, "DAT/chara_Winter日本語.dat"))) == Sha(bytes), "old file unchanged before overwrite ACK");
        try { await overwrite.DrainPersistenceAsync(_ => throw new IOException("test quota failure")); } catch (IOException) { }
        Check(overwrite.Status == BrowserRuntimeStatus.Failed && !overwrite.Output.Contains("MUST_NOT_RUN_AFTER_FAILED_SAVE"), "failed commit cannot continue to character deletion");
        Check(Sha(File.ReadAllBytes(Path.Combine(overwriteRoot, "DAT/chara_Winter日本語.dat"))) == Sha(bytes), "old dat retained after persistence failure");
        Check(!overwrite.AcknowledgePersistence(overwrite.PendingPersistence?.OperationId ?? "old"), "failure late ACK cannot restart");
        string successfulRoot = Fixture("@SYSTEM_TITLE\nADDVOIDCHARA\nNAME:0 = 新キャラ\nSAVECHARA \"winter日本語\", \"置換\", 0\nCHKCHARADATA \"Winter日本語\"\nPRINTFORML OVERWRITE=[%RESULTS%]\nINPUT\n");
        var successful = await BrowserRuntimeSession.StartSavePersistentAsync(successfulRoot, null, new Dictionary<string, byte[]> { ["chara_Winter日本語.dat"] = bytes });
        await successful.DrainPersistenceAsync(_ => Task.CompletedTask);
        Check(successful.Output.Contains("OVERWRITE=[置換]") && Directory.GetFiles(Path.Combine(successfulRoot, "DAT"), "chara_*.dat").Length == 1, "retry successful case-insensitive overwrite one file");
        foreach (var corruption in Corruptions(bytes).Where(c => c.Key is "truncated" or "metadata count overflow"))
        {
            string corruptRoot = Fixture("@SYSTEM_TITLE\nADDVOIDCHARA\nNAME:0 = KEEP_ME\nCHKCHARADATA \"broken\"\nPRINTFORML BROKEN_CHECK={RESULT}\nLOADCHARA \"broken\"\nPRINTL MUST_NOT_RUN_AFTER_BROKEN_LOAD\nINPUT\n");
            var corrupt = await BrowserRuntimeSession.StartSavePersistentAsync(corruptRoot, null, new Dictionary<string, byte[]> { ["chara_broken.dat"] = corruption.Value });
            Check(corrupt.Status == BrowserRuntimeStatus.Failed && !corrupt.Output.Contains("MUST_NOT_RUN_AFTER_BROKEN_LOAD"), "actual LOADCHARA rejects " + corruption.Key);
            var process = typeof(BrowserRuntimeSession).GetField("process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(corrupt)!;
            var evaluator = process.GetType().GetProperty("VEvaluator")!.GetValue(process)!;
            var data = evaluator.GetType().GetProperty("VariableData")!.GetValue(evaluator)!;
            var list = data.GetType().GetProperty("CharacterList")?.GetValue(data) ?? data.GetType().GetField("CharacterList")!.GetValue(data);
            Check(((System.Collections.ICollection)list!).Count == 1, "no partially appended character after " + corruption.Key);
            Check(!Directory.GetFiles(Path.Combine(corruptRoot, "DAT")).Any(p => p.Contains("pending-") || Path.GetFileName(p).StartsWith("inspect-")), "temporary resource cleanup " + corruption.Key);
        }
        string staleRoot = Fixture("@SYSTEM_TITLE\nADDVOIDCHARA\nSAVECHARA \"stale\", \"old\", 0\nPRINTL STALE_FOLLOWING\nINPUT\n");
        var stale = await BrowserRuntimeSession.StartSavePersistentAsync(staleRoot, null);
        var staleId = stale.PendingPersistence!.OperationId;
        _ = await BrowserRuntimeSession.StartSavePersistentAsync(Fixture("@SYSTEM_TITLE\nPRINTL NEW_SESSION\nINPUT\n"), null);
        Check(!stale.AcknowledgePersistence(staleId), "generation replacement rejects old matching ACK");
        Check(!stale.Output.Contains("STALE_FOLLOWING") && !File.Exists(Path.Combine(staleRoot, "DAT/chara_stale.dat")), "interrupted old save cannot expose data or resume");
        string expressionRoot = Fixture("@SYSTEM_TITLE\nADDVOIDCHARA\nLOCAL = UNSAFE_SAVE()\nPRINTL EXPRESSION_FOLLOWING\nINPUT\n@UNSAFE_SAVE\n#FUNCTION\nSAVECHARA \"unsafe\", \"memo\", 0\nDELCHARA 0\nRETURNF 1\n");
        var expression = await BrowserRuntimeSession.StartSavePersistentAsync(expressionRoot, null);
        Check(expression.Status == BrowserRuntimeStatus.Failed && expression.PendingPersistenceCount == 0 && !File.Exists(Path.Combine(expressionRoot, "DAT/chara_unsafe.dat")), "expression SAVECHARA fails before writing instead of running beyond unacknowledged save");
        foreach (string command in new[] { "SAVECHARA \"../bad\", \"x\", 0", "LOADCHARA \"../bad\"", "CHKCHARADATA \"../bad\"", "FIND_CHARADATA \"../*\"" })
        {
            var invalid = await BrowserRuntimeSession.StartSavePersistentAsync(Fixture("@SYSTEM_TITLE\nADDVOIDCHARA\n" + command + "\nPRINTL INVALID_PATH_FOLLOWING\nINPUT\n"), null);
            Check(invalid.Status == BrowserRuntimeStatus.Failed && !invalid.Output.Contains("INVALID_PATH_FOLLOWING"), "path rejected through command " + command);
        }
        if (evidence is not null && File.Exists(Path.Combine(evidence, "chara_Native.dat")))
        {
            byte[] native = File.ReadAllBytes(Path.Combine(evidence, "chara_Native.dat"));
            var imported = await BrowserRuntimeSession.StartSavePersistentAsync(Fixture("@SYSTEM_TITLE\nLOADCHARA \"Native\"\nPRINTFORML NATIVE={RESULT},{CHARANUM},N=[%NAME:0%],A={CFLAG:0:3},U={WINTER_VALUE:0:2}\nINPUT\n"), null, new Dictionary<string, byte[]> { ["chara_Native.dat"] = native });
            Check(imported.Output.Contains("NATIVE=1,2,N=[Native一人目],A=123456,U=4321"), "Native writer input restored by real LOADCHARA");
        }
        if (evidence is not null) File.WriteAllText(Path.Combine(evidence, "host-results.json"), JsonSerializer.Serialize(new { status = "PASS", checks, cases, writerBytes = bytes.Length, writerSha = Sha(bytes) }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS chara-dat-contract {checks} asserts");
    }
    static Dictionary<string, byte[]> Corruptions(byte[] original)
    {
        byte[] headerCount = (byte[])original.Clone(); BitConverter.GetBytes(uint.MaxValue).CopyTo(headerCount, 12);
        byte[] count = (byte[])original.Clone();
        using (var stream = new MemoryStream(count))
        using (var reader = new BinaryReader(stream, Encoding.Unicode))
        { stream.Position = 33; _ = reader.ReadString(); BitConverter.GetBytes(long.MaxValue).CopyTo(count, (int)stream.Position); }
        byte[] wrongType = (byte[])original.Clone(); wrongType[16] = 0;
        byte[] badEnd = (byte[])original.Clone(); badEnd[^1] = 0xfe;
        return new() { ["empty"] = [], ["truncated"] = original[..^2], ["metadata count overflow"] = headerCount, ["character count overflow"] = count,
            ["wrong file type"] = wrongType, ["bad EOF"] = badEnd, ["trailing bytes"] = original.Concat(new byte[] { 0 }).ToArray() };
    }
}

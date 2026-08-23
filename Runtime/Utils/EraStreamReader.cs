using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Sub;

internal sealed partial class EraStreamReader : IDisposable
{
    public EraStreamReader(bool useRename
#if PERFORMANCE_METRICS
        , ErbStartupFileProfile profile = null
#endif
        )
    {
        this.useRename = useRename;
#if PERFORMANCE_METRICS
        this.profile = profile;
#endif
    }

    string filepath;
    string filename;
    // [Emuera改修:MEM-13R37 2026-08-22]
    // Filename→fileIdはOpen/OpenOnCache時に1回だけ登録し、各行のScriptPosition生成でDictionary lookupを繰り返さない。
    // reader lifetime中は同じIDを再利用し、Dispose時は「位置なし」sentinelの0へ戻す。
    int fileId;
    readonly bool useRename;
#if PERFORMANCE_METRICS
    readonly ErbStartupFileProfile profile;
#endif
    int curNo;
    int nextNo = 1;
    string[] _fileLines;
    // [Emuera改修:MEM-13R34 2026-08-22]
    // ReadEnabledLineの返却先はsource/offsetだけを保持し、次のReadまでCharStreamをescapeさせないため、reader単位で再利用する。
    CharStream reusableCharStream;

    public bool Open(string path)
    {
        return Open(path, Path.GetFileName(path));
    }

    public bool Open(string path, string name)
    {
        //そんなお行儀の悪いことはしていない
        //if (disposed)
        //    throw new ExeEE("破棄したオブジェクトを再利用しようとした");
        //if ((reader != null) || (stream != null) || (filepath != null))
        //    throw new ExeEE("使用中のオブジェクトを別用途に再利用しようとした");
        filepath = path;
        filename = name;
        fileId = ScriptFileRegistry.GetId(filename);
        curNo = 0;
        nextNo = 0;
        try
        {
            _fileLines = File.ReadAllLines(filepath, Config.Encode);
        }
        catch
        {
            this.Dispose();
            return false;
        }
        return true;
    }

    internal bool OpenDirect(string path, string name, bool checkUtf8Bom)
    {
        // [Emuera改修:MEM-13R40 2026-08-23]
        // active Lazy ERBだけはPreload cacheを使わず、Preloadと同じdecode/BOM判定をdirect readで共有する。
        // safe scanではwarningを抑え、実際のindex/fallback parseで一度だけ従来相当のwarningを出す。
        filepath = path;
        filename = name;
        fileId = ScriptFileRegistry.GetId(filename);
        curNo = 0;
        nextNo = 0;
        try
        {
            _fileLines = Preload.ReadFileLines(filepath, checkUtf8Bom);
        }
        catch
        {
            this.Dispose();
            return false;
        }
        return true;
    }

    public bool OpenOnCache(string path)
    {
        return OpenOnCache(path, Path.GetFileName(path));
    }


    public bool OpenOnCache(string path, string name)
    {
        // [Emuera改修:MEM-13R40 2026-08-23]
        // R40でPreloadから明示的にskipしたactive Lazy ERBだけがcache missからdirect readへ進む。
        // 非targetのcache missは従来のGetFileLines契約を維持し、KeyNotFound等を隠さない。
        if (!Preload.TryGetFileLines(path, out string[] cachedLines)
            && LazyErbPolicy.IsActiveTarget(path))
            return OpenDirect(path, name, true);
        filepath = path.ToString();
        filename = name.ToString();
        fileId = ScriptFileRegistry.GetId(filename);
        curNo = 0;
        nextNo = 0;
        _fileLines = cachedLines ?? Preload.GetFileLines(path);
        return true;
    }

    public string ReadLine()
    {
        string ret = null;
        curNo = nextNo;
        if (_fileLines.Length > curNo)
        {
            ret = _fileLines[curNo];
            nextNo++;
#if PERFORMANCE_METRICS
            if (profile != null)
            {
                profile.PhysicalLines++;
                if (useRename)
                {
                    profile.RenameInputLines++;
                    if (ret.Contains("[[", StringComparison.Ordinal))
                        profile.RenameCandidates++;
                }
            }
#endif
        }
        return ret;
    }

    /// <summary>
    /// 次の有効な行を読む。LexicalAnalyzer経由でConfigを参照するのでConfig完成までつかわないこと。
    /// </summary>
    public CharStream ReadEnabledLine(bool disabled = false)
    {
        string line;
        CharStream st = reusableCharStream;
        while (true)
        {
            line = ReadLine();
            if (line == null)
                return null;
            if (line.Length == 0)
                continue;

            if (st == null)
                st = reusableCharStream = new CharStream(line);
            else
                st.Reset(line);
            LexicalAnalyzer.SkipWhiteSpace(st);

            if (useRename)
            {
                line = Rename.RenameString(st.Substring(), new ScriptPosition(fileId, LineNo));
                st.Reset(line);
                LexicalAnalyzer.SkipWhiteSpace(st);
            }

            if (st.EOS)
                continue;
            //[SKIPSTART]～[SKIPEND]中にここが誤爆するので無効化
            if (!disabled)
            {
                if (st.Current == '}')
                    throw new CodeEE(LocalizationManager.Error.UnexpectedContinuationEnd, new ScriptPosition(fileId, curNo));
                if (st.Current == '{')
                {
                    if (line.Trim() != "{")
                        throw new CodeEE(LocalizationManager.Error.CharacterAfterContinuation, new ScriptPosition(fileId, curNo));
                    break;
                }
            }
            return st;
        }
        //curNoはこの後加算しない(始端記号の行を行番号とする)
        StringBuilder b = new();
        while (true)
        {
            line = ReadLine();
            if (line == null)
            {
                throw new CodeEE(LocalizationManager.Error.NotCloseLineContinuation, new ScriptPosition(fileId, curNo));
            }

            if (useRename)
            {
                line = Rename.RenameString(line);
            }
            var test = line.AsSpan().TrimStart();
            if (test.Length > 0)
            {
                if (test[0] == '}')
                {
                    if (!test.TrimEnd().SequenceEqual("}"))
                        throw new CodeEE(LocalizationManager.Error.CharacterAfterContinuationEnd, new ScriptPosition(fileId, curNo));
                    break;
                }
                //行連結文字なら1字でないとおかしい、というか、こうしないとFORMの数値変数処理が誤爆する。
                //{
                //A}
                //みたいなどうしようもないコードは知ったこっちゃない
                if (test.SequenceEqual("{"))
                    throw new CodeEE(LocalizationManager.Error.UnexpectedContinuation, new ScriptPosition(fileId, curNo));
            }
            b.Append(line);
            b.Append(' ');
        }
        st.Reset(b.ToString());
        LexicalAnalyzer.SkipWhiteSpace(st);
        return st;
    }

    /// <summary>
    /// 直前に読んだ行の行番号
    /// </summary>
    public int LineNo
    { get { return curNo; } }
    public string Filename
    {
        get
        {
            return filename;
        }
    }
    internal int FileId { get { return fileId; } }
    //public string Filepath
    //{
    //    get
    //    {
    //        return filepath;
    //    }
    //}

    public void Close() { this.Dispose(); }
    bool disposed;
    #region IDisposable メンバ

    public void Dispose()
    {
        if (disposed)
            return;
        filepath = null;
        filename = null;
        fileId = 0;
        reusableCharStream = null;
        disposed = true;
        _fileLines = null;
    }

    #endregion
}

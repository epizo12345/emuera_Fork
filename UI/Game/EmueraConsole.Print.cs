using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;
using MinorShift.Emuera.UI;
using MinorShift.Emuera.Runtime.Config.JSON;

namespace MinorShift.Emuera.GameView;

//1820 EmueraConsoleのうちdisplayLineListやprintBufferに触るもの
//いつかEmueraConsoleから分離したい
internal sealed partial class EmueraConsole : IDisposable
{
    private readonly List<ConsoleDisplayLine> displayLineList;
    public bool noOutputLog;
    public SKColor bgColor = Config.BackColor.ToSKColor();

    private readonly PrintStringBuffer printBuffer;
    readonly StringMeasure stringMeasure = new();

    public void ClearDisplay()
    {
        #region EE_AnchorのCB機能移植
        CBProc.ClearScreen();
        #endregion

        displayLineList.Clear();
        _htmlElementListDict.Clear();
        logicalLineCount = 0;
        lineNo = 0;
        lastDrawnLineNo = -1;
        if (!suppressInitialPaint)
        {
            verticalScrollBarUpdate();
            window.MainPicBox.Refresh();//OnPaint発行
        }
    }


    #region Print系

    //private bool useUserStyle = true;
    public bool UseUserStyle { get; set; }
    public bool UseSetColorStyle { get; set; }
    private StringStyle defaultStyle = new(Config.ForeColor, FontStyle.Regular, null);
    private StringStyle userStyle = new(Config.ForeColor, FontStyle.Regular, null);
    //private StringStyle style = new StringStyle(Config.ForeColor, FontStyle.Regular, null);
    private StringStyle Style
    {
        get
        {
            if (!UseUserStyle)
                return defaultStyle;
            if (UseSetColorStyle)
                return userStyle;
            //PRINTD系(SETCOLORを無視する)
            if (userStyle.Color == defaultStyle.Color)
                return userStyle;
            return new StringStyle(defaultStyle.Color, userStyle.FontStyle, userStyle.Fontname);
        }
    }
    //private StringStyle Style { get { return (useUserStyle ? userStyle : defaultStyle); } }
    public StringStyle StringStyle { get { return userStyle; } }
    public void SetStringStyle(FontStyle fs) { userStyle.FontStyle = fs; }
    public void SetStringStyle(Color color) { userStyle.Color = color; userStyle.ColorChanged = color != Config.ForeColor; }
    public void SetFont(string fontname)
    {
        if (!string.IsNullOrEmpty(fontname)) userStyle.Fontname = fontname; else userStyle.Fontname = Config.FontName;
    }
    private DisplayLineAlignment alignment = DisplayLineAlignment.LEFT;
    public DisplayLineAlignment Alignment { get { return alignment; } set { alignment = value; } }
    public void ResetStyle()
    {
        userStyle = defaultStyle;
        alignment = DisplayLineAlignment.LEFT;
    }

    public bool EmptyLine { get { return printBuffer.IsEmpty; } }

    /// <summary>
    /// DRAWLINE用文字列
    /// </summary>
    string stBar;

    Stopwatch _drawStopwatch;
    bool forceTextBoxColor;
    public void SetBgColor(Color color)
    {
        this.bgColor = color.ToSKColor();
        forceTextBoxColor = true;
        if (suppressInitialPaint)
            return;
        //REDRAWされない場合はTextBoxの色は変えずにフラグだけ立てる
        //最初の再描画時に現在の背景色に合わせる
        if (redraw == ConsoleRedraw.None && window.ScrollBar.Value == window.ScrollBar.Maximum)
            return;
        //色変化が速くなりすぎないように一定時間以内の再呼び出しは強制待ちにする
        if (_drawStopwatch == null)
        {
            _drawStopwatch = Stopwatch.StartNew();
        }
        else
        {
            while (_drawStopwatch.ElapsedMilliseconds < msPerFrame)
            {
                Application.DoEvents();
            }
        }
        RefreshStrings(true);
        _drawStopwatch.Restart();
    }

    //完全に独立したHTML
    public void PrintHTMLIsland(string html, int depth = 0)
    {
        if (_htmlElementListDict.TryGetValue(depth, out var htmlElementList))
        {

        }
        else
        {
            htmlElementList = [];
            _htmlElementListDict[depth] = htmlElementList;
        }
        htmlElementList.AddRange(HtmlManager.Html2DisplayLine(html, stringMeasure, this, true));
    }
    public void ClearHTMLIsland()
    {
        _htmlElementListDict.Clear();
    }
    public void ClearHTMLIsland(int depth = 0)
    {
        _htmlElementListDict[depth].Clear();
    }



    /// <summary>
    /// 最後に描画した時にlineNoの値
    /// </summary>
    int lastDrawnLineNo = -1;
    int lineNo;
    Int64 logicalLineCount;
    public long LineCount { get { return logicalLineCount; } }

    internal (string Hash, int LineCount) GetBenchmarkDisplayState()
    {
        StringBuilder builder = new();
        foreach (ConsoleDisplayLine line in displayLineList)
            builder.Append(line).Append('\n');
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return (Convert.ToHexString(hash), displayLineList.Count);
    }

    private void addRangeDisplayLine(ConsoleDisplayLine[] lineList)
    {
        long displayAddStart = PerformanceMetrics.StartTiming();
        try
        {
            for (int i = 0; i < lineList.Length; i++)
                addDisplayLine(lineList[i], false);
        }
        finally
        {
            PerformanceMetrics.AddDisplayAdd(displayAddStart);
        }
    }

    private void addDisplayLine(ConsoleDisplayLine line, bool force_LEFT)
    {
        #region EE_AnchorのCB機能移植
        if (JSONConfig.User.CBUseClipboard)
            CBProc.AddLine(line, force_LEFT);
        #endregion

        if (LastLineIsTemporary)
            deleteLine(1);
        //不適正なFontのチェック
        AConsoleDisplayNode errorStr = null;
        foreach (ConsoleButtonString button in line.Buttons)
        {
            foreach (AConsoleDisplayNode css in button.StrArray)
            {
                if (css.Error)
                {
                    errorStr = css;
                    break;
                }
            }
        }
        if (errorStr != null)
        {
            Dialog.Show(LocalizationManager.MsgBox.IllegalFont, LocalizationManager.MsgBox.IllegalFontError);
            this.Quit();
            return;
        }
        if (force_LEFT)
            line.SetAlignment(DisplayLineAlignment.LEFT);
        else
            line.SetAlignment(alignment);
        line.LineNo = lineNo;
        if (displayLineList.Count != 0 &&
            !displayLineList[^1].IsLineEnd)
        {
            var lastline = displayLineList[^1];
            deleteLine(1);
            line.ShiftPositionX(lastline.Buttons[^1].PointX + lastline.Buttons[^1].Width);
            line.ChangeStr([.. lastline.Buttons, .. line.Buttons]);
        }
        displayLineList.Add(line);
        lineNo++;
        if (line.IsLogicalLine && displayLineList[^1].IsLineEnd)
            logicalLineCount++;
        if (lineNo == int.MaxValue)
        {
            lastDrawnLineNo = -1;
            lineNo = 0;
        }
        if (logicalLineCount == long.MaxValue)
        {
            logicalLineCount = 0;
        }
        if (displayLineList.Count > Config.MaxLog)
            displayLineList.RemoveAt(0);
    }


    public void deleteLine(int argNum)
    {
        #region EE_AnchorのCB機能移植
        if (JSONConfig.User.CBUseClipboard)
            CBProc.DelLine(Math.Min(argNum, displayLineList.Count)); //FIXIT - Do we need to worry about the count?
        #endregion

        int delNum = 0;
        int num = argNum;
        while (delNum < num)
        {
            if (displayLineList.Count == 0)
                break;
            ConsoleDisplayLine line = displayLineList[^1];
            displayLineList.RemoveAt(displayLineList.Count - 1);
            lineNo--;
            if (line.IsLogicalLine)
            {
                delNum++;
                if (line.IsLineEnd)
                {
                    logicalLineCount--;
                }
            }
        }
        if (lineNo < 0)
            lineNo += int.MaxValue;
        lastDrawnLineNo = -1;
        //RefreshStrings(true);
    }

    public bool LastLineIsTemporary
    {
        get
        {
            if (displayLineList.Count == 0)
                return false;
            return displayLineList[^1].IsTemporary;
        }
    }

    //空行であるかのチェック
    public bool LastLineIsEmpty
    {
        get
        {
            if (displayLineList.Count == 0)
                return false;
            return string.IsNullOrEmpty(displayLineList[^1].ToString().Trim());
        }
    }

    //最終行を書き換え＋次の行追加時にはその行を再利用するように設定
    public void PrintTemporaryLine(string str)
    {
        PrintSingleLine(str, true);
    }

    //最終行だけを書き換える
    private void changeLastLine(string str)
    {
        deleteLine(1);
        PrintSingleLine(str, false);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="str"></param>
    /// <param name="position"></param>
    /// <param name="level">警告レベル.0:軽微なミス.1:無視できる行.2:行が実行されなければ無害.3:致命的</param>
    public void PrintWarning(string str, ScriptPosition? position, int level)
    {
        if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
            return;
        //警告だけは強制表示
        bool b = force_temporary;
        force_temporary = false;
        if (position != null)
        {
            if (position.Value.LineNo >= 0)
            {
                PrintErrorButton(string.Format(LocalizationManager.Error.Warning1, level, position.Value.Filename, position.Value.LineNo, str), position, level);
                GlobalStatic.Process.printRawLine(position);
            }
            else
                PrintErrorButton(string.Format(LocalizationManager.Error.Warning2, level, position.Value.Filename, str), position, level);

        }
        else
        {
            PrintError(string.Format(LocalizationManager.Error.Warning3, level, str));
        }
        force_temporary = b;
    }



    /// <summary>
    /// ユーザー指定のフォントを無視する。ウィンドウサイズを考慮せず確実に一行で書く。システム用。
    /// </summary>
    /// <param name="str"></param>
    public void PrintSystemLine(string str)
    {
        PrintFlush(false);
        //RefreshStrings(false);
        UseUserStyle = false;
        PrintSingleLine(str, false);
    }
    public void PrintError(string str)
    {
        if (string.IsNullOrEmpty(str))
            return;
        if (Program.DebugMode)
        {
            this.DebugPrint(str);
            this.DebugNewLine();
        }
        PrintFlush(false);
        UseUserStyle = false;
        ConsoleDisplayLine dispLine = PrintPlainwithSingleLine(str);
        if (dispLine == null)
            return;
        addDisplayLine(dispLine, true);
        RefreshStrings(false);
    }


    internal void PrintErrorButton(string str, ScriptPosition? pos, int level = 0)
    {
        if (string.IsNullOrEmpty(str))
            return;
        if (Program.DebugMode)
        {
            this.DebugPrint(str);
            this.DebugNewLine();
        }
        UseUserStyle = false;

        var errColor = Color.FromArgb(255, 255, 255, 160);
        var errerStyle = Style;
        errerStyle.Color = level switch
        {
            0 => errColor,
            1 => errColor,
            2 => errColor,
            3 => Color.Red,
            _ => Color.Red
        };
        ConsoleDisplayLine dispLine = printBuffer.AppendAndFlushErrButton(str, errerStyle, ErrorButtonsText, pos, stringMeasure);
        if (dispLine == null)
            return;
        addDisplayLine(dispLine, true);
        RefreshStrings(false);
    }

    /// <summary>
    /// 1813 従来のPrintLineを用途を考慮してPrintSingleLineとPrintSystemLineに分割
    /// </summary>
    /// <param name="str"></param>
    public void PrintSingleLine(string str) { PrintSingleLine(str, false); }
    public void PrintSingleLine(string str, bool temporary)
    {
        if (string.IsNullOrEmpty(str))
            return;
        PrintFlush(false);
        printBuffer.Append(str, Style);
        ConsoleDisplayLine dispLine = BufferToSingleLine(true, temporary);
        if (dispLine == null)
            return;
        addDisplayLine(dispLine, false);
        RefreshStrings(false);
    }

    public void Print(string str, bool lineEnd = true)
    {
        if (string.IsNullOrEmpty(str))
            return;

        var lineEndIndex = str.IndexOf('\n', StringComparison.Ordinal);
        if (lineEndIndex != -1)
        {
            string upper = str[..lineEndIndex];
            printBuffer.Append(upper, Style);
            NewLine();
            if (lineEndIndex < str.Length - 1)
            {
                string lower = str[(lineEndIndex + 1)..];
                Print(lower);
            }
            return;
        }

        printBuffer.Append(str, Style, lineEnd: lineEnd);
        return;
    }


    public void PrintImg(string str)
    {
        printBuffer.Append(new ConsoleImagePart(str, null, 0, 0, 0, 0));
    }

    public void PrintShape(string type, int[] param)
    {
        ConsoleShapePart part = ConsoleShapePart.CreateShape(type, param, userStyle.Color, userStyle.ButtonColor, false);
        printBuffer.Append(part);
    }

    public void PrintHtml(string str, bool lineEnd)
    {
        if (string.IsNullOrEmpty(str))
            return;
        if (!this.Enabled)
            return;
        if (!printBuffer.IsEmpty)
        {
            ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, force_temporary);
            addRangeDisplayLine(dispList);
        }
        long displayBuildStart = PerformanceMetrics.StartTiming();
        addRangeDisplayLine(HtmlManager.Html2DisplayLine(str, stringMeasure, this, lineEnd));
        PerformanceMetrics.AddDisplayBuild(displayBuildStart);
        RefreshStrings(false);
    }

    private int printCWidth = -1;
    private int printCWidthL = -1;
    private int printCWidthL2 = -1;
    public void PrintC(string str, bool alignmentRight)
    {
        if (string.IsNullOrEmpty(str))
            return;

        printBuffer.Append(CreateTypeCString(str, alignmentRight), Style, true);
    }

    private void calcPrintCWidth(StringMeasure stringMeasure)
    {
        string str = new(' ', Config.PrintCLength);
        var font = Config.DefaultFont;
        printCWidth = StringMeasure.GetDisplayLength(str, font);

        str += " ";
        printCWidthL = StringMeasure.GetDisplayLength(str, font);

        str += " ";
        printCWidthL2 = StringMeasure.GetDisplayLength(str, font);
    }

    private string CreateTypeCString(string str, bool alignmentRight)
    {
        if (printCWidth == -1)
            calcPrintCWidth(stringMeasure);
        int length = 0;
        int width;
        if (str != null)
            length = Config.Encode.GetByteCount(str);
        int printcLength = Config.PrintCLength;
        var font = FontFactory.GetFont(Style.Fontname, FontStyle.Regular, Config.DefaultFont.Size);
        if (font == null)
        {
            return str;
        }

        if (alignmentRight && (length < printcLength))
        {
            str = new string(' ', printcLength - length) + str;
            width = StringMeasure.GetDisplayLength(str, font);
            while (width > printCWidth)
            {
                if (str[0] != ' ')
                    break;
                str = str.Remove(0, 1);
                width = StringMeasure.GetDisplayLength(str, font);
            }
        }
        else if ((!alignmentRight) && (length < printcLength + 1))
        {
            str += new string(' ', printcLength + 1 - length);
            width = StringMeasure.GetDisplayLength(str, font);
            while (width > printCWidthL)
            {
                if (str[^1] != ' ')
                    break;
                str = str.Remove(str.Length - 1, 1);
                width = StringMeasure.GetDisplayLength(str, font);
            }
        }
        return str;
    }

    internal void PrintButton(string str, string p)
    {
        if (string.IsNullOrEmpty(str))
            return;
        printBuffer.AppendButton(str, Style, p);
    }
    internal void PrintButton(string str, long p)
    {
        if (string.IsNullOrEmpty(str))
            return;
        printBuffer.AppendButton(str, Style, p);
    }
    internal void PrintButtonC(string str, string p, bool isRight)
    {
        if (string.IsNullOrEmpty(str))
            return;
        printBuffer.AppendButton(CreateTypeCString(str, isRight), Style, p);
    }
    internal void PrintButtonC(string str, long p, bool isRight)
    {
        if (string.IsNullOrEmpty(str))
            return;
        printBuffer.AppendButton(CreateTypeCString(str, isRight), Style, p);
    }

    internal void PrintPlain(string str)
    {
        if (string.IsNullOrEmpty(str))
            return;
        printBuffer.AppendPlainText(str, Style);
    }

    public void NewLine()
    {
        PrintFlush(true);
        RefreshStrings(false);
    }

    public ConsoleDisplayLine BufferToSingleLine(bool force, bool temporary)
    {
        if (!this.Enabled)
            return null;
        if (!force && printBuffer.IsEmpty)
            return null;
        if (force && printBuffer.IsEmpty)
            printBuffer.Append(" ", Style);
        long displayBuildStart = PerformanceMetrics.StartTiming();
        ConsoleDisplayLine dispLine = printBuffer.FlushSingleLine(stringMeasure, temporary | force_temporary);
        PerformanceMetrics.AddDisplayBuild(displayBuildStart);
        return dispLine;
    }

    public void ClearText()
    {
        window.clear_richText();
    }

    internal ConsoleDisplayLine PrintPlainwithSingleLine(string str)
    {
        if (!this.Enabled)
            return null;
        if (string.IsNullOrEmpty(str))
            return null;
        printBuffer.AppendPlainText(str, Style);
        ConsoleDisplayLine dispLine = printBuffer.FlushSingleLine(stringMeasure, false);
        return dispLine;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="force">バッファーが空でも改行する</param>
    public void PrintFlush(bool force)
    {
        if (!this.Enabled)
            return;
        if (!force && printBuffer.IsEmpty)
            return;
        if (force && printBuffer.IsEmpty)
            printBuffer.Append(" ", Style);
        long displayBuildStart = PerformanceMetrics.StartTiming();
        ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, force_temporary);
        PerformanceMetrics.AddDisplayBuild(displayBuildStart);
        //ConsoleDisplayLine[] dispList = printBuffer.Flush(stringMeasure, temporary | force_temporary);
        addRangeDisplayLine(dispList);
        //1819描画命令は分離
        //RefreshStrings(false);
    }

    /// <summary>
    /// DRAWLINE命令に対応。これのフォントを変更できると面倒なことになるのでRegularに固定する。
    /// </summary>
    public void PrintBar()
    {
        //初期に設定済みなので見る必要なし
        //if (stBar == null)
        //    setStBar(StaticConfig.DrawLineString);

        //1806beta001 CompatiDRAWLINEの廃止、CompatiLinefeedAs1739へ移行
        //CompatiLinefeedAs1739の処理はPrintStringBuffer.csで行う
        //if (Config.CompatiDRAWLINE)
        //	PrintFlush(false);
        StringStyle ss = userStyle;
        userStyle.FontStyle = FontStyle.Regular;
        Print(stBar);
        userStyle = ss;
    }

    public void printCustomBar(string barStr, bool isConst)
    {
        if (string.IsNullOrEmpty(barStr))
            throw new CodeEE(LocalizationManager.Error.EmptyDrawline);
        StringStyle ss = userStyle;
        userStyle.FontStyle = FontStyle.Regular;
        if (isConst)
            Print(barStr);
        else
            Print(getStBar(barStr));
        userStyle = ss;
    }

    public string getDefStBar()
    {
        return stBar;
    }

    public string getStBar(string barStr)
    {
        int targetWidth = Config.DrawableWidth;
        var font = Config.DefaultFont;
        int unitWidth = StringMeasure.GetDisplayLength(barStr, font);
        var builder = new StringBuilder();

        // 1文字ずつ追加して文字列全体を毎回測ると、横幅に対して二次的に遅くなる。
        // まず必要数を見積もり、フォントの字間差だけを下のループで正確に補正する。
        int repeatCount = unitWidth > 0 ? Math.Max(1, targetWidth / unitWidth + 1) : 1;
        builder.AppendJoin(null, Enumerable.Repeat(barStr, repeatCount));
        int width = StringMeasure.GetDisplayLength(builder.ToString(), font);

        while (width < targetWidth)
        {//境界を越えるまで一文字ずつ増やす
            builder.Append(barStr);
            width = StringMeasure.GetDisplayLength(builder.ToString(), font);
        }
        while (width > targetWidth)
        {//境界を越えたら、今度は超えなくなるまで一文字ずつ減らす（barStrに複数字の文字列がきた場合に対応するため）
            builder.Remove(builder.Length - 1, 1);
            width = StringMeasure.GetDisplayLength(builder.ToString(), font);
        }
        return builder.ToString();
    }

    public void setStBar(string barStr)
    {
        stBar = getStBar(barStr);
    }
    #endregion


    private bool outputLog(string fullpath)
    {
        try
        {
            var log = GetLog();
            File.WriteAllText(fullpath, log);
        }
        catch (Exception)
        {
            Dialog.Show(LocalizationManager.MsgBox.FailedOutputLog, LocalizationManager.MsgBox.FailedOutputLogError);
            return false;
        }
        return true;
    }


    public bool OutputLog(string filename)
    {
        if (filename == null)
            filename = Program.ExeDir + "emuera.log";

        if (!filename.StartsWith(Program.ExeDir, StringComparison.OrdinalIgnoreCase))
        {
            Dialog.Show(LocalizationManager.MsgBox.FailedOutputLog, LocalizationManager.MsgBox.CanOnlyOutputToSubDirectory);
            return false;
        }

        if (outputLog(filename))
        {
            if (window.Created)
            {
                PrintSystemLine(string.Format(LocalizationManager.SystemLine.LogFileHasBeenCreated,
                    filename.Replace(Program.ExeDir, "")));
                RefreshStrings(true);
            }
            return true;
        }
        else
            return false;
    }

    public string GetLog()
    {
        var builder = new StringBuilder();


        builder.AppendLine(LocalizationManager.SystemLine.Environment);
        builder.AppendLine(AssemblyData.EmueraVersionText);
        if(!string.IsNullOrEmpty(GlobalStatic.GameBaseData?.ScriptWindowTitle))
            builder.AppendLine(GlobalStatic.GameBaseData.ScriptWindowTitle);
        var patchVersionsPath = Path.Combine(Program.ExeDir, "patch_versions");
        if (Directory.Exists(patchVersionsPath))
        {
            builder.AppendLine(LocalizationManager.SystemLine.Patch);
            var versionTexts = Directory.EnumerateFiles(patchVersionsPath, "*.txt")
                    .Where(x => Path.GetExtension(x) == ".txt")
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .Select(x => File.ReadAllText(x).Trim());
            var versionText = string.Join("+", versionTexts);
            builder.AppendLine(versionText);
        }
        builder.AppendLine();
        builder.AppendLine(LocalizationManager.SystemLine.Log);
        builder.AppendLine();

        for (int i = 0; i < displayLineList.Count; i++)
        {
            builder.AppendLine(displayLineList[i].ToString());
        }
        return builder.ToString();
    }

    public ConsoleDisplayLine[] GetDisplayLines(Int64 lineNo)
    {
        if (lineNo < 0 || lineNo > displayLineList.Count)
            return null;
        int count = 0;
        List<ConsoleDisplayLine> list = [];
        for (int i = displayLineList.Count - 1; i >= 0; i--)
        {
            if (count == lineNo)
                list.Insert(0, displayLineList[i]);
            if (displayLineList[i].IsLogicalLine)
                count++;
            if (count > lineNo)
                break;
        }
        if (list.Count == 0)
            return null;
        ConsoleDisplayLine[] ret = new ConsoleDisplayLine[list.Count];
        list.CopyTo(ret);
        return ret;
    }
    public ConsoleDisplayLine[] PopDisplayingLines()
    {
        if (!this.Enabled)
            return null;
        if (printBuffer.IsEmpty)
            return null;
        return printBuffer.Flush(stringMeasure, force_temporary);
    }

}

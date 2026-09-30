using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game;
using MinorShift.Emuera.UI.Game.Image;
using MinorShift.Emuera.Web.Runtime;
using SkiaSharp;
using System.Drawing;
using System.Text;

namespace MinorShift.Emuera.GameView;

// WindowsのConsole表示意味論を保つ論理履歴を持つ。DOMを正本にせず、履歴全体の再生成を避けて可視範囲だけをWebへ投影する。
internal sealed class EmueraConsole
{
    public BrowserRuntimeStatus Status { get; private set; } = BrowserRuntimeStatus.Running;
    public bool IsRunning => Status == BrowserRuntimeStatus.Running;
    public InputRequest? PendingInput { get; private set; }
    public event Action<string>? TextPrinted;
    string windowTitle = string.Empty;
    string statusBar = "-";
    readonly List<(string Text, BrowserDisplayStyle Style, bool Plain)> printBuffer = [];
    readonly BrowserDisplayLineBuffer displayLines = new();
    readonly List<BrowserDisplayLine> publishedLines = [];
    readonly List<BrowserDisplayPart> currentDisplayParts = [];
    bool currentDisplayIsHtml;
    readonly SortedDictionary<int, List<BrowserDisplayLine>> htmlIslandLines = [];
    bool printBufferLineEnd = true;
    readonly bool[] keyDown = new bool[256];
    long lineCount;
    BrowserRuntimeStatus statusBeforePersistence;
    public string? PersistenceError { get; private set; }
    bool isTimeOut;
    ConsoleRedraw redraw = ConsoleRedraw.Normal;
    long displayGeneration;
    long displayStructureGeneration;
    long nextDisplayLineId = 1;
    long currentDisplayLineId;
    bool lastButtonIsInput = true;
    long lastButtonGeneration;
    long newButtonGeneration;
    LogicalLine? lastInputLine;
    int clientWidth;
    int clientHeight;
    Point? mouseClientPosition;
    int animationIntervalMilliseconds;
    string tooltipForeground = "#000000";
    string tooltipBackground = "#FFFFFF";
    int tooltipDelayMilliseconds = 500;
    int tooltipDurationMilliseconds;
    bool r3r3RawHtmlCaptureEnabled;
    int r3r3RawHtmlCaptureCount;
    long r3r3RawHtmlCaptureBytes;
    bool displayPerformanceMetricsEnabled;
#if ERB_EXECUTION_PROFILE
    internal Action<string>? GameplayTrace;
#endif
    long publishCalls;
    long publishElapsedTicks;
    long publishSourceLines;
    long publishResultLines;
    long animationRefreshCalls, animationRefreshElapsedTicks, animationLinesScanned, animationPartsScanned;
    long animationImagesScanned, animationImagesChanged, animationLinesChanged, animationPublishes;
    long animationNoChangePublishes, animationAllocatedBytes;

    public IReadOnlyList<BrowserDisplayLine> DisplayLines => publishedLines;
    public int IslandLineCount => htmlIslandLines.Values.Sum(lines => lines.Count);
    public long DisplayGeneration => displayGeneration;
    public long DisplayStructureGeneration => displayStructureGeneration;
    public long CurrentDisplayLineId => currentDisplayLineId;
    public long LastButtonGeneration => lastButtonGeneration;
    internal BrowserDisplayPerformanceSnapshot DisplayPerformance => new(
        publishCalls,
        publishElapsedTicks,
        publishSourceLines,
        publishResultLines,
        0,
        0,
        0,
        0,
        animationRefreshCalls, animationRefreshElapsedTicks, animationLinesScanned, animationPartsScanned,
        animationImagesScanned, animationImagesChanged, animationLinesChanged, animationPublishes,
        animationNoChangePublishes, animationAllocatedBytes);

    internal void EnableDisplayPerformanceMetrics() => displayPerformanceMetricsEnabled = true;

    internal bool IsActive => true;
    internal short GetKeyState(int keycode) => keycode is >= 0 and < 256 && keyDown[keycode] ? unchecked((short)0x8000) : (short)0;
    internal void SetKeyState(int keycode, bool down)
    {
        if (keycode is >= 0 and < 256) keyDown[keycode] = down;
    }
    internal void ClearKeyStates() => Array.Clear(keyDown);
    public bool Enabled => true;
    public bool RunERBFromMemory { get; set; }
    public bool UseSetColorStyle { get; set; }
    public bool UseUserStyle { get; set; }
    public bool MesSkip;
    public bool noOutputLog;
    public bool updatedGeneration;
    private bool emptyDisplayLine = true;
    bool bufferedDisplayParts;
    public bool EmptyLine => printBuffer.Count == 0 && !bufferedDisplayParts;
    public bool LastLineIsEmpty => emptyDisplayLine;
    public bool LastLineIsTemporary => displayLines.Count != 0 && displayLines[^1].IsTemporary;
    public long LineCount => lineCount;
    public StringStyle StringStyle { get; private set; }
    DisplayLineAlignment alignment;
    public DisplayLineAlignment Alignment
    {
        get => alignment;
        set => alignment = value;
    }
    public ConsoleRedraw Redraw => redraw;
    public SKColor bgColor = SKColors.Black;
    public int ClientWidth => clientWidth > 0 ? clientWidth : Config.WindowX;
    public int ClientHeight => clientHeight > 0 ? clientHeight : Config.WindowY;
    public string TooltipForeground => tooltipForeground;
    public string TooltipBackground => tooltipBackground;
    public string BackgroundColor => ToCss(bgColor);
    public int TooltipDelayMilliseconds => tooltipDelayMilliseconds;
    public int TooltipDurationMilliseconds => tooltipDurationMilliseconds;
    public bool IsTimeOut => isTimeOut;

    public EmueraConsole() => ResetStyle();

    public void SetViewport(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "viewport must be positive");
        clientWidth = width;
        clientHeight = height;
    }

    public void WaitInput(InputRequest request)
    {
#if WEB_RUNTIME
        if (request.TimedInputName is not null && request.TimedInputName is not ("TWAIT" or "TINPUT" or "TINPUTS" or "TONEINPUT" or "TONEINPUTS"))
            throw Unsupported(request.TimedInputName);
        if (request.TimedInputName == "TWAIT" && request.InputType is not (InputType.EnterKey or InputType.Void))
            throw new InvalidOperationException("TWAITの入力種別が不正です");
#endif
        PrintFlush(false);
        if (request.Timelimit > 0)
            isTimeOut = false;
        PendingInput = request;
        Status = BrowserRuntimeStatus.WaitingForInput;
        Publish();
    }

    public void ReadAnyKey(bool anykey = false, bool stopMesskip = false)
    {
        WaitInput(new InputRequest { InputType = anykey ? InputType.AnyKey : InputType.EnterKey, StopMesskip = stopMesskip });
    }

    public void CompleteInputGeneration(LogicalLine? currentLine)
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput || PendingInput?.NeedValue != true)
            return;
        if (!updatedGeneration && currentLine != lastInputLine)
            lastButtonGeneration = newButtonGeneration;
        else
            updatedGeneration = false;
        lastInputLine = currentLine;
        if (PendingInput.InputType == InputType.IntValue)
        {
            if (lastButtonGeneration == newButtonGeneration) newButtonGeneration++;
            else if (!lastButtonIsInput) lastButtonGeneration = newButtonGeneration;
            lastButtonIsInput = true;
        }
        else if (PendingInput.InputType == InputType.StrValue)
        {
            if (lastButtonGeneration == newButtonGeneration) newButtonGeneration++;
            else if (lastButtonIsInput) lastButtonGeneration = newButtonGeneration;
            lastButtonIsInput = false;
        }
    }

    public void Resume()
    {
        if (Status != BrowserRuntimeStatus.WaitingForInput)
            return;
        PendingInput = null;
        Status = BrowserRuntimeStatus.Running;
    }

    public void StartRunning()
    {
        if (Status == BrowserRuntimeStatus.BootstrapReady)
            Status = BrowserRuntimeStatus.Running;
    }

    public void SetTimeOut(bool value) => isTimeOut = value;

    public void MarkBootstrapReady()
    {
        PendingInput = null;
        Status = BrowserRuntimeStatus.BootstrapReady;
    }

    public void BeginPersistence()
    {
        if (Status == BrowserRuntimeStatus.Persisting)
            return;
        statusBeforePersistence = Status;
        Status = BrowserRuntimeStatus.Persisting;
    }

    public void CompletePersistence()
    {
        if (Status == BrowserRuntimeStatus.Persisting)
            Status = statusBeforePersistence;
    }

    public void FailPersistence(string message)
    {
        PrintFlush(false);
        PendingInput = null;
        PersistenceError = "永続保存失敗: " + message;
        Status = BrowserRuntimeStatus.Failed;
    }

    public void Quit()
    {
        // Webではウィンドウを閉じられないため、QUITをこのRuntime sessionの正常終了として表す。
        PrintFlush(false);
        PendingInput = null;
        if (Status != BrowserRuntimeStatus.Failed)
            Status = BrowserRuntimeStatus.Succeeded;
    }
    public void ThrowTitleError(bool error) => Fail();
    public void ThrowError(bool playSound) => Fail();

    public void Print(string value, bool lineEnd = true)
    {
        if (string.IsNullOrEmpty(value))
            return;
        int lineEndIndex = value.IndexOf('\n', StringComparison.Ordinal);
        if (lineEndIndex >= 0)
        {
            Print(value[..lineEndIndex]);
            NewLine();
            if (lineEndIndex < value.Length - 1)
                Print(value[(lineEndIndex + 1)..]);
            return;
        }
        BrowserDisplayStyle style = CurrentStyle();
        if (printBuffer.Count != 0 && !printBuffer[^1].Plain && printBuffer[^1].Style == style)
            printBuffer[^1] = (printBuffer[^1].Text + value, style, false);
        else
            printBuffer.Add((value, style, false));
        printBufferLineEnd = lineEnd;
        emptyDisplayLine = false;
    }
    public void PrintSingleLine(string value) => PrintSingleLine(value, false);
    public void PrintSingleLine(string value, bool temporary)
    {
        if (string.IsNullOrEmpty(value)) return;
        PrintFlush(false);
        TextPrinted?.Invoke(value + "\n");
        AddPlainOrButtons(value, CurrentStyle());
        CompleteDisplayLine(temporary);
        emptyDisplayLine = true;
        lineCount++;
    }
    public void PrintSystemLine(string value) => PrintSingleLine(value);
    public void PrintError(string value) => PrintSingleLine("ERROR: " + value);
    internal void PrintErrorButton(string value, ScriptPosition? position, int level = 0) => PrintError(value);
    public void PrintWarning(string value, ScriptPosition? position, int level) => PrintSingleLine($"WARN{level}: {value}");
    public void NewLine() => PrintFlush(true);
    internal void PrintPlain(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        printBuffer.Add((value, CurrentStyle(), true));
        emptyDisplayLine = false;
    }
    public void PrintFlush(bool force)
    {
        bufferedDisplayParts = false;
        if (!force && printBuffer.Count == 0)
            return;
        if (force && printBuffer.Count == 0)
        {
            printBuffer.Add((" ", CurrentStyle(), false));
            printBufferLineEnd = true;
        }
        TextPrinted?.Invoke(string.Concat(printBuffer.Select(part => part.Text)) + (printBufferLineEnd ? "\n" : string.Empty));
        FlushBufferedParts();
        if (printBufferLineEnd)
        {
            lineCount++;
            CompleteDisplayLine();
        }
        emptyDisplayLine = printBufferLineEnd;
        printBuffer.Clear();
        printBufferLineEnd = true;
    }
    public void RefreshStrings(bool forcePaint)
    {
        if (forcePaint || redraw == ConsoleRedraw.Normal) Publish();
    }
    public void ResetStyle()
    {
        UseUserStyle = false;
        UseSetColorStyle = true;
        Alignment = DisplayLineAlignment.LEFT;
        StringStyle = new()
        {
            Color = Config.ForeColor,
            ButtonColor = Config.FocusColor,
            FontStyle = FontStyle.Regular,
            Fontname = Config.FontName,
            FontSize = Config.FontSize
        };
    }
    public void SetStringStyle(FontStyle style)
    {
        StringStyle value = StringStyle;
        value.FontStyle = style;
        StringStyle = value;
    }
    public void SetStringStyle(Color color)
    {
        StringStyle value = StringStyle;
        value.Color = color;
        value.ColorChanged = color != Config.ForeColor;
        StringStyle = value;
    }
    public void SetFont(string fontname)
    {
        StringStyle value = StringStyle;
        value.Fontname = string.IsNullOrEmpty(fontname) ? Config.FontName : fontname;
        StringStyle = value;
    }
    public void SetBgColor(Color color)
    {
        bgColor = new SKColor(color.R, color.G, color.B, color.A);
        RefreshStrings(false);
    }
    public void SetRedraw(long value)
    {
        redraw = (value & 1) == 0 ? ConsoleRedraw.None : ConsoleRedraw.Normal;
        if ((value & 2) != 0) RefreshStrings(true);
    }
    public int AnimationIntervalMilliseconds => animationIntervalMilliseconds;
    public Func<string, string, int, double>? TextWidthMeasurer { get; set; }
    public void setRedrawTimer(int tickcount) => animationIntervalMilliseconds = Math.Max(0, tickcount);
    public bool RefreshAnimations(int firstLine = 0, int? endLine = null)
    {
        if (animationIntervalMilliseconds <= 0) return false;
        long started = displayPerformanceMetricsEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        long allocated = displayPerformanceMetricsEnabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
        bool changed = false;
        if (displayPerformanceMetricsEnabled) animationRefreshCalls++;
        int end = Math.Clamp(endLine ?? displayLines.Count, 0, displayLines.Count);
        for (int index = Math.Clamp(firstLine, 0, end); index < end; index++)
        {
            BrowserDisplayLine old = displayLines[index], refreshed = RefreshAnimationLine(old);
            changed |= !ReferenceEquals(old, refreshed);
            displayLines[index] = refreshed;
            if (!ReferenceEquals(old, refreshed)) publishedLines[index] = refreshed;
        }
        bool currentChanged = false;
        for (int index = 0; index < currentDisplayParts.Count; index++)
        {
            BrowserDisplayPart old = currentDisplayParts[index], refreshed = RefreshAnimationPart(old);
            changed |= !ReferenceEquals(old, refreshed);
            currentChanged |= !ReferenceEquals(old, refreshed);
            currentDisplayParts[index] = refreshed;
        }
        int publishedIndex = displayLines.Count;
        if (currentDisplayParts.Count != 0)
        {
            if (currentChanged) publishedLines[publishedIndex] = publishedLines[publishedIndex] with { Parts = currentDisplayParts.ToArray() };
            publishedIndex++;
        }
        foreach (List<BrowserDisplayLine> lines in htmlIslandLines.Values)
            for (int index = 0; index < lines.Count; index++)
            {
                BrowserDisplayLine old = lines[index], refreshed = RefreshAnimationLine(old);
                changed |= !ReferenceEquals(old, refreshed);
                lines[index] = refreshed;
                if (!ReferenceEquals(old, refreshed)) publishedLines[publishedIndex] = refreshed;
                publishedIndex++;
            }
        if (changed) Publish(imagesOnly: true);
        if (displayPerformanceMetricsEnabled)
        {
            if (changed) animationPublishes++;
            animationRefreshElapsedTicks += System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks;
            animationAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        return changed;
    }

    BrowserDisplayLine RefreshAnimationLine(BrowserDisplayLine line)
    {
        if (displayPerformanceMetricsEnabled) animationLinesScanned++;
        IReadOnlyList<BrowserDisplayPart> parts = RefreshAnimationParts(line.Parts);
        if (displayPerformanceMetricsEnabled && !ReferenceEquals(parts, line.Parts)) animationLinesChanged++;
        return ReferenceEquals(parts, line.Parts) ? line : line with { Parts = parts };
    }

    IReadOnlyList<BrowserDisplayPart> RefreshAnimationParts(IReadOnlyList<BrowserDisplayPart> parts)
    {
        BrowserDisplayPart[]? changed = null;
        for (int index = 0; index < parts.Count; index++)
        {
            BrowserDisplayPart refreshed = RefreshAnimationPart(parts[index]);
            if (!ReferenceEquals(refreshed, parts[index]))
                (changed ??= parts.ToArray())[index] = refreshed;
        }
        return changed ?? parts;
    }

    BrowserDisplayPart RefreshAnimationPart(BrowserDisplayPart part)
    {
        if (displayPerformanceMetricsEnabled)
        {
            animationPartsScanned++;
            if (part.Kind == BrowserDisplayPartKind.Image) animationImagesScanned++;
        }
        string? image = part.Kind == BrowserDisplayPartKind.Image ? AppContents.GetSpriteDataUrl(part.Text) : part.ImageDataUrl;
        if (displayPerformanceMetricsEnabled && image != part.ImageDataUrl) animationImagesChanged++;
        IReadOnlyList<BrowserDisplayPart>? children = part.Children is { } source ? RefreshAnimationParts(source) : null;
        return image == part.ImageDataUrl && ReferenceEquals(children, part.Children)
            ? part : part with { ImageDataUrl = image, Children = children };
    }
    public void Await(int time) => throw Unsupported("AWAIT");

    public void SetWindowTitle(string value) => windowTitle = value;
    public string GetWindowTitle() => windowTitle;
    public void setStBar(string value) => statusBar = value;
    public string getDefStBar() => getStBar(statusBar);
    public string getStBar(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (clientWidth <= 0) return value;
        using SKTypeface typeface = SKTypeface.FromFamilyName(Config.FontName);
        using var font = new SKFont(typeface, Math.Max(1, Config.FontSize));
        int Measure(string text) => Math.Max(0, (int)(TextWidthMeasurer?.Invoke(text, Config.FontName, Config.FontSize) ?? font.MeasureText(text)));
        int targetWidth = Math.Max(1, ClientWidth - Config.DrawingParam_ShapePositionShift);
        int unitWidth = Measure(value);
        var builder = new StringBuilder(string.Concat(Enumerable.Repeat(value, unitWidth > 0 ? Math.Max(1, targetWidth / unitWidth + 1) : 1)));
        while (unitWidth > 0 && Measure(builder.ToString()) < targetWidth) builder.Append(value);
        while (builder.Length > 0 && Measure(builder.ToString()) > targetWidth) builder.Length--;
        return builder.ToString();
    }
    public void PrintBar() => Print(getStBar(statusBar));
    public void printCustomBar(string value, bool isConst) => Print(isConst ? value : getStBar(value));

    public void ClearText()
    {
        bufferedDisplayParts = false;
        printBuffer.Clear();
        currentDisplayParts.Clear();
        currentDisplayLineId = 0;
        currentDisplayIsHtml = false;
        displayLines.Clear();
        publishedLines.Clear();
        htmlIslandLines.Clear();
        lineCount = 0;
        emptyDisplayLine = true;
        RefreshStrings(false);
    }
    public void deleteLine(int count)
    {
        if (count <= 0) return;
        int remaining = count;
        if (currentDisplayParts.Count != 0)
        {
            currentDisplayParts.Clear();
            currentDisplayLineId = 0;
            currentDisplayIsHtml = false;
            remaining--;
        }
        int remove = Math.Min(remaining, displayLines.Count);
        if (remove > 0)
            displayLines.RemoveLast(remove);
        lineCount = Math.Max(0, lineCount - remove);
        emptyDisplayLine = displayLines.Count == 0;
        RefreshStrings(false);
    }
    public void PrintTemporaryLine(string value) => PrintSingleLine(value, true);
    public void PrintC(string value, bool alignmentRight) => Print(CreateTypeCString(value, alignmentRight), true);
    internal void PrintButton(string value, string input) => AddButton(value, input, false);
    internal void PrintButton(string value, long input) => AddButton(value, input.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
    internal void PrintButtonC(string value, string input, bool isRight) => AddButton(CreateTypeCString(value, isRight), input, false);
    internal void PrintButtonC(string value, long input, bool isRight) => AddButton(CreateTypeCString(value, isRight), input.ToString(System.Globalization.CultureInfo.InvariantCulture), true);
    public void PrintImg(string value) => AddImage(value, 0);
    public void PrintShape(string type, int[] parameters)
    {
        if (printBuffer.Count > 0)
        {
            FlushBufferedParts();
            printBuffer.Clear();
        }
        AddPart(BrowserHtmlParser.ShapePart(type, parameters, CurrentStyle()));
        bufferedDisplayParts = true;
    }
    public void PrintHtml(string value, bool lineEnd)
    {
        PrintFlush(false);
        if (string.IsNullOrEmpty(value)) return;
        CaptureR3R3RawHtml("html", value, lineEnd);
        currentDisplayIsHtml = true;
        BrowserHtmlParser.Append(value, HtmlBaseStyle(), AppContents.GetSpriteDataUrl, AddPart, CompleteHtmlLine);
        TextPrinted?.Invoke(BrowserHtmlParser.PlainText(value) + (lineEnd ? "\n" : string.Empty));
        if (lineEnd)
        {
            lineCount++;
            CompleteDisplayLine();
        }
    }
    public void PrintHTMLIsland(string html, int depth = 0)
    {
        if (string.IsNullOrEmpty(html)) return;
        CaptureR3R3RawHtml("island", html, false);
        if (!htmlIslandLines.TryGetValue(depth, out List<BrowserDisplayLine>? lines))
            htmlIslandLines[depth] = lines = [];
        var parts = new List<BrowserDisplayPart>();
        void CompleteLine()
        {
            lines.Add(new("left", parts.ToArray(), LineId: nextDisplayLineId++));
            parts.Clear();
        }
        BrowserHtmlParser.Append(html, HtmlBaseStyle(), AppContents.GetSpriteDataUrl, part => parts.Add(StampButtonGenerations(part)), CompleteLine);
        if (parts.Count != 0) CompleteLine();
        Publish();
    }

    internal void EnableR3R3RawHtmlCapture()
    {
        r3r3RawHtmlCaptureEnabled = true;
        r3r3RawHtmlCaptureCount = 0;
        r3r3RawHtmlCaptureBytes = 0;
    }

    void CaptureR3R3RawHtml(string kind, string value, bool lineEnd)
    {
        if (!r3r3RawHtmlCaptureEnabled) return;
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (r3r3RawHtmlCaptureCount >= 4096 || r3r3RawHtmlCaptureBytes + bytes.Length > 16L * 1024 * 1024)
        {
            r3r3RawHtmlCaptureEnabled = false;
            System.Console.WriteLine($"R3R3HTML_LIMIT|{r3r3RawHtmlCaptureCount}|{r3r3RawHtmlCaptureBytes}");
            return;
        }
        System.Console.WriteLine($"R3R3HTML|{r3r3RawHtmlCaptureCount++}|{kind}|{(lineEnd ? 1 : 0)}|{Convert.ToBase64String(bytes)}");
        r3r3RawHtmlCaptureBytes += bytes.Length;
    }
    public void ClearHTMLIsland()
    {
        htmlIslandLines.Clear();
        Publish();
    }
    public void ClearHTMLIsland(int depth = 0)
    {
        htmlIslandLines.Remove(depth);
        Publish();
    }
    public ConsoleDisplayLine[]? GetDisplayLines(long lineNo)
    {
        if (lineNo < 0 || lineNo >= displayLines.Count) return null;
        return [new ConsoleDisplayLine(displayLines[displayLines.Count - 1 - (int)lineNo])];
    }

    public ConsoleDisplayLine[]? PopDisplayingLines()
    {
        if (printBuffer.Count == 0 && currentDisplayParts.Count == 0) return null;
        var parts = new List<BrowserDisplayPart>(currentDisplayParts);
        foreach ((string value, BrowserDisplayStyle style, bool plain) in printBuffer)
        {
            if (plain)
            {
                parts.Add(new(BrowserDisplayPartKind.Text, value, Style: style));
                continue;
            }
            foreach (ButtonPrimitive part in ButtonStringCreator.SplitButton(value))
                parts.Add(part.CanSelect
                    ? new(BrowserDisplayPartKind.Button, part.Str, part.Input.ToString(System.Globalization.CultureInfo.InvariantCulture), true, Style: style)
                    : new(BrowserDisplayPartKind.Text, part.Str, Style: style));
        }
        printBuffer.Clear();
        currentDisplayParts.Clear();
        currentDisplayLineId = 0;
        return [new ConsoleDisplayLine(new(alignment.ToString().ToLowerInvariant(), parts.Select(NormalizeDisplayText).ToArray()))];
    }

    public void CBG_Clear() => throw Unsupported("client background graphics");
    public void CBG_ClearRange(int zmin, int zmax) => throw Unsupported("client background graphics");
    public void CBG_ClearButton() => throw Unsupported("client background graphics");
    public void CBG_ClearBMap() => throw Unsupported("client background graphics");
    public bool CBG_SetGraphics(GraphicsImage image, int x, int y, int zdepth) => throw Unsupported("client background graphics");
    public bool CBG_SetImage(ASprite image, int x, int y, int zdepth) => throw Unsupported("client background graphics");
    public bool CBG_SetButtonMap(GraphicsImage image) => throw Unsupported("client background graphics");
    public bool CBG_SetButtonImage(int value, ASprite normal, ASprite selected, int x, int y, int zdepth, string? tooltip = null) => throw Unsupported("client background button");
    internal void SetMousePosition(int x, int y) => mouseClientPosition = new Point(x, y);
    internal Point GetMousePosition() => mouseClientPosition is { } position
        ? new(position.X, position.Y - ClientHeight)
        : new(0, 0);
    public void SetToolTipColor(Color foreColor, Color backColor)
    {
        tooltipForeground = $"#{foreColor.R:X2}{foreColor.G:X2}{foreColor.B:X2}";
        tooltipBackground = $"#{backColor.R:X2}{backColor.G:X2}{backColor.B:X2}";
    }
    public void SetToolTipDelay(int delay) => tooltipDelayMilliseconds = delay;
    public void SetToolTipDuration(int duration) => tooltipDurationMilliseconds = duration;

    public void DebugPrint(string value) => Print(value, false);
    public void DebugClear() => throw Unsupported("debug display clear");
    public void DebugNewLine() => NewLine();
    public void DebugAddTraceLog(string value) { }
    public void DebugRemoveTraceLog() { }
    public void DebugClearTraceLog() { }
    public bool OutputLog(string? filename) => throw Unsupported("log file output");
    public void ReloadErbFinished() { }

    void Fail()
    {
        PrintFlush(false);
        PendingInput = null;
        Status = BrowserRuntimeStatus.Failed;
    }

    void FlushBufferedParts()
    {
        foreach ((string text, BrowserDisplayStyle style, bool plain) in printBuffer)
            if (plain) AddPart(new(BrowserDisplayPartKind.Text, text, Style: style));
            else AddPlainOrButtons(text, style);
    }

    void AddPlainOrButtons(string value, BrowserDisplayStyle style)
    {
        foreach (ButtonPrimitive part in ButtonStringCreator.SplitButton(value))
            AddPart(part.CanSelect
                ? new(BrowserDisplayPartKind.Button, part.Str, part.Input.ToString(System.Globalization.CultureInfo.InvariantCulture), true, Style: style)
                : new(BrowserDisplayPartKind.Text, part.Str, Style: style));
    }

    void AddButton(string value, string input, bool integer)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (printBuffer.Count > 0)
        {
            FlushBufferedParts();
            printBuffer.Clear();
        }
        AddPart(new(BrowserDisplayPartKind.Button, value, input, integer, Style: CurrentStyle()));
        bufferedDisplayParts = true;
        emptyDisplayLine = false;
    }

    static string CreateTypeCString(string value, bool alignmentRight)
    {
        if (string.IsNullOrEmpty(value)) return value;
        int length = Config.Encode.GetByteCount(value);
        int target = Config.PrintCLength + (alignmentRight ? 0 : 1);
        if (length >= target) return value;
        string padding = new(' ', target - length);
        return alignmentRight ? padding + value : value + padding;
    }

    void AddImage(string name, int height)
    {
        string? data = AppContents.GetSpriteDataUrl(name);
        AddPart(new(BrowserDisplayPartKind.Image, name, ImageDataUrl: data, Height: height, Style: CurrentStyle()));
        bufferedDisplayParts = true;
        emptyDisplayLine = false;
    }

    void AddPart(BrowserDisplayPart part)
    {
        if (currentDisplayLineId == 0)
            currentDisplayLineId = nextDisplayLineId++;
        currentDisplayParts.Add(StampButtonGenerations(part));
        emptyDisplayLine = false;
    }

    BrowserDisplayPart StampButtonGenerations(BrowserDisplayPart part)
    {
        IReadOnlyList<BrowserDisplayPart>? children = part.Children?.Select(StampButtonGenerations).ToArray();
        part = NormalizeDisplayText(part) with { Children = children };
        if (part.Input is null) return part;
        long generation = newButtonGeneration;
        lastButtonGeneration = generation;
        updatedGeneration = true;
        return part with { Children = children, ButtonGeneration = generation };
    }

    BrowserDisplayPart NormalizeDisplayText(BrowserDisplayPart part)
    {
        if (part.Kind is not (BrowserDisplayPartKind.Text or BrowserDisplayPartKind.Button or BrowserDisplayPartKind.NonButton)) return part;
        part = part with { Text = part.Text.Replace("\t", "", StringComparison.Ordinal) };
        if (part.Kind == BrowserDisplayPartKind.Text && part.Style is { FontSize: > 0 } style
            && style.FontSize != Config.FontSize && TextWidthMeasurer is not null)
        {
            // Nativeは拡大可能な字幅とConfig.LineHeightを使う。Chromeの小サイズbitmap fontは字幅を整数化するため、自動幅グループが広がる場合がある。
            int width = (int)(TextWidthMeasurer(part.Text, style.FontName ?? Config.FontName, Config.FontSize)
                * style.FontSize / Config.FontSize);
            double paintedWidth = TextWidthMeasurer(part.Text, style.FontName ?? Config.FontName, style.FontSize);
            part = part with { Width = width, Height = Config.LineHeight,
                TextPaintScale = paintedWidth > 0 ? width / paintedWidth : 1 };
        }
        return part;
    }

    BrowserDisplayStyle HtmlBaseStyle() => new(ToCss(Config.ForeColor), ToCss(Config.FocusColor), FontName: Config.FontName, FontSize: Config.FontSize);

    BrowserDisplayStyle CurrentStyle() => new(
        ToCss(StringStyle.Color),
        ToCss(StringStyle.ButtonColor),
        null,
        StringStyle.FontStyle.HasFlag(FontStyle.Bold),
        StringStyle.FontStyle.HasFlag(FontStyle.Italic),
        StringStyle.FontStyle.HasFlag(FontStyle.Underline),
        StringStyle.FontStyle.HasFlag(FontStyle.Strikeout),
        StringStyle.Fontname,
        StringStyle.FontSize);

    static string ToCss(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    static string ToCss(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    void CompleteDisplayLine(bool temporary = false)
    {
        if (LastLineIsTemporary)
        {
            displayLines.RemoveLast(1);
            lineCount = Math.Max(0, lineCount - 1);
        }
        long lineId = currentDisplayLineId == 0 ? nextDisplayLineId++ : currentDisplayLineId;
        displayLines.Add(new(currentDisplayIsHtml ? "left" : alignment.ToString().ToLowerInvariant(), currentDisplayParts.ToArray(), LineId: lineId, IsTemporary: temporary));
        if (displayLines.Count > Config.MaxLog)
            displayLines.RemoveFirst();
        currentDisplayParts.Clear();
        currentDisplayIsHtml = false;
        currentDisplayLineId = 0;
        RefreshStrings(false);
    }

    void CompleteHtmlLine()
    {
        lineCount++;
        CompleteDisplayLine();
    }

    void Publish(bool imagesOnly = false)
    {
#if ERB_EXECUTION_PROFILE
        GameplayTrace?.Invoke(imagesOnly ? "visual-publish-start" : "publish-start");
#endif
        bool measure = displayPerformanceMetricsEnabled;
        long started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        int sourceLines = measure && !imagesOnly ? displayLines.Count : 0;
        displayGeneration++;
        if (!imagesOnly) displayStructureGeneration++;
        if (!imagesOnly)
        {
            publishedLines.Clear();
            publishedLines.AddRange(displayLines);
            if (currentDisplayParts.Count != 0)
                publishedLines.Add(new(currentDisplayIsHtml ? "left" : alignment.ToString().ToLowerInvariant(), currentDisplayParts.ToArray(), displayGeneration, currentDisplayLineId));
            foreach (List<BrowserDisplayLine> lines in htmlIslandLines.Values)
                publishedLines.AddRange(lines);
        }
        if (measure)
        {
            publishCalls++;
            publishElapsedTicks += System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks;
            publishSourceLines += sourceLines;
            publishResultLines += publishedLines.Count;
        }
#if ERB_EXECUTION_PROFILE
        GameplayTrace?.Invoke(imagesOnly ? "visual-publish-end" : "publish-end");
#endif
    }

    static UnsupportedRuntimeFeatureException Unsupported(string feature) => new(feature);
}

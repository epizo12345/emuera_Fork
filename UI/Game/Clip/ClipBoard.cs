using MinorShift.Emuera.GameView;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI.Game;

namespace MinorShift.Emuera.GameProc.Function;

internal partial class ClipboardProcessor
{
    private readonly bool classicMode; // New Lines Only mode

    private readonly Forms.MainWindow mainWin;

    private bool minTimePassed; //Has enough time passed since the last Clipboard update?
    private bool postWaiting; //Is there text waiting to be sent to clipboard?
    private static System.Timers.Timer minTimer = null; //Minimum timer for refrehsing the clipboard to prevent spam

    private int MaxCB; //Max length in lines of the output to clipboard
    private int ScrollPos; //Position of the clipboard output in the buffer
    private int ScrollCount; //Lines to scroll at a time
    private int NewLineCount; //Number of new lines
    private int OldNewLineCount; //Number of lines in the last update, used for Classic mode + scrolling back to bottom
    private StringBuilder OldText; //Last set of lines sent to the clipboard
    private CircularBuffer<string> lineBuffer; //Buffer for processed strings ready for clipboard

    private bool Initialized;

    internal enum CBTriggers
    {
        LeftClick,
        MiddleClick,
        DoubleLeftClick,
        AnyKeyWait,
        InputWait,
    }

    public ClipboardProcessor(Forms.MainWindow parent)
    {
        classicMode = JSONConfig.User.CBNewLinesOnly;

        mainWin = parent;

        minTimePassed = true;
        postWaiting = false;

        MaxCB = JSONConfig.User.CBMaxCB;
        ScrollPos = 0; //FIXIT - Expand it, add a button, etc
        ScrollCount = JSONConfig.User.CBScrollCount; //FIXIT - Actually use it
        NewLineCount = 0;
        OldNewLineCount = 0;

        if (!JSONConfig.User.CBUseClipboard) return;

        Init();
    }

    public void Init()
    {
        if(Initialized)
            return;
        lineBuffer = new CircularBuffer<string>(JSONConfig.User.CBBufferSize);
        minTimer = new System.Timers.Timer(JSONConfig.User.CBMinTimer) { AutoReset = false };
        minTimer.Elapsed += MinTimerDone;
        OldText = new StringBuilder();
        Initialized = true;
    }

    public void Reset()
    {
        lineBuffer = null;
        minTimer.Dispose();
        OldText = null;
        Initialized = false;
    }

    public void SetTimerInterval(int interval)
    {
        if(!Initialized)
            return;
        minTimer.Interval = interval;
    }

    public void SetMaxCB(int value)
    {
        MaxCB = value;
    }

    public void SetScrollCount(int value)
    {
        ScrollCount = value;
    }

    public bool ScrollUp(int value)
    {
        if (!JSONConfig.User.CBUseClipboard) return false;
        if (ScrollPos == 0 && classicMode && ScrollCount > OldNewLineCount) ScrollPos = OldNewLineCount;
        else ScrollPos += ScrollCount * value;
        if (lineBuffer.Count < ScrollPos) ScrollPos = lineBuffer.Count - ScrollCount;
        SendToCB(true);
        return true;
    }

    public bool ScrollDown(int value)
    {
        if (!JSONConfig.User.CBUseClipboard) return false;
        ScrollPos -= ScrollCount;
        if (ScrollPos < 0) ScrollPos = 0;
        SendToCB(true);

        return true;
    }

    private void MinTimerDone(object source, System.Timers.ElapsedEventArgs e)
    {
        minTimePassed = true;
        if (postWaiting) SendToCB(true);
    }

    private bool MinTimeCheck()
    {
        if (minTimePassed)
        {
            minTimePassed = false;
            minTimer.Start();
            return true;
        }
        else return false;
    }

    //FIXIT - Autoprocess old lines or just ditch?
    public void AddLine(ConsoleDisplayLine inputLine, bool left)
    {
        if (!JSONConfig.User.CBUseClipboard) return;

        NewLineCount++;
        string processed = ProcessLine(inputLine.ToString());
        lineBuffer.Enqueue(processed);
    }

    public void DelLine(int count)
    {
        if (!JSONConfig.User.CBUseClipboard || count <= 0) return;

        NewLineCount = Math.Max(0, NewLineCount - count);
        if (count >= lineBuffer.Count) lineBuffer.Clear();
        else
        {
            while (count > 0)
            {
                lineBuffer.Dequeue();
                count--;
            }
        }
    }

    public void ClearScreen()
    {
        if (!JSONConfig.User.CBUseClipboard) return;
        if (JSONConfig.User.CBClearBuffer)
        {
            lineBuffer.Clear();
            ScrollPos = 0;
            NewLineCount = 0;
        }
        else
        {
            lineBuffer.Enqueue("");
        }
    }

    public void Check(CBTriggers type)
    {
        if (!JSONConfig.User.CBUseClipboard) return;
        switch (type)
        {
            case CBTriggers.LeftClick:
                if (!JSONConfig.User.CBTriggerLeftClick) return;
                break;

            case CBTriggers.MiddleClick:
                if (!JSONConfig.User.CBTriggerMiddleClick) return;
                break;

            case CBTriggers.DoubleLeftClick:
                if (!JSONConfig.User.CBTriggerDoubleLeftClick) return;
                break;

            case CBTriggers.AnyKeyWait:
                if (!JSONConfig.User.CBTriggerAnyKeyWait) return;
                break;

            case CBTriggers.InputWait:
                if (!JSONConfig.User.CBTriggerInputWait) return;
                break;

            default:
                return;
        }

        ScrollPos = 0;
        SendToCB(false);
    }

    private void SendToCB(bool force)
    {
        if (!JSONConfig.User.CBUseClipboard) return;
        if (NewLineCount == 0 && !force) return;
        if (!MinTimeCheck())
        {
            postWaiting = true;
            return;
        }

        if (NewLineCount == 0 && ScrollPos == 0) NewLineCount = OldNewLineCount;

        int length;
        if (classicMode && ScrollPos == 0) length = Math.Min(NewLineCount, lineBuffer.Count);
        else length = Math.Min(MaxCB, lineBuffer.Count - ScrollPos);
        if (length <= 0) return;

        var builder = new StringBuilder();
        for (int count = 0; count < length; count++)
        {
            builder.AppendLine(lineBuffer[lineBuffer.Count - length - ScrollPos + count]);
        }
        var newText = builder.ToString();
        if (newText.Equals(OldText.ToString())) return;
        try
        {
            mainWin.Invoke(() => Clipboard.SetDataObject(newText, false, 3, 200));
            if (ScrollPos == 0) OldNewLineCount = NewLineCount;
            NewLineCount = 0;
            OldText = builder;
            postWaiting = false;
        }
        catch (Exception)
        {
            //FIXIT - For now it just fails silently
        }
    }


    [GeneratedRegex("<.*?>")]
    private static partial Regex HTMLTagRegex();

    public static string StripHTML(string input)
    {
        // still faster to use String.Contains to check if we need to do this at all first, supposedly
        if (JSONConfig.User.CBIgnoreTags && input.Contains('<'))
        {
            // regex is faster and simpler than a for loop you nerds
            return HTMLTagRegex().Replace(input, JSONConfig.User.CBReplaceTags);
        }
        return input;
    }

    private static string ProcessLine(string input)
    {
        return StripHTML(input);
    }
}

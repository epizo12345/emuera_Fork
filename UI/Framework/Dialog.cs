using System.Windows.Forms;
static class Dialog
{
    public enum Result
    {
        Yes,
        No
    }
    public static void Show(string text)
    {
        MessageBox.Show(text);
    }
    public static void Show(string title, string text)
    {
        MessageBox.Show(text, title);
    }
    public static bool ShowPrompt(string title, string text)
    {
        var result = MessageBox.Show(text, title, MessageBoxButtons.YesNo);
        return result switch
        {
            DialogResult.Yes => true,
            _ => false
        };
    }

    private static System.Diagnostics.Stopwatch startupTestPromptStopwatch;
    private static int startupTestPromptCount;

    public static bool ShowInfiniteLoopPrompt(string title, string text)
    {
        if (!MinorShift.Emuera.Program.StartupTestMode)
            return ShowPrompt(title, text);

        startupTestPromptStopwatch ??= System.Diagnostics.Stopwatch.StartNew();
        int count = ++startupTestPromptCount;
        bool stop = count > 12 || startupTestPromptStopwatch.ElapsedMilliseconds > 60_000;
        System.IO.File.AppendAllText(MinorShift.Emuera.Program.ExeDir + "startup-test-infinite-loop.log",
            $"count={count};elapsedMs={startupTestPromptStopwatch.ElapsedMilliseconds};continue={!stop};title={title}{System.Environment.NewLine}");
        return stop;
    }
}

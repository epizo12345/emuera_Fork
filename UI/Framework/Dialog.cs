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
#if R0_B1
        if (MinorShift.Emuera.Runtime.Diagnostics.B1Proof.Active) throw new System.InvalidOperationException("B1 headless dialog: " + text);
#endif
        MessageBox.Show(text);
    }
    public static void Show(string title, string text)
    {
#if R0_B1
        if (MinorShift.Emuera.Runtime.Diagnostics.B1Proof.Active) throw new System.InvalidOperationException("B1 headless dialog: " + title + " " + text);
#endif
        MessageBox.Show(text, title);
    }
    public static bool ShowPrompt(string title, string text)
    {
#if R0_B1
        if (MinorShift.Emuera.Runtime.Diagnostics.B1Proof.Active) throw new System.InvalidOperationException("B1 headless prompt: " + title + " " + text);
#endif
        var result = MessageBox.Show(text, title, MessageBoxButtons.YesNo);
        return result switch
        {
            DialogResult.Yes => true,
            _ => false
        };
    }
}

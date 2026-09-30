namespace EmueraWebPackager;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args is ["--version"]) { Console.WriteLine($"Emuera Web Packager {Packager.Version} / Runtime {Packager.RuntimeVersion}"); return; }
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show($"操作に失敗しました。\n{e.Exception.Message}", "Emuera Web Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try { Application.Run(new MainForm()); }
        catch (Exception ex) { MessageBox.Show($"起動に失敗しました。\n{ex}", "Emuera Web Packager"); Environment.ExitCode = 1; }
    }
}

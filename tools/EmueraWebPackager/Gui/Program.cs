namespace EmueraWebPackager;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args is ["--version"]) { Console.WriteLine($"Emuera Web Packager {Packager.Version} / Runtime {Packager.RuntimeVersion}"); return; }
        if (args.Length > 0)
        {
            try
            {
                if (args is not ["--package", var game, var output]) throw new ArgumentException("Usage: EmueraWebPackager.exe --package <game-folder> <new-output-folder>");
                if (Directory.Exists(output) || File.Exists(output)) throw new IOException("新しい出力先を指定してください。既存出力は上書きしません。");
                var result = Packager.Build(game, output, Path.Combine(AppContext.BaseDirectory, "runtime-template"), true, Console.WriteLine);
                Console.WriteLine($"PASS: {result.FileCount} files / {result.Bytes} bytes / {result.ItchZip}");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show($"操作に失敗しました。\n{e.Exception.Message}", "Emuera Web Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try { Application.Run(new MainForm()); }
        catch (Exception ex) { MessageBox.Show($"起動に失敗しました。\n{ex}", "Emuera Web Packager"); Environment.ExitCode = 1; }
    }
}

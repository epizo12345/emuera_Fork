using EmueraWebPackager;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize(); using var form = new MainForm(); form.CreateControl(); form.PerformLayout();
            Control Find(string name) => form.Controls.Find(name, true).Single();
            foreach (string name in new[] { "gameFolder", "outputFolder", "build", "state", "progress", "preview", "stop" })
                if (Find(name).Width <= 0 || Find(name).Height <= 0) throw new Exception($"Missing/empty GUI control: {name}");
            if (Find("preview").Enabled || Find("stop").Enabled) throw new Exception("Server/browser must not start automatically");
            // Render the standalone panel, not an invisible Form (WinForms skips hidden descendants).
            Control panel = form.Controls[0]; form.Controls.Remove(panel); panel.Dock = DockStyle.None; panel.Size = form.ClientSize; panel.PerformLayout(); panel.CreateControl();
            using var bitmap = new Bitmap(panel.Width, panel.Height); panel.DrawToBitmap(bitmap, panel.ClientRectangle);
            int colors = new HashSet<int>(Enumerable.Range(0, bitmap.Width / 4).SelectMany(x => Enumerable.Range(0, bitmap.Height / 4).Select(y => bitmap.GetPixel(x * 4, y * 4).ToArgb()))).Count();
            if (colors < 10) throw new Exception("Offscreen screenshot is blank");
            Directory.CreateDirectory(args[0]); bitmap.Save(Path.Combine(args[0], "gui-offscreen.png"));
            form.Controls.Add(panel); panel.Dock = DockStyle.Fill; form.PerformLayout();
            string source = Path.GetFullPath(Path.Combine(args[0], "gui-input")), output = Path.GetFullPath(Path.Combine(args[0], "gui-web"));
            Directory.CreateDirectory(Path.Combine(source, "Data", "ERB")); Directory.CreateDirectory(Path.Combine(source, "Data", "CSV"));
            File.WriteAllText(Path.Combine(source, "emuera.config"), "Gui input config"); File.WriteAllText(Path.Combine(source, "Data", "ERB", "LOCAL.ERB"), "@SYSTEM_TITLE\nPRINTL GUI\nINPUT\n");
            string template = Path.Combine(AppContext.BaseDirectory, "runtime-template"); Directory.CreateDirectory(Path.Combine(template, "_framework"));
            File.WriteAllText(Path.Combine(template, "index.html"), "<base href=\"/\">"); File.WriteAllText(Path.Combine(template, "_framework", "runtime.js"), "test template"); Packager.SealTemplate(template);
            ((TextBox)Find("gameFolder")).Text = source; ((TextBox)Find("outputFolder")).Text = output;
            _ = form.Handle;
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Find("build"), [EventArgs.Empty]);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !Find("state").Text.StartsWith("パッケージング完了")) { Application.DoEvents(); Thread.Sleep(10); }
            if (!Find("state").Text.StartsWith("パッケージング完了") || !File.Exists(output + "-itch.zip") || !Find("preview").Enabled) throw new Exception("GUI build event did not generate verified Web/itch output");
            Packager.VerifyWebZip(output, output + "-itch.zip");
            Packager.WriteJson(Path.Combine(args[0], "result.json"), new { status = "PASS_GUI_OFFSCREEN", buildEventPassed = true, noVisibleWindow = true, noPhysicalInput = true, browserOpened = false, title = form.Text, width = form.Width, height = form.Height, buildState = Find("state").Text });
            Console.WriteLine("PASS_GUI_OFFSCREEN"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL GUI-offscreen\n{ex}"); return 1; }
    }
}

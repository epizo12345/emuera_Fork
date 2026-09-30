using System.Diagnostics;

namespace EmueraWebPackager;

public sealed class MainForm : Form
{
    readonly TextBox game = new() { Dock = DockStyle.Fill, Name = "gameFolder" };
    readonly TextBox output = new() { Dock = DockStyle.Fill, Name = "outputFolder" };
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Name = "progress" };
    readonly Label differences = new() { AutoSize = true, Text = "変更 0　追加 0　削除 0", Name = "differences" };
    readonly Label state = new() { AutoSize = true, Text = "ゲームと出力先を選択してください", Name = "state" };
    readonly CheckBox itch = new() { AutoSize = true, Checked = true, Text = "itch.io用ZIPも作成" };
    readonly Button build = new() { Text = "Web版を作成", AutoSize = true, Name = "build" };
    readonly Button preview = new() { Text = "ローカルでテスト起動", AutoSize = true, Enabled = false, Name = "preview" };
    readonly Button stop = new() { Text = "サーバー停止", AutoSize = true, Enabled = false, Name = "stop" };
    readonly Button details = new() { Text = "変更内容を表示", AutoSize = true };
    readonly Button open = new() { Text = "出力フォルダを開く", AutoSize = true, Enabled = false };
    Difference difference = new([], [], []);
    PreviewServer? server;
    bool busy;
    string? builtOutput;
    string Template => Path.Combine(AppContext.BaseDirectory, "runtime-template");
    public MainForm()
    {
        Text = "Emuera Web Packager"; Font = new Font("Yu Gothic UI", 10); ClientSize = new(820, 520); MinimumSize = new(760, 500);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 9 };
        layout.ColumnStyles.Add(new(SizeType.AutoSize)); layout.ColumnStyles.Add(new(SizeType.Percent, 100)); layout.ColumnStyles.Add(new(SizeType.AutoSize));
        Controls.Add(layout);
        void Full(Control control, int row) { layout.Controls.Add(control, 0, row); layout.SetColumnSpan(control, 3); }
        Full(new Label { Text = $"Web Runtime: {Packager.RuntimeVersion}", AutoSize = true }, 0);
        layout.Controls.Add(new Label { Text = "ゲームフォルダ:", AutoSize = true }, 0, 1); layout.Controls.Add(game, 1, 1);
        layout.Controls.Add(new Label { Text = "出力先:", AutoSize = true }, 0, 2); layout.Controls.Add(output, 1, 2);
        var browseGame = new Button { Text = "参照...", AutoSize = true }; var browseOutput = new Button { Text = "参照...", AutoSize = true };
        layout.Controls.Add(browseGame, 2, 1); layout.Controls.Add(browseOutput, 2, 2);
        var diffRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; diffRow.Controls.AddRange([differences, details]); Full(diffRow, 3);
        var action = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; action.Controls.AddRange([build, itch]); Full(action, 4);
        Full(state, 5); Full(log, 6); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        var bottom = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; bottom.Controls.AddRange([preview, stop, open]); Full(bottom, 7);
        Full(new Label { Text = "セーブデータは配布物に含めません。別ゲームは別URL/originで配信してください。", AutoSize = true }, 8);
        browseGame.Click += (_, _) => Browse(game); browseOutput.Click += (_, _) => Browse(output);
        details.Click += async (_, _) =>
        {
            if (busy) return;
            try
            {
                SetBusy(true); var snapshot = await Task.Run(() => Packager.Capture(game.Text)); difference = Packager.Compare(snapshot, Packager.Previous(output.Text)); ShowDifference();
                using var dialog = new Form { Text = "現在入力と前回との差分", ClientSize = new Size(760, 480), StartPosition = FormStartPosition.CenterParent };
                dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                    Text = $"変更:\r\n{string.Join("\r\n", difference.Changed)}\r\n\r\n追加:\r\n{string.Join("\r\n", difference.Added)}\r\n\r\n削除:\r\n{string.Join("\r\n", difference.Deleted)}" });
                dialog.ShowDialog(this);
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        };
        build.Click += async (_, _) =>
        {
            try
            {
                string resolved = Packager.ResolveGameRoot(game.Text);
                if (!Path.GetFullPath(game.Text).TrimEnd('\\').Equals(resolved, StringComparison.OrdinalIgnoreCase) && MessageBox.Show($"検出したゲームフォルダを使用しますか？\n{resolved}", "ゲームフォルダ確認", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                game.Text = resolved; string destination = output.Text; bool makeItch = itch.Checked;
                if (server != null) { MessageBox.Show("更新前に、このツールのサーバーを停止してください"); return; }
                SetBusy(true); log.Clear();
                string config = File.Exists(Path.Combine(resolved, "Data", "emuera.config")) ? Path.Combine(resolved, "Data", "emuera.config") : Path.Combine(resolved, "emuera.config");
                log.AppendText($"使用config: {config}\r\n");
                var progress = new Progress<string>(s => { state.Text = s; log.AppendText(s + "\r\n"); });
                var result = await Task.Run(() => Packager.Build(resolved, destination, Template, makeItch, s => ((IProgress<string>)progress).Report(s)));
                difference = result.Difference; ShowDifference(); builtOutput = result.Output;
                state.Text = $"パッケージング完了　入力ファイル: {result.FileCount:N0}　生成パッケージ: {result.Packs.Length}　エラー: 0";
                preview.Enabled = open.Enabled = true;
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        };
        preview.Click += (_, _) =>
        {
            try { server ??= new PreviewServer(builtOutput!); stop.Enabled = true; state.Text = $"テストURL: {server.Url}（このツール所有server）"; Process.Start(new ProcessStartInfo(server.Url) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        };
        stop.Click += (_, _) => { server?.Dispose(); server = null; stop.Enabled = false; state.Text = "このツールのサーバーを停止しました"; };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(builtOutput!) { UseShellExecute = true });
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; MessageBox.Show("パッケージング完了までお待ちください"); return; }
            if (server != null && MessageBox.Show("このツールのサーバーを停止して終了しますか？", "終了", MessageBoxButtons.YesNo) != DialogResult.Yes) { e.Cancel = true; return; }
            server?.Dispose();
        };
    }
    void Browse(TextBox box) { if (busy) return; using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = box == game ? "Dataを含むゲームフォルダを選択" : "Web出力フォルダを選択" }; if (dialog.ShowDialog() == DialogResult.OK) box.Text = dialog.SelectedPath; }
    void SetBusy(bool value) { busy = value; build.Enabled = details.Enabled = game.Enabled = output.Enabled = itch.Enabled = !value; preview.Enabled = !value && builtOutput != null; }
    void ShowDifference() => differences.Text = $"変更 {difference.Changed.Length}　追加 {difference.Added.Length}　削除 {difference.Deleted.Length}";
    void ShowError(Exception ex) { state.Text = "エラー: 1（既存出力は維持）"; log.AppendText(ex + "\r\n"); MessageBox.Show(ex.Message, "パッケージングに失敗しました", MessageBoxButtons.OK, MessageBoxIcon.Error); }
}

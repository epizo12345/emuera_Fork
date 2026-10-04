using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmueraWebPackager;

public sealed record InputFile(string Path, string Source, long Size, string Sha256, string Category);
public sealed record Snapshot(List<InputFile> Files, string AggregateSha256, bool HasSaves);
public sealed record Difference(string[] Changed, string[] Added, string[] Deleted);
public sealed record PackageResult(string Output, int FileCount, long Bytes, string[] Packs, Difference Difference, string ManifestSha256, string? ItchZip);

public static class Packager
{
    public const string Version = "1.0.7";
    public const string RuntimeVersion = "UX16-08-RC3-Compat107";
    const string Owner = "EmueraWebPackager";
    const long PackLimit = 128L * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "sav", "reports", "artifacts", "bin", "obj", ".git", "node_modules", "fixture_base", "test", "tests", "chrome", "chrome-profiles" };
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".webp", ".bmp", ".gif", ".jpg", ".jpeg", ".svg", ".ico", ".tif", ".tiff" };
    public static string HashFile(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    static bool Within(string path, string root) => Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static void NoLinks(string path)
    {
        for (var item = new DirectoryInfo(Directory.Exists(path) ? path : Path.GetDirectoryName(Full(path))!); item != null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"リンクdirectoryは使用できません: {item.FullName}");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"リンクfileは使用できません: {path}");
    }
    public static string ResolveGameRoot(string selected)
    {
        selected = Full(selected); NoLinks(selected);
        bool Root(string path) => Directory.Exists(Path.Combine(path, "Data")) && (File.Exists(Path.Combine(path, "Data", "emuera.config")) || File.Exists(Path.Combine(path, "emuera.config")));
        if (Root(selected)) { ValidateRoot(selected); return selected; }
        var alternatives = new List<string>();
        string? parent = Path.GetDirectoryName(selected);
        if (parent != null && Root(parent)) alternatives.Add(parent);
        if (Directory.Exists(selected)) alternatives.AddRange(Directory.EnumerateDirectories(selected).Where(Root));
        if (alternatives.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1) { ValidateRoot(alternatives[0]); return alternatives[0]; }
        if (alternatives.Count > 1) throw new IOException("ゲームフォルダの候補が複数あります。Dataを含むフォルダを選択してください");
        ValidateRoot(selected); return selected;
    }
    static void ValidateRoot(string root)
    {
        string data = Path.Combine(root, "Data");
        if (!Directory.Exists(data)) throw new IOException("Data フォルダが見つかりません");
        if (!Directory.Exists(Path.Combine(data, "ERB"))) throw new IOException("ERB ディレクトリが見つかりません");
        if (!Directory.Exists(Path.Combine(data, "CSV"))) throw new IOException("CSV ディレクトリが見つかりません");
        if (!File.Exists(Path.Combine(data, "emuera.config")) && !File.Exists(Path.Combine(root, "emuera.config"))) throw new IOException("emuera.config が見つかりません");
    }
    static IEnumerable<string> Walk(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        NoLinks(directory);
        foreach (string file in Directory.EnumerateFiles(directory)) { NoLinks(file); yield return file; }
        foreach (string child in Directory.EnumerateDirectories(directory))
            if (!Excluded.Contains(Path.GetFileName(child))) foreach (string file in Walk(child)) yield return file;
    }
    static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
    static bool SafeDataPath(string relative) => !relative.StartsWith('/') && !relative.Contains(':') && relative.Split('/').All(p => p is not "" and not "." and not ".." && !Excluded.Contains(p)) && !relative.EndsWith(".sav", StringComparison.OrdinalIgnoreCase);
    public static Snapshot Capture(string root)
    {
        root = ResolveGameRoot(root); string data = Path.Combine(root, "Data");
        var selected = new Dictionary<string, (string Source, string Category)>(StringComparer.OrdinalIgnoreCase);
        void Add(string file, string category, string? logical = null)
        {
            NoLinks(file); string path = logical ?? Relative(data, file);
            if (!SafeDataPath(path)) throw new IOException($"安全でない入力パスです: {path}");
            if (selected.TryGetValue(path, out var existing) && existing.Source != file) throw new IOException($"大小文字を無視したパス衝突です: {path}");
            selected.TryAdd(path, (file, category));
        }
        foreach (string file in Walk(Path.Combine(data, "CSV"))) if (new[] { ".csv", ".config" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) Add(file, "csv");
        foreach (string file in Walk(Path.Combine(data, "ERB"))) if (new[] { ".erb", ".erh" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) Add(file, "erb");
        foreach (string name in new[] { "emuera.config", "setting.json", "setting_user.json", "macro.txt" })
        {
            string file = Path.Combine(data, name);
            if (name == "emuera.config" && !File.Exists(file)) file = Path.Combine(root, name);
            if (File.Exists(file)) Add(file, "config", name);
        }
        string resources = Path.Combine(data, "resources");
        string[] resourceFiles = Walk(resources).ToArray();
        var resourceLookup = resourceFiles.ToDictionary(f => Full(f), StringComparer.OrdinalIgnoreCase);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (string csv in resourceFiles.Where(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            Add(csv, "resources");
            foreach (string raw in File.ReadLines(csv, Encoding.GetEncoding(932)))
            {
                string line = raw.Trim(); if (line.Length == 0 || line.StartsWith(';')) continue;
                string[] tokens = line.Split(','); if (tokens.Length < 2 || !tokens[1].Contains('.')) continue;
                string requested = Full(Path.Combine(Path.GetDirectoryName(csv)!, tokens[1].Replace('\\', Path.DirectorySeparatorChar)));
                if (!Within(requested, data) || !SafeDataPath(Relative(data, requested))) throw new IOException($"resource参照が安全なData範囲外です: {csv}: {tokens[1]}");
                if (resourceLookup.TryGetValue(requested, out string? actual)) Add(actual, "resources");
                // Existing P1B accepts missing resource parents; packaging does not change that game behavior.
            }
        }
        foreach (string file in resourceFiles)
        {
            string path = Relative(data, file);
            string category = path.StartsWith("resources/10_キャラ画像/", StringComparison.OrdinalIgnoreCase) ? "face"
                : path.StartsWith("resources/20_ダンジョン画像/", StringComparison.OrdinalIgnoreCase) ? "dungeon"
                : path.StartsWith("resources/タイトル画像/", StringComparison.OrdinalIgnoreCase) ? "title" : "resources";
            // Later authority packs include license notices as well as images; retain those bytes too.
            if (!Images.Contains(Path.GetExtension(file)) && !(category != "resources" && Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase))) continue;
            Add(file, category);
        }
        var files = selected.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new InputFile(p.Key, p.Value.Source, new FileInfo(p.Value.Source).Length, HashFile(p.Value.Source), p.Value.Category)).ToList();
        string signature = string.Join('\n', files.Select(f => $"{f.Path}\t{f.Size}\t{f.Sha256}\t{f.Category}"));
        bool hasSaves = Directory.Exists(Path.Combine(data, "sav")) || Walk(data).Any(f => f.EndsWith(".sav", StringComparison.OrdinalIgnoreCase));
        return new(files, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature))), hasSaves);
    }
    public static Difference Compare(Snapshot current, Snapshot? previous)
    {
        var old = previous?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? new();
        var now = current.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        return new(now.Values.Where(f => old.TryGetValue(f.Path, out var p) && (p.Size != f.Size || p.Sha256 != f.Sha256 || p.Category != f.Category)).Select(f => f.Path).ToArray(),
            now.Keys.Where(p => !old.ContainsKey(p)).ToArray(), old.Keys.Where(p => !now.ContainsKey(p)).ToArray());
    }
    public static Snapshot? Previous(string output) => File.Exists(output + ".packager.json") ? Read<Snapshot>(output + ".packager.json") : null;
    public static T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) ?? throw new IOException($"JSONが不正です: {file}");
    public static void WriteJson(string file, object value) => File.WriteAllText(file, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
    public static void SealTemplate(string template) => WriteJson(Path.Combine(template, ".template-manifest.json"), Walk(template).Where(f => Path.GetFileName(f) != ".template-manifest.json").Select(f => new TemplateFile(Relative(template, f), HashFile(f))).OrderBy(f => f.Path).ToArray());
    static void CopyTemplate(string template, string destination)
    {
        var expected = Read<TemplateFile[]>(Path.Combine(template, ".template-manifest.json"));
        string[] actual = Walk(template).Where(f => Path.GetFileName(f) != ".template-manifest.json").Select(f => Relative(template, f)).Order().ToArray();
        if (!actual.SequenceEqual(expected.Select(f => f.Path).Order())) throw new IOException("Runtime templateのファイル一覧が一致しません");
        if (!actual.Contains("index.html") || !actual.Any(p => p.StartsWith("_framework/"))) throw new IOException("Runtime templateのindex.html/_frameworkが不足しています");
        foreach (var item in expected)
        {
            if (!SafeDataPath(item.Path) || item.Path.StartsWith("p1b-data/", StringComparison.OrdinalIgnoreCase)) throw new IOException($"Runtime templateに禁止ファイルがあります: {item.Path}");
            string source = Path.Combine(template, item.Path), target = Path.Combine(destination, item.Path);
            if (HashFile(source) != item.Sha256) throw new IOException($"Runtime template SHAが一致しません: {item.Path}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            if (HashFile(target) != item.Sha256) throw new IOException($"RuntimeコピーSHAが一致しません: {item.Path}");
        }
        string index = Path.Combine(destination, "index.html");
        File.WriteAllText(index, File.ReadAllText(index).Replace("<base href=\"/\">", "<base href=\"./\">"), new UTF8Encoding(false));
        foreach (bool brotli in new[] { true, false })
        {
            using var input = File.OpenRead(index); using var output = File.Create(index + (brotli ? ".br" : ".gz"));
            using Stream compressed = brotli ? new BrotliStream(output, CompressionLevel.Optimal) : new GZipStream(output, CompressionLevel.Optimal);
            input.CopyTo(compressed);
        }
    }
    public static PackageResult Build(string gameRoot, string output, string template, bool itch, Action<string>? progress = null)
    {
        gameRoot = ResolveGameRoot(gameRoot); output = Full(output); template = Full(template); NoLinks(output); NoLinks(template);
        if (output == gameRoot || Within(output, gameRoot) || Within(gameRoot, output) || output == template || Within(output, template) || Within(template, output)) throw new IOException("出力先は入力/Runtime templateと重ならない場所を選択してください");
        if (File.Exists(output)) throw new IOException($"出力先がファイルで占有されています: {output}");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any() && !Owned(output)) throw new IOException("本ツールで作成した出力ではないため上書きしません");
        foreach (string suffix in new[] { ".packager.json", "-itch.zip", "-packaging-report.txt", "-packaging-error.txt" }) { NoLinks(output + suffix); if (Directory.Exists(output + suffix)) throw new IOException($"出力付属ファイルがdirectoryで占有されています: {output + suffix}"); }
        string id = Guid.NewGuid().ToString("N"), stage = output + ".building-" + id;
        string zipStage = stage + "-itch.zip", snapshotStage = stage + ".packager.json", reportStage = stage + "-packaging-report.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); Directory.CreateDirectory(stage);
        try
        {
            progress?.Invoke("入力snapshotを取得しています"); var snapshot = Capture(gameRoot); var diff = Compare(snapshot, Previous(output));
            progress?.Invoke($"snapshot完了: {snapshot.Files.Count} files" + (snapshot.HasSaves ? " / セーブデータはWeb配布物には含めません" : ""));
            CopyTemplate(template, stage); string packages = Path.Combine(stage, "p1b-data"); Directory.CreateDirectory(packages);
            var manifestFiles = new List<ManifestFile>();
            foreach (var category in snapshot.Files.GroupBy(f => f.Category))
            {
                int number = 0; long size = 0; ZipArchive? zip = null; string name = "";
                try
                {
                    foreach (var file in category)
                    {
                        if (zip == null || (size > 0 && size + file.Size > PackLimit))
                        {
                            zip?.Dispose(); name = $"{category.Key}-{++number:000}.zip"; progress?.Invoke($"生成: {name}");
                            zip = ZipFile.Open(Path.Combine(packages, name), ZipArchiveMode.Create); size = 0;
                        }
                        var entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal); entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (var input = File.OpenRead(file.Source)) using (var target = entry.Open()) input.CopyTo(target);
                        manifestFiles.Add(new(file.Path, file.Size, file.Sha256, name)); size += file.Size;
                    }
                }
                finally { zip?.Dispose(); }
            }
            var manifest = new Manifest(1, snapshot.Files.Sum(f => f.Size), manifestFiles);
            WriteJson(Path.Combine(packages, "manifest.json"), manifest); progress?.Invoke("全archive bytes/SHAを検証しています"); VerifyPackages(packages, snapshot);
            string manifestSha = HashFile(Path.Combine(packages, "manifest.json"));
            WriteJson(Path.Combine(stage, "web-package-info.json"), new { application = Owner, packagerVersion = Version, runtimeTemplateVersion = RuntimeVersion, packagedUtc = DateTimeOffset.UtcNow, inputFileCount = snapshot.Files.Count, gameContentSnapshotSha256 = snapshot.AggregateSha256, packageManifestSha256 = manifestSha });
            if (itch) { progress?.Invoke("itch ZIPを生成・検証しています"); CreateWebZip(stage, zipStage); VerifyWebZip(stage, zipStage); }
            var finalSnapshot = Capture(gameRoot); var raced = Compare(finalSnapshot, snapshot);
            if (raced.Changed.Length + raced.Added.Length + raced.Deleted.Length != 0) throw new IOException("パッケージング中に入力ファイルが変更されました: " + string.Join(", ", raced.Changed.Concat(raced.Added).Concat(raced.Deleted)));
            WriteJson(snapshotStage, snapshot);
            string[] packs = manifestFiles.Select(f => f.Pack).Distinct().ToArray();
            File.WriteAllText(reportStage, $"Emuera Web Packager\n結果: 成功\nRuntime: {RuntimeVersion}\n入力ファイル: {snapshot.Files.Count}\n総入力サイズ: {manifest.TotalSize} bytes\n生成: {string.Join(", ", packs)}\n変更: {diff.Changed.Length} / 追加: {diff.Added.Length} / 削除: {diff.Deleted.Length}\nManifest: PASS\nArchive Verification: PASS\nSAV files included: 0\n{(snapshot.HasSaves ? "セーブデータはWeb配布物には含めません\n" : "")}itch.io ZIP: {(itch ? "ITCH_PACKAGE_PASS" : "未選択")}\n出力: {output}\n", new UTF8Encoding(false));
            progress?.Invoke("検証完了。出力を更新しています");
            Promote([(stage, output), (snapshotStage, output + ".packager.json"), (reportStage, output + "-packaging-report.txt"), (zipStage, output + "-itch.zip")], id);
            return new(output, snapshot.Files.Count, manifest.TotalSize, packs, diff, manifestSha, itch ? output + "-itch.zip" : null);
        }
        catch (Exception ex)
        {
            // The local error report is outside the input and the distributable.
            try { File.WriteAllText(output + "-packaging-error.txt", $"Emuera Web Packager\n結果: 失敗（既存出力は維持）\n{ex}\n", new UTF8Encoding(false)); } catch (IOException) { }
            throw;
        }
        finally
        {
            if (stage != output + ".building-" + id || !Within(stage, Path.GetDirectoryName(output)!)) throw new IOException("一時領域の所有確認に失敗しました");
            NoLinks(stage); if (Directory.Exists(stage)) Directory.Delete(stage, true);
            foreach (string file in new[] { zipStage, snapshotStage, reportStage }) { NoLinks(file); if (File.Exists(file)) File.Delete(file); }
        }
    }
    static bool Owned(string output)
    {
        try { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "web-package-info.json"))); return doc.RootElement.GetProperty("application").GetString() == Owner && File.Exists(Path.Combine(output, "index.html")); }
        catch { return false; }
    }
    static void Promote((string Stage, string Target)[] items, string id)
    {
        var moved = new List<(string Stage, string Target, string Backup, bool Existed, bool Installed)>();
        void Move(string from, string to)
        {
            try { if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to); }
            catch (IOException ex) { throw new IOException($"出力の更新に失敗しました: {from} → {to}", ex); }
        }
        try
        {
            foreach (var item in items)
            {
                string backup = item.Target + ".previous-" + id; bool exists = File.Exists(item.Target) || Directory.Exists(item.Target);
                NoLinks(item.Target); if (exists) Move(item.Target, backup);
                moved.Add((item.Stage, item.Target, backup, exists, false));
                if (Directory.Exists(item.Stage) || File.Exists(item.Stage)) { Move(item.Stage, item.Target); moved[^1] = moved[^1] with { Installed = true }; }
            }
        }
        catch
        {
            foreach (var item in moved.AsEnumerable().Reverse()) { if (item.Installed) Move(item.Target, item.Stage); if (item.Existed) Move(item.Backup, item.Target); }
            throw;
        }
        // Retain the last good version as an explicitly named backup; never recursively delete a user's previous output.
    }
    public static void VerifyPackages(string packages, Snapshot snapshot)
    {
        var manifest = Read<Manifest>(Path.Combine(packages, "manifest.json"));
        var expected = snapshot.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        if (manifest.Version != 1 || manifest.Files.Count != expected.Count || manifest.TotalSize != snapshot.Files.Sum(f => f.Size)) throw new IOException("manifestの件数/totalSizeが入力と一致しません");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in manifest.Files.GroupBy(f => f.Pack))
        {
            if (Path.GetFileName(group.Key) != group.Key || !group.Key.EndsWith(".zip")) throw new IOException("pack名が不正です");
            using var archive = ZipFile.OpenRead(Path.Combine(packages, group.Key)); var entries = group.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            if (archive.Entries.Count != entries.Count) throw new IOException($"archive件数が不正です: {group.Key}");
            foreach (var entry in archive.Entries)
            {
                if (!SafeDataPath(entry.FullName) || !seen.Add(entry.FullName) || !entries.TryGetValue(entry.FullName, out var m) || !expected.TryGetValue(entry.FullName, out var original)) throw new IOException($"想定外/重複entryです: {entry.FullName}");
                using var data = entry.Open(); string hash = Convert.ToHexString(SHA256.HashData(data));
                if (entry.Length != original.Size || m.Size != original.Size || hash != original.Sha256 || m.Sha256 != original.Sha256) throw new IOException($"パッケージング中に入力ファイルが変更されました / archive SHA不一致: {original.Path}");
            }
        }
        if (seen.Count != expected.Count || Directory.EnumerateFiles(packages).Count() != manifest.Files.Select(f => f.Pack).Distinct().Count() + 1) throw new IOException("archiveファイル一覧が一致しません");
    }
    static void CreateWebZip(string web, string destination)
    {
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (string file in Walk(web).OrderBy(f => Relative(web, f), StringComparer.Ordinal))
        {
            string path = Relative(web, file); if (!SafeDataPath(path)) throw new IOException($"配布禁止path: {path}");
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal); entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = File.OpenRead(file); using var target = entry.Open(); input.CopyTo(target);
        }
    }
    public static void VerifyWebZip(string web, string zipFile)
    {
        var expected = Walk(web).ToDictionary(f => Relative(web, f), StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(zipFile); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (zip.Entries.Count != expected.Count || !zip.Entries.Any(e => e.FullName == "index.html") || !zip.Entries.Any(e => e.FullName.StartsWith("_framework/")) || !zip.Entries.Any(e => e.FullName == "p1b-data/manifest.json")) throw new IOException("itch ZIPのroot/件数が不正です");
        foreach (var entry in zip.Entries)
        {
            if (!SafeDataPath(entry.FullName) || !seen.Add(entry.FullName) || !expected.TryGetValue(entry.FullName, out string? file)) throw new IOException($"itch ZIPのpathが不正です: {entry.FullName}");
            using var data = entry.Open(); if (entry.Length != new FileInfo(file).Length || Convert.ToHexString(SHA256.HashData(data)) != HashFile(file)) throw new IOException($"itch ZIP bytes不一致: {entry.FullName}");
        }
    }
}

public sealed record TemplateFile(string Path, string Sha256);
public sealed record ManifestFile(string Path, long Size, string Sha256, string Pack);
public sealed record Manifest(int Version, long TotalSize, List<ManifestFile> Files);

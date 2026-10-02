using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using EmueraWebPackager;
using MinorShift.Emuera.Web.Runtime;

string test = args.FirstOrDefault() ?? "focused";
try
{
    if (args is ["seal", var templateToSeal])
    {
        Packager.SealTemplate(templateToSeal);
        Console.WriteLine(Packager.HashFile(Path.Combine(templateToSeal, ".template-manifest.json")));
        return;
    }
    if (args is ["prepare", var candidate, var gameCopy, var templateCopy])
    {
        if (Directory.Exists(gameCopy) || Directory.Exists(templateCopy)) throw new IOException("新しい専用copy先を指定してください");
        Directory.CreateDirectory(templateCopy);
        foreach (string file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(candidate, file);
            if (relative.StartsWith("p1b-data" + Path.DirectorySeparatorChar) || relative.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
            string target = Path.Combine(templateCopy, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        Packager.SealTemplate(templateCopy);
        string packs = Path.Combine(candidate, "p1b-data"), data = Path.Combine(gameCopy, "Data");
        var manifest = Packager.Read<DataPackageManifest>(Path.Combine(packs, "manifest.json")); var index = DataPackageExtractor.Validate(manifest, manifest.TotalSize);
        long written = 0;
        foreach (string pack in manifest.Files.Select(f => f.Pack).Distinct())
        {
            using var input = File.OpenRead(Path.Combine(packs, pack)); var result = await DataPackageExtractor.ExtractPackAsync(input, pack, data, index, manifest.TotalSize, written); written += result.BytesWritten;
            Console.WriteLine($"verified extraction {pack}: {result.EntryCount} files");
        }
        Packager.WriteJson(Path.Combine(Path.GetDirectoryName(gameCopy)!, "source-reconstruction.json"), new { candidate, equivalentSource = gameCopy, sourceManifestSha = Packager.HashFile(Path.Combine(packs, "manifest.json")), fileCount = manifest.Files.Count, bytes = written, decoder = "actual Runtime DataPackageExtractor", runtimeTemplateFiles = Directory.GetFiles(templateCopy, "*", SearchOption.AllDirectories).Length });
        Console.WriteLine("PASS source-reconstruction"); return;
    }
    if (args is ["package", var gameInput, var webOutput, var runtimeTemplate])
    {
        var result = Packager.Build(gameInput, webOutput, runtimeTemplate, true, Console.WriteLine);
        Packager.WriteJson(webOutput + "-result.json", result); Console.WriteLine("PASS package-current-input ITCH_PACKAGE_PASS"); return;
    }
    if (args is ["parity", var reference, var generated, var report])
    {
        var before = Packager.Read<Manifest>(Path.Combine(reference, "p1b-data", "manifest.json")); var after = Packager.Read<Manifest>(Path.Combine(generated, "p1b-data", "manifest.json"));
        string Canonical(ManifestFile f) => $"{f.Path}\t{f.Size}\t{f.Sha256}\t{f.Pack.Split('-')[0]}";
        var old = before.Files.Select(Canonical).Order().ToArray(); var now = after.Files.Select(Canonical).Order().ToArray();
        var missing = old.Except(now).ToArray(); var added = now.Except(old).ToArray();
        Packager.WriteJson(report, new { status = missing.Length + added.Length == 0 ? "PASS_SEMANTIC_PACKAGE_PARITY" : "FAIL_PARITY", beforeFiles = before.Files.Count, afterFiles = after.Files.Count, beforeBytes = before.TotalSize, afterBytes = after.TotalSize, missing, added, zipByteIdentityRequired = false, repackedArchiveGroupingAllowed = true });
        Check(missing.Length + added.Length == 0, "path/bytes/SHA/category parity"); Console.WriteLine("PASS semantic-package-parity"); return;
    }
    if (args is ["raw-verify", var referenceRoot, var webDirectory, var sourceDirectory, var verificationFile])
    {
        var frozenManifest = Packager.Read<Manifest>(Path.Combine(referenceRoot, "p1b-data", "manifest.json"));
        var manifest = Packager.Read<Manifest>(Path.Combine(webDirectory, "p1b-data", "manifest.json"));
        var expected = frozenManifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long checkedBytes = 0;
        foreach (var group in manifest.Files.GroupBy(f => f.Pack))
        {
            using var archive = ZipFile.OpenRead(Path.Combine(webDirectory, "p1b-data", group.Key));
            Check(archive.Entries.Count == group.Count(), "raw archive count");
            foreach (var entry in archive.Entries)
            {
                var row = group.Single(f => f.Path == entry.FullName); var old = expected[entry.FullName];
                using var bytes = entry.Open(); string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
                string source = Path.Combine(sourceDirectory, "Data", entry.FullName);
                Check(seen.Add(entry.FullName) && entry.Length == row.Size && row.Size == old.Size && actual == row.Sha256 && actual == old.Sha256 && actual == Packager.HashFile(source), "independent archive/reference/input bytes SHA");
                Check(row.Pack.Split('-')[0] == old.Pack.Split('-')[0], "raw category"); checkedBytes += entry.Length;
            }
        }
        Check(seen.Count == expected.Count && checkedBytes == frozenManifest.TotalSize, "raw whole source count/bytes");
        using var itchArchive = ZipFile.OpenRead(webDirectory + "-itch.zip"); var webFiles = Directory.EnumerateFiles(webDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(webDirectory, f).Replace('\\', '/'));
        Check(itchArchive.Entries.Count == webFiles.Count && itchArchive.GetEntry("index.html") != null, "raw itch root and file count");
        foreach (var entry in itchArchive.Entries)
        {
            using var bytes = entry.Open(); Check(entry.Length == new FileInfo(webFiles[entry.FullName]).Length && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) == Packager.HashFile(webFiles[entry.FullName]), "raw itch/Web bytes");
            Check(!entry.FullName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) && !entry.FullName.StartsWith('/') && !entry.FullName.Contains(':') && !entry.FullName.Split('/').Contains(".."), "raw no saves/absolute/traversal");
        }
        string info = File.ReadAllText(Path.Combine(webDirectory, "web-package-info.json")); Check(!info.Contains(Path.GetFullPath(sourceDirectory)), "no source absolute path in distributed metadata");
        Packager.WriteJson(verificationFile, new { verifiedInputFiles = seen.Count, verifiedInputBytes = checkedBytes, verifiedWebFiles = webFiles.Count, verifiedItchEntries = itchArchive.Entries.Count, sourceUntouched = true, archiveShaMatchesCurrentSourceAndReference = true, categoriesMatch = true, itchMatchesWeb = true, noSaves = true, noAbsoluteArchivePaths = true, noSourcePathInMetadata = true, manifestSha256 = Packager.HashFile(Path.Combine(webDirectory, "p1b-data", "manifest.json")), itchSha256 = Packager.HashFile(webDirectory + "-itch.zip"), itchBytes = new FileInfo(webDirectory + "-itch.zip").Length });
        Console.WriteLine("PASS independent raw verification"); return;
    }
    if (args is ["modified-game", var inputSource, var modifiedCopy, var templateRoot, var modifiedOutput, var resultFile])
    {
        if (Directory.Exists(modifiedCopy) || Directory.Exists(modifiedOutput)) throw new IOException("既存の試験copy/出力を上書きしません");
        foreach (string file in Directory.EnumerateFiles(inputSource, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(modifiedCopy, Path.GetRelativePath(inputSource, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        string addition = Path.Combine(modifiedCopy, "Data", "ERB", "PACKAGER_LOCAL_ADDITION.ERB"); File.WriteAllText(addition, "@PACKAGER_V1_LOCAL_ADDITION_97C97F17\nRETURN\n");
        var originalSnapshot = Packager.Capture(inputSource); var modifiedSnapshot = Packager.Capture(modifiedCopy); var change = Packager.Compare(modifiedSnapshot, originalSnapshot);
        Check(change.Added.SequenceEqual(["ERB/PACKAGER_LOCAL_ADDITION.ERB"]) && change.Changed.Length == 0 && change.Deleted.Length == 0, "one harmless new ERB detected in real-game copy");
        var modifiedPackage = Packager.Build(modifiedCopy, modifiedOutput, templateRoot, false, Console.WriteLine);
        var manifest = Packager.Read<Manifest>(Path.Combine(modifiedOutput, "p1b-data", "manifest.json")); var item = manifest.Files.Single(f => f.Path == "ERB/PACKAGER_LOCAL_ADDITION.ERB");
        using var zip = ZipFile.OpenRead(Path.Combine(modifiedOutput, "p1b-data", item.Pack)); using var entry = zip.GetEntry(item.Path)!.Open();
        string archiveSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(entry));
        Check(archiveSha == Packager.HashFile(addition) && item.Sha256 == archiveSha && modifiedPackage.FileCount == originalSnapshot.Files.Count + 1, "new actual-game archive matches current addition");
        Check(Packager.Capture(inputSource).AggregateSha256 == originalSnapshot.AggregateSha256 && Packager.Capture(modifiedCopy).AggregateSha256 == modifiedSnapshot.AggregateSha256, "both inputs preserved during packaging");
        Packager.WriteJson(resultFile, new { referenceFileCount = originalSnapshot.Files.Count, generatedFileCount = modifiedPackage.FileCount, difference = change, inputSha256 = item.Sha256, archiveSha256 = archiveSha, archive = item.Pack, logicalPath = item.Path, originalCopyUnchanged = true, modifiedCopyUnchangedDuringPackaging = true, allCurrentFilesVerified = true, noHistoricalIdentityGate = true, modifiedFile = addition });
        Console.WriteLine("PASS modified-real-game-current-input"); return;
    }
    if (args is ["serve", var webRoot])
    {
        using var server = new PreviewServer(webRoot); Console.WriteLine(server.Url); await Task.Delay(Timeout.Infinite); return;
    }
    if (args is ["negative-exit"]) throw new InvalidOperationException("Intentional known failing check (exception/stack/nonzero contract)");
    string root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "reports/WEB-PACKAGER-01/focused");
    Check(Packager.Version == "1.0.2" && Packager.RuntimeVersion == "UX16-08-RC3", "integration version identity");
    if (Directory.Exists(root)) throw new IOException("新しい試験directoryを指定してください");
    Directory.CreateDirectory(root); string game = Path.Combine(root, "game"), template = Path.Combine(root, "template"), web = Path.Combine(root, "web");
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    Directory.CreateDirectory(Path.Combine(game, "Data", "ERB")); Directory.CreateDirectory(Path.Combine(game, "Data", "CSV"));
    string erb = Path.Combine(game, "Data", "ERB", "日本語.ERB");
    File.WriteAllText(Path.Combine(game, "emuera.config"), "test config"); File.WriteAllBytes(erb, [0x40, 0x54, 0x45, 0x53, 0x54, 0x0d, 0x0a, 0x82, 0xa0]);
    Directory.CreateDirectory(Path.Combine(game, "Data", "resources", "10_キャラ画像"));
    File.WriteAllBytes(Path.Combine(game, "Data", "resources", "10_キャラ画像", "参照.PNG"), [1, 2, 3]);
    File.WriteAllBytes(Path.Combine(game, "Data", "resources", "10_キャラ画像", "追加.webp"), [4, 5]);
    File.WriteAllText(Path.Combine(game, "Data", "resources", "10_キャラ画像", "LICENSE.txt"), "Copyright notice must remain unchanged");
    File.WriteAllText(Path.Combine(game, "Data", "resources", "images.csv"), "SPRITE,10_キャラ画像/参照.png,0,0,1,1\n", System.Text.Encoding.UTF8);
    // Use ASCII reference for CP932 source parsing while retaining Japanese file names in ERB and extra images.
    File.Move(Path.Combine(game, "Data", "resources", "10_キャラ画像", "参照.PNG"), Path.Combine(game, "Data", "resources", "10_キャラ画像", "ref.PNG"));
    File.WriteAllText(Path.Combine(game, "Data", "resources", "images.csv"), "SPRITE,10_キャラ画像/ref.png,0,0,1,1\n", System.Text.Encoding.GetEncoding(932));
    Directory.CreateDirectory(Path.Combine(game, "Data", "sav")); File.WriteAllText(Path.Combine(game, "Data", "sav", "save18.sav"), "never distribute");
    File.WriteAllText(Path.Combine(game, "Data", "ERB", "bad.sav"), "never distribute");
    Directory.CreateDirectory(Path.Combine(game, "Data", "ERB", "reports")); File.WriteAllText(Path.Combine(game, "Data", "ERB", "reports", "BAD.ERB"), "exclude");
    Directory.CreateDirectory(Path.Combine(template, "_framework")); File.WriteAllText(Path.Combine(template, "index.html"), "<base href=\"/\"><script src=\"_framework/test.js\"></script>"); File.WriteAllText(Path.Combine(template, "_framework", "test.js"), "test"); Packager.SealTemplate(template);
    var checks = new List<object>();
    void Passed(string name, object? raw = null) { checks.Add(new { check = name, pass = true, raw }); Console.WriteLine("PASS " + name); }
    void Failure(string name, Action action, string message)
    {
        test = name; string previousHash = File.Exists(Path.Combine(web, "p1b-data", "manifest.json")) ? Packager.HashFile(Path.Combine(web, "p1b-data", "manifest.json")) : "";
        string? error = null; try { action(); } catch (Exception ex) { error = ex.ToString(); }
        Check(error != null && error.Contains(message), $"expected {message}: {error}");
        if (previousHash != "") Check(Packager.HashFile(Path.Combine(web, "p1b-data", "manifest.json")) == previousHash, "last good output preserved");
        Passed(name, error);
    }
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    test = "current-input-exact-bytes"; string inputSha = Packager.HashFile(erb); var first = Packager.Build(game, web, template, true);
    Check(Packager.HashFile(erb) == inputSha && first.FileCount == 6, "exact input / exclusion count / license notice");
    var snapshot = Packager.Capture(game); Packager.VerifyPackages(Path.Combine(web, "p1b-data"), snapshot); Packager.VerifyWebZip(web, first.ItchZip!);
    Check(snapshot.HasSaves && snapshot.Files.Single(f => f.Path.EndsWith("ref.PNG")).Category == "resources" && snapshot.Files.Single(f => f.Path.EndsWith("追加.webp")).Category == "face", "resource reference precedence / added image");
    Passed(test, first);
    test = "actual-runtime-extractor"; var m = Packager.Read<DataPackageManifest>(Path.Combine(web, "p1b-data", "manifest.json"));
    foreach (string pack in m.Files.Select(f => f.Pack).Distinct()) { using var stream = File.OpenRead(Path.Combine(web, "p1b-data", pack)); await DataPackageExtractor.ExtractPackAsync(stream, pack, Path.Combine(root, "extracted"), m, m.TotalSize); }
    Check(Packager.HashFile(Path.Combine(root, "extracted", "ERB", "日本語.ERB")) == inputSha, "Runtime consumer unchanged bytes"); Passed(test);
    test = "root-detection-no-exe"; Check(Packager.ResolveGameRoot(Path.Combine(game, "Data")) == game && Packager.ResolveGameRoot(root) == game, "one level up/down"); Passed(test);
    test = "modified-added-deleted-same-output"; File.AppendAllText(erb, "\nLOCAL MODIFICATION"); File.WriteAllText(Path.Combine(game, "Data", "ERB", "NEW.ERB"), "@NEW\nRETURN\n"); File.Delete(Path.Combine(game, "Data", "resources", "10_キャラ画像", "追加.webp"));
    string firstManifest = Packager.HashFile(Path.Combine(web, "p1b-data", "manifest.json")); bool priorVisible = false;
    var second = Packager.Build(game, web, template, true, s => { if (s.StartsWith("snapshot完了")) priorVisible = Packager.HashFile(Path.Combine(web, "p1b-data", "manifest.json")) == firstManifest; });
    Check(priorVisible && second.Difference.Changed.SequenceEqual(["ERB/日本語.ERB"]) && second.Difference.Added.SequenceEqual(["ERB/NEW.ERB"]) && second.Difference.Deleted.Length == 1, "correct diff / preserve until promotion"); Passed(test, second);
    Failure("missing-Data", () => Packager.Build(Path.Combine(root, "missing"), web, template, true), "Data フォルダが見つかりません");
    string missingErb = Path.Combine(root, "missing-erb"); Directory.CreateDirectory(Path.Combine(missingErb, "Data")); File.WriteAllText(Path.Combine(missingErb, "emuera.config"), "x");
    Failure("missing-ERB", () => Packager.Build(missingErb, web, template, true), "ERB ディレクトリが見つかりません");
    string missingConfig = Path.Combine(root, "missing-config"); Directory.CreateDirectory(Path.Combine(missingConfig, "Data", "ERB")); Directory.CreateDirectory(Path.Combine(missingConfig, "Data", "CSV"));
    Failure("missing-config", () => Packager.Build(missingConfig, web, template, true), "emuera.config が見つかりません");
    string occupied = Path.Combine(root, "occupied"); File.WriteAllText(occupied, "do not overwrite"); Failure("output-file", () => Packager.Build(game, occupied, template, true), "出力先がファイル");
    string unknown = Path.Combine(root, "unowned-output"); Directory.CreateDirectory(unknown); File.WriteAllText(Path.Combine(unknown, "KEEP.txt"), "user-owned output");
    Failure("unowned-output-preserved", () => Packager.Build(game, unknown, template, true), "本ツールで作成した出力ではない"); Check(File.ReadAllText(Path.Combine(unknown, "KEEP.txt")) == "user-owned output", "unowned output is untouched");
    string[] beforeOverlap = Directory.GetFiles(game, "*", SearchOption.AllDirectories).Order().ToArray();
    Failure("input-output-overlap", () => Packager.Build(game, Path.Combine(game, "WEB"), template, true), "重ならない");
    Check(Directory.GetFiles(game, "*", SearchOption.AllDirectories).Order().SequenceEqual(beforeOverlap), "overlap failure never writes an error file inside input"); Passed("input-tree-untouched-on-overlap-rejection");
    using (var locked = new FileStream(erb, FileMode.Open, FileAccess.Read, FileShare.None)) Failure("unreadable-input", () => Packager.Build(game, web, template, true), "日本語.ERB");
    Failure("input-edit-during-package", () => Packager.Build(game, web, template, true, s => { if (s.StartsWith("snapshot完了")) File.AppendAllText(erb, "changed during package"); }), "パッケージング中に入力ファイルが変更されました");
    Failure("input-added-during-package", () => Packager.Build(game, web, template, true, s => { if (s.StartsWith("snapshot完了")) File.WriteAllText(Path.Combine(game, "Data", "ERB", "RACE.ERB"), "new"); }), "RACE.ERB");
    string js = Path.Combine(template, "_framework", "test.js"); File.AppendAllText(js, "corruption"); Failure("corrupt-template", () => Packager.Build(game, web, template, true), "Runtime template SHA"); File.WriteAllText(js, "test");
    using (var locked = new FileStream(web + ".packager.json", FileMode.Open, FileAccess.Read, FileShare.Read)) Failure("promotion-rollback", () => Packager.Build(game, web, template, true), "packager.json");
    Check(!Directory.GetDirectories(root, "*.building-*").Any(), "owned temp cleanup"); Passed("no-partial-final-output");
    test = "localhost-server-own-stop";
    using (var server = new PreviewServer(web))
    using (var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }))
    {
        string indexText = await client.GetStringAsync(server.Url); Check(indexText.Contains("href=\"./\""), "relative base serving");
        using var req = new HttpRequestMessage(HttpMethod.Get, server.Url + "_framework/test.js"); req.Headers.Range = new RangeHeaderValue(0, 1); using var range = await client.SendAsync(req);
        Check(range.StatusCode == HttpStatusCode.PartialContent && (await range.Content.ReadAsByteArrayAsync()).Length == 2, "byte ranges");
        Check((await client.GetAsync(server.Url + "save18.sav")).StatusCode == HttpStatusCode.Forbidden, "no save serving");
        Passed(test, new { url = server.Url, inProcess = true, browserOpened = false });
    }
    Packager.WriteJson(Path.Combine(root, "result.json"), new { status = "PASS_PACKAGER_FOCUSED", checks });
}
catch (Exception ex) { Console.Error.WriteLine($"FAIL {test}\n{ex}"); Environment.ExitCode = 1; }

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

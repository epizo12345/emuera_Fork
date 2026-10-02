using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MinorShift.Emuera.Web.Runtime;

public sealed record DataPackageFile(string Path, long Size, string Sha256, string Pack);
public sealed record DataPackageManifest(int Version, long TotalSize, List<DataPackageFile> Files);
public sealed record DataPackageExtractionResult(long BytesWritten, int EntryCount, int LookupCount);

// Opt-in pack-local wall times. Sampled child times are included in streamLoop,
// never added to the exclusive partition or extrapolated to unsampled files.
public sealed class DataPackageExtractionDiagnostics
{
    public Dictionary<string, double> ExclusiveMilliseconds { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, double> SampleMilliseconds { get; } = new(StringComparer.Ordinal);
    public double TotalMilliseconds { get; internal set; }
    public double ResidualMilliseconds => TotalMilliseconds - ExclusiveMilliseconds.Values.Sum();
    public long AllocatedBytes { get; internal set; }
    public bool Succeeded { get; internal set; }
    public int Files { get; internal set; }
    public long Bytes { get; internal set; }
    public long ReadCalls { get; internal set; }
    public long WriteCalls { get; internal set; }
    public long HashAppends { get; internal set; }
    public long HashFinalizations { get; internal set; }
    public long FinalFileChecks { get; internal set; }
    public long FinalMoves { get; internal set; }
    public int FinalCollisionSamples { get; internal set; }
    public int FinalPlacementSamples { get; internal set; }
    public int BufferSize { get; internal set; }
    public int SampleStride => 32;
    public int SampleFiles { get; internal set; }
    public long SampleBytes { get; internal set; }
    internal long Mark() => Stopwatch.GetTimestamp();
    internal void Add(string phase, long start, bool sample = false)
    {
        var values = sample ? SampleMilliseconds : ExclusiveMilliseconds;
        values.TryGetValue(phase, out double elapsed);
        values[phase] = elapsed + Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}

public sealed class DataPackageIndex
{
    readonly Dictionary<string, Dictionary<string, DataPackageFile>> packs;

    internal DataPackageIndex(long totalSize, Dictionary<string, Dictionary<string, DataPackageFile>> packs)
    {
        TotalSize = totalSize;
        this.packs = packs;
    }

    public long TotalSize { get; }
    internal Dictionary<string, DataPackageFile> GetPack(string packName) =>
        packs.TryGetValue(packName, out Dictionary<string, DataPackageFile>? files)
            ? files
            : throw new InvalidDataException($"manifestにpackがありません: {packName}");
}

public static class DataPackageExtractor
{
    // SHA-256はPackagerが選んだ現在の入力bytesと配布packの整合性確認用であり、特定の公式release identityを要求しない。
    public static DataPackageIndex Validate(DataPackageManifest manifest, long maxExtractedBytes)
    {
        if (manifest.Version != 1)
            throw new InvalidDataException($"未対応のmanifest versionです: {manifest.Version}");
        if (manifest.Files is null)
            throw new InvalidDataException("manifestにfilesがありません");

        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packs = new Dictionary<string, Dictionary<string, DataPackageFile>>(StringComparer.Ordinal);
        foreach (DataPackageFile file in manifest.Files)
        {
            string path = CanonicalRelativePath(file.Path);
            if (!paths.Add(path))
                throw new InvalidDataException($"大小文字を無視すると衝突する論理パスです: {file.Path}");
            if (file.Size < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException($"manifestのsizeまたはSHA-256が不正です: {file.Path}");
            if (file.Pack != Path.GetFileName(file.Pack) || !file.Pack.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"manifestのpack名が不正です: {file.Pack}");
            total = checked(total + file.Size);
            if (!packs.TryGetValue(file.Pack, out Dictionary<string, DataPackageFile>? pack))
                packs[file.Pack] = pack = new(StringComparer.OrdinalIgnoreCase);
            pack.Add(path, file with { Path = path });
        }
        if (total != manifest.TotalSize)
            throw new InvalidDataException($"manifest totalSizeが一致しません: expected={manifest.TotalSize}, actual={total}");
        if (total > maxExtractedBytes)
            throw new InvalidDataException($"展開後サイズが上限を超えます: {total} > {maxExtractedBytes}");
        return new DataPackageIndex(total, packs);
    }

    public static Task<DataPackageExtractionResult> ExtractPackAsync(Stream archive, string packName, string destinationRoot, DataPackageManifest manifest, long maxExtractedBytes, CancellationToken cancellationToken = default) =>
        ExtractPackAsync(archive, packName, destinationRoot, Validate(manifest, maxExtractedBytes), maxExtractedBytes, 0, cancellationToken);

    public static async Task<DataPackageExtractionResult> ExtractPackAsync(Stream archive, string packName, string destinationRoot, DataPackageIndex index, long maxExtractedBytes, long alreadyExtractedBytes = 0, CancellationToken cancellationToken = default, DataPackageExtractionDiagnostics? diagnostics = null)
    {
        long totalStart = diagnostics?.Mark() ?? 0;
        long allocationStart = diagnostics is null ? 0 : GC.GetTotalAllocatedBytes();
        long phaseStart = diagnostics?.Mark() ?? 0;
        Dictionary<string, DataPackageFile> expected = index.GetPack(packName);
        string destination = Path.GetFullPath(destinationRoot);
        string stage = destination + ".stage-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        long bytesWritten = 0;
        int lookups = 0;
        byte[]? buffer = null;
        try
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            var entries = zip.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
            if (entries.Length != expected.Count)
                throw new InvalidDataException($"packのファイル数が一致しません: {packName}");

            // 順次展開するこのpack呼出しだけで共有し、SHAは各ファイルの確定時にリセットする。
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var hashText = new char[64];
            // 絶対rootは呼出し中に変わらない。各targetの正規化・範囲検査は省略しない。
            long rootStart = diagnostics?.Mark() ?? 0;
            string destinationWithSeparator = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            string stageWithSeparator = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
            diagnostics?.Add("rootPreparationOnce", rootStart, sample: true);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stageParents = new HashSet<string>(StringComparer.Ordinal);
            var finalParents = new HashSet<string>(StringComparer.Ordinal);
            if (diagnostics is not null) diagnostics.BufferSize = buffer.Length;
            diagnostics?.Add("zipOpenEnumerationWorkspace", phaseStart);
            foreach (ZipArchiveEntry entry in entries)
            {
                phaseStart = diagnostics?.Mark() ?? 0;
                cancellationToken.ThrowIfCancellationRequested();
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException($"リンクentryは展開できません: {entry.FullName}");
                string relative = CanonicalRelativePath(entry.FullName);
                if (!seen.Add(relative))
                    throw new InvalidDataException($"pack内でパスが衝突します: {entry.FullName}");
                lookups++;
                if (!expected.TryGetValue(relative, out DataPackageFile? file))
                    throw new InvalidDataException($"manifestにないentryまたは所属pack違いです: {entry.FullName}");
                if (entry.Length != file.Size)
                    throw new InvalidDataException($"entry sizeが一致しません: {entry.FullName}");
                diagnostics?.Add("pathManifestSafety", phaseStart);

                phaseStart = diagnostics?.Mark() ?? 0;
                string target = SafeTarget(stage, stageWithSeparator, file.Path);
                CreateParentDirectory(target, stageParents);
                long disposeStart;
                {
                await using Stream input = entry.Open();
                await using FileStream output = File.Create(target);
                diagnostics?.Add("stageDirectoryFileStreamOpen", phaseStart);
                bool sample = diagnostics is not null && (lookups - 1) % diagnostics.SampleStride == 0;
                if (sample) diagnostics!.SampleFiles++;
                phaseStart = diagnostics?.Mark() ?? 0;
                long fileBytes = 0;
                int count;
                while (true)
                {
                    long sampleStart = sample ? diagnostics!.Mark() : 0;
                    count = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (diagnostics is not null) diagnostics.ReadCalls++;
                    if (sample) diagnostics!.Add("entryReadIncludingInflate", sampleStart, sample: true);
                    if (count == 0) break;
                    if (fileBytes + count > file.Size || alreadyExtractedBytes + bytesWritten + count > maxExtractedBytes)
                        throw new InvalidDataException($"展開中に宣言サイズまたは総サイズ上限を超えました: {entry.FullName}");
                    sampleStart = sample ? diagnostics!.Mark() : 0;
                    hash.AppendData(buffer, 0, count);
                    if (diagnostics is not null) diagnostics.HashAppends++;
                    if (sample) diagnostics!.Add("hashAppend", sampleStart, sample: true);
                    sampleStart = sample ? diagnostics!.Mark() : 0;
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    if (diagnostics is not null) diagnostics.WriteCalls++;
                    if (sample) diagnostics!.Add("stageWrite", sampleStart, sample: true);
                    fileBytes += count;
                    bytesWritten += count;
                }
                diagnostics?.Add("streamLoopReadHashWrite", phaseStart);
                if (diagnostics is not null)
                {
                    diagnostics.Files++;
                    diagnostics.Bytes += fileBytes;
                    if (sample) diagnostics.SampleBytes += fileBytes;
                }
                phaseStart = diagnostics?.Mark() ?? 0;
                if (fileBytes != file.Size)
                    throw new InvalidDataException($"展開後sizeが一致しません: {entry.FullName}");
                // このファイルのread/write完了後だけ、readバッファの先頭をSHA出力に再利用する。
                long hashSampleStart = sample ? diagnostics!.Mark() : 0;
                int hashBytes = hash.GetHashAndReset(buffer.AsSpan(0, 32));
                if (sample) diagnostics!.Add("hashFinalizeResetIncludingOutput", hashSampleStart, sample: true);
                hashSampleStart = sample ? diagnostics!.Mark() : 0;
                bool converted = Convert.TryToHexString(buffer.AsSpan(0, hashBytes), hashText, out int charsWritten);
                if (sample) diagnostics!.Add("hashHexFormat", hashSampleStart, sample: true);
                hashSampleStart = sample ? diagnostics!.Mark() : 0;
                bool hashMatches = converted && MemoryExtensions.Equals(hashText.AsSpan(0, charsWritten), file.Sha256.AsSpan(), StringComparison.OrdinalIgnoreCase);
                if (sample) diagnostics!.Add("hashExpectedCompare", hashSampleStart, sample: true);
                if (!hashMatches)
                    throw new InvalidDataException($"SHA-256が一致しません: {entry.FullName}");
                if (diagnostics is not null) diagnostics.HashFinalizations++;
                diagnostics?.Add("hashFinalizeSizeCompare", phaseStart);
                disposeStart = diagnostics?.Mark() ?? 0;
                }
                diagnostics?.Add("entryStreamsDisposeFlush", disposeStart);
            }

            phaseStart = diagnostics?.Mark() ?? 0;
            int finalIndex = 0;
            foreach (DataPackageFile file in expected.Values)
            {
                bool sample = diagnostics is not null && finalIndex++ % diagnostics.SampleStride == 0;
                if (sample) diagnostics!.FinalCollisionSamples++;
                long sampleStart = sample ? diagnostics!.Mark() : 0;
                string target = SafeTarget(destination, destinationWithSeparator, file.Path);
                if (sample) diagnostics!.Add("finalSafeTargetForExists", sampleStart, sample: true);
                sampleStart = sample ? diagnostics!.Mark() : 0;
                bool exists = File.Exists(target);
                if (sample) diagnostics!.Add("finalFileExists", sampleStart, sample: true);
                if (diagnostics is not null) diagnostics.FinalFileChecks++;
                if (exists)
                    throw new InvalidDataException($"展開先に既存ファイルがあります: {file.Path}");
            }
            finalIndex = 0;
            foreach (DataPackageFile file in expected.Values)
            {
                bool sample = diagnostics is not null && finalIndex++ % diagnostics.SampleStride == 0;
                if (sample) diagnostics!.FinalPlacementSamples++;
                long sampleStart = sample ? diagnostics!.Mark() : 0;
                string target = SafeTarget(destination, destinationWithSeparator, file.Path);
                if (sample) diagnostics!.Add("finalSafeTargetForMove", sampleStart, sample: true);
                sampleStart = sample ? diagnostics!.Mark() : 0;
                CreateParentDirectory(target, finalParents);
                if (sample) diagnostics!.Add("finalParent", sampleStart, sample: true);
                sampleStart = sample ? diagnostics!.Mark() : 0;
                string source = SafeTarget(stage, stageWithSeparator, file.Path);
                if (sample) diagnostics!.Add("finalStageSafeTargetForMove", sampleStart, sample: true);
                sampleStart = sample ? diagnostics!.Mark() : 0;
                File.Move(source, target);
                if (sample) diagnostics!.Add("finalFileMove", sampleStart, sample: true);
                if (diagnostics is not null) diagnostics.FinalMoves++;
            }
            diagnostics?.Add("finalCollisionCheckPlacement", phaseStart);
            if (diagnostics is not null) diagnostics.Succeeded = true;
            return new(bytesWritten, entries.Length, lookups);
        }
        finally
        {
            phaseStart = diagnostics?.Mark() ?? 0;
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
            diagnostics?.Add("workspaceReturnStageCleanup", phaseStart);
            if (diagnostics is not null)
            {
                diagnostics.TotalMilliseconds = Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;
                diagnostics.AllocatedBytes = GC.GetTotalAllocatedBytes() - allocationStart;
            }
        }
    }

    static void CreateParentDirectory(string target, HashSet<string> createdParents)
    {
        string parent = Path.GetDirectoryName(target)!;
        if (createdParents.Contains(parent)) return;
        Directory.CreateDirectory(parent);
        createdParents.Add(parent);
    }

    static string CanonicalRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':')
            || segments.Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"安全でない相対パスです: {path}");
        return string.Join('/', segments);
    }

    static string SafeTarget(string root, string rootWithSeparator, string relative)
    {
        string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new InvalidDataException($"展開先外のパスです: {relative}");
        return target;
    }
}

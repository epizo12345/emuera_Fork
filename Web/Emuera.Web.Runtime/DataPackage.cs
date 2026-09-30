using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MinorShift.Emuera.Web.Runtime;

public sealed record DataPackageFile(string Path, long Size, string Sha256, string Pack);
public sealed record DataPackageManifest(int Version, long TotalSize, List<DataPackageFile> Files);
public sealed record DataPackageExtractionResult(long BytesWritten, int EntryCount, int LookupCount);

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

    public static async Task<DataPackageExtractionResult> ExtractPackAsync(Stream archive, string packName, string destinationRoot, DataPackageIndex index, long maxExtractedBytes, long alreadyExtractedBytes = 0, CancellationToken cancellationToken = default)
    {
        Dictionary<string, DataPackageFile> expected = index.GetPack(packName);
        string destination = Path.GetFullPath(destinationRoot);
        string stage = destination + ".stage-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        long bytesWritten = 0;
        int lookups = 0;
        try
        {
            using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
            var entries = zip.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
            if (entries.Length != expected.Count)
                throw new InvalidDataException($"packのファイル数が一致しません: {packName}");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in entries)
            {
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

                string target = SafeTarget(stage, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using Stream input = entry.Open();
                await using FileStream output = File.Create(target);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                long fileBytes = 0;
                try
                {
                    int count;
                    while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                    {
                        if (fileBytes + count > file.Size || alreadyExtractedBytes + bytesWritten + count > maxExtractedBytes)
                            throw new InvalidDataException($"展開中に宣言サイズまたは総サイズ上限を超えました: {entry.FullName}");
                        hash.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        fileBytes += count;
                        bytesWritten += count;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                if (fileBytes != file.Size)
                    throw new InvalidDataException($"展開後sizeが一致しません: {entry.FullName}");
                string actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (!actualHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA-256が一致しません: {entry.FullName}");
            }

            foreach (DataPackageFile file in expected.Values)
                if (File.Exists(SafeTarget(destination, file.Path)))
                    throw new InvalidDataException($"展開先に既存ファイルがあります: {file.Path}");
            foreach (DataPackageFile file in expected.Values)
            {
                string target = SafeTarget(destination, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(SafeTarget(stage, file.Path), target);
            }
            return new(bytesWritten, entries.Length, lookups);
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, recursive: true);
        }
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

    static string SafeTarget(string root, string relative)
    {
        string rootWithSeparator = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new InvalidDataException($"展開先外のパスです: {relative}");
        return target;
    }
}

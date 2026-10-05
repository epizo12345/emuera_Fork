namespace MinorShift.Emuera.Web.Runtime;

/// <summary>Native chara_*.dat names, with portable leaf-name bounds. Never a path.</summary>
public static class BrowserCharacterFiles
{
    public const int MaximumFileBytes = 64 * 1024 * 1024;
    public const int MaximumCharacters = 256;
    public const string Prefix = "dat/";

    public static string ValidateName(string name, bool pattern = false)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 200 || name is "." or ".."
            || name.EndsWith(' ') || name.EndsWith('.')
            || name.Any(c => c < 32 || "<>:\"/\\|".Contains(c) || (!pattern && "*?".Contains(c))))
            throw new InvalidDataException("キャラdat名はパスを含まない200文字以内のファイル名にしてください");
        return name;
    }

    public static string NormalizeFilename(string filename)
    {
        if (!filename.StartsWith("chara_", StringComparison.OrdinalIgnoreCase)
            || !filename.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("キャラdatはchara_<名前>.dat形式です");
        ValidateName(filename[6..^4]);
        return "chara_" + filename[6..^4] + ".dat";
    }

    internal static string PathForName(string root, string name)
    {
        string filename = NormalizeFilename("chara_" + ValidateName(name) + ".dat");
        if (Directory.Exists(root))
            foreach (string path in Directory.GetFiles(root))
                if (string.Equals(Path.GetFileName(path), filename, StringComparison.OrdinalIgnoreCase)) return path;
        return Path.Combine(root, filename);
    }

    internal static void WriteAtomic(string path, byte[] bytes)
    {
        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed record BrowserCharacterFileInfo(string Filename, int State, string Memo, int CharacterCount);

internal sealed class BoundedCharacterStream(string path) : FileStream(path, FileMode.CreateNew, FileAccess.Write)
{
    void CheckLength(int count)
    {
        if (count > BrowserCharacterFiles.MaximumFileBytes - Position)
            throw new InvalidDataException("キャラdatのサイズ上限は64MiBです");
    }
    public override void Write(byte[] buffer, int offset, int count) { CheckLength(count); base.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer) { CheckLength(buffer.Length); base.Write(buffer); }
    public override void WriteByte(byte value) { CheckLength(1); base.WriteByte(value); }
}

using System;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Utils;


internal abstract class EmueraException : ApplicationException
{
    protected EmueraException(string errormes, ScriptPosition? position)
        : base(errormes)
    {
        Position = position;
    }
    protected EmueraException(string errormes)
        : base(errormes)
    {
        Position = null;
    }
    public ScriptPosition? Position;
}

/// <summary>
/// emuera本体に起因すると思われるエラー
/// </summary>

internal sealed class ExeEE : EmueraException
{
    public ExeEE(string errormes)
        : base(errormes)
    {
    }
    public ExeEE(string errormes, ScriptPosition position)
        : base(errormes, position)
    {
    }
}

/// <summary>
/// スクリプト側に起因すると思われるエラー
/// </summary>

internal class CodeEE : EmueraException
{
    public CodeEE(string errormes, ScriptPosition? position)
        : base(errormes, position)
    {
    }
    public CodeEE(string errormes)
        : base(errormes)
    {
    }
}

/// <summary>
/// スクリプト側に起因すると思われるエラーのうち、未定義の識別子に関連するもの
/// </summary>

internal sealed class IdentifierNotFoundCodeEE : CodeEE
{
    public IdentifierNotFoundCodeEE(string errormes, ScriptPosition position)
        : base(errormes, position)
    {
    }
    public IdentifierNotFoundCodeEE(string errormes)
        : base(errormes)
    {
    }
}

/// <summary>
/// 未実装エラー
/// </summary>

internal sealed class NotImplCodeEE : CodeEE
{
    public NotImplCodeEE(ScriptPosition position)
        : base(LocalizationManager.Error.CanNotUseFuncCurrentVer, position)
    {
    }
    public NotImplCodeEE()
        : base(LocalizationManager.Error.CanNotUseFuncCurrentVer)
    {
    }
}

/// <summary>
/// Save, Load中のエラー
/// </summary>

internal sealed class FileEE : EmueraException
{
    public FileEE(string errormes)
        : base(errormes)
    { }
}

/// <summary>
/// エラー箇所を表示するための位置データ。整形前のデータなのでエラー表示以外の理由で参照するべきではない。
/// </summary>
internal static class ScriptFileRegistry
{
    static readonly object sync = new();
    static readonly System.Collections.Generic.Dictionary<string, int> ids = new(StringComparer.Ordinal);
    static readonly System.Collections.Generic.List<string> filenames = new() { null };
    static string[] filenameSnapshot = [null];

    internal static int GetId(string filename)
    {
        filename ??= "";
        lock (sync)
        {
            if (ids.TryGetValue(filename, out int id))
                return id;
            id = filenames.Count;
            ids.Add(filename, id);
            filenames.Add(filename);
            filenameSnapshot = filenames.ToArray();
            return id;
        }
    }

    internal static string GetFilename(int id)
    {
        return System.Threading.Volatile.Read(ref filenameSnapshot)[id];
    }
}

readonly record struct ScriptPosition
{
    public ScriptPosition()
    {
        LineNo = -1;
        FileId = ScriptFileRegistry.GetId("");
        Filename = "";
    }
    public ScriptPosition(string srcFile, int srcLineNo)
    {
        LineNo = srcLineNo + 1;
        FileId = ScriptFileRegistry.GetId(srcFile);
        Filename = srcFile ?? "";
    }
    internal ScriptPosition(int fileId, int srcLineNo)
    {
        LineNo = srcLineNo + 1;
        FileId = fileId;
        Filename = ScriptFileRegistry.GetFilename(fileId);
    }
    public readonly int LineNo;
    internal readonly int FileId;
    public readonly string Filename;
}

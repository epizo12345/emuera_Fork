using System.Text.RegularExpressions;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Utils;

static partial class Rename
{
    [GeneratedRegex(@"\[\[.*?\]\]")]
    private static partial Regex regexRenameIdentifer();

    public static string RenameString(string @string, ScriptPosition? position = null, bool suppressWarning = false)
    {
        // [Emuera改修:START-05]
        // Rename対象は [[名前]] の形。まず安い文字検索を行い、"[["すらない普通の行では
        // 重い正規表現を実行せずそのまま返す。置換結果や警告の意味は変わらない。
        // 参照: プロジェクト資料/06_コード案内.md
        if (!@string.Contains("[["))
            return @string;

        var match = regexRenameIdentifer().Match(@string);
        while (match.Success)
        {
            //この段階でマッチしないパターンもある
            if (ParserMediator.RenameDic.TryGetValue(match.Value, out var targetStr))
            {
                @string = @string.Replace(match.Value, targetStr);
            }
            else if (!suppressWarning)
            {
                ParserMediator.Warn($"Renameに失敗 {match}", position, 1);
            }

            match = match.NextMatch();
        }
        return @string;
    }
}

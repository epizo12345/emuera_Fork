using System.Text.RegularExpressions;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Utils;

static partial class Rename
{
    [GeneratedRegex(@"\[\[.*?\]\]")]
    private static partial Regex regexRenameIdentifer();

    public static string RenameString(string @string, ScriptPosition? position = null)
    {
        var match = regexRenameIdentifer().Match(@string);
        while (match.Success)
        {
            //この段階でマッチしないパターンもある
            if (ParserMediator.RenameDic.TryGetValue(match.Value, out var targetStr))
            {
                @string = @string.Replace(match.Value, targetStr);
            }
            else
            {
                ParserMediator.Warn($"Renameに失敗 {match}", position, 1);
            }

            match = match.NextMatch();
        }
        return @string;
    }
}
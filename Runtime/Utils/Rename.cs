using System.Text.RegularExpressions;
using MinorShift.Emuera;

static partial class Rename
{
    [GeneratedRegex(@"\[\[.*?\]\]")]
    private static partial Regex regexRenameIdentifer();

    public static string RenameString(string @string)
    {
        var match = regexRenameIdentifer().Match(@string);
        while (match.Success)
        {
            //この段階でマッチしないパターンもある
            if (ParserMediator.RenameDic.TryGetValue(match.Value, out var targetStr))
            {
                @string = @string.Replace(match.Value, targetStr);
            }

            match = match.NextMatch();
        }
        return @string;
    }
}
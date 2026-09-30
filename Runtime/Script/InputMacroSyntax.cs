using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.Runtime.Script;

public readonly record struct InputMacroToken(string Value, bool MessageSkip);

public static class InputMacroSyntax
{
    static readonly string[] Splitters = ["\\n", "\r\n", "\n", "\r"];

    public static IReadOnlyList<InputMacroToken> Expand(string input) => Split(ExpandText(input))
        .Select(value => new InputMacroToken(value.Replace("\\e", string.Empty, StringComparison.Ordinal), value.Contains("\\e", StringComparison.Ordinal)))
        .ToArray();

    public static string[] Split(string input) => input.Split(Splitters, StringSplitOptions.None);

    public static string ExpandText(string input) => Parse(new CharStream(input), false);

    static string Parse(CharStream stream, bool nested)
    {
        StringBuilder output = new(20), count = new(20);
        bool hasReturn = false;
        while (!stream.EOS && (!nested || stream.Current != ')'))
        {
            if (stream.Current == '(')
            {
                stream.ShiftNext();
                string nestedText = Parse(stream, true);
                if (stream.EOS)
                {
                    output.Append(nestedText);
                    break;
                }
                stream.ShiftNext();
                if (stream.Current == '*')
                {
                    stream.ShiftNext();
                    while (char.IsNumber(stream.Current))
                    {
                        count.Append(stream.Current);
                        stream.ShiftNext();
                    }
                    if (int.TryParse(count.ToString(), out int repeat))
                        for (int index = 0; index < repeat; index++) output.Append(nestedText);
                    count.Clear();
                }
                else output.Append(nestedText);
                continue;
            }
            if (stream.Current == '\\')
            {
                stream.ShiftNext();
                switch (stream.Current)
                {
                    case 'n': if (!hasReturn) output.Append('\n'); else hasReturn = false; break;
                    case 'r': output.Append('\r'); break;
                    case 'e': output.Append("\\e\n"); hasReturn = true; break;
                    case '\n': break;
                    default: output.Append(stream.Current); break;
                }
            }
            else output.Append(stream.Current);
            stream.ShiftNext();
        }
        return output.ToString();
    }
}

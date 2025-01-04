using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Sub;
using System;
using System.IO;
using System.Text.RegularExpressions;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Script;

internal static partial class KeyMacro
{
    public static readonly string macroPath = Program.ExeDir + "macro.txt";
    private const string gID = "グループ{0}:{1}";
    public const int MaxGroup = 10;
    public const int MaxFkey = 12;
    public const int MaxMacro = MaxFkey * MaxGroup;
    /// <summary>
    /// マクロの内容
    /// </summary>
    static string[] macro = new string[MaxMacro];
    /// <summary>
    /// マクロキー
    /// </summary>
    static string[] macroName = new string[MaxMacro];
    static string[] groupName = new string[MaxGroup];
    static bool isMacroChanged;
    static KeyMacro()
    {
        for (int g = 0; g < MaxGroup; g++)
        {
            groupName[g] = string.Format(LocalizationManager.KeyMacro.SetMacroGroup, g.ToString());
            for (int f = 0; f < MaxFkey; f++)
            {
                int i = f + g * MaxFkey;
                macro[i] = "";
                if (g == 0)
                    macroName[i] = string.Format(LocalizationManager.KeyMacro.MacroKeyFJapanese, (f + 1).ToString());
                else
                    macroName[i] = string.Format(LocalizationManager.KeyMacro.GMacroKeyFJapanese, g.ToString(), (f + 1).ToString());

            }
        }
    }

    public static bool SaveMacro()
    {
        if (!isMacroChanged)
            return true;
        try
        {
            using var writer = new StreamWriter(macroPath, false, Config.Config.Encode);
            for (int g = 0; g < MaxGroup; g++)
            {
                writer.WriteLine(gID, g, groupName[g]);
            }
            for (int i = 0; i < MaxMacro; i++)
            {
                writer.WriteLine(macroName[i] + macro[i]);
            }
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }
    
    [GeneratedRegex(@"^グループ([0-9]):(.{3,})$")]
    private static partial Regex MacroGroupNameRegex { get; }
    
    [GeneratedRegex(@"^(?:G([0-9]):)?マクロキーF([1-9]|1[0-2]):(.+)$")]
    private static partial Regex MacroKeyRegex { get; }

    public static bool LoadMacroFile(string filename)
    {
        using var eReader = new EraStreamReader(false);
        if (!eReader.Open(filename))
            return false;
        try
        {
            while (eReader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line[0] == ';')
                    continue;
                if (MacroGroupNameRegex.Match(line) is { Success: true } groupNameMatch)
                {
                    int num = int.Parse(groupNameMatch.Groups[1].Value);
                    groupName[num] = groupNameMatch.Groups[2].Value;
                }
                else if (MacroKeyRegex.Match(line) is { Success: true } macroMatch)
                {
                    int groupNum = macroMatch.Groups[1].Success ? int.Parse(macroMatch.Groups[1].Value) : 0;
                    int fkeyNum = int.Parse(macroMatch.Groups[2].Value);
                    macro[fkeyNum + groupNum * MaxFkey - 1] = macroMatch.Groups[3].Value;
                }
            }
        }
        catch
        {
            return false;
        }
        return true;
    }

    public static void SetMacro(int FkeyNum, int groupNum, string macroStr)
    {
        isMacroChanged = true;
        macro[FkeyNum + groupNum * MaxFkey] = macroStr;
    }

    public static string GetMacro(int FkeyNum, int groupNum)
    {
        return macro[FkeyNum + groupNum * MaxFkey];
    }

    public static string GetGroupName(int groupNum)
    {
        return groupName[groupNum];
    }
}
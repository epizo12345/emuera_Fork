using System;
using System.IO;
using System.Reflection;

namespace MinorShift.Emuera.Runtime.Utils;

public static class AssemblyData
{
    static AssemblyData()
    {
        ExePath = Environment.ProcessPath;
        //エラー出力用
        //1815 .exeが東方板のNGワードに引っかかるそうなので除去
        ExeName = Path.GetFileNameWithoutExtension(ExePath);
        var assembly = Assembly.GetExecutingAssembly();
        emueraVer = assembly.GetName().Version;

        EmueraVersionText = ".NET Emuera " + assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.ToString();
    }

    /// <summary>
    /// 実行ファイルのパス
    /// </summary>
    public static readonly string ExePath;

    /// <summary>
    /// 実行ファイルの名前。ディレクトリなし
    /// </summary>
    public static readonly string ExeName;


    public readonly static Version emueraVer;

    public readonly static string EmueraVersionText;

    /// <summary>
    /// 2重起動防止。既に同名exeが実行されているならばtrueを返す
    /// </summary>
    /// <returns></returns>
    public static bool PrevInstance()
    {
        string thisProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        if (System.Diagnostics.Process.GetProcessesByName(thisProcessName).Length > 1)
        {
            return true;
        }
        return false;

    }
}


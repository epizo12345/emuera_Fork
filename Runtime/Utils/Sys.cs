using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

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

        EmueraVersionText = "Emuera.NET " + assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.ToString();
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

    static Mutex Mutex;

    /// <summary>
    /// 2重起動防止。Mutexでロックを取る
    /// </summary>
    /// <returns></returns>
    public static bool PrevInstance()
    {
        var pathHash = string.Join("", MD5.HashData(Encoding.UTF8.GetBytes(ExePath)));
        Mutex = new Mutex(false, pathHash);
        var hasOwn = Mutex.WaitOne(0);
        if (hasOwn)
        {
            Application.ApplicationExit += (_, _) => Mutex.Dispose();
        }
        return !hasOwn;
    }
}


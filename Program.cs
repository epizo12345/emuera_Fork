using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;
using System.Globalization;
using System.Reflection;

namespace MinorShift.Emuera;
#nullable enable

static partial class Program
{
    /*
	コードの開始地点。
	ここでMainWindowを作り、
	MainWindowがProcessを作り、
	ProcessがGameBase・ConstantData・Variableを作る。


	*.ERBの読み込み、実行、その他の処理をProcessが、
	入出力をMainWindowが、
	定数の保存をConstantDataが、
	変数の管理をVariableが行う。

	と言う予定だったが改変するうちに境界が曖昧になってしまった。

	後にEmueraConsoleを追加し、それに入出力を担当させることに。

1750 DebugConsole追加
 Debugを全て切り離すことはできないので一部EmueraConsoleにも担当させる

	TODO: 1819 MainWindow & Consoleの入力・表示組とProcess&Dataのデータ処理組だけでも分離したい

	*/
    /// <summary>
    /// アプリケーションのメイン エントリ ポイントです。
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // memo: Shift-JISを扱うためのおまじない
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var rootCommand = new RootCommand("Emuera");

        var exeDirOption = new Option<string>(
            name: "--ExeDir",
            description: LocalizationManager.Parameters.HelpExeDir
        );
        rootCommand.AddOption(exeDirOption);

        var debugModeOption = new Option<bool>(
            name: "-Debug",
            description: LocalizationManager.Parameters.HelpDebug
        );
        debugModeOption.AddAlias("-debug");
        debugModeOption.AddAlias("-DEBUG");
        rootCommand.AddOption(debugModeOption);

        var filesArg = new Argument<string[]>(
            LocalizationManager.Parameters.HelpfilesArg
        )
        { Arity = ArgumentArity.ZeroOrMore };
        rootCommand.AddArgument(filesArg);

        var result = rootCommand.Parse(args);

        //実行ディレクトリが引数で与えられた場合
        var exeDir = result.GetValueForOption(exeDirOption);
        if (exeDir != null)
        {
            SetDirPaths(exeDir);
        }

        var debugMode = result.GetValueForOption(debugModeOption);
        DebugMode = debugMode;

        var fileArgs = result.GetValueForArgument(filesArg);
        var analysisRequestPaths = fileArgs;
        if (analysisRequestPaths.Length > 0)
        {
            //必要なファイルのチェックにはConfig読み込みが必須なので、ここではフラグだけ立てておく
            AnalysisMode = true;
        }

        // --------------------------------------
        // 「ProfileOptimization」の使用が原因による起動時失敗が発生するため、処理をコメントアウト
        //ProfileOptimization.SetProfileRoot(exeDir ?? ExeDir);
        //ProfileOptimization.StartProfile(AssemblyData.EmueraVersionText + ".profile");
        // --------------------------------------

        ConfigData.Instance.LoadConfig();
        JSONConfig.Load();


        //二重起動の禁止かつ二重起動
        if ((!Config.AllowMultipleInstances) && AssemblyData.PrevInstance())
        {
            Dialog.Show(LocalizationManager.MsgBox.InstanceExists, LocalizationManager.MsgBox.MultiInstanceInfo);
            return;
        }
        if (!Directory.Exists(CsvDir))
        {
            Dialog.Show(LocalizationManager.MsgBox.NoCsvFolder, CsvDir);
            return;
        }
        if (!Directory.Exists(ErbDir))
        {
            Dialog.Show(LocalizationManager.MsgBox.NoErbFolder, ErbDir);
            return;
        }



        if (DebugMode)
        {
            ConfigData.Instance.LoadDebugConfig();
            if (!Directory.Exists(DebugDir))
            {
                try
                {
                    Directory.CreateDirectory(DebugDir);
                }
                catch
                {
                    Dialog.Show(LocalizationManager.MsgBox.FailedCreateDebugFolder, DebugDir);
                    return;
                }
            }
        }
        if (AnalysisMode)
        {
            foreach (var path in analysisRequestPaths)
            {
                if (!Path.Exists(path))
                {
                    Dialog.Show(LocalizationManager.MsgBox.FolderNotFound);
                    return;
                }
                if (File.GetAttributes(path).HasFlag(FileAttributes.Directory))
                {
                    foreach (var file in Config.GetFiles(path + "\\", "*.ERB"))
                    {
                        AnalysisFiles.Add(file.Value);
                    }
                }
                else
                {
                    if (!Path.GetExtension(path).Equals(".ERB", StringComparison.OrdinalIgnoreCase))
                    {
                        Dialog.Show(LocalizationManager.MsgBox.InvalidArg);
                        return;
                    }
                    AnalysisFiles.Add(path);
                }
            }
        }

        ApplicationConfiguration.Initialize();

        using var win = new Forms.MainWindow(args);


        Application.Run(win);

    }

    [MemberNotNull(nameof(ExeDir))]
    [MemberNotNull(nameof(CsvDir))]
    [MemberNotNull(nameof(ErbDir))]
    [MemberNotNull(nameof(DebugDir))]
    [MemberNotNull(nameof(DatDir))]
    [MemberNotNull(nameof(ContentDir))]
    private static void SetDirPaths(string exeDir)
    {
        ExeDir = Path.GetFullPath(new DirectoryInfo(exeDir).FullName + Path.DirectorySeparatorChar);

        CsvDir = Path.Combine(ExeDir, "csv") + Path.DirectorySeparatorChar;
        ErbDir = Path.Combine(ExeDir, "erb") + Path.DirectorySeparatorChar;
        DebugDir = Path.Combine(ExeDir, "debug") + Path.DirectorySeparatorChar;
        DatDir = Path.Combine(ExeDir, "dat") + Path.DirectorySeparatorChar;
        ContentDir = Path.Combine(ExeDir, "resources") + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// 実行ファイルのディレクトリ。最後にPath.DirectorySeparatorCharを付けたstring
    /// </summary>
    public static string ExeDir { get; private set; }
    public static string CsvDir { get; private set; }
    public static string ErbDir { get; private set; }
    public static string DebugDir { get; private set; }
    public static string DatDir { get; private set; }
    public static string ContentDir { get; private set; }

    public static bool AnalysisMode { get; private set; }
    public static List<string> AnalysisFiles = [];

    public static bool DebugMode { get; private set; }

    static Program()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDirectory, "Data", "erb")))
        {
            baseDirectory = Path.Combine(baseDirectory, "Data");
        }
        SetDirPaths(baseDirectory);

    }

}
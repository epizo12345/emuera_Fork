using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime;
using System.Windows.Forms;
using MinorShift.Emuera.UI.Framework;
using System.Globalization;
using System.Diagnostics;

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
        // [Emuera改修:MEASURE-01]
        // EXEが動き始めた瞬間を記録する。通常版では空処理になるため速度に影響しない。
        // 参照: プロジェクト資料/06_コード案内.md
        PerformanceMetrics.MarkProcessStart();
        // memo: Shift-JISを扱うためのおまじない
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var rootCommand = new RootCommand("Emuera");

        var exeDirOption = new Option<string>(name: "--ExeDir");
        // [Emuera改修:DEPENDENCY-01] 修正者: epizo
        // 安定版System.CommandLine 2.0では、オプションと引数をそれぞれ専用の一覧へ登録する。
        // 受け取る文字やゲーム側へ渡す値は従来と同じで、解析ライブラリだけを安定版に戻している。
        rootCommand.Options.Add(exeDirOption);

        var debugModeOption = new Option<bool>("-Debug", "-debug", "-DEBUG");
        rootCommand.Options.Add(debugModeOption);

        // [Emuera改修:TOOLS-01]
        // 自動テスト用の入口。ゲーム操作用の通常オプションではない。
        // --StartupTest は操作可能になった時点でログを保存して自動終了する。
        var startupTestOption = new Option<bool>(name: "--StartupTest")
        {
            Description = "起動完了後に画面ログをstartup-test.logへ保存して自動終了する"
        };
        rootCommand.Options.Add(startupTestOption);

        // --BenchmarkLog は計測版だけが使うJSON Linesの保存先を受け取る。
        var benchmarkLogOption = new Option<string>(name: "--BenchmarkLog")
        {
            Description = "起動・マクロ性能計測のJSON Lines出力先"
        };
        rootCommand.Options.Add(benchmarkLogOption);
#if LEGACY_ORACLE
        var legacyOracleOption = new Option<string>(name: "--LegacyOracle")
        {
            Description = "Legacy parser label manifest output path (diagnostic build only)"
        };
        rootCommand.Options.Add(legacyOracleOption);
#endif

#if PERFORMANCE_METRICS
        var erbStartupProfileOption = new Option<string>(name: "--ErbStartupProfile")
        {
            Description = "ERB詳細計測の出力フォルダ（計測ビルド専用）"
        };
        rootCommand.Options.Add(erbStartupProfileOption);
        var erbStartupProfileModeOption = new Option<string>(name: "--ErbStartupProfileMode")
        {
            Description = "ERB詳細計測モード: timing または counters"
        };
        rootCommand.Options.Add(erbStartupProfileModeOption);
#endif

        var filesArg = new Argument<string[]>(
            LocalizationManager.Parameters.HelpfilesArg
        )
        { Arity = ArgumentArity.ZeroOrMore };
        rootCommand.Arguments.Add(filesArg);

        var result = rootCommand.Parse(args);
        PerformanceMetrics.Configure(result.GetValue(benchmarkLogOption));
#if LEGACY_ORACLE
        LegacyOraclePath = result.GetValue(legacyOracleOption);
#endif
#if PERFORMANCE_METRICS
        ErbStartupProfiler.Configure(result.GetValue(erbStartupProfileOption), result.GetValue(erbStartupProfileModeOption));
#endif

        //実行ディレクトリが引数で与えられた場合
        var exeDir = result.GetValue(exeDirOption);
        if (exeDir != null)
        {
            SetDirPaths(exeDir);
        }

        var debugMode = result.GetValue(debugModeOption);
        DebugMode = debugMode;
        StartupTestMode = result.GetValue(startupTestOption);

        var fileArgs = result.GetValue(filesArg) ?? [];
        var analysisRequestPaths = fileArgs;
        if (analysisRequestPaths.Length > 0)
        {
            //必要なファイルのチェックにはConfig読み込みが必須なので、ここではフラグだけ立てておく
            AnalysisMode = true;
        }

        //利用推奨の.NET Coreのバージョン
        var targetVersion = "10.0.0";

        //使用している端末の.NET Coreのバージョンを確認し、一定以下の場合はエラーとする
        if (Environment.Version.Build < new Version(targetVersion).Build)
        {
            //.Net Coreのバージョンが一定以下の場合はエラーメッセージを表示する
            MessageBox.Show("ご使用の端末の「.NET」のバージョンは" + Environment.Version + "です。" + Environment.NewLine + targetVersion + "以上に更新してください。");

            //App.configに.Net Coreのインストール用ページを開く
            var installUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/10.0";
            Process.Start(new ProcessStartInfo(installUrl) { UseShellExecute = true });
            return;
        }

#if !LEGACY_ORACLE
        ProfileOptimization.SetProfileRoot(exeDir ?? ExeDir);
        ProfileOptimization.StartProfile(AssemblyData.EmueraVersionText + ".profile");
#endif

        ConfigData.Instance.LoadConfig();
        JSONConfig.Load();
        // [Emuera改修:MEASURE-01] 設定読込区間の終点。通常版では空処理。
        PerformanceMetrics.MarkStartup("SettingsLoaded");


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
        Application.SetColorMode(SystemColorMode.Dark);

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

    public static bool StartupTestMode { get; private set; }

#if LEGACY_ORACLE
    public static string LegacyOraclePath { get; private set; }
#endif

    static Program()
    {
        var baseDirectory = AppContext.BaseDirectory;
        // [Emuera改修:MEM-13R40.2 2026-08-23]
        // 通常のsingle-file配置では実行ファイル横のData\erbを実効DataDirとして採用し、
        // --ExeDirとsetting.jsonもこのDataDirを基準にする。テストfixtureも同じ契約で指定する。
        if (Directory.Exists(Path.Combine(baseDirectory, "Data", "erb")))
        {
            baseDirectory = Path.Combine(baseDirectory, "Data");
        }
        SetDirPaths(baseDirectory);

    }

}

using System;
using System.Drawing;
using System.Collections.Generic;
using MinorShift._Library;
using System.IO;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Windows.Forms;
using System.Threading.Tasks;
using Windows.Win32;
using System.CommandLine.Builder;

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

		var rootCommand = new RootCommand("Emuera");

		var exeDirOption = new Option<string>(
			name: "--ExeDir",
			description: "与えられたフォルダのEraを起動します"
		);
		rootCommand.AddOption(exeDirOption);

		var debugModeOption = new Option<bool>(
			name: "-Debug",
			description: "デバッグモード"
		);
		debugModeOption.AddAlias("-debug");
		debugModeOption.AddAlias("-DEBUG");
		rootCommand.AddOption(debugModeOption);

		var filesArg = new Argument<string[]>(
			"解析するファイル"
		)
		{ Arity = ArgumentArity.ZeroOrMore };
		rootCommand.AddArgument(filesArg);

		rootCommand.SetHandler(Handler,
		exeDirOption,
		debugModeOption,
		filesArg);

		var parser = new CommandLineBuilder(rootCommand)
			.UseDefaults()
			.Build();

		parser.Invoke(args);
	}

	private static void Handler(string? exeDir, bool debugMode, string[] fileArgs)
	{
		//実行ディレクトリが引数で与えられた場合
		if (exeDir is not null)
		{
			ExeDir = Path.Join(exeDir.AsSpan(), [Path.DirectorySeparatorChar]);

			CsvDir = Path.Join(ExeDir.AsSpan(), "csv", [Path.DirectorySeparatorChar]);
			ErbDir = Path.Join(ExeDir.AsSpan(), "erb", [Path.DirectorySeparatorChar]);
			DebugDir = Path.Join(ExeDir.AsSpan(), "debug", [Path.DirectorySeparatorChar]);
			DatDir = Path.Join(ExeDir.AsSpan(), "dat", [Path.DirectorySeparatorChar]);
			ContentDir = Path.Join(ExeDir.AsSpan(), "resources", [Path.DirectorySeparatorChar]);
		}

		DebugMode = debugMode;

		var analysisRequestPaths = fileArgs;
		if (analysisRequestPaths.Length > 0)
		{
			//必要なファイルのチェックにはConfig読み込みが必須なので、ここではフラグだけ立てておく
			AnalysisMode = true;
		}

		ApplicationConfiguration.Initialize();
		ConfigData.Instance.LoadConfig();
		//二重起動の禁止かつ二重起動
		if ((!Config.AllowMultipleInstances) && AssemblyData.PrevInstance())
		{
			System.Windows.MessageBox.Show("多重起動を許可する場合、emuera.configを書き換えて下さい", "既に起動しています");
			return;
		}
		if (!Directory.Exists(CsvDir))
		{
			System.Windows.MessageBox.Show(CsvDir, "csvフォルダが見つかりません");
			return;
		}
		if (!Directory.Exists(ErbDir))
		{
			System.Windows.MessageBox.Show("erbフォルダが見つかりません", "フォルダなし");
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
					System.Windows.MessageBox.Show("debugフォルダの作成に失敗しました", "フォルダなし");
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
					System.Windows.MessageBox.Show("与えられたファイル・フォルダは存在しません");
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
						System.Windows.MessageBox.Show("ドロップ可能なファイルはERBファイルのみです");
						return;
					}
					AnalysisFiles.Add(path);
				}
			}
		}

		var winState = FormWindowState.Normal;
		var rebootClientHeight = 0;
		var rebootLocation = Point.Empty;
		while (true)
		{
			var rebootFlag = false;

			using var win = new Forms.MainWindow(winState, rebootLocation, rebootClientHeight, (_) =>
			{
				rebootFlag = true;
			});


			Application.Run(win);

			Content.AppContents.UnloadContents();
			if (!rebootFlag)
				break;

			winState = win.WindowState;

			if (win.WindowState == FormWindowState.Normal)
			{
				rebootClientHeight = win.ClientSize.Height;
				rebootLocation = win.Location;
			}

			//条件次第ではParserMediatorが空でない状態で再起動になる場合がある
			ParserMediator.ClearWarningList();
			ParserMediator.Initialize(null);
			GlobalStatic.Reset();
			//GC.Collect();
			ConfigData.Instance.ReLoadConfig();
		}
	}

	/// <summary>
	/// 実行ファイルのディレクトリ。最後に\を付けたstring
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
		ExeDir = Path.Join(
			AppContext.BaseDirectory.AsSpan(),
			[Path.DirectorySeparatorChar]
		);

		CsvDir = Path.Join(ExeDir.AsSpan(), "csv", [Path.DirectorySeparatorChar]);
		ErbDir = Path.Join(ExeDir.AsSpan(), "erb", [Path.DirectorySeparatorChar]);
		DebugDir = Path.Join(ExeDir.AsSpan(), "debug", [Path.DirectorySeparatorChar]);
		DatDir = Path.Join(ExeDir.AsSpan(), "dat", [Path.DirectorySeparatorChar]);
		ContentDir = Path.Join(ExeDir.AsSpan(), "resources", [Path.DirectorySeparatorChar]);
	}

}
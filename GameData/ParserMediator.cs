using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using System.IO;
using System.Text.RegularExpressions;

namespace MinorShift.Emuera
{
	//1756 新設。ParserやLexicalAnalyzerなどが知りたい情報をまとめる
	//本当は引数として渡すべきなのかもしれないが全てのParserの引数を書きなおすのが面倒なのでstatic
	internal static class ParserMediator
	{
		/// <summary>
		/// emuera.config等で発生した警告
		/// Initializeより前に発生する
		/// </summary>
		/// <param name="str"></param>
		/// <param name="?"></param>
		public static void ConfigWarn(string str, ScriptPosition pos, int level, string stack)
		{
			if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
				return;
			warningList.Add(new ParserWarning(str, pos, level, stack));
		}

		static EmueraConsole console;
		public static void Initialize(EmueraConsole console)
		{
			ParserMediator.console = console;
		}

		#region Rename
		public static Dictionary<string, string> RenameDic { get; private set; }
		//1756 Process.Load.csより移動
		public static async void LoadEraExRenameFile(string filepath)
		{
			if (!File.Exists(filepath))
			{
				return;
			}
			if (RenameDic != null)
				RenameDic.Clear();
			//とにかく辞書を作る。辞書がnullのときは UseRenameFileがNOの時のみ
			RenameDic = [];
			var fileLine = File.ReadLinesAsync(filepath, Config.Encode);
			ScriptPosition pos = null;
			Regex reg = new(@"\\,", RegexOptions.Compiled);
			try
			{
				var lineNo = 0;
				await foreach (var line in fileLine)
				{
					if (line.Length == 0)
						continue;
					if (line.StartsWith(';'))
						continue;
					string[] baseTokens = reg.Split(line);
					if (!baseTokens[^1].Contains(','))
						continue;
					string[] last = baseTokens[^1].Split(',');
					baseTokens[^1] = last[0];
					string[] tokens = new string[2];
					tokens[0] = string.Join(',', baseTokens);
					tokens[1] = last[1];
					pos = new ScriptPosition(filepath, lineNo);
					//右がERB中の表記、左が変換先になる。
					string value = tokens[0].Trim();
					string key = string.Format("[[{0}]]", tokens[1].Trim());
					RenameDic[key] = value;
					lineNo++;
					pos = null;
				}
			}
			catch (Exception e)
			{
				if (pos != null)
					throw new CodeEE(e.Message, pos);
				else
					throw new CodeEE(e.Message);

			}
		}
		#endregion


		public static void Warn(string str, ScriptPosition pos, int level)
		{
			Warn(str, pos, level, null);
		}

		public static void Warn(string str, ScriptPosition pos, int level, string stack)
		{
			if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
				return;
			if (console != null && !console.RunERBFromMemory)
				warningList.Add(new ParserWarning(str, pos, level, stack));
		}

		/// <summary>
		/// Parser中での警告出力
		/// </summary>
		/// <param name="str"></param>
		/// <param name="line"></param>
		/// <param name="level">警告レベル.0:軽微なミス.1:無視できる行.2:行が実行されなければ無害.3:致命的</param>
		public static void Warn(string str, LogicalLine line, int level, bool isError, bool isBackComp)
		{
			Warn(str, line, level, isError, isBackComp, null);
		}

		public static void Warn(string str, LogicalLine line, int level, bool isError, bool isBackComp, string stack)
		{
			if (isError)
			{
				line.IsError = true;
				line.ErrMes = str;
			}
			if (level < Config.DisplayWarningLevel && !Program.AnalysisMode)
				return;
			if (isBackComp && !Config.WarnBackCompatibility)
				return;
			if (console != null && !console.RunERBFromMemory)
				warningList.Add(new ParserWarning(str, line.Position, level, stack));
			//				console.PrintWarning(str, line.Position, level);
		}

		private static List<ParserWarning> warningList = [];

		public static bool HasWarning { get { return warningList.Count > 0; } }
		public static void ClearWarningList()
		{
			warningList.Clear();
		}

		public static void FlushWarningList()
		{
			for (int i = 0; i < warningList.Count; i++)
			{
				ParserWarning warning = warningList[i];
				console.PrintWarning(warning.WarningMes, warning.WarningPos, warning.WarningLevel);
				if (warning.StackTrace != null)
				{
					string[] stacks = warning.StackTrace.Split('\n');
					for (int j = 0; j < stacks.Length; j++)
					{
						console.PrintSystemLine(stacks[j]);
					}
				}
			}
			warningList.Clear();
		}

		private class ParserWarning
		{
			public ParserWarning(string mes, ScriptPosition pos, int level, string stackTrace)
			{
				WarningMes = mes;
				WarningPos = pos;
				WarningLevel = level;
				StackTrace = stackTrace;
			}
			public string WarningMes;
			public ScriptPosition WarningPos;
			public int WarningLevel;
			public string StackTrace;
		}
	}
}
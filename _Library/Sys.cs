using System;
using System.IO;
using System.Reflection;

namespace MinorShift._Library
{
	public static class AssemblyData
	{
		static AssemblyData()
		{
			ExePath = Assembly.GetEntryAssembly().Location;
			//エラー出力用
			//1815 .exeが東方板のNGワードに引っかかるそうなので除去
			ExeName = Path.GetFileNameWithoutExtension(ExePath);
		}

		/// <summary>
		/// 実行ファイルのパス
		/// </summary>
		public static readonly string ExePath;

		/// <summary>
		/// 実行ファイルの名前。ディレクトリなし
		/// </summary>
		public static readonly string ExeName;


		public static Version emueraVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

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
}


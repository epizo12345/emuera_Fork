using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Sub
{
	[Serializable]
	internal abstract class EmueraException : ApplicationException
	{
		protected EmueraException(string errormes, ScriptPosition? position)
			: base(errormes)
		{
			Position = position;
		}
		protected EmueraException(string errormes)
			: base(errormes)
		{
			Position = null;
		}
		public ScriptPosition? Position;
	}

	/// <summary>
	/// emuera本体に起因すると思われるエラー
	/// </summary>
	[Serializable]
	internal sealed class ExeEE : EmueraException
	{
		public ExeEE(string errormes)
			: base(errormes)
		{
		}
		public ExeEE(string errormes, ScriptPosition position)
			: base(errormes, position)
		{
		}
	}

	/// <summary>
	/// スクリプト側に起因すると思われるエラー
	/// </summary>
	[Serializable]
	internal class CodeEE : EmueraException
	{
		public CodeEE(string errormes, ScriptPosition? position)
			: base(errormes, position)
		{
		}
		public CodeEE(string errormes)
			: base(errormes)
		{
		}
	}

	/// <summary>
	/// スクリプト側に起因すると思われるエラーのうち、未定義の識別子に関連するもの
	/// </summary>
	[Serializable]
	internal class IdentifierNotFoundCodeEE : CodeEE
	{
		public IdentifierNotFoundCodeEE(string errormes, ScriptPosition position)
			: base(errormes, position)
		{
		}
		public IdentifierNotFoundCodeEE(string errormes)
			: base(errormes)
		{
		}
	}

	/// <summary>
	/// 未実装エラー
	/// </summary>
	[Serializable]
	internal sealed class NotImplCodeEE : CodeEE
	{
		public NotImplCodeEE(ScriptPosition position)
			: base("この機能は現バージョンでは使えません", position)
		{
		}
		public NotImplCodeEE()
			: base("この機能は現バージョンでは使えません")
		{
		}
	}

	/// <summary>
	/// Save, Load中のエラー
	/// </summary>
	[Serializable]
	internal sealed class FileEE : EmueraException
	{
		public FileEE(string errormes)
			: base(errormes)
		{ }
	}

	/// <summary>
	/// エラー箇所を表示するための位置データ。整形前のデータなのでエラー表示以外の理由で参照するべきではない。
	/// </summary>
	readonly record struct ScriptPosition
	{
		public ScriptPosition()
		{
			LineNo = -1;
			Filename = "";
		}
		public ScriptPosition(string srcFile, int srcLineNo)
		{
			LineNo = srcLineNo + 1;
			Filename = srcFile ?? "";
		}
		public readonly int LineNo;
		public readonly string Filename;
	}
}

using System;
using System.Collections.Generic;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.GameData.Variable
{

	//変数の引数のうち文字列型のもの。
	internal sealed class VariableStrArgTerm : AExpression
	{
		public VariableStrArgTerm(VariableCode code, AExpression strTerm, int index)
			: base(typeof(Int64))
		{
			this.strTerm = strTerm;
			parentCode = code;
			this.index = index;
		}
		AExpression strTerm;
		readonly VariableCode parentCode;
		readonly int index;
		Dictionary<string, int> dic;
		string errPos;

		public override Int64 GetIntValue(ExpressionMediator exm)
		{
			if (dic == null)
				dic = exm.VEvaluator.Constant.GetKeywordDictionary(out errPos, parentCode, index);
			string key = strTerm.GetStrValue(exm);
			if (key == "")
				throw new CodeEE("キーワードを空には出来ません");
			if (!dic.TryGetValue(key, out int i))
			{
				if (errPos == null)
					throw new CodeEE("配列変数" + parentCode.ToString() + "の要素を文字列で指定することはできません");
				else
					throw new CodeEE(errPos + "の中に\"" + key + "\"の定義がありません");
			}
			return i;
		}

		public override AExpression Restructure(ExpressionMediator exm)
		{
			if (dic == null)
				dic = exm.VEvaluator.Constant.GetKeywordDictionary(out errPos, parentCode, index);
			strTerm = strTerm.Restructure(exm);
			if (!(strTerm is SingleTerm))
				return this;
			return new SingleLongTerm(this.GetIntValue(exm));
		}
	}

}
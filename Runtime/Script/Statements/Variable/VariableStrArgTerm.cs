using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements.Variable;


//変数の引数のうち文字列型のもの。
internal sealed class VariableStrArgTerm : AExpression
{
    public VariableStrArgTerm(VariableCode code, AExpression strTerm, int index)
        : base(typeof(long))
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

    public override long GetIntValue(ExpressionMediator exm)
    {
        if (dic == null)
            dic = exm.VEvaluator.Constant.GetKeywordDictionary(out errPos, parentCode, index);
        string key = strTerm.GetStrValue(exm);
        if (string.IsNullOrEmpty(key))
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
        return new SingleLongTerm(GetIntValue(exm));
    }
}
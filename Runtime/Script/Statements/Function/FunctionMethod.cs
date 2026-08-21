using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Script.Statements.Function;

internal abstract class FunctionMethod
{
    public Type ReturnType { get; protected set; }
    protected Type[] argumentTypeArray;
    protected string Name { get; private set; }

    //引数の数・型が一致するかどうかのテスト
    //正しくない場合はエラーメッセージを返す。
    //引数の数が不定である場合や引数の省略を許す場合にはoverrideすること。
    public virtual string CheckArgumentType(string name, List<AExpression> arguments)
    {
        if (arguments.Count != argumentTypeArray.Length)
        {
            return $"{name}関数の引数の数が間違っています";
        }
        for (int i = 0; i < argumentTypeArray.Length; i++)
        {
            if (arguments[i] == null)
                return $"{name}関数:第{i + 1}引数は省略できません";
            if (argumentTypeArray[i] != arguments[i].GetOperandType())
                return $"{name}関数:第{i + 1}引数の型が間違っています";
        }
        return null;
    }

    //Argumentが全て定数の時にMethodを解体してよいかどうか。RANDやCharaを参照するものなどは不可
    public bool CanRestructure { get; protected set; }

    //FunctionMethodが固有のRestructure()を持つかどうか
    public bool HasUniqueRestructure { get; protected set; }

    //実際の計算。
    public virtual long GetIntValue(ExpressionMediator exm, List<AExpression> arguments) { throw new ExeEE(LocalizationManager.Error.ReturnTypeDifferentOrNotImpelemnt); }
    public virtual string GetStrValue(ExpressionMediator exm, List<AExpression> arguments) { throw new ExeEE(LocalizationManager.Error.ReturnTypeDifferentOrNotImpelemnt); }
    public virtual SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
    {
        if (ReturnType == typeof(long))
            return SingleLongTerm.FromValue(GetIntValue(exm, arguments));
        else
            return new SingleStrTerm(GetStrValue(exm, arguments));
    }

    /// <summary>
    /// 戻り値は全体をRestructureできるかどうか
    /// </summary>
    /// <param name="exm"></param>
    /// <param name="arguments"></param>
    /// <returns></returns>
    public virtual bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
    { throw new ExeEE("未実装？"); }


    internal void SetMethodName(string name)
    {
        Name = name;
    }
}

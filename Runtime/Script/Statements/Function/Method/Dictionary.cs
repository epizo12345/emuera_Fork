using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using Runtime.Dictionary;

namespace MinorShift.Emuera.GameData.Function;
internal static partial class FunctionMethodCreator
{
    // [Emuera改修:DICT-01 2026-10-28]
    // 旧実装はCheckArgumentTypeが常にnullを返し、引数の数・型・省略を読込時に検査していなかった
    // (DICT_EXISTは基底の検査まで無効化していた)。実行時に.NET例外で落ちていたものを読込時エラーにする。
    // 引数: 第1=辞書名(文字列)、第2=キー(整数または文字列)、DICT_SET_VALUEの第3=値(整数または文字列)。
    static string CheckDictArguments(string name, List<AExpression> arguments, int count)
    {
        if (arguments.Count != count)
            return $"{name}関数の引数の数が間違っています";
        for (int i = 0; i < count; i++)
        {
            if (arguments[i] == null)
                return $"{name}関数:第{i + 1}引数は省略できません";
        }
        if (!arguments[0].IsString)
            return $"{name}関数:第1引数(辞書名)は文字列でなければなりません";
        return null;
    }

    // 文字列キーならstrKeyに、整数キーならintKeyに入れる(strKeyはnull)。
    static void GetDictKey(ExpressionMediator exm, AExpression keyArg, out long intKey, out string strKey)
    {
        if (keyArg.IsString)
        {
            intKey = 0;
            strKey = keyArg.GetStrValue(exm);
        }
        else
        {
            intKey = keyArg.GetIntValue(exm);
            strKey = null;
        }
    }

    public sealed class DictCreate : FunctionMethod
    {
        public DictCreate()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            ERBDictionary.CreateDictionary(name);
            return 1;
        }
    }
    public sealed class DictExist : FunctionMethod
    {
        public DictExist()
        {
            ReturnType = typeof(long);
            argumentTypeArray = [typeof(string)];
            CanRestructure = false;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);

            return ERBDictionary.ExitsDictionary(name) ? 1 : 0;
        }
    }
    public sealed class DictContainsKey : FunctionMethod
    {
        public DictContainsKey()
        {
            ReturnType = typeof(long);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return CheckDictArguments(name, arguments, 2);
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            GetDictKey(exm, arguments[1], out long intKey, out string strKey);
            return ERBDictionary.ContainsKey(Name, name, intKey, strKey) ? 1 : 0;
        }
    }

    public sealed class DictGetValueInt : FunctionMethod
    {
        public DictGetValueInt()
        {
            ReturnType = typeof(long);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return CheckDictArguments(name, arguments, 2);
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            GetDictKey(exm, arguments[1], out long intKey, out string strKey);
            return ERBDictionary.GetLong(Name, name, intKey, strKey);
        }
    }
    public sealed class DictGetValueString : FunctionMethod
    {
        public DictGetValueString()
        {
            ReturnType = typeof(string);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return CheckDictArguments(name, arguments, 2);
        }
        public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            GetDictKey(exm, arguments[1], out long intKey, out string strKey);
            return ERBDictionary.GetString(Name, name, intKey, strKey);
        }
    }
    public sealed class DictSetValue : FunctionMethod
    {
        public DictSetValue()
        {
            ReturnType = typeof(long);
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            return CheckDictArguments(name, arguments, 3);
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            var name = arguments[0].GetStrValue(exm);
            GetDictKey(exm, arguments[1], out long intKey, out string strKey);
            object value = arguments[2].IsString ? arguments[2].GetStrValue(exm) : arguments[2].GetIntValue(exm);
            ERBDictionary.Set(Name, name, intKey, strKey, value);
            return 1;
        }
    }
}

using System;
using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;
using Dict = MinorShift.Emuera.GameData.Function.FunctionMethodCreator;

namespace Emuera.ConfigRegressionTests;

// [Emuera改修:DICT-01 2026-10-28]
// DICT_*関数を、読込時の引数検査(CheckArgumentType)と実行(GetIntValue/GetStrValue)の両方で確かめる。
// 引数は定数termなのでExpressionMediatorは使われず、nullで渡す。
internal static class DictRegression
{
    private static readonly Dict.DictCreate Create = Named(new Dict.DictCreate(), "DICT_CREATE");
    private static readonly Dict.DictExist Exist = Named(new Dict.DictExist(), "DICT_EXIST");
    private static readonly Dict.DictContainsKey Contains = Named(new Dict.DictContainsKey(), "DICT_CONTAINS_KEY");
    private static readonly Dict.DictSetValue SetValue = Named(new Dict.DictSetValue(), "DICT_SET_VALUE");
    private static readonly Dict.DictGetValueString GetStr = Named(new Dict.DictGetValueString(), "DICT_GET_VALUE_STRING");
    private static readonly Dict.DictGetValueInt GetLong = Named(new Dict.DictGetValueInt(), "DICT_GET_VALUE_LONG");

    private static T Named<T>(T method, string name) where T : FunctionMethod
    {
        method.SetMethodName(name);
        return method;
    }

    private static AExpression S(string value) => SingleStrTerm.FromValue(value);
    private static AExpression L(long value) => SingleLongTerm.FromValue(value);
    private static List<AExpression> Args(params AExpression[] args) => [.. args];

    // 旧実装では文字列キー"abc"は"abc".GetHashCode()の整数キーと同じ場所だった。同一プロセス内なら必ず衝突を再現できる。
    internal static void StringKeyDoesNotCollideWithIntKey()
    {
        const string dict = "DICT_REGRESSION_COLLISION";
        Create.GetIntValue(null!, Args(S(dict)));
        long hash = "abc".GetHashCode();
        SetValue.GetIntValue(null!, Args(S(dict), S("abc"), S("文字列キーの値")));
        SetValue.GetIntValue(null!, Args(S(dict), L(hash), L(123)));
        Expect("文字列キーの値", GetStr.GetStrValue(null!, Args(S(dict), S("abc"))), "string key keeps its own value");
        Expect(123L, GetLong.GetIntValue(null!, Args(S(dict), L(hash))), "int key equal to the old hash keeps its own value");
        // 整数1と文字列"1"も別キー。
        SetValue.GetIntValue(null!, Args(S(dict), L(1), S("hot")));
        Expect(0L, Contains.GetIntValue(null!, Args(S(dict), S("1"))), "string \"1\" is not int key 1");
        Expect(1L, Contains.GetIntValue(null!, Args(S(dict), L(1))), "int key 1 exists");
    }

    internal static void CreateReplacesWithEmptyDictionary()
    {
        const string dict = "DICT_REGRESSION_RESET";
        Expect(0L, Exist.GetIntValue(null!, Args(S(dict))), "dictionary does not exist before create");
        Create.GetIntValue(null!, Args(S(dict)));
        SetValue.GetIntValue(null!, Args(S(dict), S("key2"), L(100)));
        SetValue.GetIntValue(null!, Args(S(dict), L(1), S("hot")));
        Expect(1L, Exist.GetIntValue(null!, Args(S(dict))), "dictionary exists after create");
        Expect(100L, GetLong.GetIntValue(null!, Args(S(dict), S("key2"))), "screenshot sample key2");
        Expect("hot", GetStr.GetStrValue(null!, Args(S(dict), L(1))), "screenshot sample key 1");
        SetValue.GetIntValue(null!, Args(S(dict), S("key2"), L(-5)));
        Expect(-5L, GetLong.GetIntValue(null!, Args(S(dict), S("key2"))), "set overwrites");
        Create.GetIntValue(null!, Args(S(dict)));
        Expect(0L, Contains.GetIntValue(null!, Args(S(dict), S("key2"))), "re-create empties string keys");
        Expect(0L, Contains.GetIntValue(null!, Args(S(dict), L(1))), "re-create empties int keys");
    }

    internal static void RuntimeFailuresAreCodeErrors()
    {
        const string dict = "DICT_REGRESSION_ERRORS";
        Create.GetIntValue(null!, Args(S(dict)));
        SetValue.GetIntValue(null!, Args(S(dict), S("num"), L(7)));
        SetValue.GetIntValue(null!, Args(S(dict), S("text"), S("x")));
        ExpectCodeError(() => GetLong.GetIntValue(null!, Args(S("DICT_REGRESSION_MISSING"), L(1))), "DICT_GET_VALUE_LONG", "作成されていません");
        ExpectCodeError(() => SetValue.GetIntValue(null!, Args(S("DICT_REGRESSION_MISSING"), L(1), L(1))), "DICT_SET_VALUE", "作成されていません");
        ExpectCodeError(() => Contains.GetIntValue(null!, Args(S("DICT_REGRESSION_MISSING"), L(1))), "DICT_CONTAINS_KEY", "作成されていません");
        ExpectCodeError(() => GetStr.GetStrValue(null!, Args(S(dict), S("none"))), "DICT_GET_VALUE_STRING", "存在しません");
        ExpectCodeError(() => GetLong.GetIntValue(null!, Args(S(dict), L(99))), "DICT_GET_VALUE_LONG", "存在しません");
        ExpectCodeError(() => GetStr.GetStrValue(null!, Args(S(dict), S("num"))), "DICT_GET_VALUE_STRING", "整数です");
        ExpectCodeError(() => GetLong.GetIntValue(null!, Args(S(dict), S("text"))), "DICT_GET_VALUE_LONG", "文字列です");
    }

    internal static void LoadTimeArgumentChecks()
    {
        Expect<string?>(null, Create.CheckArgumentType("DICT_CREATE", Args(S("d"))), "create accepts one string");
        Expect<string?>(null, Exist.CheckArgumentType("DICT_EXIST", Args(S("d"))), "exist accepts one string");
        Expect<string?>(null, SetValue.CheckArgumentType("DICT_SET_VALUE", Args(S("d"), L(1), S("v"))), "set accepts int key / string value");
        Expect<string?>(null, SetValue.CheckArgumentType("DICT_SET_VALUE", Args(S("d"), S("k"), L(1))), "set accepts string key / int value");
        Expect<string?>(null, GetLong.CheckArgumentType("DICT_GET_VALUE_LONG", Args(S("d"), S("k"))), "get accepts string key");
        Expect<string?>(null, Contains.CheckArgumentType("DICT_CONTAINS_KEY", Args(S("d"), L(1))), "contains accepts int key");
        NotNull(Exist.CheckArgumentType("DICT_EXIST", Args(L(1))), "exist rejects int name");
        NotNull(Exist.CheckArgumentType("DICT_EXIST", Args()), "exist rejects no argument");
        NotNull(GetStr.CheckArgumentType("DICT_GET_VALUE_STRING", Args(S("d"))), "get rejects missing key");
        NotNull(GetStr.CheckArgumentType("DICT_GET_VALUE_STRING", Args(S("d"), null!)), "get rejects omitted key");
        NotNull(GetLong.CheckArgumentType("DICT_GET_VALUE_LONG", Args(L(1), L(1))), "get rejects int name");
        NotNull(Contains.CheckArgumentType("DICT_CONTAINS_KEY", Args(S("d"), L(1), L(2))), "contains rejects extra argument");
        NotNull(SetValue.CheckArgumentType("DICT_SET_VALUE", Args(S("d"), L(1))), "set rejects missing value");
        NotNull(SetValue.CheckArgumentType("DICT_SET_VALUE", Args(S("d"), L(1), null!)), "set rejects omitted value");
    }

    private static void ExpectCodeError(Action action, string funcName, string fragment)
    {
        try { action(); }
        catch (CodeEE e)
        {
            if (!e.Message.Contains(funcName) || !e.Message.Contains(fragment))
                throw new InvalidOperationException($"unexpected CodeEE message: {e.Message}");
            return;
        }
        throw new InvalidOperationException($"{funcName}: expected CodeEE containing '{fragment}'");
    }

    private static void Expect<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected <{expected}>, got <{actual}>");
    }

    private static void NotNull(string? actual, string message)
    {
        if (actual == null)
            throw new InvalidOperationException($"{message}: expected an argument error");
    }
}

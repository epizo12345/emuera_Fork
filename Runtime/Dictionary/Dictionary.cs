
using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Utils;

namespace Runtime.Dictionary;

// [Emuera改修:DICT-01 2026-10-28]
// 旧実装は文字列キーをGetHashCode()でlongへ変換して整数キーと同じ表に入れていたため、
// 別の文字列同士や、文字列と整数キーが衝突すると値を上書き・誤取得していた。
// 整数キーと文字列キーを別の表に持ち、文字列は文字列のまま(序数比較)で引く。"1"と1は別キー(旧実装と同じ)。
// 失敗時は.NET例外ではなく、関数名・辞書名・キーを含むCodeEEにする。
// DICT_CREATEは同名でも空の辞書で置き換える(戦闘開始時のリセットに使われている仕様)。自動リセットはせず、セーブにも含まない。
static class ERBDictionary
{
    sealed class Store
    {
        public readonly Dictionary<long, object> IntKeys = [];
        public readonly Dictionary<string, object> StrKeys = [];
    }

    static readonly Dictionary<string, Store> _dictionarys = [];

    static Store GetStore(string funcName, string dictName)
    {
        if (!_dictionarys.TryGetValue(dictName, out Store store))
            throw new CodeEE($"{funcName}関数:辞書\"{dictName}\"は作成されていません");
        return store;
    }

    static object GetValue(string funcName, string dictName, long intKey, string strKey)
    {
        Store store = GetStore(funcName, dictName);
        object value;
        bool found = strKey == null ? store.IntKeys.TryGetValue(intKey, out value) : store.StrKeys.TryGetValue(strKey, out value);
        if (!found)
            throw new CodeEE($"{funcName}関数:辞書\"{dictName}\"にキー{KeyText(intKey, strKey)}は存在しません");
        return value;
    }

    static string KeyText(long intKey, string strKey) => strKey == null ? intKey.ToString() : $"\"{strKey}\"";

    /// <summary>strKeyがnullなら整数キーintKey、そうでなければ文字列キーstrKeyを使う。</summary>
    public static long GetLong(string funcName, string dictName, long intKey, string strKey)
    {
        object value = GetValue(funcName, dictName, intKey, strKey);
        if (value is long l)
            return l;
        throw new CodeEE($"{funcName}関数:辞書\"{dictName}\"のキー{KeyText(intKey, strKey)}の値は文字列です");
    }

    public static string GetString(string funcName, string dictName, long intKey, string strKey)
    {
        object value = GetValue(funcName, dictName, intKey, strKey);
        if (value is string s)
            return s;
        throw new CodeEE($"{funcName}関数:辞書\"{dictName}\"のキー{KeyText(intKey, strKey)}の値は整数です");
    }

    /// <summary>valueはlongかstring。</summary>
    public static void Set(string funcName, string dictName, long intKey, string strKey, object value)
    {
        Store store = GetStore(funcName, dictName);
        if (strKey == null)
            store.IntKeys[intKey] = value;
        else
            store.StrKeys[strKey] = value;
    }

    public static bool ContainsKey(string funcName, string dictName, long intKey, string strKey)
    {
        Store store = GetStore(funcName, dictName);
        return strKey == null ? store.IntKeys.ContainsKey(intKey) : store.StrKeys.ContainsKey(strKey);
    }

    public static bool ExitsDictionary(string dictName)
    {
        return _dictionarys.ContainsKey(dictName);
    }

    public static void CreateDictionary(string dictName)
    {
        _dictionarys[dictName] = new Store();
    }
}

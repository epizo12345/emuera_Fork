
using System.Collections.Generic;

namespace Runtime.Dictionary;
static class ERBDictionary
{
    static Dictionary<string, Dictionary<long, object>> _dictionarys = [];

    public static T Get<T>(string dictName, long key)
    {
        return (T)_dictionarys[dictName][key];
    }

    public static void Set<T>(string dictName, long key, T value)
    {
        _dictionarys[dictName][key] = value;
    }

    public static bool ContainsKey(string dictName, long key)
    {
        return _dictionarys[dictName].ContainsKey(key);
    }

    public static bool ExitsDictionary(string dictName)
    {
        return _dictionarys.ContainsKey(dictName);
    }

    public static void CreateDictionary(string dictName)
    {
        _dictionarys[dictName] = [];
    }
}
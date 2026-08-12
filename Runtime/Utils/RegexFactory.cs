using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

//Regexをキャッシュする
static class RegexFactory
{
    static readonly Dictionary<string, Regex> _dictionary = [];

    public static Regex GetRegex(string regex)
    {
        if (_dictionary.TryGetValue(regex, out var ret))
            return ret;

        ret = new Regex(regex, RegexOptions.Compiled);
        _dictionary.Add(regex, ret);
        return ret;
    }
}

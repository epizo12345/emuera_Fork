using MinorShift.Emuera.Runtime.Script.Data;
using System;

namespace MinorShift.Emuera.Runtime.Script.Statements.Expression;


internal sealed class NullTerm : AExpression
{
    public NullTerm(long i)
        : base(typeof(long))
    {
    }

    public NullTerm(string s)
        : base(typeof(string))
    {
    }
}

/// <summary>
/// 項。一単語だけ。
/// </summary>
internal class SingleTerm : AExpression
{
    protected SingleTerm(Type type)
        : base(type)
    {

    }

    public override AExpression Restructure(ExpressionMediator exm)
    {
        return this;
    }
}

internal sealed class SingleStrTerm : SingleTerm
{
    // [Emuera改修:PERF-13R28 2026-08-22]
    // runtimeで頻出する空文字SingleStrTermの短命allocationを避けるため、空文字だけ共有する。
    // nullと非empty文字列は従来どおり個別instanceとし、任意文字列cacheによるretained memory増加を避ける。
    private static readonly SingleStrTerm EmptyTerm = new("");

    public static SingleStrTerm FromValue(string value)
        => value != null && value.Length == 0 ? EmptyTerm : new SingleStrTerm(value);

    public SingleStrTerm(string s)
        : base(typeof(string))
    {
        sValue = s;
    }
    readonly string sValue;
    public override string GetStrValue(ExpressionMediator exm)
    {
        return sValue;
    }
    public override SingleTerm GetValue(ExpressionMediator exm)
    {
        return this;
    }
    public string Str
    {
        get
        {
            //チェック済みの上での呼び出し
            //if (type != typeof(string))
            //    throw new ExeEE("項の種別が異常");
            return sValue;
        }
    }
    public override string ToString()
    {
        return sValue.ToString();
    }
}


internal sealed class SingleLongTerm : SingleTerm
{
    // [Emuera改修:PERF-13R25 2026-08-21]
    // runtimeで頻出する-1～255だけimmutable共有し、範囲外は従来どおりnewする。
    // 共有instanceをmutable化してはいけない。
    private const int CacheMin = -1;
    private const int CacheMax = 255;
    private static readonly SingleLongTerm[] SmallValueCache = CreateSmallValueCache();

    private static SingleLongTerm[] CreateSmallValueCache()
    {
        var cache = new SingleLongTerm[CacheMax - CacheMin + 1];
        for (int i = 0; i < cache.Length; i++)
            cache[i] = new SingleLongTerm(i + CacheMin);
        return cache;
    }

    public static SingleLongTerm FromValue(long value)
        => value >= CacheMin && value <= CacheMax
            ? SmallValueCache[(int)(value - CacheMin)]
            : new SingleLongTerm(value);

    public SingleLongTerm(long i)
        : base(typeof(long))
    {
        iValue = i;
    }
    readonly long iValue;

    public override long GetIntValue(ExpressionMediator exm)
    {
        return iValue;
    }
    public override SingleTerm GetValue(ExpressionMediator exm)
    {
        return this;
    }

    public long Int
    {
        get
        {
            //チェック済みの上での呼び出し
            //if (type != typeof(Int64))
            //    throw new ExeEE("項の種別が異常");
            return iValue;
        }
    }
    public override string ToString()
    {
        return iValue.ToString();
    }

    public override AExpression Restructure(ExpressionMediator exm)
    {
        return this;
    }
}


/// <summary>
/// 項。一単語だけ。
/// </summary>
internal sealed class StrFormTerm : AExpression
{
    public StrFormTerm(StrForm sf)
        : base(typeof(string))
    {
        sfValue = sf;
    }
    readonly StrForm sfValue;

    public StrForm StrForm
    {
        get
        {
            return sfValue;
        }
    }

    public override string GetStrValue(ExpressionMediator exm)
    {
        return sfValue.GetString(exm);
    }
    public override SingleTerm GetValue(ExpressionMediator exm)
    {
        return SingleStrTerm.FromValue(sfValue.GetString(exm));
    }

    public override AExpression Restructure(ExpressionMediator exm)
    {
        sfValue.Restructure(exm);
        if (sfValue.IsConst)
            return SingleStrTerm.FromValue(sfValue.GetString(exm));
        AExpression term = sfValue.GetAExpression();
        if (term != null)
            return term;
        return this;
    }
}

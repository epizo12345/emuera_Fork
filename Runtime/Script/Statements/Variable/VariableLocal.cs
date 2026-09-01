using MinorShift.Emuera.GameData.Variable;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements.Variable;

internal delegate LocalVariableToken CreateLocalVariableToken(VariableCode varCode, int size);
internal sealed class VariableLocal
{
    public VariableLocal(VariableCode varCode, int size, CreateLocalVariableToken creater)
    {
        this.size = size;
        this.varCode = varCode;
        this.creater = creater;
    }
    readonly int size;
    internal VariableCode Code => varCode;
    public bool IsForbid { get { return size == 0; } }
    VariableCode varCode;
    //VariableData varData;
    CreateLocalVariableToken creater;
    // [Emuera改修:WARN-05]
    // 並列ERB解析で同じ関数のLOCAL/ARG情報を同時に参照しても壊れない入れ物。
    // 既存トークンを再利用するため、同じ内容を何度も作って捨てる処理も減る。
    // 参照: プロジェクト資料/06_コード案内.md
    readonly ConcurrentDictionary<string, LocalVariableToken> localVarTokens = [];
    public LocalVariableToken GetExistLocalVariableToken(string subKey)
    {
        if (localVarTokens.TryGetValue(subKey, out LocalVariableToken ret))
            return ret;
        return null;
    }

    public int GetDefaultSize()
    {
        return size;
    }

    public LocalVariableToken GetNewLocalVariableToken(string subKey, FunctionLabelLine func)
    {
        int newSize = 0;
        if (varCode == VariableCode.LOCAL)
            newSize = func.LocalLength;
        else if (varCode == VariableCode.LOCALS)
            newSize = func.LocalsLength;
        else if (varCode == VariableCode.ARG)
            newSize = func.ArgLength;
        else if (varCode == VariableCode.ARGS)
            newSize = func.ArgsLength;

        // 同じ関数のローカル変数は解析中に何度も参照される。
        // 既存トークンがあれば、同一内容のトークンを生成して捨てる処理を避ける。
        if (newSize >= 0 && localVarTokens.TryGetValue(subKey, out LocalVariableToken existing))
            return existing;

        LocalVariableToken ret;
        if (newSize > 0)
        {
            if (newSize < size && (varCode == VariableCode.ARG || varCode == VariableCode.ARGS))
                newSize = size;
            ret = creater(varCode, newSize);
        }
        else if (newSize == 0)
            ret = creater(varCode, size);
        else
        {
            ret = creater(varCode, size);
            LogicalLine line = GlobalStatic.Process.GetScaningLine();
            if (line != null)
            {
                if (!func.IsSystem)
                    ParserMediator.Warn("関数宣言に引数変数\"" + varCode + "\"が使われていない関数中で\"" + varCode + "\"が使われています(関数の引数以外の用途に使うことは推奨されません。代わりに#DIMの使用を検討してください)", line, 1, false, false);
                else
                    ParserMediator.Warn("システム関数" + func.LabelName + "中で\"" + varCode + "\"が使われています(関数の引数以外の用途に使うことは推奨されません。代わりに#DIMの使用を検討してください)", line, 1, false, false);
            }
            // 警告は従来どおり参照ごとに出すが、トークン自体は再生成しない。
            if (localVarTokens.TryGetValue(subKey, out existing))
                return existing;
            //throw new CodeEE("この関数に引数変数\"" + varCode + "\"は定義されていません");
        }
        return localVarTokens.GetOrAdd(subKey, ret);
    }

    public void ResizeLocalVariableToken(string subKey, int newSize)
    {
        if (localVarTokens.TryGetValue(subKey, out LocalVariableToken ret))
        {
            if (size < newSize)
                ret.resize(newSize);
            else
                ret.resize(size);
        }
        else
        {
            if (newSize > size)
                ret = creater(varCode, newSize);
            else if (newSize == 0)
                ret = creater(varCode, size);
            else
                return;
            localVarTokens.TryAdd(subKey, ret);
        }
    }

    public void Clear()
    {
        localVarTokens.Clear();
    }

    public void SetDefault()
    {
        foreach (KeyValuePair<string, LocalVariableToken> pair in localVarTokens)
            pair.Value.SetDefault();
    }
}

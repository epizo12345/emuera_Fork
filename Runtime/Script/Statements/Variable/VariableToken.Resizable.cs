using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Framework;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements.Variable
{

// [Emuera改修:ARRAY-01 2026-10-28]
// #DIM RESIZABLE の1次元配列。設計はプロジェクト資料/調査履歴/10_RESIZABLE可変長配列_設計.md
// 「配列の全長＝今の長さ」を常に保つ（長さを変えるたびにちょうどの長さへ作り直す）ので、
// GetArray()で.Lengthを使う既存の配列関数はそのまま動く。既存の固定長トークンには手を入れず、既存変数の読み書きは変えない。
// 伸びるのは範囲外への書き込みとRESIZE・SPLITなどの出力だけ。縮むのはRESIZEだけ。
internal interface IResizableToken
{
    /// <summary>今の長さ。CHARADATA以外はcharaNoを無視する。</summary>
    int GetCurrentLength(long charaNo);
    /// <summary>長さを変える。伸びた部分は0／null（空文字）。</summary>
    void Resize(long charaNo, long newLength);
}

internal static class ResizableArray
{
    // #DIMで宣言できる要素数の上限と同じ。書き間違えた添字で巨大な配列を作らないための上限。
    public const int MaxLength = 1000000;

    /// <summary>index番目へ書き込めるように伸ばす。範囲外への書き込みでだけ呼ぶ。</summary>
    public static void GrowFor<T>(ref T[] array, long index, string varName, string argPos)
    {
        if (index < 0 || index >= MaxLength)
            throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, argPos, index.ToString()));
        Array.Resize(ref array, (int)index + 1);
    }

    public static void ResizeTo<T>(ref T[] array, long newLength, string varName)
    {
        if (newLength < 0 || newLength > MaxLength)
            throw new CodeEE("RESIZE:変数" + varName + "の長さに" + newLength.ToString() + "は指定できません(0～" + MaxLength.ToString() + ")");
        if (array.Length != newLength)
            Array.Resize(ref array, (int)newLength);
    }

    public static T[] CreateInitial<T>(int length, T[] defArray)
    {
        var array = new T[length];
        if (defArray != null)
            defArray.AsSpan().CopyTo(array);
        return array;
    }

    /// <summary>startからendまで（endは含まない）を扱う配列関数の範囲確認。空の配列も扱えるよう、位置は長さちょうどまで許す。</summary>
    public static void CheckRange(int length, long position, long index1, long index2, string funcName, long i1, long i2, string varName)
    {
        if (position < 0 || position > length)
            throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", position.ToString()));
        if (index1 < 0 || index1 > length)
            throw new CodeEE(string.Format(LocalizationManager.Error.OoRInstructionArg, funcName, i1.ToString(), index1.ToString(), varName));
        if (index2 < 0 || index2 > length)
            throw new CodeEE(string.Format(LocalizationManager.Error.OoRInstructionArg, funcName, i2.ToString(), index2.ToString(), varName));
    }
}

}

namespace MinorShift.Emuera.GameData.Variable
{
internal sealed partial class VariableData
{
    #region RESIZABLE（広域・関数内STATIC）
    private sealed class ResizableStaticInt1DVariableToken : UserDefinedVariableToken, IResizableToken
    {
        public ResizableStaticInt1DVariableToken(UserDefinedVariableData data)
            : base(VariableCode.VAR, data)
        {
            initLength = data.Lengths[0];
            IsStatic = true;
            IsForbid = false;
            defArray = data.DefaultInt;
        }
        readonly int initLength;
        readonly long[] defArray;
        long[] array;

        void IfNullInitArray()
        {
            array ??= ResizableArray.CreateInitial(initLength, defArray);
        }
        public override void SetDefault()
        {
            array = ResizableArray.CreateInitial(initLength, defArray);
        }
        public override long GetIntValue(ExpressionMediator exm, long[] arguments)
        {
            IfNullInitArray();
            return array[arguments[0]];
        }
        public override void SetValue(long value, long[] arguments)
        {
            IfNullInitArray();
            long i = arguments[0];
            if ((ulong)i >= (ulong)array.Length)
                ResizableArray.GrowFor(ref array, i, varName, "1");
            array[i] = value;
        }
        public override void SetValue(long[] values, long[] arguments)
        {
            IfNullInitArray();
            long start = arguments[0];
            long end = start + values.Length;
            if (end > array.Length)
                ResizableArray.GrowFor(ref array, start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(array.AsSpan((int)start));
        }
        public override void SetValueAll(long value, int start, int end, int charaPos)
        {
            IfNullInitArray();
            array.AsSpan()[start..end].Fill(value);
        }
        public override long PlusValue(long value, long[] arguments)
        {
            IfNullInitArray();
            long i = arguments[0];
            if ((ulong)i >= (ulong)array.Length)
                ResizableArray.GrowFor(ref array, i, varName, "1");
            array[i] += value;
            return array[i];
        }
        public override object GetArray()
        {
            IfNullInitArray();
            return array;
        }
        public override int GetLength()
        {
            IfNullInitArray();
            return array.Length;
        }
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return GetLength();
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= GetLength()))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(GetLength(), arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => GetLength();
        public void Resize(long charaNo, long newLength)
        {
            IfNullInitArray();
            ResizableArray.ResizeTo(ref array, newLength, varName);
        }
        public override void ScopeIn() { }
        public override void ScopeOut() { }
    }

    private sealed class ResizableStaticStr1DVariableToken : UserDefinedVariableToken, IResizableToken
    {
        public ResizableStaticStr1DVariableToken(UserDefinedVariableData data)
            : base(VariableCode.VARS, data)
        {
            initLength = data.Lengths[0];
            IsStatic = true;
            IsForbid = false;
            defArray = data.DefaultStr;
        }
        readonly int initLength;
        readonly string[] defArray;
        string[] array;

        void IfNullInitArray()
        {
            array ??= ResizableArray.CreateInitial(initLength, defArray);
        }
        public override void SetDefault()
        {
            array = ResizableArray.CreateInitial(initLength, defArray);
        }
        public override string GetStrValue(ExpressionMediator exm, long[] arguments)
        {
            IfNullInitArray();
            return array[arguments[0]];
        }
        public override void SetValue(string value, long[] arguments)
        {
            IfNullInitArray();
            long i = arguments[0];
            if ((ulong)i >= (ulong)array.Length)
                ResizableArray.GrowFor(ref array, i, varName, "1");
            array[i] = value;
        }
        public override void SetValue(string[] values, long[] arguments)
        {
            IfNullInitArray();
            long start = arguments[0];
            long end = start + values.Length;
            if (end > array.Length)
                ResizableArray.GrowFor(ref array, start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(array.AsSpan((int)start));
        }
        public override void SetValueAll(string value, int start, int end, int charaPos)
        {
            IfNullInitArray();
            array.AsSpan()[start..end].Fill(value);
        }
        public override object GetArray()
        {
            IfNullInitArray();
            return array;
        }
        public override int GetLength()
        {
            IfNullInitArray();
            return array.Length;
        }
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return GetLength();
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= GetLength()))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(GetLength(), arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => GetLength();
        public void Resize(long charaNo, long newLength)
        {
            IfNullInitArray();
            ResizableArray.ResizeTo(ref array, newLength, varName);
        }
        public override void ScopeIn() { }
        public override void ScopeOut() { }
    }
    #endregion

    #region RESIZABLE（関数内DYNAMIC）
    // 再帰に備えて呼び出しごとの配列をスタックに退避する（既存のPrivate*1DVariableTokenと同じ）。
    // 長さは配列ごとに持つので、呼び出し階層ごとに独立する。
    private sealed class ResizablePrivateInt1DVariableToken : UserDefinedVariableToken, IResizableToken
    {
        public ResizablePrivateInt1DVariableToken(UserDefinedVariableData data)
            : base(VariableCode.VAR, data)
        {
            initLength = data.Lengths[0];
            IsStatic = false;
            IsForbid = false;
            defArray = data.DefaultInt;
        }
        readonly int initLength;
        readonly long[] defArray;
        readonly Stack<long[]> arrayStack = [];
        long[] array;

        public override void SetDefault() { }
        public override long GetIntValue(ExpressionMediator exm, long[] arguments)
        {
            return array[arguments[0]];
        }
        public override void SetValue(long value, long[] arguments)
        {
            long i = arguments[0];
            if ((ulong)i >= (ulong)array.Length)
                ResizableArray.GrowFor(ref array, i, varName, "1");
            array[i] = value;
        }
        public override void SetValue(long[] values, long[] arguments)
        {
            long start = arguments[0];
            long end = start + values.Length;
            if (end > array.Length)
                ResizableArray.GrowFor(ref array, start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(array.AsSpan((int)start));
        }
        public override void SetValueAll(long value, int start, int end, int charaPos)
        {
            array.AsSpan()[start..end].Fill(value);
        }
        public override long PlusValue(long value, long[] arguments)
        {
            long i = arguments[0];
            if ((ulong)i >= (ulong)array.Length)
                ResizableArray.GrowFor(ref array, i, varName, "1");
            array[i] += value;
            return array[i];
        }
        public override object GetArray() { return array; }
        public override int GetLength() => array.Length;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return array.Length;
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= array.Length))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(array.Length, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => array.Length;
        public void Resize(long charaNo, long newLength)
        {
            ResizableArray.ResizeTo(ref array, newLength, varName);
        }
        public override void ScopeIn()
        {
            if (array != null)
                arrayStack.Push(array);
            array = ResizableArray.CreateInitial(initLength, defArray);
        }
        public override void ScopeOut()
        {
            array = arrayStack.Count > 0 ? arrayStack.Pop() : null;
        }
    }

    private sealed class ResizablePrivateStr1DVariableToken : UserDefinedVariableToken, IResizableToken
    {
        public ResizablePrivateStr1DVariableToken(UserDefinedVariableData data)
            : base(VariableCode.VARS, data)
        {
            initLength = data.Lengths[0];
            IsStatic = false;
            IsForbid = false;
            defArray = data.DefaultStr;
        }
        readonly int initLength;
        readonly string[] defArray;
        readonly Stack<string[]> arrayStack = [];
        string[] array;

        public override void SetDefault() { }
        public override string GetStrValue(ExpressionMediator exm, long[] arguments)
        {
            return array[arguments[0]];
        }
        public override void SetValue(string value, long[] arguments)
        {
            long i = arguments[0];
            if ((ulong)i >= (ulong)array.Length)
                ResizableArray.GrowFor(ref array, i, varName, "1");
            array[i] = value;
        }
        public override void SetValue(string[] values, long[] arguments)
        {
            long start = arguments[0];
            long end = start + values.Length;
            if (end > array.Length)
                ResizableArray.GrowFor(ref array, start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(array.AsSpan((int)start));
        }
        public override void SetValueAll(string value, int start, int end, int charaPos)
        {
            array.AsSpan()[start..end].Fill(value);
        }
        public override object GetArray() { return array; }
        public override int GetLength() => array.Length;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return array.Length;
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= array.Length))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(array.Length, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => array.Length;
        public void Resize(long charaNo, long newLength)
        {
            ResizableArray.ResizeTo(ref array, newLength, varName);
        }
        public override void ScopeIn()
        {
            if (array != null)
                arrayStack.Push(array);
            array = ResizableArray.CreateInitial(initLength, defArray);
        }
        public override void ScopeOut()
        {
            array = arrayStack.Count > 0 ? arrayStack.Pop() : null;
        }
    }
    #endregion

    #region RESIZABLE（CHARADATA）
    // キャラごとの配列（CharacterData.UserDefCVarDataList[ArrayIndex]）を差し替えて伸縮する。
    // GetLength()は従来どおり宣言時の初期サイズ（キャラを指定できないため）。キャラごとの長さはGetCurrentLengthで取る。
    private sealed class ResizableCharaInt1DVariableToken : UserDefinedCharaVariableToken, IResizableToken
    {
        public ResizableCharaInt1DVariableToken(UserDefinedVariableData data, VariableData varData, int arrayIndex)
            : base(VariableCode.CVAR, data, varData, arrayIndex)
        {
            IsForbid = false;
        }
        public override long GetIntValue(ExpressionMediator exm, long[] arguments)
        {
            long[] array = (long[])GetArrayChara((int)arguments[0]);
            return array[arguments[1]];
        }
        public override void SetValue(long value, long[] arguments)
        {
            List<object> list = varData.CharacterList[(int)arguments[0]].UserDefCVarDataList;
            long[] array = (long[])list[ArrayIndex];
            long i = arguments[1];
            if ((ulong)i >= (ulong)array.Length)
            {
                ResizableArray.GrowFor(ref array, i, varName, "2");
                list[ArrayIndex] = array;
            }
            array[i] = value;
        }
        public override void SetValue(long[] values, long[] arguments)
        {
            List<object> list = varData.CharacterList[(int)arguments[0]].UserDefCVarDataList;
            long[] array = (long[])list[ArrayIndex];
            long start = arguments[1];
            long end = start + values.Length;
            if (end > array.Length)
            {
                ResizableArray.GrowFor(ref array, start < 0 ? start : end - 1, varName, "2");
                list[ArrayIndex] = array;
            }
            values.AsSpan().CopyTo(array.AsSpan((int)start));
        }
        public override void SetValueAll(long value, int start, int end, int charaPos)
        {
            long[] array = (long[])GetArrayChara(charaPos);
            array.AsSpan()[start..end].Fill(value);
        }
        public override long PlusValue(long value, long[] arguments)
        {
            List<object> list = varData.CharacterList[(int)arguments[0]].UserDefCVarDataList;
            long[] array = (long[])list[ArrayIndex];
            long i = arguments[1];
            if ((ulong)i >= (ulong)array.Length)
            {
                ResizableArray.GrowFor(ref array, i, varName, "2");
                list[ArrayIndex] = array;
            }
            array[i] += value;
            return array[i];
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= varData.CharacterList.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, varName, "1", arguments[0].ToString()));
            if (doCheck.Length > 1 && doCheck[1] && (arguments[1] < 0 || arguments[1] >= GetCurrentLength(arguments[0])))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, varName, "2", arguments[1].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            if (arguments[0] < 0 || arguments[0] >= varData.CharacterList.Count)
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, varName, "1", arguments[0].ToString()));
            ResizableArray.CheckRange(GetCurrentLength(arguments[0]), arguments.Length > 1 ? arguments[1] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => ((long[])GetArrayChara((int)charaNo)).Length;
        public void Resize(long charaNo, long newLength)
        {
            List<object> list = varData.CharacterList[(int)charaNo].UserDefCVarDataList;
            long[] array = (long[])list[ArrayIndex];
            ResizableArray.ResizeTo(ref array, newLength, varName);
            list[ArrayIndex] = array;
        }
    }

    private sealed class ResizableCharaStr1DVariableToken : UserDefinedCharaVariableToken, IResizableToken
    {
        public ResizableCharaStr1DVariableToken(UserDefinedVariableData data, VariableData varData, int arrayIndex)
            : base(VariableCode.CVARS, data, varData, arrayIndex)
        {
            IsForbid = false;
        }
        public override string GetStrValue(ExpressionMediator exm, long[] arguments)
        {
            string[] array = (string[])GetArrayChara((int)arguments[0]);
            return array[arguments[1]];
        }
        public override void SetValue(string value, long[] arguments)
        {
            List<object> list = varData.CharacterList[(int)arguments[0]].UserDefCVarDataList;
            string[] array = (string[])list[ArrayIndex];
            long i = arguments[1];
            if ((ulong)i >= (ulong)array.Length)
            {
                ResizableArray.GrowFor(ref array, i, varName, "2");
                list[ArrayIndex] = array;
            }
            array[i] = value;
        }
        public override void SetValue(string[] values, long[] arguments)
        {
            List<object> list = varData.CharacterList[(int)arguments[0]].UserDefCVarDataList;
            string[] array = (string[])list[ArrayIndex];
            long start = arguments[1];
            long end = start + values.Length;
            if (end > array.Length)
            {
                ResizableArray.GrowFor(ref array, start < 0 ? start : end - 1, varName, "2");
                list[ArrayIndex] = array;
            }
            values.AsSpan().CopyTo(array.AsSpan((int)start));
        }
        public override void SetValueAll(string value, int start, int end, int charaPos)
        {
            string[] array = (string[])GetArrayChara(charaPos);
            array.AsSpan()[start..end].Fill(value);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= varData.CharacterList.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, varName, "1", arguments[0].ToString()));
            if (doCheck.Length > 1 && doCheck[1] && (arguments[1] < 0 || arguments[1] >= GetCurrentLength(arguments[0])))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, varName, "2", arguments[1].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            if (arguments[0] < 0 || arguments[0] >= varData.CharacterList.Count)
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, varName, "1", arguments[0].ToString()));
            ResizableArray.CheckRange(GetCurrentLength(arguments[0]), arguments.Length > 1 ? arguments[1] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => ((string[])GetArrayChara((int)charaNo)).Length;
        public void Resize(long charaNo, long newLength)
        {
            List<object> list = varData.CharacterList[(int)charaNo].UserDefCVarDataList;
            string[] array = (string[])list[ArrayIndex];
            ResizableArray.ResizeTo(ref array, newLength, varName);
            list[ArrayIndex] = array;
        }
    }
    #endregion
}
}

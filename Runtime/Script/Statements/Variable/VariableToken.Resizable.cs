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
// 中身は容量を倍々で確保するバッファ（ResizableBuffer）に持ち、今の長さは別に持つ（比較用ブランチ: ①容量確保）。
// 配列関数がGetArray()／GetArrayChara()で取り出すときだけ今の長さちょうどへ切り詰めて渡すので、
// .Lengthを使う既存の配列関数はそのまま動く。既存の固定長トークンには手を入れず、既存変数の読み書きは変えない。
// 伸びるのは範囲外への書き込みとRESIZE・SPLITなどの出力だけ。縮むのはRESIZEだけ。
internal interface IResizableToken
{
    /// <summary>今の長さ。CHARADATA以外はcharaNoを無視する。</summary>
    int GetCurrentLength(long charaNo);
    /// <summary>長さを変える。伸びた部分は0／null（空文字）。</summary>
    void Resize(long charaNo, long newLength);
    /// <summary>
    /// 容量込みの内部配列を切り詰めずに返す（長さはGetCurrentLength）。範囲[start, end)だけを読む配列関数専用。
    /// GetArray()と違って切り詰めないので、追加と探索を交互に繰り返しても配列を作り直さない。
    /// </summary>
    Array GetBackingArray(long charaNo);
    /// <summary>中身の入れ物（ResizableBuffer）。REF RESIZABLEの参照渡しで、配列ではなく入れ物ごと渡すために使う。</summary>
    IResizableBuffer GetBuffer(long charaNo);
}

/// <summary>CHARADATAのキャラごとの格納場所（CharacterData.UserDefCVarDataList）から型を問わず扱うための口。</summary>
internal interface IResizableBuffer
{
    /// <summary>今の長さちょうどの配列（必要なら切り詰めて、その配列を以後の中身にする）。</summary>
    Array AsExactArray();
    /// <summary>容量込みの内部配列（メモリ計測用。切り詰めない）。</summary>
    Array Backing { get; }
    IResizableBuffer CloneBuffer();
    /// <summary>ロード用。保存されていた長さに変えて、その長さちょうどの配列を返す（読み込み先にする）。</summary>
    Array ResizeForLoad(int length, string varName);
}

/// <summary>容量を倍々で確保し、今の長さを別に持つ配列。Items[Count..]は常に既定値（0／null）に保つ。</summary>
internal sealed class ResizableBuffer<T> : IResizableBuffer
{
    public T[] Items;
    public int Count;

    public ResizableBuffer(int length, T[] defArray)
    {
        Items = new T[length];
        Count = length;
        if (defArray != null)
            defArray.AsSpan().CopyTo(Items);
    }
    ResizableBuffer(T[] items, int count)
    {
        Items = items;
        Count = count;
    }

    /// <summary>index番目へ書き込めるように今の長さを伸ばす。範囲外への書き込みでだけ呼ぶ。</summary>
    public void GrowFor(long index, string varName, string argPos)
    {
        if (index < 0 || index >= ResizableArray.MaxLength)
            throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, argPos, index.ToString()));
        int needed = (int)index + 1;
        if (needed > Items.Length)
            Array.Resize(ref Items, ResizableArray.NextCapacity(Items.Length, needed));
        Count = needed;
    }

    public void ResizeTo(long newLength, string varName)
    {
        if (newLength < 0 || newLength > ResizableArray.MaxLength)
            throw new CodeEE("RESIZE:変数" + varName + "の長さに" + newLength.ToString() + "は指定できません(0～" + ResizableArray.MaxLength.ToString() + ")");
        int n = (int)newLength;
        if (n > Items.Length)
            Array.Resize(ref Items, n);
        else if (n < Count)
            Array.Clear(Items, n, Count - n);
        Count = n;
    }

    /// <summary>宣言時の長さと初期値に戻す。入れ物は作り直さない（REF RESIZABLEが参照中でも同じ入れ物を指し続けるため）。</summary>
    public void Reset(int length, T[] defArray)
    {
        Array.Clear(Items, 0, Count);
        Count = 0;
        ResizeTo(length, null);
        if (defArray != null)
            defArray.AsSpan().CopyTo(Items);
    }

    public T[] AsExact()
    {
        if (Items.Length != Count)
            Array.Resize(ref Items, Count);
        return Items;
    }

    Array IResizableBuffer.AsExactArray() => AsExact();
    Array IResizableBuffer.Backing => Items;
    public IResizableBuffer CloneBuffer() => new ResizableBuffer<T>(Items.AsSpan(0, Count).ToArray(), Count);
    public Array ResizeForLoad(int length, string varName)
    {
        ResizeTo(length, varName);
        return AsExact();
    }
}

internal static class ResizableArray
{
    // #DIMで宣言できる要素数の上限と同じ。書き間違えた添字で巨大な配列を作らないための上限。
    public const int MaxLength = 1000000;

    /// <summary>容量は倍々（最低4）。上限はMaxLength。</summary>
    public static int NextCapacity(int current, int needed)
    {
        long doubled = Math.Max(4L, (long)current * 2);
        return (int)Math.Min(ResizableArray.MaxLength, Math.Max(needed, doubled));
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

    /// <summary>CHARADATAの格納場所の値を配列として取り出す（RESIZABLEなら今の長さちょうどに切り詰める）。</summary>
    public static object AsArray(object stored) => stored is IResizableBuffer buffer ? buffer.AsExactArray() : stored;
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
        ResizableBuffer<long> buffer;

        ResizableBuffer<long> Buffer => buffer ??= new ResizableBuffer<long>(initLength, defArray);

        public override void SetDefault()
        {
            // [Emuera改修:ARRAY-03 2026-10-28] 入れ物を作り直さず中身を戻す（REF RESIZABLEで参照中の入れ物を古くしない）。
            if (buffer == null)
                buffer = new ResizableBuffer<long>(initLength, defArray);
            else
                buffer.Reset(initLength, defArray);
        }
        public override long GetIntValue(ExpressionMediator exm, long[] arguments)
        {
            var b = Buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        public override void SetValue(long value, long[] arguments)
        {
            var b = Buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            b.Items[i] = value;
        }
        public override void SetValue(long[] values, long[] arguments)
        {
            var b = Buffer;
            long start = arguments[0];
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        public override void SetValueAll(long value, int start, int end, int charaPos)
        {
            Buffer.Items.AsSpan()[start..end].Fill(value);
        }
        public override long PlusValue(long value, long[] arguments)
        {
            var b = Buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            b.Items[i] += value;
            return b.Items[i];
        }
        public override object GetArray() => Buffer.AsExact();
        public override int GetLength() => Buffer.Count;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return Buffer.Count;
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= Buffer.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(Buffer.Count, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => Buffer.Count;
        public void Resize(long charaNo, long newLength) => Buffer.ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => Buffer.Items;
        public IResizableBuffer GetBuffer(long charaNo) => Buffer;
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
        ResizableBuffer<string> buffer;

        ResizableBuffer<string> Buffer => buffer ??= new ResizableBuffer<string>(initLength, defArray);

        public override void SetDefault()
        {
            // [Emuera改修:ARRAY-03 2026-10-28] 入れ物を作り直さず中身を戻す（REF RESIZABLEで参照中の入れ物を古くしない）。
            if (buffer == null)
                buffer = new ResizableBuffer<string>(initLength, defArray);
            else
                buffer.Reset(initLength, defArray);
        }
        public override string GetStrValue(ExpressionMediator exm, long[] arguments)
        {
            var b = Buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        public override void SetValue(string value, long[] arguments)
        {
            var b = Buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            b.Items[i] = value;
        }
        public override void SetValue(string[] values, long[] arguments)
        {
            var b = Buffer;
            long start = arguments[0];
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        public override void SetValueAll(string value, int start, int end, int charaPos)
        {
            Buffer.Items.AsSpan()[start..end].Fill(value);
        }
        public override object GetArray() => Buffer.AsExact();
        public override int GetLength() => Buffer.Count;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return Buffer.Count;
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= Buffer.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(Buffer.Count, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => Buffer.Count;
        public void Resize(long charaNo, long newLength) => Buffer.ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => Buffer.Items;
        public IResizableBuffer GetBuffer(long charaNo) => Buffer;
        public override void ScopeIn() { }
        public override void ScopeOut() { }
    }
    #endregion

    #region RESIZABLE（関数内DYNAMIC）
    // 再帰に備えて呼び出しごとのバッファをスタックに退避する（既存のPrivate*1DVariableTokenと同じ）。
    // 長さはバッファごとに持つので、呼び出し階層ごとに独立する。
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
        readonly Stack<ResizableBuffer<long>> bufferStack = [];
        ResizableBuffer<long> buffer;

        public override void SetDefault() { }
        public override long GetIntValue(ExpressionMediator exm, long[] arguments)
        {
            var b = buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        public override void SetValue(long value, long[] arguments)
        {
            var b = buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            b.Items[i] = value;
        }
        public override void SetValue(long[] values, long[] arguments)
        {
            var b = buffer;
            long start = arguments[0];
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        public override void SetValueAll(long value, int start, int end, int charaPos)
        {
            buffer.Items.AsSpan()[start..end].Fill(value);
        }
        public override long PlusValue(long value, long[] arguments)
        {
            var b = buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            b.Items[i] += value;
            return b.Items[i];
        }
        public override object GetArray() => buffer.AsExact();
        public override int GetLength() => buffer.Count;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return buffer.Count;
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= buffer.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(buffer.Count, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => buffer.Count;
        public void Resize(long charaNo, long newLength) => buffer.ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => buffer.Items;
        public IResizableBuffer GetBuffer(long charaNo) => buffer;
        public override void ScopeIn()
        {
            if (buffer != null)
                bufferStack.Push(buffer);
            buffer = new ResizableBuffer<long>(initLength, defArray);
        }
        public override void ScopeOut()
        {
            buffer = bufferStack.Count > 0 ? bufferStack.Pop() : null;
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
        readonly Stack<ResizableBuffer<string>> bufferStack = [];
        ResizableBuffer<string> buffer;

        public override void SetDefault() { }
        public override string GetStrValue(ExpressionMediator exm, long[] arguments)
        {
            var b = buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        public override void SetValue(string value, long[] arguments)
        {
            var b = buffer;
            long i = arguments[0];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            b.Items[i] = value;
        }
        public override void SetValue(string[] values, long[] arguments)
        {
            var b = buffer;
            long start = arguments[0];
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        public override void SetValueAll(string value, int start, int end, int charaPos)
        {
            buffer.Items.AsSpan()[start..end].Fill(value);
        }
        public override object GetArray() => buffer.AsExact();
        public override int GetLength() => buffer.Count;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return buffer.Count;
            return base.GetLength(dimension);
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= buffer.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(buffer.Count, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => buffer.Count;
        public void Resize(long charaNo, long newLength) => buffer.ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => buffer.Items;
        public IResizableBuffer GetBuffer(long charaNo) => buffer;
        public override void ScopeIn()
        {
            if (buffer != null)
                bufferStack.Push(buffer);
            buffer = new ResizableBuffer<string>(initLength, defArray);
        }
        public override void ScopeOut()
        {
            buffer = bufferStack.Count > 0 ? bufferStack.Pop() : null;
        }
    }
    #endregion

    #region RESIZABLE（CHARADATA）
    // キャラごとの格納場所（CharacterData.UserDefCVarDataList[ArrayIndex]）にResizableBufferを置く。
    // 配列として取り出す箇所（GetArrayChara、COPYCHARA、SORTCHARAのキー）はResizableArray.AsArray等で今の長さに切り詰める。
    // GetLength()は従来どおり宣言時の初期サイズ（キャラを指定できないため）。キャラごとの長さはGetCurrentLengthで取る。
    private sealed class ResizableCharaInt1DVariableToken : UserDefinedCharaVariableToken, IResizableToken
    {
        public ResizableCharaInt1DVariableToken(UserDefinedVariableData data, VariableData varData, int arrayIndex)
            : base(VariableCode.CVAR, data, varData, arrayIndex)
        {
            IsForbid = false;
        }
        ResizableBuffer<long> BufferOf(long charaNo) => (ResizableBuffer<long>)varData.CharacterList[(int)charaNo].UserDefCVarDataList[ArrayIndex];

        public override object GetArrayChara(int charano) => BufferOf(charano).AsExact();
        public override long GetIntValue(ExpressionMediator exm, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long i = arguments[1];
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        public override void SetValue(long value, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long i = arguments[1];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "2");
            b.Items[i] = value;
        }
        public override void SetValue(long[] values, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long start = arguments[1];
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "2");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        public override void SetValueAll(long value, int start, int end, int charaPos)
        {
            BufferOf(charaPos).Items.AsSpan()[start..end].Fill(value);
        }
        public override long PlusValue(long value, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long i = arguments[1];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "2");
            b.Items[i] += value;
            return b.Items[i];
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
        public int GetCurrentLength(long charaNo) => BufferOf(charaNo).Count;
        public void Resize(long charaNo, long newLength) => BufferOf(charaNo).ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => BufferOf(charaNo).Items;
        public IResizableBuffer GetBuffer(long charaNo) => BufferOf(charaNo);
    }

    private sealed class ResizableCharaStr1DVariableToken : UserDefinedCharaVariableToken, IResizableToken
    {
        public ResizableCharaStr1DVariableToken(UserDefinedVariableData data, VariableData varData, int arrayIndex)
            : base(VariableCode.CVARS, data, varData, arrayIndex)
        {
            IsForbid = false;
        }
        ResizableBuffer<string> BufferOf(long charaNo) => (ResizableBuffer<string>)varData.CharacterList[(int)charaNo].UserDefCVarDataList[ArrayIndex];

        public override object GetArrayChara(int charano) => BufferOf(charano).AsExact();
        public override string GetStrValue(ExpressionMediator exm, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long i = arguments[1];
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        public override void SetValue(string value, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long i = arguments[1];
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "2");
            b.Items[i] = value;
        }
        public override void SetValue(string[] values, long[] arguments)
        {
            var b = BufferOf(arguments[0]);
            long start = arguments[1];
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "2");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        public override void SetValueAll(string value, int start, int end, int charaPos)
        {
            BufferOf(charaPos).Items.AsSpan()[start..end].Fill(value);
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
        public int GetCurrentLength(long charaNo) => BufferOf(charaNo).Count;
        public void Resize(long charaNo, long newLength) => BufferOf(charaNo).ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => BufferOf(charaNo).Items;
        public IResizableBuffer GetBuffer(long charaNo) => BufferOf(charaNo);
    }
    #endregion

    #region REF RESIZABLE（関数の参照渡しの引数）
    // [Emuera改修:ARRAY-03 2026-10-28]
    // 普通のREFは配列そのものを持つため、RESIZABLEが伸びて配列を作り直すと古い配列を見続ける。
    // REF RESIZABLEは配列ではなく入れ物（ResizableBuffer）を持つので、関数の中で伸び縮みしても呼び出し元と同じ中身を見る。
    // RESIZABLE変数だけを受け取る（MatchTypeで確認）。再帰に備えて、受け取った入れ物を呼び出し階層ごとに退避する。
    private abstract class ResizableRef1DToken<T> : ReferenceToken, IResizableToken
    {
        protected ResizableRef1DToken(VariableCode varCode, UserDefinedVariableData data)
            : base(varCode, data)
        {
        }
        readonly Stack<ResizableBuffer<T>> bufferStack = [];
        ResizableBuffer<T> buffer;

        protected ResizableBuffer<T> B => buffer ?? throw new CodeEE(string.Format(LocalizationManager.Error.EmptyRefVar, varName));

        public override void SetRef(object target)
        {
            buffer = (ResizableBuffer<T>)target;
        }
        public override void ScopeIn()
        {
            bufferStack.Push(buffer);
            buffer = null;
        }
        public override void ScopeOut()
        {
            buffer = bufferStack.Count > 0 ? bufferStack.Pop() : null;
        }
        public override object GetArray() => B.AsExact();
        public override int GetLength() => B.Count;
        public override int GetLength(int dimension)
        {
            if (dimension == 0)
                return B.Count;
            throw new CodeEE(string.Format(LocalizationManager.Error.GetSizeNonExistDim, varName));
        }
        public override void CheckElement(long[] arguments, bool[] doCheck)
        {
            var b = B;
            if (doCheck[0] && (arguments[0] < 0 || arguments[0] >= b.Count))
                throw new CodeEE(string.Format(LocalizationManager.Error.OoRVarArg, varName, "1", arguments[0].ToString()));
        }
        public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2)
        {
            ResizableArray.CheckRange(B.Count, arguments.Length > 0 ? arguments[0] : 0, index1, index2, funcName, i1, i2, varName);
        }
        public int GetCurrentLength(long charaNo) => B.Count;
        public void Resize(long charaNo, long newLength) => B.ResizeTo(newLength, varName);
        public Array GetBackingArray(long charaNo) => B.Items;
        public IResizableBuffer GetBuffer(long charaNo) => B;

        protected T Get(long i)
        {
            var b = B;
            if ((ulong)i >= (ulong)b.Count)
                throw new IndexOutOfRangeException();
            return b.Items[i];
        }
        protected ResizableBuffer<T> ForWrite(long i)
        {
            var b = B;
            if ((ulong)i >= (ulong)b.Count)
                b.GrowFor(i, varName, "1");
            return b;
        }
        protected void SetRange(T[] values, long start)
        {
            var b = B;
            long end = start + values.Length;
            if (start < 0 || end > b.Count)
                b.GrowFor(start < 0 ? start : end - 1, varName, "1");
            values.AsSpan().CopyTo(b.Items.AsSpan((int)start));
        }
        protected void Fill(T value, int start, int end) => B.Items.AsSpan()[start..end].Fill(value);
    }

    private sealed class ResizableRefInt1DToken : ResizableRef1DToken<long>
    {
        public ResizableRefInt1DToken(UserDefinedVariableData data)
            : base(VariableCode.REF, data)
        {
        }
        public override long GetIntValue(ExpressionMediator exm, long[] arguments) => Get(arguments[0]);
        public override void SetValue(long value, long[] arguments) => ForWrite(arguments[0]).Items[arguments[0]] = value;
        public override void SetValue(long[] values, long[] arguments) => SetRange(values, arguments[0]);
        public override void SetValueAll(long value, int start, int end, int charaPos) => Fill(value, start, end);
        public override long PlusValue(long value, long[] arguments)
        {
            var b = ForWrite(arguments[0]);
            b.Items[arguments[0]] += value;
            return b.Items[arguments[0]];
        }
    }

    private sealed class ResizableRefStr1DToken : ResizableRef1DToken<string>
    {
        public ResizableRefStr1DToken(UserDefinedVariableData data)
            : base(VariableCode.REFS, data)
        {
        }
        public override string GetStrValue(ExpressionMediator exm, long[] arguments) => Get(arguments[0]);
        public override void SetValue(string value, long[] arguments) => ForWrite(arguments[0]).Items[arguments[0]] = value;
        public override void SetValue(string[] values, long[] arguments) => SetRange(values, arguments[0]);
        public override void SetValueAll(string value, int start, int end, int charaPos) => Fill(value, start, end);
    }
    #endregion
}
}

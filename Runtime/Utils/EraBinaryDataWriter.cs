using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MinorShift.Emuera.Runtime.Utils;

//reader/writer共通のデータはreaderの方に


/// <summary>
/// 1808追加 新しいデータ保存形式
/// Reader と違ってWriterは最新の書き込み方式だけ知っていればよい
/// WriteHeader -> WriteFileType -> ... -> WriteEFO
/// </summary>
internal sealed class EraBinaryDataWriter : IDisposable
{
    // [Emuera改修:MEASURE-03]
    // FileStream限定からStream全般へ広げ、通常のファイル保存に加えてMemoryStreamへも
    // 同じ形式を書けるようにした。マクロ比較用の状態ハッシュ作成に利用する。
    // 既存セーブの書式や書き込み内容は変わらない。
    // 参照: プロジェクト資料/06_コード案内.md
    public EraBinaryDataWriter(Stream fs)
    {
        writer = new BinaryWriter(fs, Encoding.Unicode);
    }
    BinaryWriter writer;

    public void WriteHeader()
    {
        writer.Write(EraBDConst.Header);
        writer.Write(EraBDConst.Version1808);
        writer.Write(EraBDConst.DataCount);
        for (int i = 0; i < EraBDConst.DataCount; i++)
        {
            writer.Write((uint)0);
        }
    }

    public void WriteFileType(EraSaveFileType type)
    {
        writer.Write((byte)type);
    }


    /// <summary>
    /// システム用。keyなしでInt64を保存
    /// </summary>
    /// <param name="v"></param>
    public void WriteInt64(long v)
    {
        //圧縮しない
        writer.Write(v);
    }
    /// <summary>
    /// システム用。keyなしでstringを保存
    /// </summary>
    /// <param name="s"></param>
    public void WriteString(string s)
    {
        writer.Write(s);
    }


    public void WriteSeparator()
    {
        writer.Write((byte)EraSaveDataType.Separator);
    }
    public void WriteEOC()
    {
        writer.Write((byte)EraSaveDataType.EOC);
    }
    public void WriteEOF()
    {
        writer.Write((byte)EraSaveDataType.EOF);
    }

    public void WriteWithKey(string key, object v)
    {
        if (v is long)
        {
            writer.Write((byte)EraSaveDataType.Int);
            writer.Write(key);
            writeData((long)v);
        }
        else if (v is long[])
        {
            writer.Write((byte)EraSaveDataType.IntArray);
            writer.Write(key);
            writeData((long[])v);
        }
        else if (v is long[,])
        {
            writer.Write((byte)EraSaveDataType.IntArray2D);
            writer.Write(key);
            writeData((long[,])v);
        }
        else if (v is long[,,])
        {
            writer.Write((byte)EraSaveDataType.IntArray3D);
            writer.Write(key);
            writeData((long[,,])v);
        }
        else if (v is string)
        {
            writer.Write((byte)EraSaveDataType.Str);
            writer.Write(key);
            writeData((string)v);
        }
        else if (v is string[])
        {
            writer.Write((byte)EraSaveDataType.StrArray);
            writer.Write(key);
            writeData((string[])v);
        }
        else if (v is string[,])
        {
            writer.Write((byte)EraSaveDataType.StrArray2D);
            writer.Write(key);
            writeData((string[,])v);
        }
        else if (v is string[,,])
        {
            writer.Write((byte)EraSaveDataType.StrArray3D);
            writer.Write(key);
            writeData((string[,,])v);
        }
    }

    public void WriteZeroIntArray2DWithKey(string key, int length0, int length1)
    {
        // [Emuera改修:MEM-C1W]
        // null CDFLAGをmaterializeせず、従来のall-zero 2Dと同じbyte列で書く限定経路。
        writer.Write((byte)EraSaveDataType.IntArray2D);
        writer.Write(key);
        writer.Write(length0);
        writer.Write(length1);
        writer.Write(Ebdb.EoD);
    }

    #region private

    private void m_WriteInt(long v)
    {
        //セーブデータ容量の爆発を避けるためにできるだけWrite(Int64)はしない
        if (v >= 0 && v <= Ebdb.Byte)//0～207まではそのままbyteに詰め込む
            writer.Write((byte)v);
        else if (v >= short.MinValue && v <= short.MaxValue)//整数の範囲に応じて適当に
        {
            writer.Write(Ebdb.Int16);
            writer.Write((short)v);
        }
        else if (v >= int.MinValue && v <= int.MaxValue)
        {
            writer.Write(Ebdb.Int32);
            writer.Write((int)v);
        }
        else
        {
            writer.Write(Ebdb.Int64);
            writer.Write(v);
        }
    }

    private void writeData(long v)
    {
        m_WriteInt(v);
    }

    private void writeData(long[] array)
    {
        // [Emuera改修:SAVE-01]
        // 1次元・2次元整数配列の連続する0をReadOnlySpan<long>とIndexOfAnyExcept(0L)でまとめて探索する。
        // セーブ形式とZero/ZeroA1/EoA1/EoDの出力規則を維持し、byte単位の互換性試験を実施済み。
        //配列の記憶。0が連続する場合には圧縮を試みる。
        writer.Write(array.Length);
        ReadOnlySpan<long> remaining = array;
        while (!remaining.IsEmpty)
        {
            int nextNonZero = remaining.IndexOfAnyExcept(0L);
            if (nextNonZero < 0)
                break;

            if (nextNonZero > 0)
            {
                writer.Write(Ebdb.Zero);
                m_WriteInt(nextNonZero);
            }

            m_WriteInt(remaining[nextNonZero]);
            remaining = remaining[(nextNonZero + 1)..];
        }
        //記憶途中で配列の残りが全部0であるなら0の数も記憶せず配列の終わりを記憶
        writer.Write(Ebdb.EoD);
    }

    private void writeData(long[,] array)
    {
        // [Emuera改修:SAVE-01]
        // 行ごとに0の連続範囲をまとめて探索し、既存のZero/ZeroA1/EoA1/EoDの出力規則を維持する。
        int countAllZero = 0;//列の要素が全て0である列の連続する数を記憶する。列の要素に一つでも非0があるなら通常の記憶方式。
        int length0 = array.GetLength(0);
        int length1 = array.GetLength(1);
        writer.Write(length0);
        writer.Write(length1);

        // An empty row has no first element. The old writer emits no row markers for this shape.
        if (length1 == 0)
        {
            writer.Write(Ebdb.EoD);
            return;
        }

        for (int x = 0; x < length0; x++)
        {
            // ECMA-335 I.8.9.1 requires the rightmost array dimension to be contiguous.
            // x is a valid row, [x, 0] is its checked first element, and length1 is exactly
            // the row length. Keep this read-only span local; its length is not inferred.
            ReadOnlySpan<long> row = MemoryMarshal.CreateReadOnlySpan(ref array[x, 0], length1);
            int nextNonZero = row.IndexOfAnyExcept(0L);
            if (nextNonZero < 0)
            {
                countAllZero++;
                continue;
            }

            if (countAllZero > 0)
            {
                writer.Write(Ebdb.ZeroA1);
                m_WriteInt(countAllZero);
                countAllZero = 0;
            }
            if (nextNonZero > 0)
            {
                writer.Write(Ebdb.Zero);
                m_WriteInt(nextNonZero);
            }
            m_WriteInt(row[nextNonZero]);

            int index = nextNonZero + 1;
            while (index < length1)
            {
                int zeroRun = row[index..].IndexOfAnyExcept(0L);
                if (zeroRun < 0)
                    break;
                if (zeroRun > 0)
                {
                    writer.Write(Ebdb.Zero);
                    m_WriteInt(zeroRun);
                }
                index += zeroRun;
                m_WriteInt(row[index]);
                index++;
            }
            writer.Write(Ebdb.EoA1);//非0があるなら列終端記号を記憶
        }
        writer.Write(Ebdb.EoD);
    }

    private void writeData(long[,,] array)
    {
        // [Emuera改修:SAVE-02]
        // 3次元整数配列にも同じ探索を適用し、最右次元だけを連続した行として走査する。
        // セーブ形式とZero/ZeroA1/ZeroA2/EoA1/EoA2/EoDの意味は変更しない。最右次元が0なら先頭要素を参照しない。
        // byte単位の互換性試験を実施済み。
        int countAllZero = 0;//plane内で全要素が0であるrowの連続数
        int countAllZero2D = 0;//全要素が0であるplaneの連続数
        int length0 = array.GetLength(0);
        int length1 = array.GetLength(1);
        int length2 = array.GetLength(2);
        writer.Write(length0);
        writer.Write(length1);
        writer.Write(length2);

        // An empty row has no first element. No values or markers can be emitted for this shape.
        if (length2 == 0)
        {
            writer.Write(Ebdb.EoD);
            return;
        }

        for (int x = 0; x < length0; x++)
        {
            for (int y = 0; y < length1; y++)
            {
                // ECMA-335 I.8.9.1 requires the rightmost array dimension to be contiguous.
                // [x, y, 0] is a checked first element and length2 is exactly the row length.
                ReadOnlySpan<long> row = MemoryMarshal.CreateReadOnlySpan(ref array[x, y, 0], length2);
                int nextNonZero = row.IndexOfAnyExcept(0L);
                if (nextNonZero < 0)
                {
                    countAllZero++;
                    continue;
                }

                if (countAllZero2D > 0)
                {
                    writer.Write(Ebdb.ZeroA2);
                    m_WriteInt(countAllZero2D);
                    countAllZero2D = 0;
                }
                if (countAllZero > 0)
                {
                    writer.Write(Ebdb.ZeroA1);
                    m_WriteInt(countAllZero);
                    countAllZero = 0;
                }
                if (nextNonZero > 0)
                {
                    writer.Write(Ebdb.Zero);
                    m_WriteInt(nextNonZero);
                }
                m_WriteInt(row[nextNonZero]);

                int index = nextNonZero + 1;
                while (index < length2)
                {
                    int zeroRun = row[index..].IndexOfAnyExcept(0L);
                    if (zeroRun < 0)
                        break;
                    if (zeroRun > 0)
                    {
                        writer.Write(Ebdb.Zero);
                        m_WriteInt(zeroRun);
                    }
                    index += zeroRun;
                    m_WriteInt(row[index]);
                    index++;
                }
                writer.Write(Ebdb.EoA1);
            }
            if (countAllZero == length1)
                countAllZero2D++;
            else
                writer.Write(Ebdb.EoA2);
            countAllZero = 0;
        }
        writer.Write(Ebdb.EoD);
    }

    private void writeData(string v)
    {
        if (v != null)
            writer.Write(v);
        else
            writer.Write("");
    }

    private void writeData(string[] array)
    {
        int countZero = 0;
        writer.Write(array.Length);
        for (int x = 0; x < array.Length; x++)
        {
            if (array[x] == null || array[x].Length == 0)
                countZero++;
            else
            {
                if (countZero > 0)
                {
                    writer.Write(Ebdb.Zero);
                    m_WriteInt(countZero);
                    countZero = 0;
                }
                writer.Write(Ebdb.String);
                writer.Write(array[x]);
            }
        }
        writer.Write(Ebdb.EoD);
    }

    private void writeData(string[,] array)
    {
        int countZero = 0;
        int countAllZero = 0;
        int length0 = array.GetLength(0);
        int length1 = array.GetLength(1);
        writer.Write(length0);
        writer.Write(length1);
        for (int x = 0; x < length0; x++)
        {
            for (int y = 0; y < length1; y++)
            {
                if (array[x, y] == null || array[x, y].Length == 0)
                    countZero++;
                else
                {
                    if (countAllZero > 0)
                    {
                        writer.Write(Ebdb.ZeroA1);
                        m_WriteInt(countAllZero);
                        countAllZero = 0;
                    }
                    if (countZero > 0)
                    {
                        writer.Write(Ebdb.Zero);
                        m_WriteInt(countZero);
                        countZero = 0;
                    }
                    writer.Write(Ebdb.String);
                    writer.Write(array[x, y]);
                }
            }
            if (countZero == length1)
                countAllZero++;
            else
                writer.Write(Ebdb.EoA1);
            countZero = 0;
        }
        writer.Write(Ebdb.EoD);
    }

    private void writeData(string[,,] array)
    {
        int countZero = 0;
        int countAllZero = 0;
        int countAllZero2D = 0;
        int length0 = array.GetLength(0);
        int length1 = array.GetLength(1);
        int length2 = array.GetLength(2);
        writer.Write(length0);
        writer.Write(length1);
        writer.Write(length2);
        for (int x = 0; x < length0; x++)
        {
            for (int y = 0; y < length1; y++)
            {
                for (int z = 0; z < length2; z++)
                {
                    if (array[x, y, z] == null || array[x, y, z].Length == 0)
                        countZero++;
                    else
                    {
                        if (countAllZero2D > 0)
                        {
                            writer.Write(Ebdb.ZeroA2);
                            m_WriteInt(countAllZero2D);
                            countAllZero2D = 0;
                        }
                        if (countAllZero > 0)
                        {
                            writer.Write(Ebdb.ZeroA1);
                            m_WriteInt(countAllZero);
                            countAllZero = 0;
                        }
                        if (countZero > 0)
                        {
                            writer.Write(Ebdb.Zero);
                            m_WriteInt(countZero);
                            countZero = 0;
                        }
                        writer.Write(Ebdb.String);
                        writer.Write(array[x, y, z]);
                    }
                }
                if (countZero == length2)
                    countAllZero++;
                else
                    writer.Write(Ebdb.EoA1);
                countZero = 0;
            }
            if (countAllZero == length1)
                countAllZero2D++;
            else
                writer.Write(Ebdb.EoA2);
            countAllZero = 0;
        }
        writer.Write(Ebdb.EoD);
    }
    #endregion
    #region IDisposable メンバ

    public void Dispose()
    {
        if (writer != null)
            writer.Close();
        writer = null;
    }

    #endregion
    public void Close()
    {
        Dispose();
    }

}

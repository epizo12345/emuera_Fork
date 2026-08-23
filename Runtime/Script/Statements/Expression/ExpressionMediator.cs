using Microsoft.VisualBasic;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Text;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Script.Statements.Expression;

//1756 元ExpressionEvaluator。GetValueの仕事はなくなったので改名。
//AExpression間での通信や共通の処理に使う。
//変数が絡む仕事はVariableEvaluatorへ。
internal sealed class ExpressionMediator
{
    public ExpressionMediator(Process proc, VariableEvaluator vev, EmueraConsole console)
    {
        VEvaluator = vev;
        Process = proc;
        Console = console;
    }
    public readonly VariableEvaluator VEvaluator;
    public readonly Process Process;
    public readonly EmueraConsole Console;



    private bool forceHiragana;
    private bool forceKatakana;
    private bool halftoFull;

    public void ForceKana(long flag)
    {
        if (flag < 0 || flag > 3)
            throw new CodeEE(LocalizationManager.Error.OoRForcekanaArg);
        forceKatakana = flag == 1;
        forceHiragana = flag > 1;
        halftoFull = flag == 3;
    }

    public bool ForceKana()
    {
        return forceHiragana | forceKatakana | halftoFull;
    }

    public void OutputToConsole(string str, FunctionIdentifier func, bool lineEnd)
    {
        if (func.IsPrintSingle())
            Console.PrintSingleLine(str, false);
        else
        {
            Console.Print(str, lineEnd);
            if (func.IsNewLine() || func.IsWaitInput())
            {
                Console.NewLine();
                if (func.IsWaitInput())
                    Console.ReadAnyKey();
            }
        }
        Console.UseSetColorStyle = true;
    }

    public string ConvertStringType(string str)
    {
        if (!(forceHiragana | forceKatakana | halftoFull))
            return str;
        if (forceKatakana)
            return Strings.StrConv(str, VbStrConv.Katakana, 0x0411);
        else if (forceHiragana)
        {
            if (halftoFull)
                return Strings.StrConv(str, VbStrConv.Hiragana | VbStrConv.Wide, 0x0411);
            else
                return Strings.StrConv(str, VbStrConv.Hiragana, 0x0411);
        }
        return str;
    }

    public static string CheckEscape(string str)
    {
        CharStream st = new(str);
        StringBuilder buffer = new();

        while (!st.EOS)
        {
            //エスケープ文字の使用
            if (st.Current == '\\')
            {
                st.ShiftNext();
                switch (st.Current)
                {
                    case '\\':
                        buffer.Append('\\');
                        buffer.Append('\\');
                        break;
                    case '{':
                    case '}':
                    case '%':
                    case '@':
                        buffer.Append('\\');
                        buffer.Append(st.Current);
                        break;
                    default:
                        buffer.Append("\\\\");
                        buffer.Append(st.Current);
                        break;
                }
                st.ShiftNext();
                continue;
            }
            buffer.Append(st.Current);
            st.ShiftNext();
        }
        return buffer.ToString();
    }

    public static long CalculatePower(long x, long y)
    {
        // Phase 10Aの互換性修正: POWERはdouble経由の丸めやlong.MinValue境界を避け、整数結果をcheckedで計算する。
        // overflow時だけMath.Powを診断に使い、非有限値/64-bit範囲外の既存error semanticsを維持する。
        if (y < 0)
        {
            if (x == 0)
                throw new CodeEE(LocalizationManager.Error.PowerResultInfinite);
            if (x == 1)
                return 1;
            if (x == -1)
                return (y & 1) == 0 ? 1 : -1;
            return 0;
        }

        long result = 1;
        long factor = x;
        long exponent = y;
        try
        {
            while (exponent != 0)
            {
                if ((exponent & 1) != 0)
                    result = checked(result * factor);
                exponent >>= 1;
                if (exponent != 0)
                    factor = checked(factor * factor);
            }
        }
        catch (OverflowException)
        {
            double pow = Math.Pow(x, y);
            if (double.IsInfinity(pow))
                throw new CodeEE(LocalizationManager.Error.PowerResultInfinite);
            throw new CodeEE("累乗結果(" + pow.ToString() + ")が64ビット符号付き整数の範囲外です");
        }
        return result;
    }

    public static string CreateBar(long var, long max, long length)
    {
        if (max <= 0)
            throw new CodeEE(LocalizationManager.Error.MaxBarNotPositive);
        if (length <= 0)
            throw new CodeEE(LocalizationManager.Error.BarNotPositive);
        if (length >= 100)//暴走を防ぐため。
            throw new CodeEE(LocalizationManager.Error.TooLongBar);
        StringBuilder builder = new();
        builder.Append('[');
        int count;
        unchecked
        {
            count = (int)(var * length / max);
        }
        if (count < 0)
            count = 0;
        if (count > length)
            count = (int)length;
        builder.Append(Config.Config.BarChar1, count);
        builder.Append(Config.Config.BarChar2, (int)length - count);
        builder.Append(']');
        return builder.ToString();
    }
}


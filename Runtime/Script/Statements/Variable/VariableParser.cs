using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Script.Statements.Variable;

internal static class VariableParser
{
    public static void Initialize()
    {
        ZeroTerm = new SingleLongTerm(0);
        AExpression[] zeroArgs = [ZeroTerm];
        TARGET = new VariableTerm(GlobalStatic.VariableData.GetSystemVariableToken("TARGET"), zeroArgs);
    }

    public static SingleTerm ZeroTerm { get; private set; }
    public static VariableTerm TARGET { get; private set; }

    public static bool IsVariable(string ids)
    {
        if (string.IsNullOrEmpty(ids))
            return false;
        string[] idlist = ids.Split(':');
        //idlist = synonym.ApplySynonym(idlist);
        VariableToken id = GlobalStatic.IdentifierDictionary.GetVariableToken(idlist[0], null, false);
        return id != null;
    }

    ///// <summary>
    ///// まだ最初の識別子を読んでいない状態から決め打ちで変数を解読する
    ///// </summary>
    ///// <param name="st"></param>
    ///// <returns></returns>
    //public static VariableTerm ReduceVariable(WordCollection wc)
    //{
    //    IdentifierWord id = wc.Current as IdentifierWord;
    //    if (id == null)
    //        return null;
    //    wc.ShiftNext();
    //    VariableToken vid = ExpressionParser.ReduceVariableIdentifier(wc, id.Code);
    //    if (vid == null)
    //        throw new CodeEE("\"" + id.Code + "\"は解釈できない識別子です");
    //    return ReduceVariable(vid, wc);
    //}

    /// <summary>
    /// 識別子を読み終えた状態からの解析
    /// </summary>
    /// <param name="st"></param>
    /// <returns></returns>
    public static VariableTerm ReduceVariable(VariableToken id, WordCollection wc)
    {
        AExpression operand;
        AExpression op1 = null;
        AExpression op2 = null;
        AExpression op3 = null;
        int i = 0;
        while (true)
        {
            if (wc.Current.Type != ':')
                break;
            if (i >= 3)
                throw new CodeEE(id.Code.ToString() + "の引数が多すぎます");
            wc.ShiftNext();

            operand = ExpressionParser.ReduceVariableArgument(wc, id.Code);
            if (i == 0)
                op1 = operand;
            else if (i == 1)
                op2 = operand;
            else if (i == 2)
                op3 = operand;
            i++;
        }
        return ReduceVariable(id, op1, op2, op3);

    }



    public static VariableTerm ReduceVariable(VariableToken id, AExpression p1, AExpression p2, AExpression p3)
    {
        AExpression[] terms;
        AExpression op1 = p1;
        AExpression op2 = p2;
        AExpression op3 = p3;
        //引数の推測
        if (id.IsCharacterData)
        {
            if (id.IsArray2D)
            {
                if (op1 == null && op2 == null && op3 == null)
                    return new VariableNoArgTerm(id);
                if (op1 == null || op2 == null || op3 == null)
                    throw new CodeEE(string.Format(LocalizationManager.Error.CanNotOmit1DCharaVarArg1, id.Name));
                terms = [op1, op2, op3];
            }
            else if (id.IsArray1D)
            {
                if (op3 != null)
                    throw new CodeEE(string.Format(LocalizationManager.Error.TooMany1DCharaVarArg, id.Name));
                if (op1 == null && op2 == null && op3 == null && Config.Config.SystemNoTarget)
                    return new VariableNoArgTerm(id);
                if (op2 == null)
                {
                    if (Config.Config.SystemNoTarget)
                        throw new CodeEE(string.Format(LocalizationManager.Error.CanNotOmit1DCharaVarArg2, id.Name));
                    if (op1 == null)
                        op2 = ZeroTerm;
                    else
                        op2 = op1;
                    op1 = TARGET;
                }
                terms = [op1, op2];
            }
            else
            {
                if (op2 != null)
                    throw new CodeEE(string.Format(LocalizationManager.Error.TooManyCharaVarArg, id.Name));
                if (op1 == null && op2 == null && op3 == null && Config.Config.SystemNoTarget)
                    return new VariableNoArgTerm(id);
                if (op1 == null)
                {
                    if (Config.Config.SystemNoTarget)
                        throw new CodeEE(string.Format(LocalizationManager.Error.CanNotOmitCharaVarArg2, id.Name));
                    op1 = TARGET;
                }
                terms = [op1];
            }
        }
        else if (id.IsArray3D)
        {
            if (op1 == null && op2 == null && op3 == null)
                return new VariableNoArgTerm(id);
            if (op1 == null || op2 == null || op3 == null)
                throw new CodeEE(string.Format(LocalizationManager.Error.CanNotOmit3DVarArg, id.Name));
            terms = [op1, op2, op3];
        }
        else if (id.IsArray2D)
        {
            if (op1 == null && op2 == null && op3 == null)
                return new VariableNoArgTerm(id);
            if (op1 == null || op2 == null)
                throw new CodeEE(string.Format(LocalizationManager.Error.CanNotOmit2DVarArg, id.Name));
            if (op3 != null)
                throw new CodeEE(string.Format(LocalizationManager.Error.TooMany2DVarArg, id.Name));
            terms = [op1, op2];
        }
        else if (id.IsArray1D)
        {
            if (op2 != null)
                throw new CodeEE(string.Format(LocalizationManager.Error.TooMany1DVarArg, id.Name));
            if (op1 == null)
            {
                op1 = ZeroTerm;
                if (!Config.Config.CompatiRAND && id.Code == VariableCode.RAND)
                {
                    throw new CodeEE(LocalizationManager.Error.OmittedRandArg);
                }
            }
            if (!Config.Config.CompatiRAND && op1 is SingleTerm op1SingleTerm && id.Code == VariableCode.RAND)
            {
                if (((SingleLongTerm)op1SingleTerm).Int == 0)
                    throw new CodeEE(LocalizationManager.Error.RandArgIsZero);
            }
            terms = [op1];
        }
        else if (op1 != null)
        {
            throw new CodeEE(string.Format(LocalizationManager.Error.ZeroDVarHasArg, id.Name));
        }
        else
            terms = [];

        for (int i = 0; i < terms.Length; i++)
            if (terms[i].IsString)
                terms[i] = new VariableStrArgTerm(id.Code, terms[i], i);

        return new VariableTerm(id, terms);
    }
    //public static string ErrorMes = null;
    //public static void ResetError()
    //{
    //    ErrorMes = null;
    //}

}

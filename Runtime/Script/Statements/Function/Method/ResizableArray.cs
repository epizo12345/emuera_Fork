using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.GameData.Function;
internal static partial class FunctionMethodCreator
{
    // [Emuera改修:ARRAY-01 2026-10-28]
    // RESIZABLE配列の長さ変更と長さ取得。設計はプロジェクト資料/調査履歴/10_RESIZABLE可変長配列_設計.md
    // 引数の評価順は既存の配列関数と同じ（他の引数 → 変数の添字）。CHARADATAは変数の第1添字をキャラ番号として使う。

    /// <summary>RESIZE(変数, 新しい長さ)。伸ばすのも縮めるのも可。新しい長さを返す。</summary>
    public sealed class ResizeArrayMethod : FunctionMethod
    {
        public ResizeArrayMethod()
        {
            ReturnType = typeof(long);
            argumentTypeArray = null;
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            if (arguments.Count != 2)
                return name + "関数の引数の数が間違っています";
            if (arguments[0] is not VariableTerm varTerm)
                return name + "関数の1番目の引数が変数ではありません";
            if (varTerm.Identifier is not IResizableToken)
                return name + "関数の1番目の引数がRESIZABLE変数ではありません";
            if (arguments[1] == null)
                return name + "関数の2番目の引数は省略できません";
            if (arguments[1].GetOperandType() != typeof(long))
                return name + "関数の2番目の引数の型が正しくありません";
            return null;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            long newLength = arguments[1].GetIntValue(exm);
            VariableTerm varTerm = (VariableTerm)arguments[0];
            using VariableTerm.FixedVariableTermLease lease = varTerm.RentFixedVariableTerm(exm);
            FixedVariableTerm p = lease.Term;
            var resizable = (IResizableToken)p.Identifier;
            if (p.Identifier.IsCharacterData)
                p.Identifier.CheckElement([p.Index1, 0], [true, false]);
            resizable.Resize(p.Index1, newLength);
            return newLength;
        }
    }

    /// <summary>ARRAYSIZE(変数)。1次元配列の今の長さ。RESIZABLE以外は宣言時のサイズ。</summary>
    public sealed class ArraySizeMethod : FunctionMethod
    {
        public ArraySizeMethod()
        {
            ReturnType = typeof(long);
            argumentTypeArray = null;
            CanRestructure = false;
        }
        public override string CheckArgumentType(string name, List<AExpression> arguments)
        {
            if (arguments.Count != 1)
                return name + "関数の引数の数が間違っています";
            if (arguments[0] is not VariableTerm varTerm)
                return name + "関数の1番目の引数が変数ではありません";
            if (!varTerm.Identifier.IsArray1D)
                return name + "関数の1番目の引数が1次元配列変数ではありません";
            return null;
        }
        public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
        {
            VariableTerm varTerm = (VariableTerm)arguments[0];
            if (varTerm.Identifier is not IResizableToken resizable)
                return varTerm.GetLength();
            using VariableTerm.FixedVariableTermLease lease = varTerm.RentFixedVariableTerm(exm);
            FixedVariableTerm p = lease.Term;
            if (p.Identifier.IsCharacterData)
                p.Identifier.CheckElement([p.Index1, 0], [true, false]);
            return resizable.GetCurrentLength(p.Index1);
        }
    }
}

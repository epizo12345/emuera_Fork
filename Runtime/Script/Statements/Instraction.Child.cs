using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using MinorShift.Emuera.UI.Framework;
using System.Threading;

namespace MinorShift.Emuera.GameProc.Function;

internal sealed partial class FunctionIdentifier
{


    private sealed class VARI_Instruction : AInstruction
    {

        public VARI_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            //ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            var arg = (IntAsignArgument)func.Argument;
            var varName = arg.ConstStr;

            var privateVar = func.ParentLabelLine.GetPrivateVariable(varName);
            if (privateVar.GetLength(0) == 1)
            {
                privateVar.SetValue(arg.Exp.GetIntValue(exm), [0]);
            }
            else
            {

            }
        }

        public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
        {
            var statementsSpan = line.PopArgumentPrimitive().SubstringROS();
            var commentIndex = statementsSpan.IndexOf(';');
            if (commentIndex != -1)
            {
                statementsSpan = statementsSpan[..commentIndex];
            }

            var equalsIndex = statementsSpan.IndexOf('=');
            string left;
            ReadOnlySpan<char> right = "";
            if (equalsIndex == -1)
            {
                left = statementsSpan.ToString();
            }
            else
            {
                left = statementsSpan[..equalsIndex].ToString();
                right = statementsSpan[(equalsIndex + 1)..];
            }

            var leftSplit = left.Split(',');
            var varName = leftSplit[0].Trim();
            List<int> lengths = [1];


            AExpression exp = null;
            if (leftSplit.Length > 1)
            {
                //配列である
                lengths.Clear();
                for (int i = 1; i < leftSplit.Length; i++)
                {
                    lengths.Add(int.Parse(leftSplit[i].Trim()));
                }
            }
            else
            {
                //初期値がある
                if (!right.IsWhiteSpace())
                {
                    var wc = LexicalAnalyzer.Analyse(new CharStream(right.ToString()), LexEndWith.EoL, LexAnalyzeFlag.None);
                    exp = ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL);
                }
            }

            IntAsignArgument argment;
            if (exp != null)
            {
                argment = new IntAsignArgument(varName, [.. lengths], exp);
            }
            else
            {
                argment = new IntAsignArgument(varName, [.. lengths], new SingleLongTerm(default));
            }
            return argment;
        }
    }
    private sealed class VARS_Instruction : AInstruction
    {
        public VARS_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            //ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            var arg = (StrAsignArgument)func.Argument;
            var varName = arg.ConstStr;

            var privateVar = func.ParentLabelLine.GetPrivateVariable(varName);
            if (privateVar.GetLength(0) == 1)
            {
                privateVar.SetValue(arg.Value, [0]);
            }
            else
            {

            }
        }

        public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
        {
            var statementsSpan = line.PopArgumentPrimitive().SubstringROS();
            var commentIndex = statementsSpan.IndexOf(';');
            if (commentIndex != -1)
            {
                statementsSpan = statementsSpan[..commentIndex];
            }

            var equalsIndex = statementsSpan.IndexOf('=');
            string left;
            ReadOnlySpan<char> right = "";
            if (equalsIndex == -1)
            {
                left = statementsSpan.ToString();
            }
            else
            {
                left = statementsSpan[..equalsIndex].ToString();
                right = statementsSpan[(equalsIndex + 1)..];
            }

            var leftSplit = left.Split(',');
            var varName = leftSplit[0].Trim();
            List<int> lengths = [1];

            string value = default;
            if (leftSplit.Length > 1)
            {
                //配列である
                lengths.Clear();
                for (int i = 1; i < leftSplit.Length; i++)
                {
                    lengths.Add(int.Parse(leftSplit[i].Trim()));
                }
            }
            else
            {
                //初期値がある
                if (!right.IsWhiteSpace())
                {
                    var literalStart = right.IndexOf('\"');
                    var literalEnd = right.LastIndexOf('\"');
                    value = right[(literalStart + 1)..literalEnd].ToString();
                }
            }

            var varData = new UserDefinedVariableData
            {
                Name = varName,
                Static = false,
                Lengths = [.. lengths],
                Dimension = lengths.Count,
                TypeIsStr = true
            };

            return new StrAsignArgument(varName, varData.Lengths, value);
        }
    }

    #region normalFunction
    private sealed class PRINT_Instruction : AInstruction
    {
        bool isLineEnd = true;
        public PRINT_Instruction(string name)
        {
            //PRINT(|V|S|FORM|FORMS)(|K)(|D)(|L|W) コレと
            //PRINTSINGLE(|V|S|FORM|FORMS)(|K)(|D) コレと
            //PRINT(|FORM)(C|LC)(|K)(|D) コレ
            //PRINTDATA(|K)(|D)(|L|W) ←は別クラス
            flag = IS_PRINT;
            CharStream st = new(name);
            st.Jump(5);//PRINT
            if (st.CurrentEqualTo("SINGLE"))
            {
                flag |= PRINT_SINGLE | EXTENDED;
                st.Jump(6);
            }

            if (st.CurrentEqualTo("V"))
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_PRINTV);
                isPrintV = true;
                st.Jump(1);
            }
            else if (st.CurrentEqualTo("S"))
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
                st.Jump(1);
            }
            else if (st.CurrentEqualTo("FORMS"))
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
                isForms = true;
                st.Jump(5);
            }
            else if (st.CurrentEqualTo("FORM"))
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
                st.Jump(4);
            }
            else
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_NULLABLE);
            }
            if (st.CurrentEqualTo("LC"))
            {
                flag |= EXTENDED;
                isLC = true;
                st.Jump(2);
            }
            else if (st.CurrentEqualTo("C"))
            {
                if (name == "PRINTFORMC")
                    flag |= EXTENDED;
                isC = true;
                st.Jump(1);
            }
            if (st.CurrentEqualTo("K"))
            {
                flag |= ISPRINTKFUNC | EXTENDED;
                st.Jump(1);
            }
            if (st.CurrentEqualTo("D"))
            {
                flag |= ISPRINTDFUNC | EXTENDED;
                st.Jump(1);
            }
            if (st.CurrentEqualTo("N"))
            {
                isLineEnd = false;
                flag |= PRINT_WAITINPUT;
                st.Jump(1);
            }
            if (st.CurrentEqualTo("L"))
            {
                flag |= PRINT_NEWLINE;
                flag |= METHOD_SAFE;
                st.Jump(1);
            }
            else if (st.CurrentEqualTo("W"))
            {
                flag |= PRINT_NEWLINE | PRINT_WAITINPUT;
                st.Jump(1);
            }
            else
            {
                flag |= METHOD_SAFE;
            }
            if ((ArgBuilder == null) || (!st.EOS))
                throw new ExeEE(LocalizationManager.Error.AbnormalPrint);
        }

        readonly bool isPrintV;
        readonly bool isLC;
        readonly bool isC;
        readonly bool isForms;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            exm.Console.UseUserStyle = true;
            exm.Console.UseSetColorStyle = !func.Function.IsPrintDFunction();
            string str;
            if (func.Argument.IsConst)
                str = func.Argument.ConstStr;
            else if (isPrintV)
            {
                StringBuilder builder = new();
                var terms = ((SpPrintVArgument)func.Argument).Terms;
                foreach (AExpression termV in terms)
                {
                    if (termV.GetOperandType() == typeof(Int64))
                        builder.Append(termV.GetIntValue(exm));
                    else
                        builder.Append(termV.GetStrValue(exm));
                }
                str = builder.ToString();
            }
            else
            {
                str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
                if (isForms)
                {
                    str = ExpressionMediator.CheckEscape(str);
                    StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new CharStream(str), FormStrEndWith.EoL, false);
                    StrForm strForm = StrForm.FromWordToken(wt);
                    str = strForm.GetString(exm);
                }
            }
            if (func.Function.IsPrintKFunction())
                str = exm.ConvertStringType(str);
            if (isC)
                exm.Console.PrintC(str, true);
            else if (isLC)
                exm.Console.PrintC(str, false);
            else
                exm.OutputToConsole(str, func.Function, isLineEnd);
            exm.Console.UseSetColorStyle = true;
        }
    }

    private sealed class PRINT_DATA_Instruction : AInstruction
    {
        public PRINT_DATA_Instruction(string name)
        {
            //PRINTDATA(|K)(|D)(|L|W)
            flag = EXTENDED | IS_PRINT | IS_PRINTDATA | PARTIAL;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VAR_INT);
            CharStream st = new(name);
            st.Jump(9);//PRINTDATA
            if (st.CurrentEqualTo("K"))
            {
                flag |= ISPRINTKFUNC | EXTENDED;
                st.Jump(1);
            }
            if (st.CurrentEqualTo("D"))
            {
                flag |= ISPRINTDFUNC | EXTENDED;
                st.Jump(1);
            }
            if (st.CurrentEqualTo("L"))
            {
                flag |= PRINT_NEWLINE;
                flag |= METHOD_SAFE;
                st.Jump(1);
            }
            else if (st.CurrentEqualTo("W"))
            {
                flag |= PRINT_NEWLINE | PRINT_WAITINPUT;
                st.Jump(1);
            }
            else
            {
                flag |= METHOD_SAFE;
            }
            if ((ArgBuilder == null) || (!st.EOS))
                throw new ExeEE(LocalizationManager.Error.AbnormalPrintdata);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            exm.Console.UseUserStyle = true;
            exm.Console.UseSetColorStyle = !func.Function.IsPrintDFunction();
            //表示データが空なら何もしないで飛ぶ
            if (func.dataList.Count == 0)
            {
                state.JumpTo(func.JumpTo);
                return;
            }
            int count = func.dataList.Count;
            int choice = (int)exm.VEvaluator.GetNextRand(count);
            VariableTerm iTerm = ((PrintDataArgument)func.Argument).Var;
            if (iTerm != null)
            {
                iTerm.SetValue(choice, exm);
            }
            List<InstructionLine> iList = func.dataList[choice];
            int i = 0;
            AExpression term;
            string str;
            foreach (InstructionLine selectedLine in iList)
            {
                state.CurrentLine = selectedLine;
                if (selectedLine.Argument == null)
                    ArgumentParser.SetArgumentTo(selectedLine);
                term = ((ExpressionArgument)selectedLine.Argument).Term;
                str = term.GetStrValue(exm);
                if (func.Function.IsPrintKFunction())
                    str = exm.ConvertStringType(str);
                exm.Console.Print(str);
                if (++i < (int)iList.Count)
                    exm.Console.NewLine();
            }
            if (func.Function.IsNewLine() || func.Function.IsWaitInput())
            {
                exm.Console.NewLine();
                if (func.Function.IsWaitInput())
                    exm.Console.ReadAnyKey();
            }
            exm.Console.UseSetColorStyle = true;
            //ジャンプするが、流れが連続であることを保証。
            state.JumpTo(func.JumpTo);
            //state.RunningLine = null;
        }
    }

    private sealed class HTML_PRINT_Instruction : AInstruction
    {
        public HTML_PRINT_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_HTML_PRINT);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            string str;
            if (func.Argument.IsConst)
                str = func.Argument.ConstStr;
            else
                str = ((HTML_PRINTArgument)func.Argument).Term.GetStrValue(exm);
            exm.Console.PrintHtml(str, ((HTML_PRINTArgument)func.Argument).LineEnd.GetIntValue(exm) == 0);
        }
    }

    private sealed class HTML_TAGSPLIT_Instruction : AInstruction
    {
        public HTML_TAGSPLIT_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_HTMLSPLIT);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpHtmlSplitArgument spSplitArg = (SpHtmlSplitArgument)func.Argument;
            string str = spSplitArg.TargetStr.GetStrValue(exm);
            string[] strs = HtmlManager.HtmlTagSplit(str);

            if (strs == null)
            {
                spSplitArg.Num.SetValue(-1, exm);
                return;
            }

            spSplitArg.Num.SetValue(strs.Length, exm);
            string[] output = (string[])spSplitArg.Var.GetArray();
            int outputlength = Math.Min(output.Length, strs.Length);
            Array.Copy(strs, output, outputlength);
        }
    }

    private sealed class HTML_PRINT_ISLAND_Instruction : AInstruction
    {
        public HTML_PRINT_ISLAND_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_HTML_PRINT_ISLAND);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            string str;
            var depth = 0;
            var args = (HTML_PRINT_ISLANDArgument)func.Argument;
            if (func.Argument.IsConst)
            {
                str = args.ConstStr;
            }
            else
            {
                str = args.Term.GetStrValue(exm);
                depth = (int)args.Layer.GetIntValue(exm);
            }
            exm.Console.PrintHTMLIsland(str, depth);
        }
    }

    private sealed class HTML_PRINT_ISLAND_CLEAR_Instruction : AInstruction
    {
        public HTML_PRINT_ISLAND_CLEAR_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_HTML_PRINT_ISLAND_CLEAR);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            var args = (HTML_PRINT_ISLAND_CLEARArgument)func.Argument;
            if (args.TargetLayer is NullTerm)
            {
                exm.Console.ClearHTMLIsland();
            }
            else
            {
                exm.Console.ClearHTMLIsland((int)args.TargetLayer.GetIntValue(exm));
            }
        }
    }


    private sealed class PRINT_IMG_Instruction : AInstruction
    {
        public PRINT_IMG_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            string str;
            if (func.Argument.IsConst)
                str = func.Argument.ConstStr;
            else
                str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
            exm.Console.PrintImg(str);
        }
    }

    private sealed class PRINT_RECT_Instruction : AInstruction
    {
        public PRINT_RECT_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            ExpressionArrayArgument intExpArg = (ExpressionArrayArgument)func.Argument;
            int[] param = new int[intExpArg.TermList.Length];
            for (int i = 0; i < intExpArg.TermList.Length; i++)
                param[i] = FunctionIdentifier.toUInt32inArg(intExpArg.TermList[i].GetIntValue(exm), "PRINT_RECT", i + 1);

            exm.Console.PrintShape("rect", param);
        }
    }

    private sealed class PRINT_SPACE_Instruction : AInstruction
    {
        public PRINT_SPACE_Instruction()
        {
            flag = EXTENDED | METHOD_SAFE;
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            Int64 param;
            if (func.Argument.IsConst)
                param = func.Argument.ConstInt;
            else
                param = ((ExpressionArgument)func.Argument).Term.GetIntValue(exm);
            int param32 = FunctionIdentifier.toUInt32inArg(param, "PRINT_SPACE", 1);
            exm.Console.PrintShape("space", [param32]);
        }
    }

    private sealed class CUSTOMDRAWLINE_Instruction : AInstruction
    {
        public CUSTOMDRAWLINE_Instruction()
        {
            ArgBuilder = null;
            flag = METHOD_SAFE | EXTENDED;
        }

        public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
        {
            CharStream st = line.PopArgumentPrimitive();
            string rowStr;
            if (st.EOS)
                throw new CodeEE(LocalizationManager.Error.MissingArg);
            else
                rowStr = st.Substring();
            rowStr = GlobalStatic.Console.getStBar(rowStr);
            Argument ret = new ExpressionArgument(new SingleStrTerm(rowStr))
            {
                ConstStr = rowStr,
                IsConst = true
            };
            return ret;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (GlobalStatic.Process.SkipPrint)
                return;
            GlobalStatic.Console.printCustomBar(func.Argument.ConstStr, true);
            exm.Console.NewLine();
        }
    }

    private sealed class DEBUGPRINT_Instruction : AInstruction
    {
        public DEBUGPRINT_Instruction(bool form, bool newline)
        {
            if (form)
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
            else
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_NULLABLE);
            flag = METHOD_SAFE | EXTENDED | DEBUG_FUNC;
            if (newline)
                flag |= PRINT_NEWLINE;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            string str;
            if (func.Argument.IsConst)
                str = func.Argument.ConstStr;
            else
                str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
            exm.Console.DebugPrint(str);
            if (func.Function.IsNewLine())
                exm.Console.DebugNewLine();
        }
    }

    private sealed class DEBUGCLEAR_Instruction : AInstruction
    {
        public DEBUGCLEAR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED | DEBUG_FUNC;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.DebugClear();
        }
    }

    private sealed class METHOD_Instruction : AInstruction
    {
        public METHOD_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.METHOD);
            flag = METHOD_SAFE | EXTENDED;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            AExpression term = ((MethodArgument)func.Argument).MethodTerm;
            //Type type = term.GetOperandType();
            if (term.GetOperandType() == typeof(Int64))
                exm.VEvaluator.RESULT = term.GetIntValue(exm);
            else// if (func.Argument.MethodTerm.GetOperandType() == typeof(string))
                exm.VEvaluator.RESULTS = term.GetStrValue(exm);
            //これら以外の型は現状ない
            //else
            //	throw new ExeEE(func.Function.Name + "命令の型が不明");
        }
    }

    /// <summary>
    /// 代入文
    /// </summary>
    private sealed class SET_Instruction : AInstruction
    {
        public SET_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SET);
            flag = METHOD_SAFE;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (func.Argument is SpSetArrayArgument arg)
            {
                if (arg.VariableDest.IsInteger)
                {
                    if (arg.IsConst)
                        arg.VariableDest.SetValue(arg.ConstIntList, exm);
                    else
                    {
                        Int64[] values = new Int64[arg.TermList.Count];
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = arg.TermList[i].GetIntValue(exm);
                        }
                        arg.VariableDest.SetValue(values, exm);
                    }
                }
                else
                {
                    if (arg.IsConst)
                        arg.VariableDest.SetValue(arg.ConstStrList, exm);
                    else
                    {
                        string[] values = new string[arg.TermList.Count];
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = arg.TermList[i].GetStrValue(exm);
                        }
                        arg.VariableDest.SetValue(values, exm);
                    }
                }
                return;
            }
            SpSetArgument spsetarg = (SpSetArgument)func.Argument;
            if (spsetarg.VariableDest.IsInteger)
            {
                Int64 src = spsetarg.IsConst ? spsetarg.ConstInt : spsetarg.Term.GetIntValue(exm);
#if R0_F2
                GlobalStatic.Process?.R0F2BeforeLegacyScalarWrite(func, src);
#endif
#if R0_F3
                GlobalStatic.Process?.R0F3BeforeLegacyScalarWrite(func, src);
#endif
#if R0_F4B
                GlobalStatic.Process?.R0F4BBeforeLegacyScalarWrite(func, src);
#endif
#if R0_F4D5
                GlobalStatic.Process?.R0F4D5BeforeLegacyScalarWrite(func, src);
#endif
                if (spsetarg.AddConst)
                    spsetarg.VariableDest.ChangeValue(src, exm);
                else
                    spsetarg.VariableDest.SetValue(src, exm);
#if R0_F2
                GlobalStatic.Process?.R0F2AfterLegacyScalarWrite(func);
#endif
#if R0_F3
                GlobalStatic.Process?.R0F3AfterLegacyScalarWrite(func);
#endif
#if R0_F4B
                GlobalStatic.Process?.R0F4BAfterLegacyScalarWrite(func);
#endif
#if R0_F4D5
                GlobalStatic.Process?.R0F4D5AfterLegacyScalarWrite(func);
#endif
            }
            else
            {
                string src = spsetarg.IsConst ? spsetarg.ConstStr : spsetarg.Term.GetStrValue(exm);
#if R0_F4A
                GlobalStatic.Process?.R0F4ABeforeLegacyStringWrite(func, src);
#endif
#if R0_F4C
                GlobalStatic.Process?.R0F4CBeforeLegacyStringWrite(func, src);
#endif
#if R0_F4E1
                GlobalStatic.Process?.R0F4E1BeforeLegacyStringWrite(func, src);
#endif
#if R0_F4E2
                GlobalStatic.Process?.R0F4E2BeforeLegacyStringWrite(func, src);
#endif
                var traceExtraTitle = spsetarg.VariableDest.Identifier.Name.Equals("EXTRA_TITLE", StringComparison.OrdinalIgnoreCase)
                    && spsetarg.VariableDest.isAllConst && spsetarg.VariableDest.Identifier.IsArray1D;
                var traceIndex = traceExtraTitle ? spsetarg.VariableDest.getEl1forArg : -1;
                var traceOld = traceExtraTitle ? spsetarg.VariableDest.Identifier.GetStrValue(exm, [traceIndex]) ?? string.Empty : string.Empty;
                spsetarg.VariableDest.SetValue(src, exm);
#if R0_F4A
                GlobalStatic.Process?.R0F4AAfterLegacyStringWrite(func);
#endif
#if R0_F4C
                GlobalStatic.Process?.R0F4CAfterLegacyStringWrite(func);
#endif
#if R0_F4E1
                GlobalStatic.Process?.R0F4E1AfterLegacyStringWrite(func);
#endif
#if R0_F4E2
                GlobalStatic.Process?.R0F4E2AfterLegacyStringWrite(func);
#endif
                if (traceExtraTitle)
                    GlobalStatic.Process.TraceR1_4G2ExtraTitleWrite(func, spsetarg.VariableDest, traceIndex, traceOld,
                        spsetarg.VariableDest.Identifier.GetStrValue(exm, [traceIndex]) ?? string.Empty, "SET");
            }
        }
    }

    private sealed class REUSELASTLINE_Instruction : AInstruction
    {
        public REUSELASTLINE_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
            flag = METHOD_SAFE | EXTENDED | IS_PRINT;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            AExpression term = ((ExpressionArgument)func.Argument).Term;
            string str = term.GetStrValue(exm);
            exm.Console.PrintTemporaryLine(str);
        }
    }

    private sealed class CLEARLINE_Instruction : AInstruction
    {
        public CLEARLINE_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED | IS_PRINT;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument intExpArg = (ExpressionArgument)func.Argument;
            Int32 delNum = (Int32)intExpArg.Term.GetIntValue(exm);
            exm.Console.deleteLine(delNum);
            exm.Console.RefreshStrings(false);
        }
    }

    private sealed class STRLEN_Instruction : AInstruction
    {
        public STRLEN_Instruction(bool argisform, bool unicode)
        {
            if (argisform)
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
            else
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_NULLABLE);
            flag = METHOD_SAFE | EXTENDED;
            this.unicode = unicode;
        }

        bool unicode;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            string str;
            if (func.Argument.IsConst)
                str = func.Argument.ConstStr;
            else
                str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
            if (unicode)
                exm.VEvaluator.RESULT = str.Length;
            else
                exm.VEvaluator.RESULT = LangManager.GetStrlenLang(str);
        }
    }

    private sealed class SETBIT_Instruction : AInstruction
    {
        public SETBIT_Instruction(int op)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.BIT_ARG);
            flag = METHOD_SAFE | EXTENDED;
            this.op = op;
        }

        int op;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            BitArgument spsetarg = (BitArgument)func.Argument;
            VariableTerm varTerm = spsetarg.VariableDest;
            AExpression[] terms = spsetarg.Term;
            for (int i = 0; i < terms.Length; i++)
            {
                Int64 x = terms[i].GetIntValue(exm);
                if ((x < 0) || (x > 63))
                    throw new CodeEE("第2引数がビットのレンジ(0から63)を超えています");
                Int64 baseValue = varTerm.GetIntValue(exm);
                Int64 shift = 1L << (int)x;
                if (op == 1)
                    baseValue |= shift;
                else if (op == 0)
                    baseValue &= ~shift;
                else
                    baseValue ^= shift;
                varTerm.SetValue(baseValue, exm);
            }
        }
    }

    private sealed class WAIT_Instruction : AInstruction
    {
        public WAIT_Instruction(bool force)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = IS_PRINT;
            isForce = force;
        }

        bool isForce;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (isForce)
                exm.Console.ReadAnyKey(false, true);
            else
                exm.Console.ReadAnyKey();
        }
    }

    private sealed class WAITANYKEY_Instruction : AInstruction
    {
        public WAITANYKEY_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = IS_PRINT;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.ReadAnyKey(true, false);
        }
    }

    private sealed class TWAIT_Instruction : AInstruction
    {
        public TWAIT_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
            flag = IS_PRINT | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.ReadAnyKey();
            SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
            Int64 time = arg.X.GetIntValue(exm);
            Int64 flag = arg.Y.GetIntValue(exm);
            InputRequest req = new()
            {
                InputType = InputType.EnterKey
            };
            if (flag != 0)
                req.InputType = InputType.Void;
            req.Timelimit = time;
            exm.Console.WaitInput(req);
        }
    }

    private sealed class INPUT_Instruction : AInstruction
    {
        public INPUT_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
            flag = IS_PRINT | IS_INPUT;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            InputRequest req = new()
            {
                InputType = InputType.IntValue
            };
            if (arg.Term != null)
            {
                Int64 def;
                if (arg.IsConst)
                    def = arg.ConstInt;
                else
                    def = arg.Term.GetIntValue(exm);
                req.HasDefValue = true;
                req.DefIntValue = def;
            }
            exm.Console.WaitInput(req);
        }
    }
    private sealed class INPUTS_Instruction : AInstruction
    {
        public INPUTS_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUTS);
            flag = IS_PRINT | IS_INPUT;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            InputRequest req = new()
            {
                InputType = InputType.StrValue
            };
            if (arg.Term != null)
            {
                string def;
                if (arg.IsConst)
                    def = arg.ConstStr;
                else
                    def = arg.Term.GetStrValue(exm);
                req.HasDefValue = true;
                req.DefStrValue = def;
            }
            exm.Console.WaitInput(req);
        }
    }

    private sealed class ONEINPUT_Instruction : AInstruction
    {
        public ONEINPUT_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
            flag = IS_PRINT | IS_INPUT | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            InputRequest req = new()
            {
                InputType = InputType.IntValue,
                OneInput = true
            };
            if (arg.Term != null)
            {
                //TODO:二文字以上セットできるようにするかエラー停止するか
                //少なくともONETINPUTとの仕様を統一すべき
                Int64 def;
                if (arg.IsConst)
                    def = arg.ConstInt;
                else
                    def = arg.Term.GetIntValue(exm);
                if (def > 9)
                    def = Int64.Parse(def.ToString().Remove(1));
                if (def >= 0)
                {
                    req.HasDefValue = true;
                    req.DefIntValue = def;
                }
            }
            exm.Console.WaitInput(req);
        }
    }

    private sealed class ONEINPUTS_Instruction : AInstruction
    {
        public ONEINPUTS_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUTS);
            flag = IS_PRINT | IS_INPUT | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            InputRequest req = new()
            {
                InputType = InputType.StrValue,
                OneInput = true
            };
            if (arg.Term != null)
            {
                string def;
                if (arg.IsConst)
                    def = arg.ConstStr;
                else
                    def = arg.Term.GetStrValue(exm);
                if (def.Length > 1)
                    def = def.Remove(1);
                if (def.Length > 0)
                {
                    req.HasDefValue = true;
                    req.DefStrValue = def;
                }
            }
            exm.Console.WaitInput(req);
        }
    }

    private sealed class TINPUT_Instruction : AInstruction
    {
        public TINPUT_Instruction(bool oneInput)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TINPUT);
            flag = IS_PRINT | IS_INPUT | EXTENDED;
            this.isOne = oneInput;
        }

        bool isOne;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpTInputsArgument tinputarg = (SpTInputsArgument)func.Argument;

            InputRequest req = new()
            {
                InputType = InputType.IntValue,
                HasDefValue = true,
                OneInput = isOne
            };
            Int64 x = tinputarg.Time.GetIntValue(exm);
            Int64 y = tinputarg.Def.GetIntValue(exm);
            //TODO:ONEINPUTと標準の値を統一
            if (isOne)
            {
                if (y < 0)
                    y = Math.Abs(y);
                if (y >= 10)
                    y = y / (long)Math.Pow(10.0, Math.Log10((double)y));
            }
            Int64 z = (tinputarg.Disp != null) ? tinputarg.Disp.GetIntValue(exm) : 1;
            req.Timelimit = x;
            req.DefIntValue = y;
            req.DisplayTime = z != 0;
            req.TimeUpMes = (tinputarg.Timeout != null) ? tinputarg.Timeout.GetStrValue(exm) : Config.TimeupLabel;
            exm.Console.WaitInput(req);
        }
    }

    private sealed class TINPUTS_Instruction : AInstruction
    {
        public TINPUTS_Instruction(bool oneInput)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TINPUTS);
            flag = IS_PRINT | IS_INPUT | EXTENDED;
            this.isOne = oneInput;
        }

        bool isOne;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpTInputsArgument tinputarg = (SpTInputsArgument)func.Argument;
            InputRequest req = new()
            {
                InputType = InputType.StrValue,
                HasDefValue = true,
                OneInput = isOne
            };
            Int64 x = tinputarg.Time.GetIntValue(exm);
            string strs = tinputarg.Def.GetStrValue(exm);
            if (isOne && strs.Length > 1)
                strs = strs.Remove(1);
            Int64 z = (tinputarg.Disp != null) ? tinputarg.Disp.GetIntValue(exm) : 1;
            req.Timelimit = x;
            req.DefStrValue = strs;
            req.DisplayTime = z != 0;
            req.TimeUpMes = (tinputarg.Timeout != null) ? tinputarg.Timeout.GetStrValue(exm) : Config.TimeupLabel;
            exm.Console.WaitInput(req);
        }
    }

    private sealed class CALLF_Instruction : AInstruction
    {
        public CALLF_Instruction(bool form)
        {
            if (form)
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLFORMF);
            else
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLF);
            flag = EXTENDED | METHOD_SAFE | FORCE_SETARG;
        }

        public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
        {
            if (!func.Argument.IsConst)
            {
                useCallForm = true;
                return;
            }
            SpCallFArgment callfArg = (SpCallFArgment)func.Argument;
            try
            {
                callfArg.FuncTerm = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, callfArg.ConstStr, callfArg.RowArgs, true);
            }
            catch (CodeEE e)
            {
                ParserMediator.Warn(e.Message, func, 2, true, false);
                return;
            }
            if (callfArg.FuncTerm == null)
            {
                if (!Program.AnalysisMode)
                    ParserMediator.Warn("指定された関数名\"@" + callfArg.ConstStr + "\"は存在しません", func, 2, true, false);
                else
                    ParserMediator.Warn(callfArg.ConstStr, func, 2, true, false);
                return;
            }
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            AExpression mToken;
            string labelName;
            if ((!func.Argument.IsConst) || exm.Console.RunERBFromMemory)
            {
                SpCallFArgment spCallformArg = (SpCallFArgment)func.Argument;
                labelName = spCallformArg.FuncnameTerm.GetStrValue(exm);
                mToken = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, labelName, spCallformArg.RowArgs, true);
            }
            else
            {
                labelName = func.Argument.ConstStr;
                mToken = ((SpCallFArgment)func.Argument).FuncTerm;
            }
            if (mToken == null)
                throw new CodeEE("式中関数\"@" + labelName + "\"が見つかりません");
            mToken.GetValue(exm);
        }
    }

    private sealed class BAR_Instruction : AInstruction
    {
        public BAR_Instruction(bool newline)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_BAR);
            flag = IS_PRINT | METHOD_SAFE | EXTENDED;
            this.newline = newline;
        }

        bool newline;

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpBarArgument barArg = (SpBarArgument)func.Argument;
            Int64 var = barArg.Terms[0].GetIntValue(exm);
            Int64 max = barArg.Terms[1].GetIntValue(exm);
            Int64 length = barArg.Terms[2].GetIntValue(exm);
            exm.Console.Print(ExpressionMediator.CreateBar(var, max, length));
            if (newline)
                exm.Console.NewLine();
        }
    }

    private sealed class TIMES_Instruction : AInstruction
    {
        public TIMES_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TIMES);
            flag = METHOD_SAFE;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpTimesArgument timesArg = (SpTimesArgument)func.Argument;
            VariableTerm var = timesArg.VariableDest;
            if (Config.TimesNotRigorousCalculation)
            {
                double d = (double)var.GetIntValue(exm) * timesArg.DoubleValue;
                unchecked
                {
                    var.SetValue((Int64)d, exm);
                }
            }
            else
            {
                decimal d = var.GetIntValue(exm) * (decimal)timesArg.DoubleValue;
                unchecked
                {
                    //decimal型は強制的にOverFlowExceptionを投げるので対策が必要
                    //OverFlowの場合は昔の挙動に近づけてみる
                    if (d <= Int64.MaxValue && d >= Int64.MinValue)
                        var.SetValue((Int64)d, exm);
                    else
                        var.SetValue((Int64)(double)d, exm);
                }
            }
        }
    }


    private sealed class ADDCHARA_Instruction : AInstruction
    {
        public ADDCHARA_Instruction(bool flagSp, bool flagDel)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
            flag = METHOD_SAFE;
            isDel = flagDel;
            isSp = flagSp;
        }

        bool isDel;
        bool isSp;

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (!Config.CompatiSPChara && isSp)
                throw new CodeEE(LocalizationManager.Error.SPCharaConfigIsOff);
            ExpressionArrayArgument intExpArg = (ExpressionArrayArgument)func.Argument;
            Int64 integer;
            Int64[] charaNoList = new Int64[intExpArg.TermList.Length];
            int i = 0;
            foreach (AExpression int64Term in intExpArg.TermList)
            {
                integer = int64Term.GetIntValue(exm);
                if (isDel)
                {
                    charaNoList[i] = integer;
                    i++;
                }
                else
                {
                    if (Config.CompatiSPChara)
                        exm.VEvaluator.AddCharacter_UseSp(integer, isSp);
                    else
                        exm.VEvaluator.AddCharacter(integer);
                }
            }
            if (isDel)
            {
                if (charaNoList.Length == 1)
                    exm.VEvaluator.DelCharacter(charaNoList[0]);
                else
                    exm.VEvaluator.DelCharacter(charaNoList);
            }
        }
    }

    private sealed class ADDVOIDCHARA_Instruction : AInstruction
    {
        public ADDVOIDCHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.VEvaluator.AddPseudoCharacter();
        }
    }

    private sealed class SWAPCHARA_Instruction : AInstruction
    {
        public SWAPCHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
            long x = arg.X.GetIntValue(exm);
            long y = arg.Y.GetIntValue(exm);
            exm.VEvaluator.SwapChara(x, y);
        }
    }
    private sealed class COPYCHARA_Instruction : AInstruction
    {
        public COPYCHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
            long x = arg.X.GetIntValue(exm);
            long y = arg.Y.GetIntValue(exm);
            exm.VEvaluator.CopyChara(x, y);
        }
    }

    private sealed class ADDCOPYCHARA_Instruction : AInstruction
    {
        public ADDCOPYCHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
            flag = METHOD_SAFE;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArrayArgument intExpArg = (ExpressionArrayArgument)func.Argument;
            foreach (AExpression int64Term in intExpArg.TermList)
                exm.VEvaluator.AddCopyChara(int64Term.GetIntValue(exm));
        }
    }

    private sealed class SORTCHARA_Instruction : AInstruction
    {
        public SORTCHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SORTCHARA);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpSortcharaArgument spSortArg = (SpSortcharaArgument)func.Argument;
            Int64 elem = 0;
            VariableTerm sortKey = spSortArg.SortKey;
            if (sortKey.Identifier.IsArray1D)
                elem = sortKey.GetElementInt(1, exm);
            else if (sortKey.Identifier.IsArray2D)
            {
                elem = sortKey.GetElementInt(1, exm) << 32;
                elem += sortKey.GetElementInt(2, exm);
            }

            exm.VEvaluator.SortChara(sortKey.Identifier, elem, spSortArg.SortOrder, true);
        }
    }

    private sealed class RESETCOLOR_Instruction : AInstruction
    {
        public RESETCOLOR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.SetStringStyle(Config.ForeColor);
        }
    }

    private sealed class RESETBGCOLOR_Instruction : AInstruction
    {
        public RESETBGCOLOR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.SetBgColor(Config.BackColor);
        }
    }

    private sealed class FONTBOLD_Instruction : AInstruction
    {
        public FONTBOLD_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.SetStringStyle(exm.Console.StringStyle.FontStyle | FontStyle.Bold);
        }
    }
    private sealed class FONTITALIC_Instruction : AInstruction
    {
        public FONTITALIC_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.SetStringStyle(exm.Console.StringStyle.FontStyle | FontStyle.Italic);
        }
    }
    private sealed class FONTREGULAR_Instruction : AInstruction
    {
        public FONTREGULAR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.Console.SetStringStyle(FontStyle.Regular);
        }
    }

    private sealed class VARSET_Instruction : AInstruction
    {
        public VARSET_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_VAR_SET);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {

            SpVarSetArgument spvarsetarg = (SpVarSetArgument)func.Argument;
            VariableTerm var = spvarsetarg.VariableDest;
            // [Emuera改修:MACRO-05]
            // VARSETは戦闘中にも頻繁に使われるため、添字を写した一時参照を処理中だけ借りる。
            // usingの範囲を抜けると自動で返却される。評価順と実際の一括代入処理は従来どおり。
            // 仕組みの本体は VariableTerm.RentFixedVariableTerm を参照。
            using VariableTerm.FixedVariableTermLease fixedTermLease = var.RentFixedVariableTerm(exm);
            FixedVariableTerm p = fixedTermLease.Term;
            int start = 0;
            int end = 0;
            //endを先に取って判定の処理変更
            if (spvarsetarg.End != null)
                end = (int)spvarsetarg.End.GetIntValue(exm);
            else if (var.Identifier.IsArray1D)
                end = (int)var.GetLength();
            if (spvarsetarg.Start != null)
            {
                start = (int)spvarsetarg.Start.GetIntValue(exm);
                if (start > end)
                {
                    (end, start) = (start, end);
                }
            }
            if (var.IsString)
            {
                string src = spvarsetarg.Term.GetStrValue(exm);
                VariableEvaluator.SetValueAll(p, src, start, end);
            }
            else
            {
                long src = spvarsetarg.Term.GetIntValue(exm);
                VariableEvaluator.SetValueAll(p, src, start, end);
            }
        }
    }

    private sealed class CVARSET_Instruction : AInstruction
    {
        public CVARSET_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CVAR_SET);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpCVarSetArgument spvarsetarg = (SpCVarSetArgument)func.Argument;
            // [Emuera改修:MACRO-05] VARSETと同じく、CVARSETでも処理中だけ一時参照を借りる。
            using VariableTerm.FixedVariableTermLease fixedTermLease = spvarsetarg.VariableDest.RentFixedVariableTerm(exm);
            FixedVariableTerm p = fixedTermLease.Term;
            SingleTerm index = spvarsetarg.Index.GetValue(exm);
            int charaNum = (int)exm.VEvaluator.CHARANUM;
            int start = 0;
            if (spvarsetarg.Start != null)
            {
                start = (int)spvarsetarg.Start.GetIntValue(exm);
                if (start < 0 || start >= charaNum)
                    throw new CodeEE("命令CVARSETの第４引数(" + start.ToString() + ")がキャラクタの範囲外です");
            }
            int end;
            if (spvarsetarg.End != null)
            {
                end = (int)spvarsetarg.End.GetIntValue(exm);
                if (end < 0 || end > charaNum)
                    throw new CodeEE("命令CVARSETの第５引数(" + end.ToString() + ")がキャラクタの範囲外です");
            }
            else
                end = charaNum;
            if (start > end)
            {
                int temp = start;
                start = end;
                end = temp;
            }
            if (!p.Identifier.IsCharacterData)
                throw new CodeEE("命令CVARSETにキャラクタ変数でない変数" + p.Identifier.Name + "が渡されました");
            if (index is SingleStrTerm singleStrTerm && p.Identifier.IsArray1D)
            {
                if (!GlobalStatic.ConstantData.isDefined(p.Identifier.Code, singleStrTerm.Str))
                    throw new CodeEE("文字列" + singleStrTerm.Str + "は配列変数" + p.Identifier.Name + "の要素ではありません");
            }
            if (p.Identifier.IsString)
            {
                string src = spvarsetarg.Term.GetStrValue(exm);
                exm.VEvaluator.SetValueAllEachChara(p, index, src, start, end);
            }
            else
            {
                long src = spvarsetarg.Term.GetIntValue(exm);
                exm.VEvaluator.SetValueAllEachChara(p, index, src, start, end);
            }
        }
    }

    private sealed class RANDOMIZE_Instruction : AInstruction
    {
        public RANDOMIZE_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION_NULLABLE);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            Int64 iValue;
            if (func.Argument.IsConst)
                iValue = func.Argument.ConstInt;
            else
                iValue = ((ExpressionArgument)func.Argument).Term.GetIntValue(exm);

            if (JSONConfig.Game.UseNewRandom)
            {
                ParserMediator.Warn("新しい乱数アルゴリズムではRANDOMIZEは無視されます", null, 1);
                ParserMediator.FlushWarningList();
            }
            else
            {
                exm.VEvaluator.Randomize(iValue);
            }
        }
    }
    private sealed class INITRAND_Instruction : AInstruction
    {
        public INITRAND_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (JSONConfig.Game.UseNewRandom)
            {
                ParserMediator.Warn("新しい乱数アルゴリズムではINITRANDは機能しません", null, 1);
                ParserMediator.FlushWarningList();
            }
            else
            {
                exm.VEvaluator.InitRanddata();
            }
        }
    }

    private sealed class DUMPRAND_Instruction : AInstruction
    {
        public DUMPRAND_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if (JSONConfig.Game.UseNewRandom)
            {
                ParserMediator.Warn("新しい乱数アルゴリズムではDUMPRANDは機能しません", null, 1);
                ParserMediator.FlushWarningList();
            }
            else
            {
                exm.VEvaluator.DumpRanddata();
            }
        }
    }


    private sealed class SAVEGLOBAL_Instruction : AInstruction
    {
        public SAVEGLOBAL_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
#if R0_F2
            GlobalStatic.Process?.R0F2BeforeLegacySaveGlobal(func);
#endif
            var result = exm.VEvaluator.SaveGlobal();
#if R0_F2
            GlobalStatic.Process?.R0F2AfterLegacySaveGlobal(func, result);
#endif
        }
    }

    private sealed class LOADGLOBAL_Instruction : AInstruction
    {
        public LOADGLOBAL_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
#if R0_F1
            GlobalStatic.Process?.R0F1BeforeLegacyLoadGlobal(func);
#endif
            if (exm.VEvaluator.LoadGlobal())
                exm.VEvaluator.RESULT = 1;
            else
                exm.VEvaluator.RESULT = 0;
#if R0_F1
            GlobalStatic.Process?.R0F1AfterLegacyLoadGlobal(func);
#endif
        }
    }

    private sealed class RESETDATA_Instruction : AInstruction
    {
        public RESETDATA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.VEvaluator.ResetData();
            exm.Console.ResetStyle();
        }
    }

    private sealed class RESETGLOBAL_Instruction : AInstruction
    {
        public RESETGLOBAL_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            exm.VEvaluator.ResetGlobalData();
        }
    }

    private static int toUInt32inArg(Int64 value, string funcName, int argnum)
    {
        if (value < 0)
            throw new CodeEE(funcName + "の第" + argnum.ToString() + "引数に負の値(" + value.ToString() + ")が指定されました");
        else if (value > Int32.MaxValue)
            throw new CodeEE(funcName + "の第" + argnum.ToString() + "引数の値(" + value.ToString() + ")が大きすぎます");

        return (int)value;
    }

    private sealed class SAVECHARA_Instruction : AInstruction
    {
        public SAVECHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SAVECHARA);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
            AExpression[] terms = arg.TermList;
            string datFilename = terms[0].GetStrValue(exm);
            string savMes = terms[1].GetStrValue(exm);
            int[] savCharaList = new int[terms.Length - 2];
            int charanum = (int)exm.VEvaluator.CHARANUM;
            for (int i = 0; i < savCharaList.Length; i++)
            {
                Int64 v = terms[i + 2].GetIntValue(exm);
                savCharaList[i] = FunctionIdentifier.toUInt32inArg(v, "SAVECHARA", i + 3);
                if (savCharaList[i] >= charanum)
                    throw new CodeEE("SAVECHARAの第" + (i + 3).ToString() + "引数の値がキャラ登録番号の範囲を超えています");
                for (int j = 0; j < i; j++)
                {
                    if (savCharaList[i] == savCharaList[j])
                        throw new CodeEE("同一のキャラ登録番号(" + savCharaList[i].ToString() + ")が複数回指定されました");
                }
            }
            exm.VEvaluator.SaveChara(datFilename, savMes, savCharaList);
        }
    }

    private sealed class LOADCHARA_Instruction : AInstruction
    {
        public LOADCHARA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            string datFilename;
            if (arg.IsConst)
                datFilename = arg.ConstStr;
            else
                datFilename = arg.Term.GetStrValue(exm);
            exm.VEvaluator.LoadChara(datFilename);
        }
    }


    private sealed class SAVEVAR_Instruction : AInstruction
    {
        public SAVEVAR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SAVEVAR);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            throw new NotImplCodeEE();
            //SpSaveVarArgument arg = (SpSaveVarArgument)func.Argument;
            //VariableToken[] vars = arg.VarTokens;
            //string datFilename = arg.Term.GetStrValue(exm);
            //string savMes = arg.SavMes.GetStrValue(exm);
            //exm.VEvaluator.SaveVariable(datFilename, savMes, vars);
        }
    }
    private sealed class LOADVAR_Instruction : AInstruction
    {
        public LOADVAR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            throw new NotImplCodeEE();
            //ExpressionArgument arg = (ExpressionArgument)func.Argument;
            //string datFilename = null;
            //if (arg.IsConst)
            //    datFilename = arg.ConstStr;
            //else
            //    datFilename = arg.Term.GetStrValue(exm);
            //exm.VEvaluator.LoadVariable(datFilename);

        }
    }

    private sealed class DELDATA_Instruction : AInstruction
    {
        public DELDATA_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            Int64 target;
            if (func.Argument.IsConst)
                target = func.Argument.ConstInt;
            else
                target = ((ExpressionArgument)func.Argument).Term.GetIntValue(exm);

            int target32 = FunctionIdentifier.toUInt32inArg(target, "DELDATA", 1);
#if R0_F2
            GlobalStatic.Process?.R0F2BeforeLegacyDelData(func, target32);
#endif
            VariableEvaluator.DelData(target32);
#if R0_F2
            GlobalStatic.Process?.R0F2AfterLegacyDelData(func, target32);
#endif
        }
    }

    private sealed class DO_NOTHING_Instruction : AInstruction
    {
        public DO_NOTHING_Instruction()
        {
            //事実上ENDIFの非フローコントロール版
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED | PARTIAL;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            //何もしない
        }
    }

    private sealed class REF_Instruction : AInstruction
    {
        public REF_Instruction(bool byname)
        {
            this.byname = byname;
            if (byname)
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_REFBYNAME);
            else
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_REF);

            flag = METHOD_SAFE | EXTENDED;
        }

        bool byname;

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            throw new NotImplCodeEE();

#pragma warning disable CS0162 // 到達できないコードが検出されました
            RefArgument arg = (RefArgument)func.Argument;
#pragma warning restore CS0162 // 到達できないコードが検出されました
            string str = null;
            if (arg.SrcTerm != null)
                str = arg.SrcTerm.GetStrValue(exm);
            if (arg.RefMethodToken != null)
            {
                UserDefinedRefMethod srcRef = arg.SrcRefMethodToken;
                CalledFunction call = arg.SrcCalledFunction;
                if (str != null)//REFBYNAMEかつ第二引数が定数でない
                {
                    srcRef = GlobalStatic.IdentifierDictionary.GetRefMethod(str);
                    if (srcRef == null)
                    {
                        FunctionLabelLine label = GlobalStatic.LabelDictionary.GetNonEventLabel(str);
                        //if (label == null)
                        //    throw new CodeEE("式中関数" + str + "が見つかりません");
                        //if (!label.IsMethod)
                        //    throw new CodeEE("#FUNCTION(S)属性を持たない関数" + str + "は参照できません");
                        if (label != null && label.IsMethod)
                            call = CalledFunction.CreateCalledFunctionMethod(label, str);
                    }
                }
                else if (srcRef != null)
                    call = srcRef.CalledFunction;//第二引数が関数参照。callがnullならエラー
                if (call == null || !arg.RefMethodToken.MatchType(call))
                {
                    arg.RefMethodToken.SetReference(null);
                    exm.VEvaluator.RESULT = 0;
                }
                else
                {
                    arg.RefMethodToken.SetReference(call);
                    exm.VEvaluator.RESULT = 1;
                }
                return;
            }

            ReferenceToken refVar = arg.RefVarToken;
            VariableToken srcVar = arg.SrcVarToken;
            if (str != null)
            {
                srcVar = GlobalStatic.IdentifierDictionary.GetVariableToken(str, null, true);

                //if (srcVar == null)
                //    throw new CodeEE("変数" + str + "が見つかりません");
            }
            if (srcVar == null || !refVar.MatchType(srcVar, false, out string errmes))
            {
                refVar.SetRef(null);
                exm.VEvaluator.RESULT = 0;
            }
            else
            {
                refVar.SetRef((Array)srcVar.GetArray());
                exm.VEvaluator.RESULT = 1;
            }
            return;
        }
    }

    private sealed class TOOLTIP_SETCOLOR_Instruction : AInstruction
    {
        public TOOLTIP_SETCOLOR_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
            flag = METHOD_SAFE | EXTENDED;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
            long foreColor = arg.X.GetIntValue(exm);
            long backColor = arg.Y.GetIntValue(exm);
            if (foreColor < 0 || foreColor > 0xFFFFFF)
                throw new CodeEE("第１引数が色を表す整数の範囲外です");
            if (backColor < 0 || backColor > 0xFFFFFF)
                throw new CodeEE("第２引数が色を表す整数の範囲外です");
            Color fc = Color.FromArgb((int)foreColor >> 16, (int)foreColor >> 8 & 0xFF, (int)foreColor & 0xFF);
            Color bc = Color.FromArgb((int)backColor >> 16, (int)backColor >> 8 & 0xFF, (int)backColor & 0xFF);
            exm.Console.SetToolTipColor(fc, bc);
            return;
        }
    }

    private sealed class TOOLTIP_SETDELAY_Instruction : AInstruction
    {
        public TOOLTIP_SETDELAY_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            long delay;
            if (arg.IsConst)
                delay = arg.ConstInt;
            else
                delay = arg.Term.GetIntValue(exm);
            if (delay < 0 || delay > int.MaxValue)
                throw new CodeEE(LocalizationManager.Error.ArgIsOoR);
            exm.Console.SetToolTipDelay((int)delay);
            return;
        }
    }

    private sealed class TOOLTIP_SETDURATION_Instruction : AInstruction
    {
        public TOOLTIP_SETDURATION_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument arg = (ExpressionArgument)func.Argument;
            long duration;
            if (arg.IsConst)
                duration = arg.ConstInt;
            else
                duration = arg.Term.GetIntValue(exm);
            if (duration < 0 || duration > int.MaxValue)
                throw new CodeEE(LocalizationManager.Error.ArgIsOoR);
            if (duration > short.MaxValue)
                duration = short.MaxValue;
            exm.Console.SetToolTipDuration((int)duration);
            return;
        }
    }

    private sealed class INPUTMOUSEKEY_Instruction : AInstruction
    {
        public INPUTMOUSEKEY_Instruction()
        {
            ArgBuilder = ArgumentParser.GetNormalArgumentBuilder("I", 0);
            //スキップ不可
            //flag = IS_PRINT | IS_INPUT | EXTENDED;
            flag = EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionsArgument arg = (ExpressionsArgument)func.Argument;
            Int64 time = 0;
            if (arg.ArgumentArray.Count > 0)
                time = arg.ArgumentArray[0].GetIntValue(exm);
            InputRequest req = new()
            {
                InputType = InputType.PrimitiveMouseKey
            };
            if (time > 0)
                req.Timelimit = (int)time;
            exm.Console.WaitInput(req);
        }
    }

    private sealed class AWAIT_Instruction : AInstruction
    {
        public AWAIT_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION_NULLABLE);
            //スキップ不可
            //flag = IS_PRINT | IS_INPUT | EXTENDED;
            flag = EXTENDED;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            Int64 waittime = -1;
            ExpressionArgument arg = func.Argument as ExpressionArgument;
            if (arg != null && arg.Term != null)
            {
                waittime = arg.Term.GetIntValue(exm);
                if (waittime < 0)
                    throw new CodeEE("AWAIT命令:負の値(" + waittime.ToString() + ")が指定されました");
                if (waittime > 10000)
                    throw new CodeEE("AWAIT命令:10秒以上の待機時間(" + waittime.ToString() + " ms)が指定されました");
            }

            exm.Console.Await((int)waittime);
        }
    }

    private sealed class MATCHALL_Instruction : AInstruction
    {
        public MATCHALL_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_MATCHALL);
            flag = EXTENDED | METHOD_SAFE;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            var arg = func.Argument as SpMatchAllArgument;
            var token = arg.Token;
            var valExpr = arg.Value;
            var type = valExpr.GetOperandType();
            var beg = arg.Beg?.GetIntValue(exm) ?? 0;
            long end;
            long len;
            var count = 0;
            var arr = exm.VEvaluator.RESULT_ARRAY;
            var output = arr.AsSpan()[1..];
            if (token.IsCharacterData)
            {
                if (token.IsArray1D)
                    len = exm.VEvaluator.CHARANUM;
                else if (token.IsArray2D || token.IsArray3D)
                    throw new ExeEE("type error");
                else
                    len = exm.VEvaluator.CHARANUM;
            }
            else
            {
                if (token.IsArray1D)
                    len = token.GetLength(0);
                else
                    throw new ExeEE("type error");
            }
            if (arg.End is not null)
                end = arg.End.GetIntValue(exm);
            else
                end = len;
            if (beg < 0 || end < 0)
                throw new CodeEE("検索範囲に負の値が渡されました");
            if (beg > end)
                throw new CodeEE("検索範囲の指定が不正です");
            if (long.Max(beg, end) > len)
                throw new CodeEE("検索範囲が変数のサイズを超えています");

            var idxs = new long[2];
            int p = 0;
            if (arg.Index.HasValue)
                idxs[1] = arg.Index.Value;
            if (type == typeof(long))
            {
                var val = valExpr.GetIntValue(exm);
                for (var i = beg; i < end; i++)
                {
                    idxs[p] = i;
                    if (val == token.GetIntValue(exm, idxs))
                    {
                        if (output.Length > count)
                            output[count] = i;
                        ++count;
                    }
                }
            }
            else if (type == typeof(string))
            {
                var val = valExpr.GetStrValue(exm);
                for (var i = beg; i < end; i++)
                {
                    idxs[p] = i;
                    if (val == token.GetStrValue(exm, idxs))
                    {
                        if (output.Length > count)
                            output[count] = i;
                        ++count;
                    }
                }
            }
            else
                throw new ExeEE("unknown type");
            arr[0] = count;
        }
    }
    #endregion

    #region flowControlFunction

    private sealed class BEGIN_Instruction : AInstruction
    {
        public BEGIN_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR);
            flag = FLOW_CONTROL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            string keyword = func.Argument.ConstStr;
            state.SetBegin(keyword);
            state.Return(0);
            exm.Console.ResetStyle();
#if R0_F5B
            GlobalStatic.Process.R0F5BAfterBeginStyleReset();
#endif
        }
    }

    private sealed class SAVELOADGAME_Instruction : AInstruction
    {
        public SAVELOADGAME_Instruction(bool isSave)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = FLOW_CONTROL;
            this.isSave = isSave;
        }
        readonly bool isSave;
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            if ((state.SystemState & SystemStateCode.__CAN_SAVE__) != SystemStateCode.__CAN_SAVE__)
            {
                string funcName = state.Scope;
                if (funcName == null)
                    funcName = "";
                throw new CodeEE("@" + funcName + "中でSAVEGAME/LOADGAME命令を実行することはできません");
            }
            GlobalStatic.Process.saveCurrentState(true);
            //バックアップに入れた旧ProcessStateの方を参照するため、ここでstateは使えない
            GlobalStatic.Process.getCurrentState.SaveLoadData(isSave);
        }
    }

    private sealed class REPEAT_Instruction : AInstruction
    {
        public REPEAT_Instruction(bool fornext)
        {
            flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL;
            if (fornext)
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_FOR_NEXT);
                flag |= EXTENDED;
            }
            else
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            }
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            LoopInstructionLine loop = (LoopInstructionLine)func;
            SpForNextArgment forArg = (SpForNextArgment)func.Argument;
            loop.LoopCounter = forArg.Cnt;
            //1.725 順序変更。REPEATにならう。
            loop.LoopCounter.SetValue(forArg.Start.GetIntValue(exm), exm);
            loop.LoopEnd = forArg.End.GetIntValue(exm);
            loop.LoopStep = forArg.Step.GetIntValue(exm);
            if (func.FunctionCode == FunctionCode.FOR)
                GlobalStatic.Process.TraceR1_4EForInit(loop, exm);
            if ((loop.LoopStep > 0) && (loop.LoopEnd > loop.LoopCounter.GetIntValue(exm)))//まだ回数が残っているなら、
                return;//そのまま次の行へ
            else if ((loop.LoopStep < 0) && (loop.LoopEnd < loop.LoopCounter.GetIntValue(exm)))//まだ回数が残っているなら、
                return;//そのまま次の行へ
            state.JumpTo(func.JumpTo);
        }
    }

    private sealed class WHILE_Instruction : AInstruction
    {
        public WHILE_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument expArg = (ExpressionArgument)func.Argument;
            if (expArg.Term.GetIntValue(exm) != 0)//式が真
                return;//そのまま中の処理へ
            state.JumpTo(func.JumpTo);
        }
    }

    private sealed class SIF_Instruction : AInstruction
    {
        public SIF_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
        }

        public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
        {
            LogicalLine jumpto = func.NextLine;
            if ((jumpto == null) || (jumpto.NextLine == null) ||
                (jumpto is FunctionLabelLine) || (jumpto is NullLine))
            {
                ParserMediator.Warn(LocalizationManager.Error.NothingAfterSif, func, 2, true, false);
                return;
            }
            else if (jumpto is InstructionLine)
            {
                InstructionLine sifFunc = (InstructionLine)jumpto;
                if (sifFunc.Function.IsPartial())
                    ParserMediator.Warn("SIF文の次の行を" + sifFunc.Function.Name + "文にすることはできません", func, 2, true, false);
                else
                    func.JumpTo = func.NextLine.NextLine;
            }
            else if (jumpto is GotoLabelLine)
                ParserMediator.Warn(LocalizationManager.Error.LabelCanNotAfterSif, func, 2, true, false);
            else
                func.JumpTo = func.NextLine.NextLine;

            if ((func.JumpTo != null) && (func.Position.Value.LineNo + 1 != func.NextLine.Position.Value.LineNo))
                ParserMediator.Warn(LocalizationManager.Error.EmptyAfterSif, func, 0, false, true);
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument expArg = (ExpressionArgument)func.Argument;
            var value = expArg.Term.GetIntValue(exm);
            GlobalStatic.Process.TraceR1_4GTitleSif(func, state, value);
            if (value == 0)//評価式が真ならそのまま流れ落ちる
                state.ShiftNextLine();//偽なら一行とばす。順に来たときと同じ扱いにする
        }
    }

    private sealed class ELSEIF_Instruction : AInstruction
    {
        public ELSEIF_Instruction(FunctionArgType argtype)
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(argtype);
            flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            //if (iFuncCode == FunctionCode.ELSE || iFuncCode == FunctionCode.ELSEIF
            //	|| iFuncCode == FunctionCode.CASE || iFuncCode == FunctionCode.CASEELSE)
            //チェック済み
            //if (func.JumpTo == null)
            //	throw new ExeEE(func.Function.Name + "のジャンプ先が設定されていない");
            state.JumpTo(func.JumpTo);
        }
    }
    private sealed class ENDIF_Instruction : AInstruction
    {
        public ENDIF_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = FLOW_CONTROL | PARTIAL | FORCE_SETARG;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
        }
    }

    private sealed class IF_Instruction : AInstruction
    {
        public IF_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            LogicalLine ifJumpto = func.JumpTo;//ENDIF
                                               //チェック済み
                                               //if (func.IfCaseList == null)
                                               //	throw new ExeEE("IFのIF-ELSEIFリストが適正に作成されていない");
                                               //if (func.JumpTo == null)
                                               //	throw new ExeEE("IFに対応するENDIFが設定されていない");
            foreach (var line in func.IfCaseList)
            {
                if (line.IsError)
                    continue;
                if (line.FunctionCode == FunctionCode.ELSE)
                {
                    ifJumpto = line;
                    break;
                }

                //ExpressionArgument expArg = (ExpressionArgument)(line.Argument);
                //チェック済み
                //if (expArg == null)
                //	throw new ExeEE("IFチェック中。引数が解析されていない。", func.IfCaseList[i].Position);

                //1730 ELSEIFが出したエラーがIFのエラーとして検出されていた
                state.CurrentLine = line;
                long value = 0;
                if (line.Argument.IsConst)
                {
                    value = line.Argument.ConstInt;
                }
                else
                {
                    value = ((ExpressionArgument)line.Argument).Term.GetIntValue(exm);
                }
                if (value != 0)//式が真
                {
                    ifJumpto = line;
                    break;
                }
            }
            if (ifJumpto != func)//自分自身がジャンプ先ならそのまま
                state.JumpTo(ifJumpto);
            //state.RunningLine = null;
        }
    }


    private sealed class SELECTCASE_Instruction : AInstruction
    {
        public SELECTCASE_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION);
            flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            LogicalLine caseJumpto = func.JumpTo;//ENDSELECT
            AExpression selectValue = ((ExpressionArgument)func.Argument).Term;
            string sValue = null;
            Int64 iValue = 0;
            if (selectValue.IsInteger)
                iValue = selectValue.GetIntValue(exm);
            else
                sValue = selectValue.GetStrValue(exm);
            //チェック済み
            //if (func.IfCaseList == null)
            //	throw new ExeEE("SELECTCASEのCASEリストが適正に作成されていない");
            //if (func.JumpTo == null)
            //	throw new ExeEE("SELECTCASEに対応するENDSELECTが設定されていない");
            foreach (var line in func.IfCaseList)
            {
                if (line.IsError)
                    continue;
                if (line.FunctionCode == FunctionCode.CASEELSE)
                {
                    caseJumpto = line;
                    break;
                }
                CaseArgument caseArg = (CaseArgument)line.Argument;
                //チェック済み
                //if (caseArg == null)
                //	throw new ExeEE("CASEチェック中。引数が解析されていない。", func.IfCaseList[i].Position);

                state.CurrentLine = line;
                if (selectValue.IsInteger)
                {
                    Int64 Is = iValue;
                    foreach (CaseExpression caseExp in caseArg.CaseExps)
                    {
                        if (caseExp.GetBool(Is, exm))
                        {
                            caseJumpto = line;
                            goto casefound;
                        }
                    }
                }
                else
                {
                    string Is = sValue;
                    foreach (CaseExpression caseExp in caseArg.CaseExps)
                    {
                        if (caseExp.GetBool(Is, exm))
                        {
                            caseJumpto = line;
                            goto casefound;
                        }
                    }
                }

            }
        casefound:
            state.JumpTo(caseJumpto);
            //state.RunningLine = null;
        }
    }

    private sealed class RETURNFORM_Instruction : AInstruction
    {
        public RETURNFORM_Instruction()
        {
            //ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_ANY);
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR);
            flag = EXTENDED | FLOW_CONTROL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            //int termnum = 0;
            //foreach (AExpression term in ((ExpressionArrayArgument)func.Argument).TermList)
            //{
            //    string arg = term.GetStrValue(exm);
            //    StringStream aSt = new StringStream(arg);
            //    WordCollection wc = LexicalAnalyzer.Analyse(aSt, LexEndWith.EoL, LexAnalyzeFlag.None);
            //    exm.VEvaluator.SetResultX((ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL).GetIntValue(exm)), termnum);
            //    termnum++;
            //}
            //state.Return(exm.VEvaluator.RESULT);
            //if (state.ScriptEnd)
            //    return;
            //int termnum = 0;
            CharStream aSt = new(((ExpressionArgument)func.Argument).Term.GetStrValue(exm));
            List<long> termList = [];
            while (!aSt.EOS)
            {
                WordCollection wc = LexicalAnalyzer.Analyse(aSt, LexEndWith.Comma, LexAnalyzeFlag.None);
                //exm.VEvaluator.SetResultX(ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL).GetIntValue(exm), termnum++);
                termList.Add(ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL).GetIntValue(exm));
                aSt.ShiftNext();
                LexicalAnalyzer.SkipHalfSpace(aSt);
                //termnum++;
            }
            if (termList.Count == 0)
                termList.Add(0);
            exm.VEvaluator.SetResultX(termList);
            state.Return(exm.VEvaluator.RESULT);
            if (state.ScriptEnd)
                return;
        }
    }

    private sealed class RETURN_Instruction : AInstruction
    {
        public RETURN_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
            flag = FLOW_CONTROL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            //int termnum = 0;
            ExpressionArrayArgument expArrayArg = (ExpressionArrayArgument)func.Argument;
            if (expArrayArg.TermList.Length == 0)
            {
                exm.VEvaluator.RESULT = 0;
                state.Return(0);
                return;
            }
            List<long> termList = [];
            foreach (AExpression term in expArrayArg.TermList)
            {
                termList.Add(term.GetIntValue(exm));
                //exm.VEvaluator.SetResultX(term.GetIntValue(exm), termnum++);
            }
            if (termList.Count == 0)
                termList.Add(0);
            exm.VEvaluator.SetResultX(termList);
            state.Return(exm.VEvaluator.RESULT);
        }
    }

    private sealed class CATCH_Instruction : AInstruction
    {
        public CATCH_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            //if (sequential)//上から流れてきたなら何もしないでENDCATCHに飛ぶ
            state.JumpTo(func.JumpToEndCatch);
        }
    }

    private sealed class RESTART_Instruction : AInstruction
    {
        public RESTART_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | FLOW_CONTROL | EXTENDED;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            state.JumpTo(func.ParentLabelLine);
        }
    }

    private sealed class BREAK_Instruction : AInstruction
    {
        public BREAK_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | FLOW_CONTROL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ////BREAKのJUMP先はRENDまたはNEXT。そのジャンプ先であるREPEATかFORをiLineに代入。
            //1.723 仕様変更。BREAKのJUMP先にはREPEAT、FOR、WHILEを記憶する。そのJUMP先が本当のJUMP先。
            InstructionLine jumpTo = (InstructionLine)func.JumpTo;
            InstructionLine iLine = (InstructionLine)jumpTo.JumpTo;
            //WHILEとDOはカウンタがないので、即ジャンプ
            if (jumpTo.FunctionCode != FunctionCode.WHILE && jumpTo.FunctionCode != FunctionCode.DO)
            {
                LoopInstructionLine loop = (LoopInstructionLine)jumpTo;
                unchecked
                {//eramakerではBREAK時にCOUNTが回る
                    loop.LoopCounter.ChangeValue(loop.LoopStep, exm);
                }
            }
            state.JumpTo(iLine);
        }
    }

    private sealed class CONTINUE_Instruction : AInstruction
    {
        public CONTINUE_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | FLOW_CONTROL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            InstructionLine jumpTo = (InstructionLine)func.JumpTo;
            if ((jumpTo.FunctionCode == FunctionCode.REPEAT) || (jumpTo.FunctionCode == FunctionCode.FOR))
            {
                LoopInstructionLine loop = (LoopInstructionLine)jumpTo;
                //ループ変数が不明(REPEAT、FORを経由せずにループしようとした場合は無視してループを抜ける(eramakerがこういう仕様だったりする))
                if (loop.LoopCounter == null)
                {
                    state.JumpTo(jumpTo.JumpTo);
                    return;
                }
                unchecked
                {
                    loop.LoopCounter.ChangeValue(loop.LoopStep, exm);
                }
                Int64 counter = loop.LoopCounter.GetIntValue(exm);
                //まだ回数が残っているなら、
                if (((loop.LoopStep > 0) && (loop.LoopEnd > counter))
                    || ((loop.LoopStep < 0) && (loop.LoopEnd < counter)))
                    state.JumpTo(func.JumpTo);
                else
                    state.JumpTo(jumpTo.JumpTo);
                return;
            }
            if (jumpTo.FunctionCode == FunctionCode.WHILE)
            {
                if (((ExpressionArgument)jumpTo.Argument).Term.GetIntValue(exm) != 0)
                    state.JumpTo(func.JumpTo);
                else
                    state.JumpTo(jumpTo.JumpTo);
                return;
            }
            if (jumpTo.FunctionCode == FunctionCode.DO)
            {
                //こいつだけはCONTINUEよりも後ろに判定行があるため、判定行にエラーがあった場合に問題がある
                InstructionLine tFunc = (InstructionLine)((InstructionLine)func.JumpTo).JumpTo;//LOOP
                if (tFunc.IsError)
                    throw new CodeEE(tFunc.ErrMes, tFunc.Position);
                ExpressionArgument expArg = (ExpressionArgument)tFunc.Argument;
                if (expArg.Term.GetIntValue(exm) != 0)//式が真
                    state.JumpTo(jumpTo);//DO
                else
                    state.JumpTo(tFunc);//LOOP
                return;
            }
            throw new ExeEE(LocalizationManager.Error.AbnormalContinue);
        }
    }

    private sealed class REND_Instruction : AInstruction
    {
        public REND_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            LoopInstructionLine jumpTo = (LoopInstructionLine)func.JumpTo;
            //ループ変数が不明(REPEAT、FORを経由せずにループしようとした場合は無視してループを抜ける(eramakerがこういう仕様だったりする))
            if (jumpTo.LoopCounter == null)
            {
                state.JumpTo(jumpTo.JumpTo);
                return;
            }
            var counterBefore = GlobalStatic.Process.ReadR1_4ELoopCounter(jumpTo, exm);
            unchecked
            {
                jumpTo.LoopCounter.ChangeValue(jumpTo.LoopStep, exm);
            }
            Int64 counter = jumpTo.LoopCounter.GetIntValue(exm);
            var continueDecision = ((jumpTo.LoopStep > 0) && (jumpTo.LoopEnd > counter))
                || ((jumpTo.LoopStep < 0) && (jumpTo.LoopEnd < counter));
            if (func.FunctionCode == FunctionCode.NEXT)
                GlobalStatic.Process.TraceR1_4EForNext(jumpTo, exm, counterBefore, continueDecision);
            //まだ回数が残っているなら、
            if (continueDecision)
                state.JumpTo(func.JumpTo);
        }
    }

    private sealed class WEND_Instruction : AInstruction
    {
        public WEND_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
            flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            InstructionLine jumpTo = (InstructionLine)func.JumpTo;
            if (((ExpressionArgument)jumpTo.Argument).Term.GetIntValue(exm) != 0)
                state.JumpTo(func.JumpTo);
        }
    }

    private sealed class LOOP_Instruction : AInstruction
    {
        public LOOP_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
            flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            ExpressionArgument expArg = (ExpressionArgument)func.Argument;
            if (expArg.Term.GetIntValue(exm) != 0)//式が真
                state.JumpTo(func.JumpTo);
        }
    }


    private sealed class RETURNF_Instruction : AInstruction
    {
        public RETURNF_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION_NULLABLE);
            flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL;
        }

        public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
        {
            FunctionLabelLine label = func.ParentLabelLine;
            if (!label.IsMethod)
            {
                ParserMediator.Warn("RETURNFは#FUNCTION以外では使用できません", func, 2, true, false);
            }
            if (func.Argument != null)
            {
                AExpression term = ((ExpressionArgument)func.Argument).Term;
                if (term != null)
                {
                    if (label.MethodType != term.GetOperandType())
                    {
                        if (label.MethodType == typeof(Int64))
                            ParserMediator.Warn(LocalizationManager.Error.ReturnfStrInIntFunc, func, 2, true, false);
                        else if (label.MethodType == typeof(string))
                            ParserMediator.Warn(LocalizationManager.Error.ReturnfIntInStrFunc, func, 2, true, false);
                    }
                }
            }
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            AExpression term = ((ExpressionArgument)func.Argument).Term;
            SingleTerm ret = null;
            if (term != null)
            {
                ret = term.GetValue(exm);
            }
            GlobalStatic.Process.TraceR1_4GTitleReturn(func, ret, exm);
            state.ReturnF(ret);
        }
    }

    private sealed class CALL_Instruction : AInstruction
    {
        public CALL_Instruction(bool form, bool isJump, bool isTry, bool isTryCatch)
        {
            if (form)
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLFORM);
            else
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALL);
            flag = FLOW_CONTROL | FORCE_SETARG;
            if (isJump)
                flag |= IS_JUMP;
            if (isTry)
                flag |= IS_TRY;
            if (isTryCatch)
                flag |= IS_TRYC | PARTIAL;
            this.isJump = isJump;
            this.isTry = isTry;
        }
        readonly bool isJump;
        readonly bool isTry;

        public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
        {
            if (!func.Argument.IsConst)
            {
                useCallForm = true;
                return;
            }
            SpCallArgment callArg = (SpCallArgment)func.Argument;
            string labelName = callArg.ConstStr;
            CalledFunction call = CalledFunction.CallFunction(GlobalStatic.Process, labelName, func);
            if ((call == null) && (!func.Function.IsTry()))
            {
                FunctionoNotFoundName = labelName;
                return;
            }
            if (call != null)
            {
                func.JumpTo = call.TopLabel;
                if (call.TopLabel.Depth < 0)
                    call.TopLabel.Depth = currentDepth + 1;
                if (call.TopLabel.IsError)
                {
                    func.IsError = true;
                    func.ErrMes = call.TopLabel.ErrMes;
                    return;
                }
                callArg.UDFArgument = call.ConvertArg(callArg.RowArgs, out string errMes);
                if (callArg.UDFArgument == null)
                {
                    ParserMediator.Warn(errMes, func, 2, true, false);
                    return;
                }
            }
            callArg.CallFunc = call;
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            SpCallArgment spCallArg = (SpCallArgment)func.Argument;
            CalledFunction caller = state.functionCount == 0 ? null : state.CurrentCalled;
            CalledFunction call;
            string labelName;
            UserDefinedFunctionArgument arg = null;
            if (spCallArg.IsConst)
            {
                call = spCallArg.CallFunc;
                labelName = spCallArg.ConstStr;
                arg = spCallArg.UDFArgument;
            }
            else
            {
                labelName = spCallArg.FuncnameTerm.GetStrValue(exm);
                call = CalledFunction.CallFunction(GlobalStatic.Process, labelName, func);
            }
#if R0_F4A
            GlobalStatic.Process?.R0F4AObserveLegacyDynamicCall(func, labelName, call is not null);
#endif
#if R0_F4B
            GlobalStatic.Process?.R0F4BObserveLegacyDynamicCall(func, labelName, call is not null);
#endif
#if R0_F4C
            GlobalStatic.Process?.R0F4CObserveLegacyDynamicCall(func, labelName, call is not null);
#endif
#if R0_F4D5
            GlobalStatic.Process?.R0F4D5ObserveLegacyDynamicCall(func, labelName, call is not null);
#endif
#if R0_F4E1
            GlobalStatic.Process?.R0F4E1ObserveLegacyDynamicCall(func, labelName, call is not null);
#endif
#if R0_F4E2
            GlobalStatic.Process?.R0F4E2ObserveLegacyDynamicCall(func, labelName, call is not null);
#endif
            if (call == null)
            {
                if (!isTry)
                    throw new CodeEE("関数\"@" + labelName + "\"が見つかりません");
                if (func.JumpToEndCatch != null)
                    state.JumpTo(func.JumpToEndCatch);
                return;
            }
            call.IsJump = isJump;
            if (arg == null)
            {
                arg = call.ConvertArg(spCallArg.RowArgs, out string errMes);
                if (arg == null)
                    throw new CodeEE(errMes);
            }
            state.IntoFunction(call, arg, exm);
            if (caller is not null)
                GlobalStatic.Process.TraceR1_4ECallEntered(caller, func, call);
        }
    }

    private sealed class CALLEVENT_Instruction : AInstruction
    {
        public CALLEVENT_Instruction()
        {
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR);
            flag = FLOW_CONTROL | EXTENDED;
        }

        public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
        {
            //EVENT関数からCALLされた先でCALLEVENTされるようなパターンはIntoFunctionで捕まえる
            FunctionLabelLine label = func.ParentLabelLine;
            if (label.IsEvent)
            {
                ParserMediator.Warn(LocalizationManager.Error.CanNotUseCallevent, func, 2, true, false);
            }
        }

        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            string labelName = func.Argument.ConstStr;
            CalledFunction call = CalledFunction.CallEventFunction(GlobalStatic.Process, labelName, func);
            if (call == null)
                return;
            state.IntoFunction(call, null, null);
        }
    }

    private sealed class GOTO_Instruction : AInstruction
    {
        public GOTO_Instruction(bool form, bool isTry, bool isTryCatch)
        {
            if (form)
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLFORM);
            else
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALL);
            this.isTry = isTry;
            flag = METHOD_SAFE | FLOW_CONTROL | FORCE_SETARG;
            if (isTry)
                flag |= IS_TRY;
            if (isTryCatch)
                flag |= IS_TRYC | PARTIAL;
        }
        readonly bool isTry;

        public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
        {
            GotoLabelLine jumpto;
            func.JumpTo = null;
            if (func.Argument.IsConst)
            {
                string labelName = func.Argument.ConstStr;
                jumpto = GlobalStatic.LabelDictionary.GetLabelDollar(labelName, func.ParentLabelLine);
                if (jumpto == null)
                {
                    if (!func.Function.IsTry())
                        ParserMediator.Warn("指定されたラベル名\"$" + labelName + "\"は現在の関数内に存在しません", func, 2, true, false);
                    else
                        return;
                }
                else if (jumpto.IsError)
                    ParserMediator.Warn("指定されたラベル名\"$" + labelName + "\"は無効な$ラベル行です", func, 2, true, false);
                else if (jumpto != null)
                {
                    func.JumpTo = jumpto;
                }
            }
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
        {
            string label;
            LogicalLine jumpto;
            if (func.Argument.IsConst)
            {
                label = func.Argument.ConstStr;
                if (func.JumpTo != null)
                    jumpto = func.JumpTo;
                else
                    return;
            }
            else
            {
                label = ((SpCallArgment)func.Argument).FuncnameTerm.GetStrValue(exm);
                jumpto = state.CurrentCalled.CallLabel(GlobalStatic.Process, label);
            }
            if (jumpto == null)
            {
                if (!func.Function.IsTry())
                    throw new CodeEE("指定されたラベル名\"$" + label + "\"は現在の関数内に存在しません");
                if (func.JumpToEndCatch != null)
                    state.JumpTo(func.JumpToEndCatch);
                return;
            }
            else if (jumpto.IsError)
                throw new CodeEE("指定されたラベル名\"$" + label + "\"は無効な$ラベル行です");
            state.JumpTo(jumpto);
        }
    }
    #endregion
}

using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Script.Parser;

internal static class LogicalLineParser
{
    // [Emuera改修:WARN-03]
    // OnlyLabelは複数ERBから同時に#ONLY等を登録するため、並列対応Dictionaryを受け取る。
    // 普通のDictionary/Listを共有すると登録途中の内容を別スレッドが誤読することがある。
    // 参照: プロジェクト資料/06_コード案内.md
    public static bool ParseSharpLine(FunctionLabelLine label, CharStream st, ScriptPosition? position, ConcurrentDictionary<string, byte> OnlyLabel)
    {
        st.ShiftNext();//'#'を飛ばす
        var token = LexicalAnalyzer.ReadSingleIdentifier(st);//#～自体にはマクロ非適用
                                                             //#行として不正な行でもAnalyzeに行って引っかかることがあるので、先に存在しない#～は弾いてしまう
        if (string.IsNullOrEmpty(token))
        {
            ParserMediator.Warn(LocalizationManager.Error.CanNotInterpretSharpLine, position, 1);
            return false;
        }
        try
        {
            switch (token)
            {
                case var s when s.Equals("SINGLE", Config.Config.StringComparison):
                    if (label.IsMethod)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UseSingleUserFunc, position, 1);
                        break;
                    }
                    else if (!label.IsEvent)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UsableSingleEventFunc, position, 1);
                        break;
                    }
                    else if (label.IsSingle)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.DuplicateSingle, position, 1);
                        break;
                    }
                    else if (label.IsOnly)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.OnlyWithSingle, position, 1);
                        break;
                    }
                    label.IsSingle = true;
                    break;
                case var s when s.Equals("LATER", Config.Config.StringComparison):
                    if (label.IsMethod)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UseLaterUserFunc, position, 1);
                        break;
                    }
                    else if (!label.IsEvent)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UsableLaterEventFunc, position, 1);
                        break;
                    }
                    else if (label.IsLater)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.DuplicateLater, position, 1);
                        break;
                    }
                    else if (label.IsOnly)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.OnlyWithLater, position, 1);
                        break;
                    }
                    else if (label.IsPri)
                        ParserMediator.Warn(LocalizationManager.Error.PriWithLater, position, 1);
                    label.IsLater = true;
                    break;
                case var s when s.Equals("PRI", Config.Config.StringComparison):
                    if (label.IsMethod)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UsePriUserFunc, position, 1);
                        break;
                    }
                    else if (!label.IsEvent)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UsablePriEventFunc, position, 1);
                        break;
                    }
                    else if (label.IsPri)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.DuplicatePri, position, 1);
                        break;
                    }
                    else if (label.IsOnly)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.OnlyWithPri, position, 1);
                        break;
                    }
                    else if (label.IsLater)
                        ParserMediator.Warn(LocalizationManager.Error.PriWithLater, position, 1);
                    label.IsPri = true;
                    break;
                case var s when s.Equals("ONLY", Config.Config.StringComparison):
                    if (label.IsMethod)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UseOnlyUserFunc, position, 1);
                        break;
                    }
                    else if (!label.IsEvent)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UsableOnlyEventFunc, position, 1);
                        break;
                    }
                    else if (label.IsOnly)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.DuplicateOnly, position, 1);
                        break;
                    }
                    else if (!OnlyLabel.TryAdd(label.LabelName, 0))
                        ParserMediator.Warn("このイベント関数\"@" + label.LabelName + "\"にはすでに#ONLYが宣言されています（この関数は実行されません）", position, 1);
                    label.IsOnly = true;
                    if (label.IsPri)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.BeIgnorePri, position, 1);
                        label.IsPri = false;
                    }
                    if (label.IsLater)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.BeIgnoreLater, position, 1);
                        label.IsLater = false;
                    }
                    if (label.IsSingle)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.BeIgnoreSingle, position, 1);
                        label.IsSingle = false;
                    }
                    break;
                case var s when s.Equals("FUNCTION", Config.Config.StringComparison) ||
                                s.Equals("FUNCTIONS", Config.Config.StringComparison):
                    if (!string.IsNullOrEmpty(label.LabelName) && char.IsDigit(label.LabelName[0]))
                    {
                        ParserMediator.Warn($"#{token}属性は関数名が数字で始まる関数には指定できません", position, 1);
                        label.IsError = true;
                        label.ErrMes = LocalizationManager.Error.FuncNameBeginNumber;
                        break;
                    }
                    if (label.IsMethod)
                    {
                        if (label.MethodType == typeof(long) && token.Equals("FUNCTION", Config.Config.StringComparison) || label.MethodType == typeof(string) && token.Equals("FUNCTIONS", Config.Config.StringComparison))
                        {
                            ParserMediator.Warn($"関数{label.LabelName}にはすでに#{token}が宣言されています(この行は無視されます)", position, 1);
                            return false;
                        }
                        if (label.MethodType == typeof(long) && token.Equals("FUNCTIONS", Config.Config.StringComparison))
                            ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONが宣言されています", position, 2);
                        else if (label.MethodType == typeof(string) && token.Equals("FUNCTION", Config.Config.StringComparison))
                            ParserMediator.Warn("関数" + label.LabelName + "にはすでに#FUNCTIONSが宣言されています", position, 2);
                        return false;
                    }
                    if (label.Depth == 0)
                    {
                        ParserMediator.Warn($"システム関数に#{token}が指定されています", position, 2);
                        return false;
                    }
                    label.IsMethod = true;
                    label.Depth = 0;
                    if (token.Equals("FUNCTIONS", Config.Config.StringComparison))
                        label.MethodType = typeof(string);
                    else
                        label.MethodType = typeof(long);
                    if (label.IsPri)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UsePriUserFunc, position, 1);
                        label.IsPri = false;
                    }
                    if (label.IsLater)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UseLaterUserFunc, position, 1);
                        label.IsLater = false;
                    }
                    if (label.IsSingle)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UseSingleUserFunc, position, 1);
                        label.IsSingle = false;
                    }
                    if (label.IsOnly)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.UseOnlyUserFunc, position, 1);
                        label.IsOnly = false;
                    }
                    break;
                case var s when s.Equals("LOCALSIZE", Config.Config.StringComparison) ||
                                s.Equals("LOCALSSIZE", Config.Config.StringComparison):
                    {
                        WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
                        if (wc.EOL)
                        {
                            ParserMediator.Warn($"#{token}の後に有効な数値が指定されていません", position, 2);
                            break;
                        }
                        //イベント関数では指定しても無視される
                        if (label.IsEvent)
                        {
                            ParserMediator.Warn($"イベント関数では#{token}による{token[..^4]}のサイズ指定は無視されます", position, 1);
                            break;
                        }
                        AExpression arg = ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL);
                        if (arg.Restructure(null) is not SingleLongTerm sizeTerm || sizeTerm.GetOperandType() != typeof(long))
                        {
                            ParserMediator.Warn($"#{token}の後に有効な定数式が指定されていません", position, 2);
                            break;
                        }
                        if (sizeTerm.Int <= 0)
                        {
                            ParserMediator.Warn($"#{token}に0以下の値({sizeTerm.Int})が与えられました。設定は無視されます", position, 1);
                            break;
                        }
                        if (sizeTerm.Int >= int.MaxValue)
                        {
                            ParserMediator.Warn($"#{token}に大きすぎる値({sizeTerm.Int})が与えられました。設定は無視されます", position, 1);
                            break;
                        }
                        int size = (int)sizeTerm.Int;
                        if (token.Equals("LOCALSIZE", Config.Config.StringComparison))
                        {
                            if (GlobalStatic.IdentifierDictionary.getLocalIsForbid("LOCAL"))
                            {
                                ParserMediator.Warn($"#{token}が指定されていますが変数LOCALは使用禁止されています", position, 2);
                                break;
                            }
                            if (label.LocalLength > 0)
                                ParserMediator.Warn(LocalizationManager.Error.DuplicateLocalsize, position, 1);
                            label.LocalLength = size;
                        }
                        else
                        {
                            if (GlobalStatic.IdentifierDictionary.getLocalIsForbid("LOCALS"))
                            {
                                ParserMediator.Warn($"#{token}が指定されていますが変数LOCALSは使用禁止されています", position, 2);
                                break;
                            }
                            if (label.LocalsLength > 0)
                                ParserMediator.Warn(LocalizationManager.Error.DuplicateLocalssize, position, 1);
                            label.LocalsLength = size;
                        }
                    }
                    break;
                case var s when s.Equals("DIM", Config.Config.StringComparison) ||
                                s.Equals("DIMS", Config.Config.StringComparison):
                    {
                        var wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);

                        UserDefinedVariableData data = UserDefinedVariableData.Create(wc, token.Equals("DIMS", Config.Config.StringComparison), true, position);
                        if (!label.AddPrivateVariable(data))
                        {
                            ParserMediator.Warn($"変数名{data.Name}は既に使用されています", position, 2);
                            return false;
                        }
                        break;
                    }
                default:
                    ParserMediator.Warn(LocalizationManager.Error.ExtraCharacterAfterSharp, position, 1);
                    break;
            }
        }
        catch (Exception e)
        {
            ParserMediator.Warn(e.Message, position, 2);
            return false;
        }
        return true;
    }

    public static LogicalLine ParseLine(string str, EmueraConsole console)
    {
        ScriptPosition? position = new();
        CharStream stream = new(str);
        return ParseLine(stream, position, console);
    }

    public static LogicalLine ParseLabelLine(CharStream stream, ScriptPosition? position, EmueraConsole console)
    {
        bool isFunction = stream.Current == '@';
        //int lineNo = Position.Value.LineNo;
        string labelName = "";
        string errMes = "";
        try
        {
            int warnLevel = -1;
            stream.ShiftNext();//@か$を除去
            var wc = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
            if (wc.EOL || wc.Current is not IdentifierWord iw)
            {
                return err(position, isFunction, ref labelName, LocalizationManager.Error.InvalidFunc);
            }
            labelName = iw.Code;
            wc.ShiftNext();
            GlobalStatic.IdentifierDictionary.CheckUserLabelName(out errMes, ref warnLevel, isFunction, labelName);
            if (warnLevel >= 0)
            {
                if (warnLevel >= 2)
                    return err(position, isFunction, ref labelName, errMes);
                ParserMediator.Warn(errMes, position, warnLevel);
            }
            if (!isFunction)//$ならこの時点で終了
            {
                if (!wc.EOL)
                    ParserMediator.Warn(LocalizationManager.Error.LabelHasArg, position, 1);
                return new GotoLabelLine(position, labelName);
            }



            //labelName = LexicalAnalyzer.ReadString(stream, StrEndWith.LeftParenthesis_Bracket_Comma_Semicolon);
            //labelName = labelName.Trim();
            //if (Config.Config.IgnoreCase)
            //    labelName = labelName.ToUpper();
            //GlobalStatic.IdentifierDictionary.CheckUserLabelName(ref errMes, ref warnLevel, isFunction, labelName);
            //if(warnLevel >= 0)
            //{
            //    if (warnLevel >= 2)
            //        goto err;
            //    ParserMediator.Warn(errMes, position, warnLevel);
            //}
            //if (!isFunction)//$ならこの時点で終了
            //{
            //    LexicalAnalyzer.SkipWhiteSpace(stream);
            //    if (!stream.EOS)
            //        ParserMediator.Warn("$で始まるラベルに引数が設定されています", position, 1);
            //    return new GotoLabelLine(position, labelName);
            //}

            ////関数名部分に_renameを使えないように変更
            //if (ParserMediator.RenameDic != null && ((stream.ToString().IndexOf("[[") >= 0) && (stream.ToString().IndexOf("]]") >= 0)))
            //{
            //    string line = stream.ToString();
            //    foreach (KeyValuePair<string, string> pair in ParserMediator.RenameDic)
            //        line = line.Replace(pair.Key, pair.Value);
            //    stream = new StringStream(line);
            //}
            //WordCollection wc = null;
            //wc = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
            if (Program.AnalysisMode)
                console.PrintC("@" + labelName, false);
            FunctionLabelLine funclabelLine = new(position, labelName, wc);
            if (IdentifierDictionary.IsEventLabelName(labelName))
            {
                funclabelLine.IsEvent = true;
                funclabelLine.IsSystem = true;
                funclabelLine.Depth = 0;
            }
            else if (IdentifierDictionary.IsSystemLabelName(labelName))
            {
                funclabelLine.IsSystem = true;
                funclabelLine.Depth = 0;
            }
            return funclabelLine;
        }
        catch (CodeEE e)
        {
            errMes = e.Message;
        }
        return err(position, isFunction, ref labelName, errMes);

        static LogicalLine err(ScriptPosition? position, bool isFunction, ref string labelName, string errMes)
        {
            System.Media.SystemSounds.Hand.Play();
            if (isFunction)
            {
                if (labelName.Length == 0)
                    labelName = "<Error>";
                return new InvalidLabelLine(position, labelName, errMes);
            }
            return new InvalidLine(position, errMes);
        }
    }


    public static LogicalLine ParseLine(CharStream stream, ScriptPosition? position, EmueraConsole console, FunctionLabelLine parentLine = null)
    {
        //int lineNo = Position.Value.LineNo;
        string errMes;
        LexicalAnalyzer.SkipWhiteSpace(stream);//先頭のホワイトスペースを読み飛ばす
        if (stream.EOS)
            return null;
        //コメント行かどうかはここに来る前に判定しておく
        try
        {
            #region 前置インクリメント、デクリメント行

            var op = stream.Current;
            if (op == '+' || op == '-')
            {
                WordCollection wc = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.None);
                if (wc.Current is not OperatorWord opWT || opWT.Code != OperatorCode.Increment && opWT.Code != OperatorCode.Decrement)
                {
                    if (op == '+')
                        errMes = "行が\'+\'から始まっていますが、インクリメントではありません";
                    else
                        errMes = "行が\'-\'から始まっていますが、デクリメントではありません";
                    return new InvalidLine(position, errMes);
                }
                wc.ShiftNext();
                //token = EpressionParser.単語一個分取得(wc)
                //token非変数
                //token文字列形
                //token変更不可能
                //if (wc != EOS)
                //
                return new InstructionLine(position, FunctionIdentifier.SETFunction, opWT.Code, wc, null);
            }
            #endregion

            string firstIdentifier = LexicalAnalyzer.ReadFirstIdentifier(stream);
            if (firstIdentifier != null)
            {
                FunctionIdentifier func = GlobalStatic.IdentifierDictionary.GetFunctionIdentifier(firstIdentifier);

                //命令文
                if (func != null)//関数文
                {
                    if (func.Code == FunctionCode.VARI || func.Code == FunctionCode.VARS)
                    {
                        var line = InstructionLine.Create(position, func, stream);
                        line.ParentLabelLine = parentLine;
                        var statementsSpan = stream.SubstringROS();
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

                        if (func.Code == FunctionCode.VARI)
                        {
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

                            }

                            var varData = new UserDefinedVariableData
                            {
                                Name = varName,
                                Static = false,
                                Lengths = [.. lengths],
                                Dimension = lengths.Count,
                                TypeIsStr = false
                            };
                            parentLine.AddPrivateVariable(varData);

                            return line;
                        }
                        else if (func.Code == FunctionCode.VARS)
                        {
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

                            }

                            var varData = new UserDefinedVariableData
                            {
                                Name = varName,
                                Static = false,
                                Lengths = [.. lengths],
                                Dimension = lengths.Count,
                                TypeIsStr = true
                            };
                            parentLine.AddPrivateVariable(varData);

                            return line;
                        }
                    }



                    if (stream.EOS) //引数の無い関数
                        return InstructionLine.Create(position, func, stream);
                    var current = stream.Current;
                    if (current != ';' && current != ' ' && current != '\t' && (!Config.Config.SystemAllowFullSpace || current != '　'))
                    {
                        if (current == '　')
                            errMes = "命令で行が始まっていますが、命令の直後に半角スペース・タブ以外の文字が来ています(この警告はシステムオプション「" + Config.Config.GetConfigName(Config.ConfigCode.SystemAllowFullSpace) + "」により無視できます)";
                        else
                            errMes = LocalizationManager.Error.InvalidCharacterAfterInstruction2;
                        return new InvalidLine(position, errMes)
                        {
                            ParentLabelLine = parentLine
                        };
                    }
                    stream.ShiftNext();
                    return InstructionLine.Create(position, func, stream);
                }
            }
            LexicalAnalyzer.SkipWhiteSpace(stream);
            if (stream.EOS)
            {
                errMes = LocalizationManager.Error.CanNotInterpretedLine;
                return new InvalidLine(position, errMes);
            }
            //命令行ではない→代入行のはず
            stream.Seek(0, System.IO.SeekOrigin.Begin);
            OperatorCode assignOP = OperatorCode.NULL;
            WordCollection wc1 = LexicalAnalyzer.Analyse(stream, LexEndWith.Operator, LexAnalyzeFlag.None);
            //if (idWT != null)
            //	wc1.Collection.Insert(0, idWT);
            try
            {
                assignOP = LexicalAnalyzer.ReadAssignmentOperator(stream);
            }
            catch (CodeEE)
            {
                errMes = LocalizationManager.Error.CanNotInterpretedLine;
                return new InvalidLine(position, errMes);
            }
            //eramaker互換警告
            //stream.Jump(-1);
            //if ((stream.Current != ' ') && (stream.Current != '\t'))
            //{
            //	errMes = "変数で行が始まっていますが、演算子の直前に半角スペースまたはタブがありません";
            //	goto err;
            //}
            //stream.ShiftNext();


            if (assignOP == OperatorCode.Equal)
            {
                if (console != null)
                    ParserMediator.Warn("代入演算子に\"==\"が使われています", position, 0);
                //"=="を代入文に使うのは本当はおかしいが結構使われているので仕様にする
                assignOP = OperatorCode.Assignment;
            }
            return new InstructionLine(position, FunctionIdentifier.SETFunction, assignOP, wc1, stream);
        }
        catch (CodeEE e)
        {
            System.Media.SystemSounds.Hand.Play();
            return new InvalidLine(position, e.Message);
        }
    }

}

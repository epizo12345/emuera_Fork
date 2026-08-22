using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
using MinorShift.Emuera.UI.Framework;

namespace MinorShift.Emuera.Runtime.Script.Loader;

internal sealed class ErhLoader
{
    public ErhLoader(EmueraConsole main, IdentifierDictionary idDic, Process proc)
    {
        output = main;
        parentProcess = proc;
        this.idDic = idDic;
    }
    readonly Process parentProcess;
    readonly EmueraConsole output;
    readonly IdentifierDictionary idDic;

    bool noError = true;
    Queue<DimLineWC> dimlines;


    /// <summary>
    /// 
    /// </summary>
    /// <param name="erbDir"></param>
    /// <param name="displayReport"></param>
    /// <returns></returns>
    public bool LoadHeaderFiles(string headerDir, bool displayReport)
    {
        List<KeyValuePair<string, string>> headerFiles = Config.Config.GetFiles(headerDir, "*.ERH");
        bool noError = true;
        dimlines = new Queue<DimLineWC>();
        try
        {
            foreach (var (filename, file) in headerFiles)
            {
                if (displayReport)
                    output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.LoadingFile, filename));
                noError = loadHeaderFile(file, filename);
                if (!noError)
                    break;
            }
            //エラーが起きてる場合でも読み込めてる分だけはチェックする
            if (dimlines.Count > 0)
            {
                //&=でないと、ここで起きたエラーをキャッチできない
                noError &= analyzeSharpDimLines();
            }

            dimlines.Clear();
        }
        finally
        {
            ParserMediator.FlushWarningList();
        }
        return noError;
    }


    private bool loadHeaderFile(string filepath, string filename)
    {
        CharStream st;
        ScriptPosition? position = null;
        //EraStreamReader eReader = new EraStreamReader(false);
        //1815修正 _rename.csvの適用
        //eramakerEXの仕様的には.ERHに適用するのはおかしいけど、もうEmueraの仕様になっちゃってるのでしかたないか
        using var eReader = new EraStreamReader(true);

        if (!eReader.OpenOnCache(filepath, filename))
        {
            throw new CodeEE(string.Format(LocalizationManager.Error.FailedOpenFile, eReader.Filename));
            //return false;
        }
        try
        {
            while ((st = eReader.ReadEnabledLine()) != null)
            {
                if (!noError)
                    return false;
                position = new ScriptPosition(eReader.FileId, eReader.LineNo);
                LexicalAnalyzer.SkipWhiteSpace(st);
                if (st.Current != '#')
                    throw new CodeEE(LocalizationManager.Error.NotStartedSharpLineInHeader, position);
                st.ShiftNext();
                var sharpID = LexicalAnalyzer.ReadSingleIdentifierROS(st);
                if (sharpID.IsEmpty)
                {
                    ParserMediator.Warn(LocalizationManager.Error.CanNotInterpretSharpLine, position, 1);
                    return false;
                }
                LexicalAnalyzer.SkipWhiteSpace(st);
                switch (sharpID)
                {
                    case var s when s.Equals("DEFINE", Config.Config.StringComparison):
                        analyzeSharpDefine(st, position);
                        break;
                    case var s when s.Equals("FUNCTION", Config.Config.StringComparison) ||
                                    s.Equals("FUNCTIONS", Config.Config.StringComparison):
                        analyzeSharpFunction(st, position, sharpID == "FUNCTIONS");
                        break;
                    case var s when s.Equals("DIM", Config.Config.StringComparison) ||
                                    s.Equals("DIMS", Config.Config.StringComparison):
                        //1822 #DIMは保留しておいて後でまとめてやる
                        {
                            WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
                            dimlines.Enqueue(new DimLineWC(wc, sharpID.SequenceEqual("DIMS"), false, position));
                        }
                        //analyzeSharpDim(st, position, sharpID == "DIMS");
                        break;
                    default:
                        throw new CodeEE(
                            string.Format(LocalizationManager.Error.UnknownPreprocessorInSharpLine, sharpID.ToString()),
                            position);
                }
            }
        }
        catch (CodeEE e)
        {
            if (e.Position != null)
                position = e.Position;
            ParserMediator.Warn(e.Message, position, 2);
            return false;
        }
        finally
        {
            eReader.Close();
        }
        return true;
    }

    //#define FOO (～～)     id to wc
    //#define BAR($1) (～～)     idwithargs to wc(replaced)
    //#diseble FOOBAR             
    //#dim piyo, i
    //#dims puyo, j
    //static List<string> keywordsList = new List<string>();

    private void analyzeSharpDefine(CharStream st, ScriptPosition? position)
    {
        //LexicalAnalyzer.SkipWhiteSpace(st);呼び出し前に行う。
        string srcID = LexicalAnalyzer.ReadSingleIdentifier(st);
        if (string.IsNullOrEmpty(srcID))
            throw new CodeEE(LocalizationManager.Error.MissingReplacementSource, position);

        //ここで名称重複判定しないと、大変なことになる
        string errMes = "";
        int errLevel = -1;
        idDic.CheckUserMacroName(ref errMes, ref errLevel, srcID);
        if (errLevel >= 0)
        {
            ParserMediator.Warn(errMes, position, errLevel);
            if (errLevel >= 2)
            {
                noError = false;
                return;
            }
        }

        bool hasArg = st.Current == '(';//引数を指定する場合には直後に(が続いていなければならない。ホワイトスペースも禁止。
                                        //1808a3 代入演算子許可（関数宣言用）
        WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
        if (wc.EOL)
        {
            //throw new CodeEE("置換先の式がありません", position);
            //1808a3 空マクロの許可
            DefineMacro nullmac = new(srcID, new WordCollection(), 0);
            idDic.AddMacro(nullmac);
            return;
        }

        List<string> argID = [];
        if (hasArg)//関数型マクロの引数解析
        {
            wc.ShiftNext();//'('を読み飛ばす
            if (wc.Current.Type == ')')
                throw new CodeEE(LocalizationManager.Error.FuncMacroArgIs0, position);
            while (!wc.EOL)
            {
                IdentifierWord word = wc.Current as IdentifierWord;
                if (word == null)
                    throw new CodeEE(LocalizationManager.Error.WrongFormatReplacementSource, position);
                word.SetIsMacro();
                string id = word.Code;
                if (argID.Contains(id))
                    throw new CodeEE(LocalizationManager.Error.DuplicateCharacterReplcaementSource, position);
                argID.Add(id);
                wc.ShiftNext();
                if (wc.Current.Type == ',')
                {
                    wc.ShiftNext();
                    continue;
                }
                if (wc.Current.Type == ')')
                    break;
                throw new CodeEE(LocalizationManager.Error.WrongFormatReplacementSource, position);
            }
            if (wc.EOL)
                throw new CodeEE(LocalizationManager.Error.NotCloseBrackets, position);

            wc.ShiftNext();
        }
        if (wc.EOL)
            throw new CodeEE(LocalizationManager.Error.MissingSubstitution, position);
        WordCollection destWc = new();
        while (!wc.EOL)
        {
            destWc.Add(wc.Current);
            wc.ShiftNext();
        }
        if (hasArg)//関数型マクロの引数セット
        {
            while (!destWc.EOL)
            {
                IdentifierWord word = destWc.Current as IdentifierWord;
                if (word == null)
                {
                    destWc.ShiftNext();
                    continue;
                }
                for (int i = 0; i < argID.Count; i++)
                {
                    if (string.Equals(word.Code, argID[i], Config.Config.StringComparison))
                    {
                        destWc.Remove();
                        destWc.Insert(new MacroWord(i));
                        break;
                    }
                }
                destWc.ShiftNext();
            }
            destWc.PointerReset();
        }
        if (hasArg)//1808a3 関数型マクロの封印
            throw new CodeEE(LocalizationManager.Error.CanNotDeclaredFuncMacro, position);
        DefineMacro mac = new(srcID, destWc, argID.Count);
        idDic.AddMacro(mac);
    }

    //private void analyzeSharpDim(StringStream st, ScriptPosition? position, bool dims)
    //{
    //	//WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
    //	//UserDefinedVariableData data = UserDefinedVariableData.Create(wc, dims, false, position);
    //	//if (data.Reference)
    //	//	throw new NotImplCodeEE();
    //	//VariableToken var = null;
    //	//if (data.CharaData)
    //	//	var = parentProcess.VEvaluator.VariableData.CreateUserDefCharaVariable(data);
    //	//else
    //	//	var = parentProcess.VEvaluator.VariableData.CreateUserDefVariable(data);
    //	//idDic.AddUseDefinedVariable(var);
    //}

    //1822 #DIMだけまとめておいて後で処理
    private bool analyzeSharpDimLines()
    {
        bool noError = true;
        bool tryAgain = true;
        while (dimlines.Count > 0)
        {
            int count = dimlines.Count;
            for (int i = 0; i < count; i++)
            {
                DimLineWC dimline = dimlines.Dequeue();
                try
                {
                    UserDefinedVariableData data = UserDefinedVariableData.Create(dimline);
                    if (data.Reference)
                        throw new NotImplCodeEE();
                    VariableToken var = null;
                    if (data.CharaData)
                        var = parentProcess.VEvaluator.VariableData.CreateUserDefCharaVariable(data);
                    else
                        var = parentProcess.VEvaluator.VariableData.CreateUserDefVariable(data);
                    idDic.AddUseDefinedVariable(var);
                }
                catch (IdentifierNotFoundCodeEE e)
                {
                    //繰り返すことで解決する見込みがあるならキューの最後に追加
                    if (tryAgain)
                    {
                        dimline.WC.PointerReset();
                        dimlines.Enqueue(dimline);
                    }
                    else
                    {
                        ParserMediator.Warn(e.Message, dimline.SC, 2);
                        noError = true;
                    }
                }
                catch (CodeEE e)
                {
                    ParserMediator.Warn(e.Message, dimline.SC, 2);
                    noError = false;
                }
            }
            if (dimlines.Count == count)
                tryAgain = false;
        }
        return noError;
    }

    private static void analyzeSharpFunction(CharStream st, ScriptPosition? position, bool funcs)
    {
        throw new NotImplCodeEE();
        //WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
        //UserDefinedFunctionData data = UserDefinedFunctionData.Create(wc, funcs, position);
        //idDic.AddRefMethod(UserDefinedRefMethod.Create(data));
    }
}

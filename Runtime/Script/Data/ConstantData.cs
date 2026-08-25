using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MinorShift.Emuera.UI.Framework;
using MinorShift.Emuera.Runtime.Config.JSON;
using System.Linq;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

namespace MinorShift.Emuera.Runtime.Script.Data;

internal enum CharacterStrData
{
    NAME = 0,
    CALLNAME = 1,
    NICKNAME = 2,
    MASTERNAME = 3,
    CSTR = 4,
}

internal enum CharacterIntData
{
    BASE = 0,
    ABL = 1,
    TALENT = 2,
    MARK = 3,
    EXP = 4,
    RELATION = 5,
    CFLAG = 6,
    EQUIP = 7,
    JUEL = 8,

}

internal sealed class ConstantData
{

    private const int ablIndex = (int)(VariableCode.ABLNAME & VariableCode.__LOWERCASE__);
    private const int expIndex = (int)(VariableCode.EXPNAME & VariableCode.__LOWERCASE__);
    private const int talentIndex = (int)(VariableCode.TALENTNAME & VariableCode.__LOWERCASE__);
    private const int paramIndex = (int)(VariableCode.PALAMNAME & VariableCode.__LOWERCASE__);
    private const int trainIndex = (int)(VariableCode.TRAINNAME & VariableCode.__LOWERCASE__);
    private const int markIndex = (int)(VariableCode.MARKNAME & VariableCode.__LOWERCASE__);
    private const int itemIndex = (int)(VariableCode.ITEMNAME & VariableCode.__LOWERCASE__);
    private const int baseIndex = (int)(VariableCode.BASENAME & VariableCode.__LOWERCASE__);
    private const int sourceIndex = (int)(VariableCode.SOURCENAME & VariableCode.__LOWERCASE__);
    private const int exIndex = (int)(VariableCode.EXNAME & VariableCode.__LOWERCASE__);
    private const int strIndex = (int)(VariableCode.__DUMMY_STR__ & VariableCode.__LOWERCASE__);
    private const int equipIndex = (int)(VariableCode.EQUIPNAME & VariableCode.__LOWERCASE__);
    private const int tequipIndex = (int)(VariableCode.TEQUIPNAME & VariableCode.__LOWERCASE__);
    private const int flagIndex = (int)(VariableCode.FLAGNAME & VariableCode.__LOWERCASE__);
    private const int tflagIndex = (int)(VariableCode.TFLAGNAME & VariableCode.__LOWERCASE__);
    private const int cflagIndex = (int)(VariableCode.CFLAGNAME & VariableCode.__LOWERCASE__);
    private const int tcvarIndex = (int)(VariableCode.TCVARNAME & VariableCode.__LOWERCASE__);
    private const int cstrIndex = (int)(VariableCode.CSTRNAME & VariableCode.__LOWERCASE__);
    private const int stainIndex = (int)(VariableCode.STAINNAME & VariableCode.__LOWERCASE__);
    private const int cdflag1Index = (int)(VariableCode.CDFLAGNAME1 & VariableCode.__LOWERCASE__);
    private const int cdflag2Index = (int)(VariableCode.CDFLAGNAME2 & VariableCode.__LOWERCASE__);
    private const int strnameIndex = (int)(VariableCode.STRNAME & VariableCode.__LOWERCASE__);
    private const int tstrnameIndex = (int)(VariableCode.TSTRNAME & VariableCode.__LOWERCASE__);
    private const int savestrnameIndex = (int)(VariableCode.SAVESTRNAME & VariableCode.__LOWERCASE__);
    private const int globalIndex = (int)(VariableCode.GLOBALNAME & VariableCode.__LOWERCASE__);
    private const int globalsIndex = (int)(VariableCode.GLOBALSNAME & VariableCode.__LOWERCASE__);
    private const int countNameCsv = (int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__;

    public int[] MaxDataList = new int[countNameCsv];
    readonly HashSet<VariableCode> changedCode = [];

    public int[] VariableIntArrayLength;
    public int[] VariableStrArrayLength;
    public long[] VariableIntArray2DLength;
    public long[] VariableStrArray2DLength;
    public long[] VariableIntArray3DLength;
    public long[] VariableStrArray3DLength;
    public int[] CharacterIntArrayLength;
    public int[] CharacterStrArrayLength;
    public long[] CharacterIntArray2DLength;
    public long[] CharacterStrArray2DLength;

    //private readonly GameBase gamebase;
    private readonly string[][] names = new string[(int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__][];
    private readonly Dictionary<string, int>[] nameToIntDics = new Dictionary<string, int>[(int)VariableCode.__COUNT_CSV_STRING_ARRAY_1D__];
    private readonly Dictionary<string, int> relationDic = [];
    public string[] GetCsvNameList(VariableCode code)
    {
        return names[(int)(code & VariableCode.__LOWERCASE__)];
    }

    public long[] ItemPrice;

    readonly Lock _characterTmplListLock = new();
    private readonly List<CharacterTemplate> CharacterTmplList;
    private Dictionary<string, long> _nameToTemplateMap = new();
    private Dictionary<string, long> _nicknameToTemplateMap = new();
    private Dictionary<string, long> _callnameToTemplateMap = new();
    private Dictionary<string, long> _masternameToTemplateMap = new();
    public ReadOnlyDictionary<string, long> NameToTemplateMap => _nameToTemplateMap.AsReadOnly();
    public ReadOnlyDictionary<string, long> NicknameToTemplateMap => _nicknameToTemplateMap.AsReadOnly();
    public ReadOnlyDictionary<string, long> CallnameToTemplateMap => _callnameToTemplateMap.AsReadOnly();
    public ReadOnlyDictionary<string, long> MasternameToTemplateMap => _masternameToTemplateMap.AsReadOnly();
    private EmueraConsole output;

    public ConstantData()
    {
        //this.gamebase = gamebase;
        setDefaultArrayLength();

        CharacterTmplList = [];
        useCompatiName = Config.Config.CompatiCALLNAME;
    }

    readonly bool useCompatiName;

    private void setDefaultArrayLength()
    {
        MaxDataList[ablIndex] = 100;
        MaxDataList[talentIndex] = 1000;
        MaxDataList[expIndex] = 100;
        MaxDataList[markIndex] = 100;
        MaxDataList[trainIndex] = 1000;
        MaxDataList[paramIndex] = 200;
        MaxDataList[itemIndex] = 1000;
        MaxDataList[baseIndex] = 100;
        MaxDataList[sourceIndex] = 1000;
        MaxDataList[exIndex] = 100;
        MaxDataList[equipIndex] = 100;
        MaxDataList[tequipIndex] = 100;
        MaxDataList[flagIndex] = 10000;
        MaxDataList[tflagIndex] = 1000;
        MaxDataList[cflagIndex] = 1000;
        MaxDataList[tcvarIndex] = 100;
        MaxDataList[cstrIndex] = 100;
        MaxDataList[stainIndex] = 1000;
        MaxDataList[strIndex] = 20000;
        MaxDataList[cdflag1Index] = 1;
        MaxDataList[cdflag2Index] = 1;
        MaxDataList[strnameIndex] = 20000;
        MaxDataList[tstrnameIndex] = 100;
        MaxDataList[savestrnameIndex] = 100;
        MaxDataList[globalIndex] = 1000;
        MaxDataList[globalsIndex] = 100;

        VariableIntArrayLength = new int[(int)VariableCode.__COUNT_INTEGER_ARRAY__];
        VariableStrArrayLength = new int[(int)VariableCode.__COUNT_STRING_ARRAY__];
        VariableIntArray2DLength = new long[(int)VariableCode.__COUNT_INTEGER_ARRAY_2D__];
        VariableStrArray2DLength = [];
        VariableIntArray3DLength = new long[(int)VariableCode.__COUNT_INTEGER_ARRAY_3D__];
        VariableStrArray3DLength = [];
        CharacterIntArrayLength = new int[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY__];
        CharacterStrArrayLength = new int[(int)VariableCode.__COUNT_CHARACTER_STRING_ARRAY__];
        CharacterIntArray2DLength = new long[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY_2D__];
        CharacterStrArray2DLength = [];
        for (int i = 0; i < VariableIntArrayLength.Length; i++)
            VariableIntArrayLength[i] = 1000;
        VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.FLAG)] = 10000;
        VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ITEMPRICE)] = MaxDataList[itemIndex];

        VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.RANDDATA)] = 625;

        for (int i = 0; i < VariableStrArrayLength.Length; i++)
            VariableStrArrayLength[i] = 100;
        VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.STR)] = MaxDataList[strIndex];

        for (int i = 0; i < VariableIntArray2DLength.Length; i++)
            VariableIntArray2DLength[i] = (100L << 32) + 100L;
        for (int i = 0; i < VariableStrArray2DLength.Length; i++)
            VariableStrArray2DLength[i] = (100L << 32) + 100L;

        for (int i = 0; i < VariableIntArray3DLength.Length; i++)
            VariableIntArray3DLength[i] = (100L << 40) + (100L << 20) + 100L;
        for (int i = 0; i < VariableStrArray3DLength.Length; i++)
            VariableStrArray3DLength[i] = (100L << 40) + (100L << 20) + 100L;

        for (int i = 0; i < CharacterIntArrayLength.Length; i++)
            CharacterIntArrayLength[i] = 100;
        CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)] = 1000;
        CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)] = 1000;
        CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)] = 200;
        CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.GOTJUEL)] = 200;

        for (int i = 0; i < CharacterStrArrayLength.Length; i++)
            CharacterStrArrayLength[i] = 100;

        for (int i = 0; i < CharacterIntArray2DLength.Length; i++)
            CharacterIntArray2DLength[i] = (1L << 32) + 1L;
        for (int i = 0; i < CharacterStrArray2DLength.Length; i++)
            CharacterStrArray2DLength[i] = (1L << 32) + 1L;
    }

    private void loadVariableSizeData(string csvPath, bool disp)
    {
        if (!File.Exists(csvPath))
            return;
        using var eReader = new EraStreamReader(false);
        if (!eReader.Open(csvPath))
        {
            output.PrintError(string.Format(LocalizationManager.Error.FailedOpenFile, eReader.Filename));
            return;
        }
        ScriptPosition? position = null;
        if (disp)
            output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.LoadingFile, eReader.Filename));
        try
        {
            CharStream st = null;
            while ((st = eReader.ReadEnabledLine()) != null)
            {
                position = new ScriptPosition(eReader.FileId, eReader.LineNo);
                changeVariableSizeData(st.Substring(), position);
            }
            position = new ScriptPosition(eReader.FileId, -1);
        }
        catch
        {
            System.Media.SystemSounds.Hand.Play();
            if (position != null)
                ParserMediator.Warn(LocalizationManager.Error.UnexpectedError, position, 3);
            else
                output.PrintError(LocalizationManager.Error.UnexpectedError);
            return;
        }
        finally
        {
            eReader.Close();
        }
        decideActualArraySize(position);
    }


    private void changeVariableSizeData(string line, ScriptPosition? position)
    {
        string[] tokens = line.Split(',');
        if (tokens.Length < 2)
        {
            ParserMediator.Warn("\",\"が必要です", position, 1);
            return;
        }
        string idtoken = tokens[0].Trim();
        VariableIdentifier id = VariableIdentifier.GetVariableId(idtoken);
        if (id == null)
        {
            ParserMediator.Warn("一つ目の値を変数名として認識できません", position, 1);
            return;
        }
        if (!id.IsArray1D && !id.IsArray2D && !id.IsArray3D)
        {
            ParserMediator.Warn("配列変数でない変数" + id.ToString() + "のサイズを変更できません", position, 1);
            return;
        }
        if (id.IsCalc || id.Code == VariableCode.RANDDATA)
        {
            ParserMediator.Warn(id.ToString() + "のサイズは変更できません", position, 1);
            return;
        }
        int length2 = 0;
        int length3 = 0;
        if (!int.TryParse(tokens[1], out int length))
        {
            ParserMediator.Warn("二つ目の値を整数値として認識できません", position, 1);
            return;
        }
        //1820a16 変数禁止指定 負の値を指定する
        if (length <= 0)
        {
            if (length == 0)
            {
                ParserMediator.Warn(LocalizationManager.Error.ArrayLengthIs0, position, 2);
                return;
            }
            if (!id.CanForbid)
            {
                ParserMediator.Warn(LocalizationManager.Error.CanNotDisableVarArrayLengthIsNegative, position, 2);
                return;
            }
            if (tokens.Length > 2 && tokens[2].Length > 0 && tokens[2].Trim().Length > 0 && char.IsDigit(tokens[2].Trim()[0]))
            {
                ParserMediator.Warn("一次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
            }
            length = 0;
            goto check1break;
        }
        if (id.IsArray1D)
        {
            if (tokens.Length > 2 && tokens[2].Length > 0 && tokens[2].Trim().Length > 0 && char.IsDigit(tokens[2].Trim()[0]))
            {
                ParserMediator.Warn("一次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
            }
            if (id.IsLocal && length < 1)
            {
                ParserMediator.Warn(LocalizationManager.Error.LocalVarSizeCanNotLessThan1, position, 1);
                return;
            }
            if (!id.IsLocal && length < 100)
            {
                ParserMediator.Warn(LocalizationManager.Error.InternalVarSizeCanNotLessThan100, position, 1);
                return;
            }
            if (length > 1000000)
            {
                ParserMediator.Warn(LocalizationManager.Error.OneDVarSizeCanNotGreaterThan1M, position, 1);
                return;
            }
        }
        else if (id.IsArray2D)
        {
            if (tokens.Length < 3)
            {
                ParserMediator.Warn("二次元配列のサイズ指定には2つの数値が必要です", position, 1);
                return;
            }
            if (tokens.Length > 3 && tokens[3].Length > 0 && tokens[3].Trim().Length > 0 && char.IsDigit(tokens[3].Trim()[0]))
            {
                ParserMediator.Warn("二次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
            }
            if (!int.TryParse(tokens[2], out length2))
            {
                ParserMediator.Warn("三つ目の値を整数値として認識できません", position, 1);
                return;
            }
            if (length < 1 || length2 < 1)
            {
                ParserMediator.Warn(LocalizationManager.Error.VarSizeCanNotLessThan1, position, 1);
                return;
            }
            if (length > 1000000 || length2 > 1000000)
            {
                ParserMediator.Warn(LocalizationManager.Error.VarSizeCanNotGreaterThan1M, position, 1);
                return;
            }
            // Phase 7の配列上限修正: 次元積をintで計算すると上限判定前にoverflowするため、longへ拡張してから制限値と比較する。
            if ((long)length * length2 > 1000000)
            {
                ParserMediator.Warn("二次元配列の要素数は最大で100万個までです", position, 1);
                return;
            }
        }
        else if (id.IsArray3D)
        {
            if (tokens.Length < 4)
            {
                ParserMediator.Warn("三次元配列のサイズ指定には3つの数値が必要です", position, 1);
                return;
            }
            if (tokens.Length > 4 && tokens[4].Length > 0 && tokens[4].Trim().Length > 0 && char.IsDigit(tokens[4].Trim()[0]))
            {
                ParserMediator.Warn("三次元配列のサイズ指定に不必要なデータは無視されます", position, 0);
            }
            if (!int.TryParse(tokens[2], out length2))
            {
                ParserMediator.Warn("三つ目の値を整数値として認識できません", position, 1);
                return;
            }
            if (!int.TryParse(tokens[3], out length3))
            {
                ParserMediator.Warn("四つ目の値を整数値として認識できません", position, 1);
                return;
            }
            if (length < 1 || length2 < 1 || length3 < 1)
            {
                ParserMediator.Warn(LocalizationManager.Error.VarSizeCanNotLessThan1, position, 1);
                return;
            }
            //1802 サイズ保存の都合上、2^20超えるとバグる
            if (length > 1000000 || length2 > 1000000 || length3 > 1000000)
            {
                ParserMediator.Warn(LocalizationManager.Error.VarSizeCanNotGreaterThan1M, position, 1);
                return;
            }
            if ((long)length * length2 * length3 > 10000000)
            {
                ParserMediator.Warn("三次元配列の要素数は最大で1000万個までです", position, 1);
                return;
            }
        }
    check1break:
        switch (id.Code)
        {
            //1753a PALAMだけ仕様が違うのはかえって問題なので、変数と要素文字列配列数の同期は全部バックアウト
            //基本的には旧来の処理に戻しただけ
            case VariableCode.ITEMNAME:
            case VariableCode.ITEMPRICE:
                VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ITEMPRICE)] = length;
                MaxDataList[itemIndex] = length;
                break;
            case VariableCode.STR:
                VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.STR)] = length;
                MaxDataList[strIndex] = length;
                break;
            case VariableCode.ABLNAME:
            case VariableCode.TALENTNAME:
            case VariableCode.EXPNAME:
            case VariableCode.MARKNAME:
            case VariableCode.PALAMNAME:
            case VariableCode.TRAINNAME:
            case VariableCode.BASENAME:
            case VariableCode.SOURCENAME:
            case VariableCode.EXNAME:
            case VariableCode.EQUIPNAME:
            case VariableCode.TEQUIPNAME:
            case VariableCode.FLAGNAME:
            case VariableCode.TFLAGNAME:
            case VariableCode.CFLAGNAME:
            case VariableCode.TCVARNAME:
            case VariableCode.CSTRNAME:
            case VariableCode.STAINNAME:
            case VariableCode.CDFLAGNAME1:
            case VariableCode.CDFLAGNAME2:
            case VariableCode.TSTRNAME:
            case VariableCode.SAVESTRNAME:
            case VariableCode.STRNAME:
            case VariableCode.GLOBALNAME:
            case VariableCode.GLOBALSNAME:
                MaxDataList[(int)(id.Code & VariableCode.__LOWERCASE__)] = length;
                break;
            default:
                {
                    if (id.IsCharacterData)
                    {
                        if (id.IsArray2D)
                        {
                            long length64 = ((long)length << 32) + length2;
                            if (id.IsInteger)
                                CharacterIntArray2DLength[id.CodeInt] = length64;
                            else if (id.IsString)
                                CharacterStrArray2DLength[id.CodeInt] = length64;
                        }
                        else
                        {
                            if (id.IsInteger)
                                CharacterIntArrayLength[id.CodeInt] = length;
                            else if (id.IsString)
                                CharacterStrArrayLength[id.CodeInt] = length;
                        }
                    }
                    else if (id.IsArray2D)
                    {
                        long length64 = ((long)length << 32) + length2;
                        if (id.IsInteger)
                            VariableIntArray2DLength[id.CodeInt] = length64;
                        else if (id.IsString)
                            VariableStrArray2DLength[id.CodeInt] = length64;
                    }
                    else if (id.IsArray3D)
                    {
                        //Int64 length3d = ((Int64)length << 32) + ((Int64)length2 << 16) + (Int64)length3;
                        long length3d = ((long)length << 40) + ((long)length2 << 20) + length3;
                        if (id.IsInteger)
                            VariableIntArray3DLength[id.CodeInt] = length3d;
                        else
                            VariableStrArray3DLength[id.CodeInt] = length3d;
                    }
                    else
                    {
                        if (id.IsInteger)
                            VariableIntArrayLength[id.CodeInt] = length;
                        else if (id.IsString)
                            VariableStrArrayLength[id.CodeInt] = length;
                    }
                }
                break;
        }
        //1803beta004 二重定義を警告対象に
        if (!changedCode.Add(id.Code))
            ParserMediator.Warn(id.Code.ToString() + "の要素数は既に定義されています（上書きします）", position, 1);
    }

    private void _decideActualArraySize_sub(VariableCode mainCode, VariableCode nameCode, int[] arraylength, ScriptPosition? position)
    {
        int nameIndex = (int)(nameCode & VariableCode.__LOWERCASE__);
        int mainLengthIndex = (int)(mainCode & VariableCode.__LOWERCASE__);
        if (changedCode.Contains(nameCode) && changedCode.Contains(mainCode))
        {
            if (MaxDataList[nameIndex] != arraylength[mainLengthIndex])
            {
                int i = Math.Max(MaxDataList[nameIndex], arraylength[mainLengthIndex]);
                arraylength[mainLengthIndex] = i;
                MaxDataList[nameIndex] = i;
                //1803beta004 不適切な指定として警告Lv1の対象にする
                if (MaxDataList[nameIndex] == 0 || arraylength[mainLengthIndex] == 0)
                    ParserMediator.Warn(mainCode.ToString() + "と" + nameCode.ToString() + "の禁止設定が異なります（使用禁止を解除します）", position, 1);
                else
                    ParserMediator.Warn(mainCode.ToString() + "と" + nameCode.ToString() + "の要素数が異なります（大きい方に合わせます）", position, 1);
            }
        }
        else if (changedCode.Contains(nameCode) && !changedCode.Contains(mainCode))
            arraylength[mainLengthIndex] = MaxDataList[nameIndex];
        else if (!changedCode.Contains(nameCode) && changedCode.Contains(mainCode))
            MaxDataList[nameIndex] = arraylength[mainLengthIndex];
    }

    private void decideActualArraySize(ScriptPosition? position)
    {
        _decideActualArraySize_sub(VariableCode.ABL, VariableCode.ABLNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.TALENT, VariableCode.TALENTNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.EXP, VariableCode.EXPNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.MARK, VariableCode.MARKNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.BASE, VariableCode.BASENAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.SOURCE, VariableCode.SOURCENAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.EX, VariableCode.EXNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.EQUIP, VariableCode.EQUIPNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.TEQUIP, VariableCode.TEQUIPNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.FLAG, VariableCode.FLAGNAME, VariableIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.TFLAG, VariableCode.TFLAGNAME, VariableIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.CFLAG, VariableCode.CFLAGNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.TCVAR, VariableCode.TCVARNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.CSTR, VariableCode.CSTRNAME, CharacterStrArrayLength, position);
        _decideActualArraySize_sub(VariableCode.STAIN, VariableCode.STAINNAME, CharacterIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.STR, VariableCode.STRNAME, VariableStrArrayLength, position);
        _decideActualArraySize_sub(VariableCode.TSTR, VariableCode.TSTRNAME, VariableStrArrayLength, position);
        _decideActualArraySize_sub(VariableCode.SAVESTR, VariableCode.SAVESTRNAME, VariableStrArrayLength, position);
        _decideActualArraySize_sub(VariableCode.GLOBAL, VariableCode.GLOBALNAME, VariableIntArrayLength, position);
        _decideActualArraySize_sub(VariableCode.GLOBALS, VariableCode.GLOBALSNAME, VariableStrArrayLength, position);


        //PALAM(JUEL込み)
        //PALAMかJUELが変わっているときは大きい方をとる
        if (changedCode.Contains(VariableCode.PALAM) || changedCode.Contains(VariableCode.JUEL))
        {
            int palamJuelMax = Math.Max(CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)]
                    , CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)]);
            //PALAMNAMEが変わっているなら、それと比較して大きい方を採用
            if (changedCode.Contains(VariableCode.PALAMNAME))
            {
                if (MaxDataList[paramIndex] != palamJuelMax)
                {
                    int i = Math.Max(MaxDataList[paramIndex], palamJuelMax);
                    MaxDataList[paramIndex] = i;
                    if (CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)] == palamJuelMax)
                        CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)] = i;
                    if (CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)] == palamJuelMax)
                        CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)] = i;
                    //1803beta004 不適切な指定として警告Lv1の対象にする
                    ParserMediator.Warn(LocalizationManager.Error.InappropriatePalamJuelPalamname, position, 1);
                }
            }
            else//PALAMNAMEの指定がないなら大きい方にPALAMNAMEをあわせる
                MaxDataList[paramIndex] = palamJuelMax;
        }
        //PALAMとJUEL不変でPALAMNAMEが変わっている場合
        else if (changedCode.Contains(VariableCode.PALAMNAME))
        {
            //PALAMを指定のPALAMNAMEにあわせる
            CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.PALAM)] = MaxDataList[paramIndex];
            //指定のPALAMNAMEがJUELより小さければ警告出してJUELにあわせる
            if (MaxDataList[paramIndex] < CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)])
            {
                ParserMediator.Warn(LocalizationManager.Error.PalamnameSizeLessThanJuelSize, position, 1);
                MaxDataList[paramIndex] = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)];
            }
        }
        //CDFLAG
        //一部変更されたら双方変更されたと扱う
        bool cdflagNameLengthChanged = changedCode.Contains(VariableCode.CDFLAGNAME1) || changedCode.Contains(VariableCode.CDFLAGNAME2);
        int mainLengthIndex = (int)(VariableCode.__LOWERCASE__ & VariableCode.CDFLAG);
        long length64 = CharacterIntArray2DLength[mainLengthIndex];
        int length1 = (int)(length64 >> 32);
        int length2 = (int)(length64 & 0x7FFFFFFF);
        if (changedCode.Contains(VariableCode.CDFLAG) && cdflagNameLengthChanged)
        {
            //調整が面倒なので投げる
            if (length1 != MaxDataList[cdflag1Index] || length2 != MaxDataList[cdflag2Index])
                throw new CodeEE(LocalizationManager.Error.DoesNotMatchCdflagElements, position);
        }
        else if (cdflagNameLengthChanged && !changedCode.Contains(VariableCode.CDFLAG))
        {
            length1 = MaxDataList[cdflag1Index];
            length2 = MaxDataList[cdflag2Index];
            if ((long)length1 * length2 > 1000000)
            {
                //調整が面倒なので投げる
                throw new CodeEE(LocalizationManager.Error.TooManyCdflagElements, position);
            }
            CharacterIntArray2DLength[mainLengthIndex] = ((long)length1 << 32) + length2;
        }
        else if (!cdflagNameLengthChanged && changedCode.Contains(VariableCode.CDFLAG))
        {
            MaxDataList[cdflag1Index] = length1;
            MaxDataList[cdflag2Index] = length2;
        }
        //もう使わないのでデータ破棄
        changedCode.Clear();
    }


    public void LoadData(string csvDir, EmueraConsole console, bool disp)
    {
        var startTime = Stopwatch.GetTimestamp();
        Debug.WriteLine($"start:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");
        output = console;
        loadVariableSizeData(Path.Combine(csvDir, "VariableSize.CSV"), disp);
        Debug.WriteLine($"loadVariableSizeData:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");
        for (int i = 0; i < countNameCsv; i++)
        {
            names[i] = new string[MaxDataList[i]];
            nameToIntDics[i] = [];
        }
        ItemPrice = new long[MaxDataList[itemIndex]];
        Debug.WriteLine($"e1:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");

        loadDataTo(Path.Combine(csvDir, "ABL.CSV"), ablIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "EXP.CSV"), expIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "TALENT.CSV"), talentIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "PALAM.CSV"), paramIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "TRAIN.CSV"), trainIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "MARK.CSV"), markIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "ITEM.CSV"), itemIndex, ItemPrice, disp);
        loadDataTo(Path.Combine(csvDir, "BASE.CSV"), baseIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "SOURCE.CSV"), sourceIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "EX.CSV"), exIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "STR.CSV"), strIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "EQUIP.CSV"), equipIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "TEQUIP.CSV"), tequipIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "FLAG.CSV"), flagIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "TFLAG.CSV"), tflagIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "CFLAG.CSV"), cflagIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "TCVAR.CSV"), tcvarIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "CSTR.CSV"), cstrIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "STAIN.CSV"), stainIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "CDFLAG1.CSV"), cdflag1Index, null, disp);
        loadDataTo(Path.Combine(csvDir, "CDFLAG2.CSV"), cdflag2Index, null, disp);

        loadDataTo(Path.Combine(csvDir, "STRNAME.CSV"), strnameIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "TSTR.CSV"), tstrnameIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "SAVESTR.CSV"), savestrnameIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "GLOBAL.CSV"), globalIndex, null, disp);
        loadDataTo(Path.Combine(csvDir, "GLOBALS.CSV"), globalsIndex, null, disp);
        Debug.WriteLine($"loadDataTo:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");

        //逆引き辞書を作成
        for (int i = 0; i < names.Length; i++)
        {
            if (i == 10)//Strは逆引き無用
                continue;
            string[] nameArray = names[i];
            for (int j = 0; j < nameArray.Length; j++)
            {
                if (!string.IsNullOrEmpty(nameArray[j]) && !nameToIntDics[i].ContainsKey(nameArray[j]))
                    nameToIntDics[i].Add(nameArray[j], j);
            }
        }
        Debug.WriteLine($"Reverse1:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");
        //if (!Program.AnalysisMode)
        loadCharacterData(csvDir, disp);
        Debug.WriteLine($"loadCharacterData:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");

        CharacterTmplList.Sort((left, right) => (int)(left.No - right.No));
        foreach (var t in ((IEnumerable<CharacterTemplate>)CharacterTmplList).Reverse())
        {
            if (t.Name is not null)
                _nameToTemplateMap[t.Name] = t.No;
            if (t.Nickname is not null)
                _nicknameToTemplateMap[t.Nickname] = t.No;
            if (t.Callname is not null)
                _callnameToTemplateMap[t.Callname] = t.No;
            if (t.Mastername is not null)
                _masternameToTemplateMap[t.Mastername] = t.No;
        }
        Debug.WriteLine($"Reverse2:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");

        //逆引き辞書を作成2 (RELATION)
        for (int i = 0; i < CharacterTmplList.Count; i++)
        {
            CharacterTemplate tmpl = CharacterTmplList[i];
            if (!string.IsNullOrEmpty(tmpl.Name) && !relationDic.ContainsKey(tmpl.Name))
                relationDic.Add(tmpl.Name, (int)tmpl.No);
            if (!string.IsNullOrEmpty(tmpl.Callname) && !relationDic.ContainsKey(tmpl.Callname))
                relationDic.Add(tmpl.Callname, (int)tmpl.No);
            if (!string.IsNullOrEmpty(tmpl.Nickname) && !relationDic.ContainsKey(tmpl.Nickname))
                relationDic.Add(tmpl.Nickname, (int)tmpl.No);
            if (!string.IsNullOrEmpty(tmpl.Mastername) && !relationDic.ContainsKey(tmpl.Mastername))
                relationDic.Add(tmpl.Mastername, (int)tmpl.No);
        }
        Debug.WriteLine($"Reverse3:{Stopwatch.GetElapsedTime(startTime).TotalMilliseconds}ms");
    }

    public bool isDefined(VariableCode varCode, string str)
    {
        if (string.IsNullOrEmpty(str))
            return false;
        Dictionary<string, int> dic;
        if (varCode == VariableCode.CDFLAG)
        {
            dic = GetKeywordDictionary(out _, VariableCode.CDFLAGNAME1, -1);
            if (dic == null || !dic.ContainsKey(str))
                dic = GetKeywordDictionary(out _, VariableCode.CDFLAGNAME2, -1);
            if (dic == null)
                return false;
            return dic.ContainsKey(str);
        }
        dic = GetKeywordDictionary(out _, varCode, -1);
        if (dic == null)
            return false;
        return dic.ContainsKey(str);
    }


    public bool TryKeywordToInteger(out int ret, VariableCode code, string key, int index)
    {
        ret = 0;
        if (string.IsNullOrEmpty(key))
            return false;
        Dictionary<string, int> dic;
        try
        {
            dic = GetKeywordDictionary(out string errPos, code, index);
            if (dic == null)
                return false;
        }
        catch { return false; }
        return dic.TryGetValue(key, out ret);
    }

    public int KeywordToInteger(VariableCode code, string key, int index)
    {
        if (string.IsNullOrEmpty(key))
            throw new CodeEE(LocalizationManager.Error.KeywordsCannotBeEmpty);
        Dictionary<string, int> dic = GetKeywordDictionary(out string errPos, code, index);
        if (dic.TryGetValue(key, out int ret))
            return ret;
        if (errPos == null)
            throw new CodeEE("配列変数" + code.ToString() + "の要素を文字列で指定することはできません");
        else
            throw new CodeEE(errPos + "の中に\"" + key + "\"の定義がありません");
    }

    public Dictionary<string, int> GetKeywordDictionary(out string errPos, VariableCode code, int index)
    {
        errPos = null;
        int allowIndex = -1;
        Dictionary<string, int> ret = null;
        switch (code)
        {
            case VariableCode.ABL:
                ret = nameToIntDics[ablIndex];//AblName;
                errPos = "abl.csv";
                allowIndex = 1;
                break;
            case VariableCode.EXP:
                ret = nameToIntDics[expIndex];//ExpName;
                errPos = "exp.csv";
                allowIndex = 1;
                break;
            case VariableCode.TALENT:
                ret = nameToIntDics[talentIndex];//TalentName;
                errPos = "talent.csv";
                allowIndex = 1;
                break;
            case VariableCode.UP:
            case VariableCode.DOWN:
                ret = nameToIntDics[paramIndex];//ParamName　１;
                errPos = "palam.csv";
                allowIndex = 0;
                break;
            case VariableCode.PALAM:
            case VariableCode.JUEL:
            case VariableCode.GOTJUEL:
            case VariableCode.CUP:
            case VariableCode.CDOWN:
                ret = nameToIntDics[paramIndex];//ParamName　２;
                errPos = "palam.csv";
                allowIndex = 1;
                break;

            case VariableCode.TRAINNAME:
                ret = nameToIntDics[trainIndex];//TrainName;
                errPos = "train.csv";
                allowIndex = 0;
                break;
            case VariableCode.MARK:
                ret = nameToIntDics[markIndex];//MarkName;
                errPos = "mark.csv";
                allowIndex = 1;
                break;
            case VariableCode.ITEM:
            case VariableCode.ITEMSALES:
            case VariableCode.ITEMPRICE:
                ret = nameToIntDics[itemIndex];//ItemName;
                errPos = "Item.csv";
                allowIndex = 0;
                break;
            case VariableCode.LOSEBASE:
                ret = nameToIntDics[baseIndex];//BaseName;
                errPos = "base.csv";
                allowIndex = 0;
                break;
            case VariableCode.BASE:
            case VariableCode.MAXBASE:
            case VariableCode.DOWNBASE:
                ret = nameToIntDics[baseIndex];//BaseName;
                errPos = "base.csv";
                allowIndex = 1;
                break;
            case VariableCode.SOURCE:
                ret = nameToIntDics[sourceIndex];//SourceName;
                errPos = "source.csv";
                allowIndex = 1;
                break;
            case VariableCode.EX:
            case VariableCode.NOWEX:
                ret = nameToIntDics[exIndex];//ExName;
                errPos = "ex.csv";
                allowIndex = 1;
                break;


            case VariableCode.EQUIP:
                ret = nameToIntDics[equipIndex];//EquipName;
                errPos = "equip.csv";
                allowIndex = 1;
                break;
            case VariableCode.TEQUIP:
                ret = nameToIntDics[tequipIndex];//TequipName;
                errPos = "tequip.csv";
                allowIndex = 1;
                break;
            case VariableCode.FLAG:
                ret = nameToIntDics[flagIndex];//FlagName;
                errPos = "flag.csv";
                allowIndex = 0;
                break;
            case VariableCode.TFLAG:
                ret = nameToIntDics[tflagIndex];//TFlagName;
                errPos = "tflag.csv";
                allowIndex = 0;
                break;
            case VariableCode.CFLAG:
                ret = nameToIntDics[cflagIndex];//CFlagName;
                errPos = "cflag.csv";
                allowIndex = 1;
                break;
            case VariableCode.TCVAR:
                ret = nameToIntDics[tcvarIndex];//TCVarName;
                errPos = "tcvar.csv";
                allowIndex = 1;
                break;
            case VariableCode.CSTR:
                ret = nameToIntDics[cstrIndex];//CStrName;
                errPos = "cstr.csv";
                allowIndex = 1;
                break;

            case VariableCode.STAIN:
                ret = nameToIntDics[stainIndex];//StainName;
                errPos = "stain.csv";
                allowIndex = 1;
                break;
            case VariableCode.CDFLAGNAME1:
                ret = nameToIntDics[cdflag1Index];
                errPos = "cdflag1.csv";
                allowIndex = 0;
                break;
            case VariableCode.CDFLAGNAME2:
                ret = nameToIntDics[cdflag2Index];
                errPos = "cdflag2.csv";
                allowIndex = 0;
                break;
            case VariableCode.CDFLAG:
                {
                    if (index == 1)
                    {
                        ret = nameToIntDics[cdflag1Index];//CDFlagName1
                        errPos = "cdflag1.csv";
                    }
                    else if (index == 2)
                    {
                        ret = nameToIntDics[cdflag2Index];//CDFlagName2
                        errPos = "cdflag2.csv";
                    }
                    else if (index >= 0)
                        throw new CodeEE("配列変数" + code.ToString() + "の" + (index + 1).ToString() + "番目の要素を文字列で指定することはできません");
                    else
                        throw new CodeEE(LocalizationManager.Error.UseCdflagname);
                    return ret;
                }
            case VariableCode.STR:
                ret = nameToIntDics[strnameIndex];
                errPos = "strname.csv";
                allowIndex = 0;
                break;
            case VariableCode.TSTR:
                ret = nameToIntDics[tstrnameIndex];
                errPos = "tstr.csv";
                allowIndex = 0;
                break;
            case VariableCode.SAVESTR:
                ret = nameToIntDics[savestrnameIndex];
                errPos = "savestr.csv";
                allowIndex = 0;
                break;
            case VariableCode.GLOBAL:
                ret = nameToIntDics[globalIndex];
                errPos = "global.csv";
                allowIndex = 0;
                break;
            case VariableCode.GLOBALS:
                ret = nameToIntDics[globalsIndex];
                errPos = "globals.csv";
                allowIndex = 0;
                break;
            case VariableCode.RELATION:
                ret = relationDic;
                errPos = "chara*.csv";
                allowIndex = 1;
                break;
            case VariableCode.NAME:
            case VariableCode.CALLNAME:
            case VariableCode.NICKNAME:
            case VariableCode.MASTERNAME:
                ret = relationDic;
                errPos = "chara*.csv";
                allowIndex = -1;
                break;

        }
        if (index < 0)
            return ret;
        if (ret == null)
            throw new CodeEE("配列変数" + code.ToString() + "の要素を文字列で指定することはできません");
        if (index != allowIndex)
        {
            if (allowIndex < 0)//GETNUM専用
                throw new CodeEE("配列変数" + code.ToString() + "の要素を文字列で指定することはできません");
            throw new CodeEE("配列変数" + code.ToString() + "の" + (index + 1).ToString() + "番目の要素を文字列で指定することはできません");
        }
        return ret;
    }

    public CharacterTemplate GetCharacterTemplate(long index)
    {
        foreach (CharacterTemplate chara in CharacterTmplList)
        {
            if (chara.No == index)
                return chara;
        }
        return null;
    }

    public CharacterTemplate GetCharacterTemplate_UseSp(long index, bool sp)
    {
        var i = CharacterTmplList.BinarySearch(null, Comparer<CharacterTemplate>.Create((left, right) => (int)(left.No - index)));
        if (i < 0)
        {
            return null;
        }
        return CharacterTmplList[i];
    }

    public CharacterTemplate GetCharacterTemplateFromCsvNo(long index)
    {
        foreach (CharacterTemplate chara in CharacterTmplList)
        {
            if (chara.csvNo != index)
                continue;
            return chara;
        }
        return null;
    }

    public CharacterTemplate GetPseudoChara()
    {
        return new CharacterTemplate(0, this);
    }

    //private CharacterData dummyChara = null;
    //public CharacterData DummyChara
    //{
    //    get { if (dummyChara == null) dummyChara = new CharacterData(GlobalStatic.VEvaluator.Constant, GetPseudoChara(),varData); return dummyChara; }
    //    set { dummyChara = value; }
    //}

    ConcurrentQueue<string> logQueue = [];

    private void loadCharacterData(string csvDir, bool disp)
    {
        if (!Directory.Exists(csvDir))
            return;
        List<KeyValuePair<string, string>> csvPaths = Config.Config.GetFiles(csvDir, "CHARA*.CSV");

        var t1 = Task.Run(() => csvPaths.AsParallel().ForAll(x => loadCharacterDataFile(x.Value, x.Key, disp)));
        var source = new CancellationTokenSource();
        if (disp)
        {
            var locks = new Lock();
            Task.Run(() =>
            {
                while (source.IsCancellationRequested)
                {
                    if (logQueue.TryDequeue(out var log))
                    {
                        lock (locks)
                        {
                            output.PrintSystemLine(log);
                        }
                    }
                }
            }, source.Token);
        }
        t1.Wait();
        source.Cancel();

        if (useCompatiName)
        {
            foreach (CharacterTemplate tmpl in CharacterTmplList)
                if (string.IsNullOrEmpty(tmpl.Callname))
                    tmpl.Callname = tmpl.Name;
        }

        foreach (CharacterTemplate tmpl in CharacterTmplList)
            tmpl.SetSpFlag();

        Dictionary<long, CharacterTemplate> nList = [];
        Dictionary<long, CharacterTemplate> spList = [];
        foreach (CharacterTemplate tmpl in CharacterTmplList)
        {
            Dictionary<long, CharacterTemplate> targetList = nList;
            if (Config.Config.CompatiSPChara && tmpl.IsSpchara)
            {
                targetList = spList;
            }
            if (targetList.TryGetValue(tmpl.No, out CharacterTemplate chara))
            {

                if (!Config.Config.CompatiSPChara && tmpl.IsSpchara != chara.IsSpchara)
                    ParserMediator.Warn("番号" + tmpl.No.ToString() + "のキャラが複数回定義されています(SPキャラとして定義するには互換性オプション「SPキャラを使用する」をONにしてください)", null, 1);
                else
                    ParserMediator.Warn("番号" + tmpl.No.ToString() + "のキャラが複数回定義されています", null, 1);
            }
            else
                targetList.Add(tmpl.No, tmpl);
        }
    }


    private void loadCharacterDataFile(string csvPath, string csvName, bool disp)
    {
        CharacterTemplate tmpl = null;
        using var eReader = new EraStreamReader(JSONConfig.Game.UseRenameInCharaCSV);
        if (!eReader.OpenOnCache(csvPath, csvName))
        {
            output.PrintError(string.Format(LocalizationManager.Error.FailedOpenFile, eReader.Filename));
            return;
        }
        ScriptPosition? position = null;
        try
        {
            long index = -1;
            CharStream st = null;
            while ((st = eReader.ReadEnabledLine()) != null)
            {
                position = new ScriptPosition(eReader.FileId, eReader.LineNo);
                string[] tokens = st.Substring().Split(',');
                if (tokens.Length < 2)
                {
                    ParserMediator.Warn(LocalizationManager.Error.MissingComma, position, 1);
                    continue;
                }
                if (tokens[0].Length == 0)
                {
                    ParserMediator.Warn(LocalizationManager.Error.ProhibitedArrayName, position, 2);
                    continue;
                }
                if (tokens[0].Equals("NO", Config.Config.StringComparison)
                    || tokens[0].Equals("番号", Config.Config.StringComparison))
                {
                    if (tmpl != null)
                    {
                        ParserMediator.Warn(LocalizationManager.Error.CharaNoDefinedTwice, position, 1);
                        continue;
                    }
                    if (!long.TryParse(tokens[1].TrimEnd(), out index))
                    {
                        ParserMediator.Warn(LocalizationManager.Error.FirstValueCanNotConvertToInt, position, 1);
                        continue;
                    }
                    tmpl = new CharacterTemplate(index, this);
                    string no = eReader.Filename;
                    no = no[(no.IndexOf("CHARA", StringComparison.OrdinalIgnoreCase) + 5)..];
                    StringBuilder sb = new();
                    CharStream ss = new(no);
                    while (!ss.EOS && char.IsDigit(ss.Current))
                    {
                        sb.Append(ss.Current);
                        ss.ShiftNext();
                    }
                    if (sb.Length > 0)
                        tmpl.csvNo = long.Parse(sb.ToString());
                    else
                        tmpl.csvNo = 0;
                    //tmpl.csvNo = index;
                    lock (_characterTmplListLock)
                    {
                        CharacterTmplList.Add(tmpl);
                    }
                    continue;
                }

                if (tmpl == null)
                {
                    ParserMediator.Warn(LocalizationManager.Error.StartedDataBeforeCharaNo, position, 1);
                    continue;
                }
                toCharacterTemplate(position, tmpl, tokens);
            }
        }
        catch
        {
            System.Media.SystemSounds.Hand.Play();
            if (position != null)
                ParserMediator.Warn(LocalizationManager.Error.UnexpectedError, position, 3);
            else
                output.PrintError(LocalizationManager.Error.UnexpectedError);
            return;
        }
        finally
        {
            eReader.Dispose();
        }
    }

    private static bool tryToInt64(string str, out long p)
    {
        p = -1;
        if (string.IsNullOrEmpty(str))
            return false;
        CharStream st = new(str);
        int sign = 1;
        if (st.Current == '+')
            st.ShiftNext();
        else if (st.Current == '-')
        {
            sign = -1;
            st.ShiftNext();
        }
        //1803beta005 char.IsDigitは全角数字とかまでひろってしまうので･･･
        //if (!char.IsDigit(st.Current))
        // return false;
        switch (st.Current)
        {
            case '0':
            case '1':
            case '2':
            case '3':
            case '4':
            case '5':
            case '6':
            case '7':
            case '8':
            case '9':
                break;
            default:
                return false;
        }
        try
        {
            p = LexicalAnalyzer.ReadInt64(st, false);
            p *= sign;
        }
        catch
        {
            return false;
        }
        return true;
    }

    private void toCharacterTemplate(ScriptPosition? position, CharacterTemplate chara, string[] tokens)
    {
        if (chara == null)
            return;
        int length;
        Dictionary<int, long> intArray = null;
        Dictionary<int, string> strArray = null;
        Dictionary<string, int> namearray;

        string errPos = null;
        Span<char> chars = stackalloc char[tokens[0].Length];
        var varname = tokens[0].AsSpan().ToUpper(chars, CultureInfo.InvariantCulture);
        switch (chars)
        {
            case "NAME":
            case "名前":
                chara.Name = tokens[1];
                return;
            case "CALLNAME":
            case "呼び名":
                chara.Callname = tokens[1];
                return;
            case "NICKNAME":
            case "あだ名":
                chara.Nickname = tokens[1];
                return;
            case "MASTERNAME":
            case "主人の呼び方":
                chara.Mastername = tokens[1];
                return;
            case "MARK":
            case "刻印":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.MARK)];
                intArray = chara.Mark;
                namearray = nameToIntDics[markIndex];
                errPos = "mark.csv";
                break;
            case "EXP":
            case "経験":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.EXP)];
                intArray = chara.Exp;
                namearray = nameToIntDics[expIndex];//ExpName;
                errPos = "exp.csv";
                break;
            case "ABL":
            case "能力":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ABL)];
                intArray = chara.Abl;
                namearray = nameToIntDics[ablIndex];//AblName;
                errPos = "abl.csv";
                break;
            case "BASE":
            case "基礎":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.MAXBASE)];
                intArray = chara.Maxbase;
                namearray = nameToIntDics[baseIndex];//BaseName;
                errPos = "base.csv";
                break;
            case "TALENT":
            case "素質":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)];
                intArray = chara.Talent;
                namearray = nameToIntDics[talentIndex];//TalentName;
                errPos = "talent.csv";
                break;
            case "RELATION":
            case "相性":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.RELATION)];
                intArray = chara.Relation;
                namearray = null;
                break;
            case "CFLAG":
            case "フラグ":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)];
                intArray = chara.CFlag;
                namearray = nameToIntDics[cflagIndex];//CFlagName;
                errPos = "cflag.csv";
                break;
            case "EQUIP":
            case "装着物":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.EQUIP)];
                intArray = chara.Equip;
                namearray = nameToIntDics[equipIndex];//EquipName;
                errPos = "equip.csv";
                break;
            case "JUEL":
            case "珠":
                length = CharacterIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)];
                intArray = chara.Juel;
                namearray = nameToIntDics[paramIndex];//ParamName;
                errPos = "palam.csv";
                break;
            case "CSTR":
                length = CharacterStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CSTR)];
                strArray = chara.CStr;
                namearray = nameToIntDics[cstrIndex];//CStrName;
                errPos = "cstr.csv";
                break;
            case "ISASSI":
            case "助手":
                return;
            default:
                ParserMediator.Warn(string.Format(LocalizationManager.Error.CanNotInterpreted, tokens[0]), position, 1);
                return;
        }
        if (length < 0)
        {
            ParserMediator.Warn(LocalizationManager.Error.ProgramError, position, 3);
            return;
        }
        if (length == 0)
        {
            ParserMediator.Warn(string.Format(LocalizationManager.Error.IsProhibitedVar, varname), position, 2);
            return;
        }
        bool p1isNumeric = tryToInt64(tokens[1].TrimEnd(), out long p1);
        if (p1isNumeric && (p1 < 0 || p1 >= length))
        {
            ParserMediator.Warn(string.Format(LocalizationManager.Error.OoRArray, p1.ToString()), position, 1);
            return;
        }
        int index = (int)p1;
        if (!p1isNumeric && namearray != null)
        {
            if (!namearray.TryGetValue(tokens[1], out index))
            {
                ParserMediator.Warn(string.Format(LocalizationManager.Error.NotDefinedKey, errPos, tokens[1]), position, 1);
                //ParserMediator.Warn("\"" + tokens[1] + "\"は解釈できない識別子です", position, 1);
                return;
            }
            else if (index >= length)
            {
                ParserMediator.Warn(string.Format(LocalizationManager.Error.OoRArray, tokens[1]), position, 1);
                return;
            }
        }

        if (index < 0 || index >= length)
        {
            if (p1isNumeric)
                ParserMediator.Warn(string.Format(LocalizationManager.Error.OoRArray, index.ToString()), position, 1);
            else if (tokens[1].Length == 0)
                ParserMediator.Warn(LocalizationManager.Error.MissingSecondIdentifier, position, 1);
            else
                ParserMediator.Warn(string.Format(LocalizationManager.Error.CanNotInterpreted, tokens[1]), position, 1);
            return;
        }
        if (strArray != null)
        {
            if (tokens.Length < 3)
                ParserMediator.Warn(LocalizationManager.Error.MissingThirdIdentifier, position, 1);
            if (strArray.ContainsKey(index))
                ParserMediator.Warn(string.Format(LocalizationManager.Error.VarKeyAreadyDefined, varname, index.ToString()), position, 1);
            strArray[index] = tokens[2];
        }
        else
        {
            if (tokens.Length < 3 || !tryToInt64(tokens[2], out long p2))
                p2 = 1;
            if (intArray.ContainsKey(index))
                ParserMediator.Warn(string.Format(LocalizationManager.Error.VarKeyAreadyDefined, varname, index.ToString()), position, 1);
            intArray[index] = p2;
        }
    }


    private void loadDataTo(string csvPath, int targetIndex, long[] targetI, bool disp)
    {

        if (!File.Exists(csvPath))
            return;
        string[] target = names[targetIndex];
        HashSet<int> defined = [];
        using var eReader = new EraStreamReader(false);
        if (!eReader.OpenOnCache(csvPath))
        {
            output.PrintError(string.Format(LocalizationManager.Error.FailedOpenFile, eReader.Filename));
            return;
        }
        ScriptPosition? position = null;

        if (disp || Program.AnalysisMode)
            output.PrintSystemLine(string.Format(LocalizationManager.SystemLine.LoadingFile, eReader.Filename));
        try
        {
            CharStream st = null;
            Span<Range> dest = stackalloc Range[5];
            while ((st = eReader.ReadEnabledLine()) != null)
            {
                position = new ScriptPosition(eReader.FileId, eReader.LineNo);
                var ros = st.SubstringROS();
                var length = ros.Split(dest, [',']);
                if (length < 2)
                {
                    ParserMediator.Warn("\",\"が必要です", position, 1);
                    continue;
                }
                if (!int.TryParse(ros[dest[0]], out int index))
                {
                    ParserMediator.Warn(LocalizationManager.Error.FirstValueCanNotConvertToInt, position, 1);
                    continue;
                }
                if (target.Length == 0)
                {
                    ParserMediator.Warn(LocalizationManager.Error.ProhibitedArrayName, position, 2);
                    break;
                }
                if (index < 0 || target.Length <= index)
                {
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.OoRArray, index.ToString()), position, 1);
                    continue;
                }
                if (!defined.Add(index))
                    ParserMediator.Warn(string.Format(LocalizationManager.Error.VarKeyAreadyDefined, index.ToString()), position, 1);
                target[index] = ros[dest[1]].ToString();
                if (targetI != null && length >= 3)
                {

                    if (!long.TryParse(ros[dest[2]].TrimEnd(), out long price))
                    {
                        ParserMediator.Warn(LocalizationManager.Error.CanNotReadAmountOfMoney, position, 1);
                        continue;
                    }

                    targetI[index] = price;
                }
            }
        }
        catch
        {
            System.Media.SystemSounds.Hand.Play();
            if (position != null)
                ParserMediator.Warn(LocalizationManager.Error.UnexpectedError, position, 3);
            else
                output.PrintError(LocalizationManager.Error.UnexpectedError);
            return;
        }
        finally
        {
            eReader.Close();
        }


    }
}

internal sealed class CharacterTemplate
{
    readonly int[] arraySize;
    readonly int cstrSize;

    public string Name;
    public string Callname;
    public string Nickname;
    public string Mastername;
    public readonly long No;
    public readonly Dictionary<int, long> Maxbase = [];
    public readonly Dictionary<int, long> Mark = [];
    public readonly Dictionary<int, long> Exp = [];
    public readonly Dictionary<int, long> Abl = [];
    public readonly Dictionary<int, long> Talent = [];
    public readonly Dictionary<int, long> Relation = [];
    public readonly Dictionary<int, long> CFlag = [];
    public readonly Dictionary<int, long> Equip = [];
    public readonly Dictionary<int, long> Juel = [];
    public readonly Dictionary<int, string> CStr = [];
    public long csvNo;
    public bool IsSpchara { get; private set; }

    public CharacterTemplate(long index, ConstantData constant)
    {
        arraySize = constant.CharacterIntArrayLength;
        cstrSize = constant.CharacterStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.CSTR)];
        No = index;
    }
    public int ArrayStrLength(CharacterStrData type)
    {
        switch (type)
        {
            case CharacterStrData.CSTR:
                return cstrSize;
            default:
                throw new CodeEE(LocalizationManager.Error.NotExistKey);
        }
    }

    public int ArrayLength(CharacterIntData type)
    {
        switch (type)
        {
            case CharacterIntData.BASE:
                {
                    int size = arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.BASE)];
                    int maxSize = arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.MAXBASE)];
                    return size > maxSize ? size : maxSize;
                }
            case CharacterIntData.MARK:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.MARK)];
            case CharacterIntData.ABL:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.ABL)];
            case CharacterIntData.EXP:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.EXP)];
            case CharacterIntData.RELATION:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.RELATION)];
            case CharacterIntData.TALENT:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.TALENT)];
            case CharacterIntData.CFLAG:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.CFLAG)];
            case CharacterIntData.EQUIP:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.EQUIP)];
            case CharacterIntData.JUEL:
                return arraySize[(int)(VariableCode.__LOWERCASE__ & VariableCode.JUEL)];
            default:
                throw new CodeEE(LocalizationManager.Error.NotExistKey);
        }
    }

    internal void SetSpFlag()
    {
        //bool res;
        if (CFlag.TryGetValue(0, out long value) && value != 0L)
            IsSpchara = true;
    }
}

using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements;

/// <summary>
/// 命令文1行に相当する抽象クラス
/// </summary>
internal abstract class LogicalLine
{
    // [Emuera改修:MEM-13R37 2026-08-22]
    // 同一ERB内のLogicalLineはFilename stringを共有するため、行ごとに参照slotを保持しない。
    // fileId + lineNoだけをsnapshotし、Filenameはerror/warning/reload/source表示時にregistryから復元する。
    protected int scriptFileId;
    protected int scriptLineNo;

    protected void SetPosition(ScriptPosition? position)
    {
        if (position == null || position.Value.Filename == null)
        {
            scriptFileId = 0;
            scriptLineNo = 0;
            return;
        }
        scriptFileId = position.Value.FileId;
        scriptLineNo = position.Value.LineNo;
    }

    //LogicalLine prevLine;
    LogicalLine nextLine;
    public ScriptPosition? Position
    {
        get { return scriptFileId == 0 ? null : new ScriptPosition(scriptFileId, scriptLineNo - 1); }
    }

    public FunctionLabelLine ParentLabelLine { get; set; }
    public LogicalLine NextLine
    {
        get { return nextLine; }
        set { nextLine = value; }
    }
    public override string ToString()
    {
        if (scriptFileId == 0)
            return base.ToString();
        ScriptPosition position = Position.Value;
        return string.Format("{0}:{1}:{2}", position.Filename, position.LineNo, Process.getRawTextFormFilewithLine(position));
    }

    public abstract string ErrMes { get; set; }
    public abstract bool IsError { get; set; }
}

// [Emuera改修:MEM-13R35 2026-08-22]
// InstructionLine以外は従来どおりerror messageを専用slotへ保持する。
// InstructionLineだけはR34のargumentStorageをerror messageと共用し、
// argumentPrimitivePositionのsentinelでraw/Argumentとerrorを区別する。
internal abstract class ErrorCapableLogicalLine : LogicalLine
{
    // [Emuera改修:MEM-13R32 2026-08-22]
    // productionではerror flagとmessageは独立した状態を保持せず、正常行はnull、
    // error行は空文字または実メッセージを持つ。IsError=trueを先に設定する既存の
    // lazy parse / warning / CALL伝播順を受けるため、true setterは空文字sentinelを作る。
    protected string errMes;

    public override string ErrMes
    {
        get { return errMes ?? ""; }
        set { errMes = value; }
    }
    public override bool IsError
    {
        get { return errMes != null; }
        set
        {
            if (value)
                errMes ??= "";
            else
                errMes = null;
        }
    }
}

///// <summary>
///// コメント行。
///// </summary>
//internal sealed class CommentLine : LogicalLine
//{
//    public CommentLine(ScriptPosition? thePosition, string str)
//    {
//        base.position = thePosition;
//        //comment = str;
//    }
//    //string comment;
//    public override bool IsError
//    {
//        get { return false; }
//    }
//}

/// <summary>
/// 無効な行。
/// </summary>
internal sealed class InvalidLine : ErrorCapableLogicalLine
{
    public InvalidLine(ScriptPosition? thePosition, string err)
    {
        SetPosition(thePosition);
        errMes = err;
    }
    public override bool IsError
    {
        get { return true; }
    }
}

/// <summary>
/// 命令文
/// </summary>
internal class InstructionLine : LogicalLine
{
    const int OperatorBits = 20;
    const int OperatorMask = (1 << OperatorBits) - 1;
    const int FunctionCodeShift = OperatorBits;
    const int ErrorArgumentPosition = int.MinValue;

    public InstructionLine(ScriptPosition? thePosition, FunctionIdentifier theFunc, CharStream theArgPrimitive)
    {
        SetPosition(thePosition);
        packedInstructionData = Pack(theFunc.Code, OperatorCode.NULL);
        if (theFunc.Code == FunctionCode.__NULL__)
            auxiliaryData = theFunc;
        // [Emuera改修:MEM-13R34 2026-08-22]
        // lazy行はCharStream object identityを必要とせず、同じsourceとoffsetだけを必要とする。
        // source / offsetをsnapshotし、初回lazy parse時だけCharStreamを復元することで、行がreaderの一時streamをretainedしない。
        argumentStorage = theArgPrimitive?.RowString;
        argumentPrimitivePosition = theArgPrimitive?.CurrentPosition ?? 0;
    }

    public InstructionLine(ScriptPosition? thePosition, FunctionIdentifier functionIdentifier, OperatorCode assignOP, WordCollection dest, CharStream theArgPrimitive)
    {
        SetPosition(thePosition);
        packedInstructionData = Pack(functionIdentifier.Code, assignOP);
        // [Emuera改修:MEM-13R30 2026-08-22]
        // 代入左辺はSET引数解析までだけ必要で、IF/PRINTDATA/TRYCALLLIST/EndCatch用データとは命令種別上共存しない。
        // 遅延引数解析と左辺→右辺の解析順を維持したままauxiliaryDataを一時利用し、全InstructionLineの専用参照slotを持たせない。
        auxiliaryData = dest;
        argumentStorage = theArgPrimitive?.RowString;
        argumentPrimitivePosition = theArgPrimitive?.CurrentPosition ?? 0;
    }
    public static InstructionLine Create(ScriptPosition? thePosition, FunctionIdentifier theFunc, CharStream theArgPrimitive)
    {
        if (theFunc.Code is FunctionCode.FOR or FunctionCode.REPEAT)
            return new LoopInstructionLine(thePosition, theFunc, theArgPrimitive);
        return new InstructionLine(thePosition, theFunc, theArgPrimitive);
    }

    int packedInstructionData;
    object argumentStorage;
    int argumentPrimitivePosition;

    public override string ErrMes
    {
        get => argumentPrimitivePosition == ErrorArgumentPosition ? argumentStorage as string ?? "" : "";
        set
        {
            if (value == null)
            {
                argumentPrimitivePosition = 0;
                argumentStorage = null;
                return;
            }
            argumentPrimitivePosition = ErrorArgumentPosition;
            argumentStorage = value;
        }
    }
    public override bool IsError
    {
        get => argumentPrimitivePosition == ErrorArgumentPosition;
        set
        {
            if (value)
            {
                argumentPrimitivePosition = ErrorArgumentPosition;
                argumentStorage = "";
            }
            else
            {
                argumentPrimitivePosition = 0;
                argumentStorage = null;
            }
        }
    }

    static int Pack(FunctionCode code, OperatorCode assignOperator)
    {
        return ((int)code << FunctionCodeShift) | (int)assignOperator;
    }

    public OperatorCode AssignOperator
    {
        get { return (OperatorCode)((uint)packedInstructionData & OperatorMask); }
    }
    public FunctionCode FunctionCode
    {
        get { return (FunctionCode)((uint)packedInstructionData >> FunctionCodeShift); }
    }
    public FunctionIdentifier Function
    {
        get { return FunctionCode == FunctionCode.__NULL__ ? auxiliaryData as FunctionIdentifier : FunctionIdentifier.GetBuiltIn(FunctionCode); }
    }
    public Argument Argument
    {
        get => argumentStorage as Argument;
        set
        {
            if (value != null)
                argumentStorage = value;
            else if (argumentStorage is Argument)
                argumentStorage = null;
        }
    }
    public CharStream PopArgumentPrimitive()
    {
        if (argumentPrimitivePosition == ErrorArgumentPosition || argumentStorage is not string source)
            return null;
        argumentStorage = null;
        // Popで一度だけ復元・消費し、parse後は従来どおりArgumentを保持する。
        var ret = new CharStream(source) { CurrentPosition = argumentPrimitivePosition };
        return ret;
    }
    public WordCollection PopAssignmentDestStr()
    {
        WordCollection ret = auxiliaryData as WordCollection;
        auxiliaryData = null;
        return ret;
    }

    private LogicalLine jumpto;
    // JumpToEndCatch / IfCaseList / dataList / callList are mutually exclusive by command type.
    // Keep them in one reference slot to reduce the retained size of every InstructionLine.
    private object auxiliaryData;

    //IF文とSELECT文のみが使う。
    public LinkedList<InstructionLine> IfCaseList
    {
        get { return auxiliaryData as LinkedList<InstructionLine>; }
        set { auxiliaryData = value; }
    }
    //PRINTDATA文のみが使う。
    public List<List<InstructionLine>> dataList
    {
        get { return auxiliaryData as List<List<InstructionLine>>; }
        set { auxiliaryData = value; }
    }
    //TRYCALLLIST系が使う
    public List<InstructionLine> callList
    {
        get { return auxiliaryData as List<InstructionLine>; }
        set { auxiliaryData = value; }
    }

    public LogicalLine JumpTo
    {
        get { return jumpto; }
        set { jumpto = value; }
    }

    public LogicalLine JumpToEndCatch
    {
        get { return auxiliaryData as LogicalLine; }
        set { auxiliaryData = value; }
    }

}

// ERB起動時に大量生成される通常InstructionLineへ、FOR/REPEATだけが使うloop stateを持たせない。
// factoryでloop命令だけLoopInstructionLineへ分け、通常命令のlayout/retained sizeとloop semanticsを両立する。
internal sealed class LoopInstructionLine : InstructionLine
{
    public LoopInstructionLine(ScriptPosition? thePosition, FunctionIdentifier theFunc, CharStream theArgPrimitive)
        : base(thePosition, theFunc, theArgPrimitive) { }

    internal long LoopEnd;
    internal VariableTerm LoopCounter;
    internal long LoopStep;
}

/// <summary>
/// ファイルの始端と終端
/// </summary>
internal sealed class NullLine : ErrorCapableLogicalLine { }

/// <summary>
/// ラベルがエラーになっている関数行専用のクラス
/// </summary>
internal sealed class InvalidLabelLine : FunctionLabelLine
{
    public InvalidLabelLine(ScriptPosition? thePosition, string labelname, string err)
    {
        SetPosition(thePosition);
        LabelName = labelname;
        errMes = err;
        IsSingle = false;
        Depth = -1;
        IsMethod = false;
        MethodType = typeof(void);
    }
    public override bool IsError
    {
        get { return true; }
    }
}

/// <summary>
/// @で始まるラベル行
/// </summary>
internal class FunctionLabelLine : ErrorCapableLogicalLine, IComparable<FunctionLabelLine>
{
    protected FunctionLabelLine() { }
    public FunctionLabelLine(ScriptPosition? thePosition, string labelname, WordCollection wc)
    {
        SetPosition(thePosition);
        LabelName = labelname;
        IsSingle = false;
        hasPrivDynamicVar = false;
        Depth = -1;
        LocalLength = 0;
        LocalsLength = 0;
        ArgLength = 0;
        ArgsLength = 0;
        IsMethod = false;
        MethodType = typeof(void);
        this.wc = wc;

        //ArgOptional = true;
        //ArgAutoConvert = true;
    }
    WordCollection wc;
    public WordCollection PopRowArgs()
    {
        WordCollection ret = wc;
        wc = null;
        return ret;
    }

    public string LabelName { get; protected set; }
    public bool IsEvent { get; set; }
    public bool IsSystem { get; set; }
    public bool IsSingle { get; set; }
    public bool IsPri { get; set; }
    public bool IsLater { get; set; }
    public bool IsOnly { get; set; }
    public bool hasPrivDynamicVar { get; set; }
    public int LocalLength { get; set; }
    public int LocalsLength { get; set; }
    public int ArgLength { get; set; }
    public int ArgsLength { get; set; }

    //public bool ArgOptional { get; set; }
    //public bool ArgAutoConvert { get; set; }

    public bool IsMethod { get; set; }
    public Type MethodType { get; set; }
    public VariableTerm[] Arg { get; set; }
    public SingleTerm[] Def { get; set; }
    //public SingleTerm[] SubNames { get; set; }
    public int Depth { get; set; }

    #region IComparable<FunctionLabelLine> メンバ
    //ソート用情報
    public int FileIndex { get; set; }
    public int CompareTo(FunctionLabelLine other)
    {
        if (FileIndex != other.FileIndex)
            return FileIndex.CompareTo(other.FileIndex);
        //position == nullであるLine(デバッグコマンドなど)をSortすることはないはず
        return scriptLineNo.CompareTo(other.scriptLineNo);
    }
    #endregion
    #region private変数
    Dictionary<string, UserDefinedVariableToken> privateVar;
    internal bool AddPrivateVariable(UserDefinedVariableData data)
    {
        privateVar ??= new Dictionary<string, UserDefinedVariableToken>(Config.Config.StrComper);
        if (privateVar.ContainsKey(data.Name))
            return false;
        UserDefinedVariableToken var = GlobalStatic.VariableData.CreatePrivateVariable(data);
        privateVar.Add(data.Name, var);
        //静的な変数のみの場合は関数呼び出し時に何もする必要がない
        if (!data.Static)
            hasPrivDynamicVar = true;
        return true;
    }
    internal UserDefinedVariableToken GetPrivateVariable(string key)
    {
        if (privateVar == null)
            return null;
        privateVar.TryGetValue(key, out UserDefinedVariableToken var);
        return var;
    }

    /// <summary>
    /// 引数の値の確定後、引数の代入より前に呼ぶこと
    /// </summary>
    internal void ScopeIn()
    {
#if DEBUG
        GlobalStatic.StackList.Add(this);
#endif
        if (privateVar == null)
            return;
        foreach (UserDefinedVariableToken var in privateVar.Values)
            if (!var.IsStatic)
                var.ScopeIn();
    }
    internal void ScopeOut()
    {
#if DEBUG
        GlobalStatic.StackList.Remove(this);
#endif
        if (privateVar == null)
            return;
        foreach (UserDefinedVariableToken var in privateVar.Values)
            if (!var.IsStatic)
                var.ScopeOut();
    }
    #endregion

}

/// <summary>
/// $で始まるラベル行
/// </summary>
internal sealed class GotoLabelLine : ErrorCapableLogicalLine, IEqualityComparer<GotoLabelLine>
{
    public GotoLabelLine(ScriptPosition? thePosition, string labelname)
    {
        SetPosition(thePosition);
        this.labelname = labelname;
    }
    readonly string labelname = "";
    public string LabelName
    {
        get { return labelname; }
    }

    #region IEqualityComparer<GotoLabelLine> メンバ

    public bool Equals(GotoLabelLine x, GotoLabelLine y)
    {
        if (x == null || y == null)
            return false;
        return x.ParentLabelLine == y.ParentLabelLine && x.labelname == y.labelname;
    }

    public int GetHashCode(GotoLabelLine obj)
    {
        return labelname.GetHashCode(StringComparison.Ordinal) ^ ParentLabelLine.GetHashCode();
    }

    #endregion
}

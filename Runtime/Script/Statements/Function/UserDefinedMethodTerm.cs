using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;
#if WEB_RUNTIME // R7 direct-call metadata
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.UI.Framework;
#endif // R7 direct-call metadata
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements.Function;

internal abstract class SuperUserDefinedMethodTerm : AExpression
{
    protected SuperUserDefinedMethodTerm(Type returnType)
        : base(returnType)
    {
    }
    public abstract UserDefinedFunctionArgument Argument { get; }
    public abstract CalledFunction Call { get; }
    public override long GetIntValue(ExpressionMediator exm)
    {
        if (exm.Process.GetValue(this) is not SingleLongTerm term)
            return 0;
        return term.Int;
    }
    public override string GetStrValue(ExpressionMediator exm)
    {
        if (exm.Process.GetValue(this) is not SingleStrTerm term)
            return "";
        return term.Str;
    }
    public override SingleTerm GetValue(ExpressionMediator exm)
    {
        SingleTerm term = exm.Process.GetValue(this);
        if (term == null)
        {
            if (GetOperandType() == typeof(long))
                return SingleLongTerm.FromValue(0);
            else
                return SingleStrTerm.FromValue("");
        }
        return term;
    }
}

internal sealed class UserDefinedMethodTerm : SuperUserDefinedMethodTerm
{

    /// <summary>
    /// エラーならnullを返す。
    /// </summary>
#if WEB_RUNTIME // R7 direct-call metadata
    // Webでは静的に解決できる1～32引数のcallsiteだけpacked metadata経路を使い、動的/大きな呼出しとWindowsは従来経路を保つ。
    public static SuperUserDefinedMethodTerm Create(FunctionLabelLine targetLabel, List<AExpression> srcArgs, out string errMes, bool reusableCallsite = true)
#else // R7 direct-call metadata
    public static UserDefinedMethodTerm Create(FunctionLabelLine targetLabel, List<AExpression> srcArgs, out string errMes)
#endif // R7 direct-call metadata
    {
        CalledFunction call = CalledFunction.CreateCalledFunctionMethod(targetLabel, targetLabel.LabelName);
        UserDefinedFunctionArgument arg = call.ConvertArg(srcArgs, out errMes);
        if (arg == null)
            return null;
#if WEB_RUNTIME // R7 direct-call metadata
        if (reusableCallsite && arg.Arguments.Length > 0 && arg.Arguments.Length <= 32)
            return new DirectUserDefinedMethodTerm(arg, call.TopLabel.MethodType, call);
#endif // R7 direct-call metadata
        return new UserDefinedMethodTerm(arg, call.TopLabel.MethodType, call);
    }

    private UserDefinedMethodTerm(UserDefinedFunctionArgument arg, Type returnType, CalledFunction call)
        : base(returnType)
    {
        argment = arg;
        called = call;
    }
    public override UserDefinedFunctionArgument Argument { get { return argment; } }
    public override CalledFunction Call { get { return called; } }
    private readonly UserDefinedFunctionArgument argment;
    private readonly CalledFunction called;

    public override AExpression Restructure(ExpressionMediator exm)
    {
        Argument.Restructure(exm);
        return this;
    }



}
#if WEB_RUNTIME // R7 direct-call metadata
internal sealed class DirectUserDefinedMethodTerm : SuperUserDefinedMethodTerm
{
    private readonly UserDefinedFunctionArgument argment;
    private readonly CalledFunction called;
    public override UserDefinedFunctionArgument Argument => argment;
    public override CalledFunction Call => called;
    public override AExpression Restructure(ExpressionMediator exm) { Argument.Restructure(exm); return this; }
    internal readonly ulong slotKinds;
    internal DirectUserDefinedMethodTerm(UserDefinedFunctionArgument arg, Type returnType, CalledFunction call) : base(returnType)
    {
        argment = arg;
        called = call;
        for (int i = 0; i < arg.Arguments.Length; i++)
        {
            ulong kind = arg.Arguments[i] == null ? 0UL
                : call.TopLabel.Arg[i].Identifier.IsReference ? 3UL
                : call.TopLabel.Arg[i].GetOperandType() == typeof(long) ? 1UL : 2UL;
            slotKinds |= kind << (i * 2);
        }
    }
    internal void SetTransporter(ExpressionMediator exm)
    {
        ulong kinds = slotKinds;
        for (int i = 0; i < argment.Arguments.Length; i++, kinds >>= 2)
        {
            switch ((int)(kinds & 3))
            {
                case 1: argment.TransporterInt[i] = argment.Arguments[i].GetIntValue(exm); break;
                case 2: argment.TransporterStr[i] = argment.Arguments[i].GetStrValue(exm); break;
                case 3:
                    var vTerm = (MinorShift.Emuera.Runtime.Script.Statements.Variable.VariableTerm)argment.Arguments[i];
                    if (vTerm.Identifier.IsCharacterData)
                    {
                        long charaNo = vTerm.GetElementInt(0, exm);
                        if (charaNo < 0 || charaNo >= GlobalStatic.VariableData.CharacterList.Count)
                            throw new CodeEE(string.Format(LocalizationManager.Error.OoRCharaVarArg, vTerm.Identifier.Name, "1", charaNo.ToString()));
                        argment.TransporterRef[i] = (Array)vTerm.Identifier.GetArrayChara((int)charaNo);
                    }
                    else argment.TransporterRef[i] = (Array)vTerm.Identifier.GetArray();
                    break;
            }
        }
    }

    public override long GetIntValue(ExpressionMediator exm)
    {
        if (exm.Process.GetDirectValue(this) is not SingleLongTerm term)
            return 0;
        return term.Int;
    }
    public override string GetStrValue(ExpressionMediator exm)
    {
        if (exm.Process.GetDirectValue(this) is not SingleStrTerm term)
            return "";
        return term.Str;
    }
    public override SingleTerm GetValue(ExpressionMediator exm)
    {
        SingleTerm term = exm.Process.GetDirectValue(this);
        if (term == null)
        {
            if (GetOperandType() == typeof(long))
                return SingleLongTerm.FromValue(0);
            else
                return SingleStrTerm.FromValue("");
        }
        return term;
    }
}
#endif // R7 direct-call metadata
internal sealed class UserDefinedRefMethodTerm : SuperUserDefinedMethodTerm
{
    public UserDefinedRefMethodTerm(UserDefinedRefMethod reffunc, List<AExpression> srcArgs)
        : base(reffunc.RetType)
    {
        this.srcArgs = srcArgs;
        this.reffunc = reffunc;
    }
    List<AExpression> srcArgs;
    readonly UserDefinedRefMethod reffunc;
    public override UserDefinedFunctionArgument Argument
    {
        get
        {
            if (reffunc.CalledFunction == null)
                throw new CodeEE("何も参照していない関数参照" + reffunc.Name + "を呼び出しました");
            UserDefinedFunctionArgument arg = reffunc.CalledFunction.ConvertArg(srcArgs, out string errMes);
            if (arg == null)
                throw new CodeEE(errMes);
            return arg;
        }
    }
    public override CalledFunction Call
    {
        get
        {
            if (reffunc.CalledFunction == null)
                throw new CodeEE("何も参照していない関数参照" + reffunc.Name + "を呼び出しました");
            return reffunc.CalledFunction;
        }
    }

    public override AExpression Restructure(ExpressionMediator exm)
    {
        for (int i = 0; i < srcArgs.Count; i++)
        {
            if ((reffunc.ArgTypeList[i] & UserDifinedFunctionDataArgType.__Ref) == UserDifinedFunctionDataArgType.__Ref)
                srcArgs[i].Restructure(exm);
            else
                srcArgs[i] = srcArgs[i].Restructure(exm);
        }
        return this;
    }


}

internal sealed class UserDefinedRefMethodNoArgTerm : SuperUserDefinedMethodTerm
{
    public UserDefinedRefMethodNoArgTerm(UserDefinedRefMethod reffunc)
        : base(reffunc.RetType)
    {
        this.reffunc = reffunc;
    }
    readonly UserDefinedRefMethod reffunc;
    public override UserDefinedFunctionArgument Argument
    { get { throw new CodeEE("引数のない関数参照" + reffunc.Name + "を呼び出しました"); } }
    public override CalledFunction Call
    { get { throw new CodeEE("引数のない関数参照" + reffunc.Name + "を呼び出しました"); } }
    public override long GetIntValue(ExpressionMediator exm)
    { throw new CodeEE("引数のない関数参照" + reffunc.Name + "を呼び出しました"); }
    public override string GetStrValue(ExpressionMediator exm)
    { throw new CodeEE("引数のない関数参照" + reffunc.Name + "を呼び出しました"); }
    public override SingleTerm GetValue(ExpressionMediator exm)
    { throw new CodeEE("引数のない関数参照" + reffunc.Name + "を呼び出しました"); }
    public override AExpression Restructure(ExpressionMediator exm)
    {
        return this;
    }
}

#if R0_B2
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal static class FlatQueryProof
{
    internal static bool ForbidLegacy;
    internal static int LegacyEntryAttempts;
}
internal readonly record struct QueryValue(long Integer,string? Text)
{
    internal static QueryValue I(long value)=>new(value,null);
    internal static QueryValue S(string? value)=>new(0,value??"");
}
internal enum QueryOp : byte { Integer,String,Copy,LoadCell,StoreCell,ReadHost,Keyword,Unary,Binary,FormatInteger,Jump,False,True,ForTest,ForNext,Call,PrepareBuiltin,Builtin,Return }
internal readonly record struct QueryInstruction(QueryOp Op,int Dest=0,int A=0,int B=0,int C=0,long Immediate=0);
internal readonly record struct QueryParameter(int Cell,bool String,QueryValue Default,bool HasDefault);
internal sealed record QueryHostRead(VariableToken Token,int[] Indices);
internal sealed record QueryStaticCall(int Program,int[] Arguments);
internal enum PureQueryId : byte { GetNum,Match,FindChara,StrCount }
internal sealed record QueryBuiltin(PureQueryId Id,int[] Registers,bool[] Strings,VariableToken? Reference,int[] ReferenceIndices);
internal sealed class QueryProgram
{
    internal required string Key;
    internal required QueryInstruction[] Code;
    internal required string[] Text;
    internal required bool[] RegisterStrings;
    internal required QueryParameter[] Parameters;
    internal required QueryHostRead[] Reads;
    internal required QueryStaticCall[] Calls;
    internal required QueryBuiltin[] Builtins;
    internal required int CellCount;
    internal bool ReturnsString;
    internal FlatMethodProgram? FastLeaf;
    internal int RequiredFrames=1;
}
internal sealed class QueryFrame(int registers,int builtinCount,int fastRegisters)
{
    internal readonly QueryValue[] Values=new QueryValue[registers];
    internal readonly QueryValue[] Arguments=new QueryValue[32];
    internal readonly long[] HostIndices=new long[3];
    internal readonly FlatMethodFrame Fast=new(fastRegisters);
    internal readonly Regex?[] PreparedPatterns=new Regex?[builtinCount];
}

// Caller-owned storage, preallocated at every permitted static-call depth. No shared Legacy method frame.
internal sealed class FlatQueryContext
{
    private readonly QueryProgram[] programs;
    private readonly QueryFrame[] frames;
    private readonly int[] builtinOffsets;
    private readonly ExpressionMediator mediator=GlobalStatic.EMediator;
    // Context-owned cache only; never writes Legacy RegexFactory/global game state.
    // ponytail: bounded 128-pattern memo; uncached patterns remain executable with allocation.
    private readonly Dictionary<string,Regex> patterns=new(StringComparer.Ordinal);
    internal int RemainingSteps;
    internal int MaxObservedDepth;
    internal long CallsExecuted;
    internal long BuiltinsExecuted;
    internal FlatQueryContext(QueryProgram[] programs,int depthLimit=32)
    {
        this.programs=programs;
        int registers=programs.Max(p=>p.RegisterStrings.Length),count=0;
        builtinOffsets=new int[programs.Length];
        for(int i=0;i<programs.Length;i++){builtinOffsets[i]=count;count+=programs[i].Builtins.Length;}
        frames=new QueryFrame[depthLimit];
        int fastRegisters=programs.Max(p=>p.FastLeaf?.RegisterCount??0);
        for(int d=0;d<frames.Length;d++){
            frames[d]=new QueryFrame(registers,count,fastRegisters);
        }
    }
#if R0_E2
    internal long R0E2RetainedEstimate()
    {
        long bytes=96+24+programs.Length*8L+24+builtinOffsets.Length*4L+24+frames.Length*8L+64+patterns.Count*80L;
        foreach(var frame in frames)
            bytes+=96+24+frame.Values.Length*16L+24+frame.Arguments.Length*16L+24+frame.HostIndices.Length*8L
                +24+frame.PreparedPatterns.Length*8L+96+24+frame.Fast.Integers.Length*8L
                +24+frame.Fast.Strings.Length*8L+24+frame.Fast.HostIndices.Length*8L;
        return bytes;
    }
#endif
    internal QueryValue Execute(int id,ReadOnlySpan<QueryValue> arguments,int stepLimit=1_000_000)
    {
        RemainingSteps=stepLimit;
        return Run(id,arguments,0);
    }
    private QueryValue Run(int id,ReadOnlySpan<QueryValue> arguments,int depth)
    {
        if((uint)depth>=(uint)frames.Length)throw new InvalidOperationException("Flat explicit FAIL: CallDepth");
        MaxObservedDepth=Math.Max(MaxObservedDepth,depth);CallsExecuted++;
        var program=programs[id];var frame=frames[depth];var values=frame.Values;
        if(arguments.Length>program.Parameters.Length)throw new InvalidOperationException("Flat explicit FAIL: argument arity");
        for(int i=0;i<arguments.Length;i++)if((arguments[i].Text!=null)!=program.Parameters[i].String)throw new InvalidOperationException("Flat explicit FAIL: argument type");
        for(int i=0;i<program.Parameters.Length;i++){
            var parameter=program.Parameters[i];values[parameter.Cell]=i<arguments.Length?arguments[i]:parameter.Default;
        }
        if(program.FastLeaf!=null)
        {
            // B1's executor stays byte-for-byte unchanged. Reserve its forward-only code's
            // worst-case fuel once, so sibling calls cannot each reuse the same budget.
            // ponytail: conservative fuel may reject a short taken branch at a very low limit;
            // expose actual executed-step count only if exact fuel accounting becomes required.
            int available=RemainingSteps;RemainingSteps-=program.FastLeaf.Code.Length;
            if(RemainingSteps<0)throw new InvalidOperationException("Flat explicit FAIL: StepLimit");
            long arg=0;string args="";
            foreach(var parameter in program.Parameters){if(parameter.String)args=values[parameter.Cell].Text??"";else arg=values[parameter.Cell].Integer;}
            return QueryValue.I(program.FastLeaf.Execute(frame.Fast,arg,args,available));
        }
        int pc=0;
        while((uint)pc<(uint)program.Code.Length){
            if(--RemainingSteps<0)throw new InvalidOperationException("Flat explicit FAIL: StepLimit");
            ref readonly var op=ref program.Code[pc++];
            switch(op.Op){
                case QueryOp.Integer:values[op.Dest]=QueryValue.I(op.Immediate);break;
                case QueryOp.String:values[op.Dest]=QueryValue.S(program.Text[op.A]);break;
                case QueryOp.Copy:case QueryOp.LoadCell:case QueryOp.StoreCell:values[op.Dest]=values[op.A];break;
                case QueryOp.Unary:
                    long a=values[op.A].Integer;
                    values[op.Dest]=QueryValue.I((SemanticOperator)op.B switch{SemanticOperator.Not=>a==0?1:0,SemanticOperator.Minus=>unchecked(-a),SemanticOperator.Plus=>a,_=>throw new InvalidOperationException("Flat explicit FAIL: unary")});break;
                case QueryOp.Binary:
                    var left=values[op.A];var right=values[op.B];var operation=(SemanticOperator)op.C;
                    values[op.Dest]=program.RegisterStrings[op.A]
                        ? operation switch{SemanticOperator.Plus=>QueryValue.S(string.Concat(left.Text,right.Text)),SemanticOperator.Equal=>QueryValue.I(left.Text==right.Text?1:0),SemanticOperator.NotEqual=>QueryValue.I(left.Text!=right.Text?1:0),_=>throw new InvalidOperationException("Flat explicit FAIL: string operator")}
                        : QueryValue.I(operation switch{SemanticOperator.Plus=>unchecked(left.Integer+right.Integer),SemanticOperator.Minus=>unchecked(left.Integer-right.Integer),SemanticOperator.Equal=>left.Integer==right.Integer?1:0,SemanticOperator.NotEqual=>left.Integer!=right.Integer?1:0,SemanticOperator.Less=>left.Integer<right.Integer?1:0,SemanticOperator.Greater=>left.Integer>right.Integer?1:0,SemanticOperator.LessEqual=>left.Integer<=right.Integer?1:0,SemanticOperator.GreaterEqual=>left.Integer>=right.Integer?1:0,_=>throw new InvalidOperationException("Flat explicit FAIL: binary")});break;
                case QueryOp.ReadHost:
                    var read=program.Reads[op.A];
                    for(int i=0;i<read.Indices.Length;i++)frame.HostIndices[i]=values[read.Indices[i]].Integer;
                    read.Token.CheckElement(frame.HostIndices);
                    values[op.Dest]=read.Token.IsInteger?QueryValue.I(read.Token.GetIntValue(mediator,frame.HostIndices)):QueryValue.S(read.Token.GetStrValue(mediator,frame.HostIndices));break;
                case QueryOp.Keyword:
                    // Input-dependent CSV data-key conversion, not function/variable-name dispatch.
                    var token=program.Reads[op.A].Token;
                    values[op.Dest]=QueryValue.I(GlobalStatic.ConstantData.KeywordToInteger(token.Code,values[op.B].Text??"",op.C));break;
                case QueryOp.FormatInteger:values[op.Dest]=QueryValue.S(values[op.A].Integer.ToString());break;
                case QueryOp.Jump:pc=op.Dest;break;
                case QueryOp.False:if(values[op.A].Integer==0)pc=op.Dest;break;
                case QueryOp.True:if(values[op.A].Integer!=0)pc=op.Dest;break;
                case QueryOp.ForTest:
                    long step=values[op.C].Integer,current=values[op.A].Integer,end=values[op.B].Integer;
                    if(!(step>0&&current<end||step<0&&current>end))pc=op.Dest;break;
                case QueryOp.ForNext:values[op.A]=QueryValue.I(unchecked(values[op.A].Integer+values[op.B].Integer));break;
                case QueryOp.Call:
                    var call=program.Calls[op.A];
                    for(int i=0;i<call.Arguments.Length;i++)frame.Arguments[i]=values[call.Arguments[i]];
                    values[op.Dest]=Run(call.Program,frame.Arguments.AsSpan(0,call.Arguments.Length),depth+1);break;
                case QueryOp.PrepareBuiltin:Prepare(program.Builtins[op.A],frame,builtinOffsets[id]+op.A);break;
                case QueryOp.Builtin:BuiltinsExecuted++;values[op.Dest]=QueryValue.I(Builtin(program.Builtins[op.A],frame,builtinOffsets[id]+op.A));break;
                case QueryOp.Return:return values[op.A];
                default:throw new InvalidOperationException("Flat explicit FAIL: unsupported opcode");
            }
        }
        throw new InvalidOperationException("Flat explicit FAIL: missing return");
    }
    private void Prepare(QueryBuiltin b,QueryFrame frame,int slot)
    {
        var v=frame.Values;
        if(b.Id==PureQueryId.StrCount){
            string pattern=v[b.Registers[1]].Text??"";
            if(!patterns.TryGetValue(pattern,out var regex)){
                try{regex=new Regex(pattern,RegexOptions.Compiled);}
                catch(ArgumentException ex){throw new CodeEE("第2引数が正規表現として不正です："+ex.Message);}
                if(patterns.Count<128)patterns.Add(pattern,regex);
            }
            frame.PreparedPatterns[slot]=regex;return;
        }
        if(b.Id==PureQueryId.GetNum)return;
        for(int i=0;i<b.ReferenceIndices.Length;i++)frame.HostIndices[i]=v[b.ReferenceIndices[i]].Integer;
        long start=v[b.Registers[2]].Integer,end=v[b.Registers[3]].Integer;
        if(b.Id==PureQueryId.Match)b.Reference!.IsArrayRangeValid(frame.HostIndices,start,end,"MATCH",3,4);
        else{
            long count=mediator.VEvaluator.CHARANUM;
            if(start<0||start>=count)throw new CodeEE("関数の第3引数("+start+")はキャラクタ位置の範囲外です");
            if(end<0||end>count)throw new CodeEE("関数の第4引数("+end+")はキャラクタ位置の範囲外です");
        }
    }
    private long Builtin(QueryBuiltin b,QueryFrame frame,int slot)
    {
        var v=frame.Values;
        if(b.Id==PureQueryId.GetNum)return GlobalStatic.ConstantData.TryKeywordToInteger(out var found,b.Reference!.Code,v[b.Registers[1]].Text??"",-1)?found:-1;
        if(b.Id==PureQueryId.StrCount)return frame.PreparedPatterns[slot]!.Count(v[b.Registers[0]].Text??"");
        // Restore prefix indices after target-expression nested queries; only frame-owned storage is touched.
        for(int i=0;i<b.ReferenceIndices.Length;i++)frame.HostIndices[i]=v[b.ReferenceIndices[i]].Integer;
        long start=v[b.Registers[2]].Integer,end=v[b.Registers[3]].Integer;
        var token=b.Reference!;
        if(b.Id==PureQueryId.Match){
            var array=(long[])(token.IsCharacterData?token.GetArrayChara((int)frame.HostIndices[0]):token.GetArray());long result=0,target=v[b.Registers[1]].Integer;
            for(int i=(int)start;i<(int)end;i++)if(array[i]==target)result++;
            return result;
        }
        long word=v[b.Registers[1]].Integer;
        for(long i=start;i<end;i++){
            frame.HostIndices[0]=i;
            try{if(token.GetIntValue(mediator,frame.HostIndices)==word)return i;}
            catch(Exception ex)when(ex is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException){token.CheckElement(frame.HostIndices);throw;}
        }
        return -1;
    }
}
#endif

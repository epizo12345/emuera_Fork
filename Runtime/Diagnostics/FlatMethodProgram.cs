#if R0_B1
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Next.Compiler;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.Runtime.Diagnostics;

internal enum MethodControlKind : byte { Test, Jump, Return }
internal readonly record struct MethodControl(MethodControlKind Kind, int Record, int Target);
internal enum FlatOp : byte { Integer, String, Arg, Args, ReadCharacterInteger, EqualInteger, EqualString, LessInteger, BranchFalse, Jump, Return }
internal readonly record struct FlatInstruction(FlatOp Op, int Dest=0, int A=0, int B=0, int Slot=0, long Immediate=0);

// Immutable flat code; scratch belongs to a caller, not to Legacy ProcessState.
internal sealed class FlatMethodFrame(int registerCount)
{
    internal long Arg;
    internal string Args = "";
    internal readonly long[] Integers = new long[registerCount];
    internal readonly string?[] Strings = new string?[registerCount];
    internal readonly long[] HostIndices = new long[2];
}

internal sealed class FlatMethodProgram
{
    internal readonly FlatInstruction[] Code;
    internal readonly string[] Strings;
    internal readonly VariableToken[] ReadSlots;
    internal readonly MethodControl[] Control;
    internal readonly SemanticPayload Expressions;
    internal readonly int RegisterCount;
    private readonly ExpressionMediator mediator;

    private FlatMethodProgram(FlatInstruction[] code, string[] strings, VariableToken[] slots, int registers, MethodControl[] control, SemanticPayload expressions)
    {
        (Code,Strings,ReadSlots,RegisterCount,Control,Expressions)=(code,strings,slots,registers,control,expressions);
        mediator=GlobalStatic.EMediator;
    }

    internal long Execute(FlatMethodFrame frame, long arg, string args, int stepLimit=int.MaxValue)
    {
        frame.Arg=arg; frame.Args=args;
        var integers=frame.Integers; var strings=frame.Strings;
        int pc=0, steps=0;
        while ((uint)pc < (uint)Code.Length)
        {
            if (++steps > stepLimit) throw new InvalidOperationException("Flat explicit FAIL: StepLimit");
            ref readonly var op=ref Code[pc++];
            switch(op.Op)
            {
                case FlatOp.Integer: integers[op.Dest]=op.Immediate; break;
                case FlatOp.String: strings[op.Dest]=Strings[op.Slot]; break;
                case FlatOp.Arg: integers[op.Dest]=frame.Arg; break;
                case FlatOp.Args: strings[op.Dest]=frame.Args; break;
                case FlatOp.ReadCharacterInteger:
                    frame.HostIndices[0]=integers[op.A]; frame.HostIndices[1]=op.Immediate;
                    // Keep native bounds/type behavior. All name/CSV resolution happened at bind time.
                    ReadSlots[op.Slot].CheckElement(frame.HostIndices);
                    integers[op.Dest]=ReadSlots[op.Slot].GetIntValue(mediator,frame.HostIndices);
                    break;
                case FlatOp.EqualInteger: integers[op.Dest]=integers[op.A]==integers[op.B]?1:0; break;
                case FlatOp.EqualString: integers[op.Dest]=strings[op.A]==strings[op.B]?1:0; break;
                case FlatOp.LessInteger: integers[op.Dest]=integers[op.A]<integers[op.B]?1:0; break;
                case FlatOp.BranchFalse: if(integers[op.A]==0) pc=op.Dest; break;
                case FlatOp.Jump: pc=op.Dest; break;
                case FlatOp.Return: return integers[op.A];
                default: throw new InvalidOperationException("Flat explicit FAIL: UnsupportedControl");
            }
        }
        throw new InvalidOperationException("Flat explicit FAIL: missing return");
    }

    internal static FlatMethodProgram Compile(FunctionSource source, CompiledFunction compiled, StructuralSemanticEnvironment environment)
    {
        if(compiled.RuntimeMetadata?.ReturnType!=RuntimeMetadataValueType.Integer) throw new NotSupportedException("NotIntegerMethod");
        var privateNames=compiled.RuntimeMetadata.PrivateVariables.Select(v=>v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variableNames=GlobalStatic.IdentifierDictionary.GetNextRuntimeVariableTokens().Select(v=>v.Key)
            .Concat(GlobalStatic.IdentifierDictionary.GetNextRuntimeLocalVariableFamilies().Select(v=>v.Key))
            .Concat(privateNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // R0's metadata parser uses argument ordinal for bare ARGS. Bind actual bare ARG/ARGS as slot 0.
        // Reject indexed/defaulted signatures here rather than silently inheriting that metadata discrepancy.
        var header=Encoding.UTF8.GetString(source.Bytes).Split('\n')[0].Trim();
        var scan=LegacyIdentifierScanner.ReadFirstIdentifier(header[1..],environment.Compatibility);
        var suffix=header[(1+scan.StopPosition)..].Trim();
        var parameters=suffix.StartsWith('(')&&suffix.EndsWith(')')?suffix[1..^1]:suffix.StartsWith(',')?suffix[1..]:suffix;
        var signature=parameters.Length==0?[]:parameters.Split(',').Select(x=>x.Trim().ToUpperInvariant()).ToArray();
        if(signature.Any(x=>x is not ("ARG" or "ARGS")) || signature.Distinct().Count()!=signature.Length) throw new NotSupportedException("FrameSignature");
        var parts=new List<SemanticPayload>();
        var control=new List<MethodControl>();
        var groups=new Stack<(int Pending,List<int> Ends)>();
        int pendingSif=-1;
        foreach(var instruction in compiled.Instructions)
        {
            var op=instruction.Opcode;
            if(pendingSif>=0 && op!=PrototypeOpcode.RETURNF) throw new NotSupportedException("SifRequiresSingleReturn");
            int Expr()
            {
                var operand=Encoding.UTF8.GetString(source.Bytes,instruction.OperandOffset,instruction.OperandLength);
                parts.Add(SemanticIrCompiler.CompileExpression(operand,environment));
                return parts.Count-1;
            }
            switch(op)
            {
                case PrototypeOpcode.IF:
                    groups.Push((control.Count,new List<int>())); control.Add(new(MethodControlKind.Test,Expr(),-1)); break;
                case PrototypeOpcode.ELSE:
                    if(groups.Count==0) throw new NotSupportedException("UnbalancedIf");
                    var group=groups.Pop();
                    if(group.Pending<0) throw new NotSupportedException("RepeatedElse");
                    group.Ends.Add(control.Count); control.Add(new(MethodControlKind.Jump,-1,-1));
                    control[group.Pending]=control[group.Pending] with{Target=control.Count};
                    groups.Push((-1,group.Ends)); break;
                case PrototypeOpcode.ENDIF:
                    if(groups.Count==0) throw new NotSupportedException("UnbalancedEndIf");
                    var end=groups.Pop();
                    if(end.Pending>=0) control[end.Pending]=control[end.Pending] with{Target=control.Count};
                    foreach(var jump in end.Ends) control[jump]=control[jump] with{Target=control.Count};
                    break;
                case PrototypeOpcode.SIF:
                    pendingSif=control.Count; control.Add(new(MethodControlKind.Test,Expr(),-1)); break;
                case PrototypeOpcode.RETURNF:
                    control.Add(new(MethodControlKind.Return,Expr(),-1));
                    if(pendingSif>=0){control[pendingSif]=control[pendingSif] with{Target=control.Count};pendingSif=-1;}
                    break;
                default: throw new NotSupportedException("Statement:"+op);
            }
        }
        if(groups.Count!=0 || pendingSif>=0) throw new NotSupportedException("UnbalancedControl");
        // Legacy method fall-through returns zero, but retain an explicit terminal instruction.
        parts.Add(SemanticIrCompiler.CompileExpression("0",environment));
        control.Add(new(MethodControlKind.Return,parts.Count-1,-1));
        var arena=SemanticPayload.Merge(parts);
        var code=new List<FlatInstruction>(); var text=new List<string>(); var slots=new List<VariableToken>(); var kinds=new List<bool>();
        var offsets=new int[control.Count]; var patches=new List<int>();
        string Symbol(int index)=>arena.ReadSymbol(arena.Symbols[index]);
        int Register(bool isString){kinds.Add(isString);return kinds.Count-1;}
        int Emit(int nodeIndex)
        {
            var node=arena.Nodes[nodeIndex];
            switch(node.Kind)
            {
                case SemanticNodeKind.IntegerLiteral:
                    if(!long.TryParse(Symbol(node.A),NumberStyles.Integer,CultureInfo.InvariantCulture,out var literal)) throw new NotSupportedException("NonDecimalIntegerLiteral");
                    var integer=Register(false); code.Add(new(FlatOp.Integer,integer,Immediate:literal)); return integer;
                case SemanticNodeKind.StringLiteral:
                    var str=Register(true); text.Add(Symbol(node.A)); code.Add(new(FlatOp.String,str,Slot:text.Count-1));return str;
                case SemanticNodeKind.Symbol:
                    var name=Symbol(node.A).ToUpperInvariant();
                    if(name is not ("ARG" or "ARGS") || !signature.Contains(name) || privateNames.Contains(name)) throw new NotSupportedException("ScalarRead:"+name);
                    var arg=Register(name=="ARGS");code.Add(new(name=="ARGS"?FlatOp.Args:FlatOp.Arg,arg));return arg;
                case SemanticNodeKind.Variable:
                    var variable=Symbol(node.A);
                    if(privateNames.Contains(variable))throw new NotSupportedException("PrivateShadowsHost:"+variable);
                    var token=GlobalStatic.IdentifierDictionary.GetVariableToken(variable,null,false);
                    if(token is null || !token.IsCharacterData || !token.IsInteger || token.Dimension!=1 || node.C!=2 || token.IsReference || token.IsPrivate) throw new NotSupportedException("HostReadShape:"+variable);
                    var index=Emit(arena.Edges[node.B].To);
                    if(kinds[index])throw new NotSupportedException("CharacterIndexNotInteger");
                    var key=arena.Nodes[arena.Edges[node.B+1].To]; long slot;
                    if(key.Kind==SemanticNodeKind.IntegerLiteral && long.TryParse(Symbol(key.A),out slot)) { }
                    else if(key.Kind==SemanticNodeKind.Symbol)
                    {
                        var keyword=Symbol(key.A);
                        if(variableNames.Contains(keyword))throw new NotSupportedException("VariableShadowsCsv:"+keyword);
                        slot=GlobalStatic.ConstantData.KeywordToInteger(token.Code,keyword,1);
                    }
                    // Shared A/B/C subset: the current recursive host does not bind quoted CSV indices.
                    else if(key.Kind==SemanticNodeKind.StringLiteral)throw new NotSupportedException("QuotedCsvNotSharedSubset");
                    else throw new NotSupportedException("DynamicArrayIndex");
                    if(slot<0 || slot>=token.GetLength())throw new NotSupportedException("ArrayIndexRange");
                    int binding=slots.IndexOf(token);if(binding<0){binding=slots.Count;slots.Add(token);}
                    var read=Register(false);code.Add(new(FlatOp.ReadCharacterInteger,read,index,Slot:binding,Immediate:slot));return read;
                case SemanticNodeKind.Binary:
                    if(node.Operator is not (SemanticOperator.Equal or SemanticOperator.Less))throw new NotSupportedException("ExpressionOperator:"+node.Operator);
                    // Compile-time recursion only. Runtime is a forward instruction loop in Legacy left-to-right order.
                    var left=Emit(node.A);var right=Emit(node.B);
                    if(kinds[left]!=kinds[right] || node.Operator==SemanticOperator.Less && kinds[left])throw new NotSupportedException("ComparisonTypes");
                    var result=Register(false);
                    code.Add(new(node.Operator==SemanticOperator.Less?FlatOp.LessInteger:kinds[left]?FlatOp.EqualString:FlatOp.EqualInteger,result,left,right));return result;
                default:throw new NotSupportedException("ExpressionNode:"+node.Kind);
            }
        }
        for(int i=0;i<control.Count;i++)
        {
            offsets[i]=code.Count;var step=control[i];
            if(step.Kind==MethodControlKind.Jump){patches.Add(code.Count);code.Add(new(FlatOp.Jump,step.Target));continue;}
            var value=Emit(arena.Records[step.Record].RootNodeIndex);
            if(kinds[value])throw new NotSupportedException("IntegerControlOrReturnRequired");
            if(step.Kind==MethodControlKind.Return)code.Add(new(FlatOp.Return,A:value));
            else{patches.Add(code.Count);code.Add(new(FlatOp.BranchFalse,step.Target,A:value));}
        }
        foreach(var patch in patches){var target=code[patch].Dest;if((uint)target>=(uint)offsets.Length)throw new NotSupportedException("InvalidBranchTarget");code[patch]=code[patch] with{Dest=offsets[target]};if(code[patch].Dest<=patch)throw new NotSupportedException("NonForwardControl");}
        return new(code.ToArray(),text.ToArray(),slots.ToArray(),kinds.Count,control.ToArray(),arena);
    }
}
#endif

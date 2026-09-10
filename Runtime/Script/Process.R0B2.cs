#if R0_B2
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;
internal sealed partial class Process
{
    private sealed record B2Case(string Name,QueryValue[] Inputs,AExpression Legacy);
    internal object RunB2(string root,string phase)
    {
        var map=new B1HotMap(root);var catalog=new FlatQueryCatalog(map);
        string[] keys=[
            B1HotMap.Key("関数/組み込み関数/キャラクタデータ参照／ABL/HAVE_SKILL.ERB",8,"HAVE_SKILL"),
            B1HotMap.Key("関数/汎用組み込み関数/SPLIT/COUNT_SPLIT.ERB",21,"COUNT_SPLIT")];
        var ids=keys.Select(catalog.Link).ToArray();
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".linked.json"),catalog.Snapshot().Select((p,i)=>new{Id=i,p.Key,FastB1Leaf=p.FastLeaf!=null,Instructions=p.Code.Select((op,pc)=>new{Pc=pc,Opcode=op.Op.ToString(),op.Dest,op.A,op.B,op.C,op.Immediate}),p.CellCount,RegisterCount=p.RegisterStrings.Length,p.Parameters,StaticCallees=p.Calls.Select(c=>catalog.Programs[c.Program]!.Key),Builtins=p.Builtins.Select(b=>b.Id.ToString())}).ToArray());
        var programs=catalog.Snapshot();var context=new FlatQueryContext(programs);
        var nativeScratch=new List<(Array Live,Array Copy)>();
        foreach(var program in programs){
            var entry=map.Functions[program.Key];var label=labelDic.GetNonEventLabel(entry.Function.Name);
            if(label?.Position is not {} position||B1HotMap.Key(position.Filename,position.LineNo,label.LabelName)!=entry.Key)throw new InvalidOperationException("Exact Legacy binding mismatch");
            foreach(var family in new[]{"ARG","ARGS","LOCAL","LOCALS"}){
                var token=idDic.GetNextRuntimeLocalVariableToken(family,label);
                if(token!=null&&token.GetLength()>0){var array=(Array)token.GetArray();nativeScratch.Add((array,(Array)array.Clone()));}
            }
            foreach(var name in entry.Compile!.Function!.RuntimeMetadata!.PrivateVariables.Select(v=>v.Name)){
                var token=label.GetPrivateVariable(name);
                if(token==null)throw new InvalidOperationException("Native private binding missing");
                var array=(Array)token.GetArray();nativeScratch.Add((array,(Array)array.Clone()));
            }
        }
        var oldReturn=state.MethodReturnValue;var oldLine=state.CurrentLine;var oldLines=state.lineCount;
        void Restore(){foreach(var (live,copy) in nativeScratch)Array.Copy(copy,live,copy.Length);state.MethodReturnValue=oldReturn;state.lineCount=oldLines;}
        bool ScratchStable()=>nativeScratch.All(p=>p.Live.Cast<object?>().SequenceEqual(p.Copy.Cast<object?>()))&&ReferenceEquals(oldReturn,state.MethodReturnValue)&&ReferenceEquals(oldLine,state.CurrentLine)&&state.functionCount==0&&methodStack==0;
        var hash=GetBenchmarkStateHash();
        B2Case Case(string name,int probe,params QueryValue[] values){
            var expressions=values.Select(v=>v.Text==null?(AExpression)SingleLongTerm.FromValue(v.Integer):SingleStrTerm.FromValue(v.Text)).ToList();
            var term=idDic.GetFunctionMethod(labelDic,map.Functions[keys[probe]].Function.Name,expressions,userDefinedOnly:true);
            if(term is not SuperUserDefinedMethodTerm)throw new InvalidOperationException("Not real Legacy method");
            return new(name,values,term);
        }
        var abl=idDic.GetVariableToken("ABL",null,false);var cflag=idDic.GetVariableToken("CFLAG",null,false);
        int Slot(VariableToken token,string name)=>GlobalStatic.ConstantData.KeywordToInteger(token.Code,name,1);
        int first=Slot(abl,"スキル1"),equip=Slot(abl,"装備スキル1");
        var have=new List<B2Case>();
        for(int c=0;c<vEvaluator.CHARANUM;c++){
            var array=(long[])abl.GetArrayChara(c);
            var skills=array.Skip(first).Take(20).Concat(array.Skip(equip).Take(22)).Where(v=>v>0).Distinct().Take(3).ToList();
            skills.Add(long.MaxValue);
            foreach(var skill in skills)foreach(long mode in new[]{0L,1L})foreach(var form in new[]{"","通常形態","変身形態"})
                have.Add(Case("real",0,QueryValue.I(c),QueryValue.I(skill),QueryValue.I(mode),QueryValue.S(form)));
        }
        have.Add(Case("default trailing arguments",0,QueryValue.I(0),QueryValue.I(long.MaxValue)));
        have.Add(Case("negative character early return",0,QueryValue.I(-1),QueryValue.I(0)));
        have.Add(Case("implicit ARG/ARGS zero-empty defaults",0));
        var split=new List<B2Case>();
        foreach(var v in new[]{("","/",""),("one","/","one"),("a/b/a","/","a"),("a/b","/","missing"),("/a/","/",""),("日本語/日本/日本語","/","日本語"),("abc","",""),("a.b.a","\\.","a"),("aaaa","a","a")})
            split.Add(Case("string matrix",1,QueryValue.S(v.Item1),QueryValue.S(v.Item2),QueryValue.S(v.Item3)));
        split.Add(Case("default target",1,QueryValue.S("a//b"),QueryValue.S("/")));
        long Flat(int id,B2Case input){
            FlatQueryProof.ForbidLegacy=true;
            try{return context.Execute(id,input.Inputs).Integer;}
            finally{FlatQueryProof.ForbidLegacy=false;}
        }
        var correctness=new List<object>();
        void Compare(int probe,B2Case input,string fixture){
            long legacy=input.Legacy.GetIntValue(exm);Restore();long flat=Flat(ids[probe],input);
            if(legacy!=flat||!ScratchStable())throw new InvalidOperationException("B2 correctness FAIL: "+fixture+"/"+input.Name);
            correctness.Add(new{Probe=probe==0?"HAVE_SKILL":"COUNT_SPLIT",Fixture=fixture,input.Name,input.Inputs,Legacy=legacy,Flat=flat,Match=true,NativeScratchUnchanged=true});
        }
        foreach(var input in have)Compare(0,input,"actual save");
        foreach(var input in split)Compare(1,input,"string matrix");
        if(hash!=GetBenchmarkStateHash())throw new InvalidOperationException("Real correctness changed save state");
        // Isolated in-memory fixture changes, restored before any benchmark. Data/save files are never changed.
        var changed=new List<(VariableToken Token,long Character,long Slot,long Old)>();
        void Set(VariableToken token,int character,int slot,long value){changed.Add((token,character,slot,token.GetIntValue(exm,[character,slot])));token.SetValue(value,[character,slot]);}
        try{
            Set(cflag,0,Slot(cflag,"悪魔変身"),0);
            Set(cflag,0,Slot(cflag,"リンクキャラリアル加入時間0"),737373);
            Set(cflag,1,Slot(cflag,"リアル加入時間"),737373);
            Set(abl,1,first,71717171);
            Set(abl,0,equip,72727272);
            var syntheticHash=GetBenchmarkStateHash();
            foreach(long mode in new[]{0L,1L}){
                Compare(0,Case("link character branch",0,QueryValue.I(0),QueryValue.I(71717171),QueryValue.I(mode),QueryValue.S("変身形態")),"synthetic host state");
                Compare(0,Case("equipment branch",0,QueryValue.I(0),QueryValue.I(72727272),QueryValue.I(mode),QueryValue.S("通常形態")),"synthetic host state");
            }
            if(syntheticHash!=GetBenchmarkStateHash())throw new InvalidOperationException("Candidate changed synthetic state");
            Set(cflag,0,Slot(cflag,"悪魔変身"),1);
            Set(abl,0,Slot(abl,"人間時スキル1"),74747474);
            syntheticHash=GetBenchmarkStateHash();
            foreach(long mode in new[]{0L,1L})
                Compare(0,Case("transformed host normal-form query",0,QueryValue.I(0),QueryValue.I(74747474),QueryValue.I(mode),QueryValue.S("通常形態")),"synthetic host state");
            if(syntheticHash!=GetBenchmarkStateHash())throw new InvalidOperationException("Candidate changed transformed synthetic state");
        }finally{for(int i=changed.Count-1;i>=0;i--){var c=changed[i];c.Token.SetValue(c.Old,[c.Character,c.Slot]);}Restore();}
        if(hash!=GetBenchmarkStateHash()||!ScratchStable())throw new InvalidOperationException("Synthetic fixture restoration failed");
        bool fault=false;FlatQueryProof.ForbidLegacy=true;
        try{context.Execute(ids[0],have[0].Inputs,0);}catch(InvalidOperationException ex)when(ex.Message.Contains("StepLimit")){fault=true;}
        finally{FlatQueryProof.ForbidLegacy=false;}
        if(!fault||FlatQueryProof.LegacyEntryAttempts!=0)throw new InvalidOperationException("Explicit-fault/no-Legacy test failed");
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".generality.json"),B2GeneralTests(map));
        if(hash!=GetBenchmarkStateHash()||!ScratchStable())throw new InvalidOperationException("General tests changed state");
        B2Coverage(map,root,phase);
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".correctness.json"),new{Cases=correctness,StepLimitExplicitFail=fault,StateUnchanged=true,NativeScratchUnchanged=true,FlatQueryProof.LegacyEntryAttempts,context.MaxObservedDepth,context.CallsExecuted,context.BuiltinsExecuted,RecursiveB="BLOCKED: new frame/static-call/builtin order not represented; no compatibility patch added"});
        if(!phase.StartsWith("b2benchmark-",StringComparison.Ordinal))return new{Phase=phase,HAVE_SKILL="PASS",COUNT_SPLIT="PASS",Cases=correctness.Count,ProgramCount=programs.Length};
        var outputs=new List<object>();
        foreach(int probe in new[]{0,1}){
            var inputs=(probe==0?have:split).ToArray();Func<B2Case,long>[] engines=[c=>c.Legacy.GetIntValue(exm),c=>Flat(ids[probe],c)];
            for(int e=0;e<2;e++){B2Batch(engines[e],inputs,200_000);Restore();}
            var raw=new List<object>();long? checksumExpected=null;var timings=new List<double>[] {[],[]};
            for(int round=0;round<6;round++)foreach(int e in (round%2==0?new[]{0,1}:new[]{1,0})){
                if(hash!=GetBenchmarkStateHash()||!ScratchStable())throw new InvalidOperationException("Pre-batch state changed");
                GC.Collect(2,GCCollectionMode.Forced,true,true);GC.WaitForPendingFinalizers();GC.Collect(2,GCCollectionMode.Forced,true,true);
                var gc=new[]{GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2)};
                long a=GC.GetAllocatedBytesForCurrentThread(),all=GC.GetTotalAllocatedBytes(true),start=Stopwatch.GetTimestamp();
                long checksum=B2Batch(engines[e],inputs,1_000_000);long ticks=Stopwatch.GetTimestamp()-start;
                long alloc=GC.GetAllocatedBytesForCurrentThread()-a,processAlloc=GC.GetTotalAllocatedBytes(true)-all;
                var gcDelta=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray();
                bool beforeRestore=ScratchStable();if(e==0)Restore();
                var stateHashAfter=GetBenchmarkStateHash();bool unchanged=hash==stateHashAfter&&ScratchStable();
                checksumExpected??=checksum;
                if(checksum!=checksumExpected||!unchanged||e==1&&!beforeRestore)throw new InvalidOperationException("Timed correctness/state FAIL");
                double ns=ticks*1e9/Stopwatch.Frequency/1_000_000;timings[e].Add(ns);
                raw.Add(new{Round=round+1,Engine=e==0?"Legacy":"Flat",Calls=1_000_000,ElapsedTicks=ticks,NanosecondsPerCall=ns,AllocatedBytes=alloc,AllocationPerCall=alloc/1_000_000.0,ProcessAllocatedBytes=processAlloc,Checksum=checksum,GcCounts=gcDelta,StateUnchanged=unchanged,StateHashBefore=hash,StateHashAfter=stateHashAfter,ScratchUnchangedBeforeRestore=beforeRestore});
            }
            double Median(List<double> values){var sorted=values.Order().ToArray();return (sorted[2]+sorted[3])/2;}
            double legacy=Median(timings[0]),flat=Median(timings[1]);
            var result=new{Probe=probe==0?"HAVE_SKILL":"COUNT_SPLIT",MatrixCases=inputs.Length,LegacyMedianNs=legacy,FlatMedianNs=flat,Speedup=legacy/flat,Raw=raw};
            outputs.Add(result);B1Proof.WriteJson(Path.Combine(root,"raw",phase+"."+result.Probe+".json"),result);
        }
        return new{Phase=phase,Measurements=outputs,ProductionBridgeUsed="NO",FlatQueryProof.LegacyEntryAttempts,B1Proof.BridgeAttempts,WholeProductSuperiority="NOT_YET_CLAIMED"};
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long B2Batch(Func<B2Case,long> engine,B2Case[] inputs,int count){
        long checksum=0;int index=0;
        for(int i=0;i<count;i++){checksum=unchecked(checksum*31+engine(inputs[index]));if(++index==inputs.Length)index=0;}
        return checksum;
    }
}
#endif

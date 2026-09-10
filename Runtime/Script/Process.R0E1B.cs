#if R0_E1B
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed class E1BTrace
    {
        internal readonly List<string> Order = [];
        internal int SideEffect, Depth, MaxDepth;
    }
    private sealed class E1BExpression(Type type, Func<QueryValue> evaluate) : AExpression(type)
    {
        public override long GetIntValue(ExpressionMediator mediator) => evaluate().Integer;
        public override string GetStrValue(ExpressionMediator mediator) => evaluate().Text ?? "";
    }
    private sealed record E1BRow(string Name, bool Pass, long?[] Values, string[] Order,
        int EvaluationCount, int SideEffect, int MaxDepth, string? Fault,
        long FlatAttempts, long FlatCompleted, long FlatFaults, int LegacyRetryAfterFlat = 0);

    internal object RunR0E1BMatrix(bool candidate)
    {
        var owner = candidate ? new CompactRuntimeOwner(this, exm) : null;
        var startup = owner?.CompiledProgramCount ?? -1;
        var oldOptional = GetOption(nameof(RuntimeConfig.CompatiFuncArgOptional));
        var oldConvert = GetOption(nameof(RuntimeConfig.CompatiFuncArgAutoConvert));
        var rows = new List<E1BRow>();

        CompactBoundCallsite Bind(string name, params CompactSourceType[] types)
            => owner!.Bind(owner.Handle(name), owner, this, types);
        QueryValue Invoke(string name, CompactBoundCallsite site, CompactSourceType[] types,
            Func<QueryValue>[] producers, int stepLimit = 1_000_000)
        {
            if (candidate)
            {
                CompactArgumentFrame values = default;
                Span<QueryValue> frame = values;
                for (var i = 0; i < producers.Length; i++)
                {
                    frame[i] = producers[i]();
                    site.EnsureLive(owner!, this);
                }
                try { return site.Execute(owner!, this, frame[..producers.Length], stepLimit); }
                finally { frame.Clear(); }
            }
            var args = producers.Select((producer, i) => (AExpression)new E1BExpression(
                types[i] == CompactSourceType.String ? typeof(string) : typeof(long), producer)).ToList();
            var term = idDic.GetFunctionMethod(labelDic, name, args, true) as UserDefinedMethodTerm
                ?? throw new InvalidOperationException("Legacy oracle method missing: " + name);
            return QueryValue.I(term.GetIntValue(exm));
        }
        E1BRow Observe(string name, Func<E1BTrace, List<long?>> body, Func<E1BTrace, bool> pass)
        {
            var trace = new E1BTrace();
            var a = owner?.Attempts ?? 0; var c = owner?.Completed ?? 0; var f = owner?.Faults ?? 0;
            string? fault = null; List<long?> values;
            try { values = body(trace); }
            catch (Exception ex) { values = [null]; fault = ex.GetType().Name + ": " + ex.Message; }
            var row = new E1BRow(name, pass(trace), values.ToArray(), trace.Order.ToArray(),
                trace.Order.Count, trace.SideEffect, trace.MaxDepth, fault,
                (owner?.Attempts ?? 0) - a, (owner?.Completed ?? 0) - c, (owner?.Faults ?? 0) - f);
            rows.Add(row); return row;
        }
        static Func<QueryValue> I(E1BTrace t, string tag, Func<long> value) => () => { t.Order.Add(tag); return QueryValue.I(value()); };
        static Func<QueryValue> S(E1BTrace t, string tag, Func<string> value) => () => { t.Order.Add(tag); return QueryValue.S(value()); };
        static CompactSourceType[] T(params CompactSourceType[] values) => values;

        try
        {
            SetOption(nameof(RuntimeConfig.CompatiFuncArgOptional), true);
            SetOption(nameof(RuntimeConfig.CompatiFuncArgAutoConvert), true);
            var haveTypes = T(CompactSourceType.Integer, CompactSourceType.Integer, CompactSourceType.Integer, CompactSourceType.String);
            var have = candidate ? Bind("HAVE_SKILL", haveTypes) : default;
            var afterHave = owner?.CompiledProgramCount ?? -1;
            if (candidate) _ = Bind("HAVE_SKILL", haveTypes);
            var repeatedHaveAdditional = candidate ? owner!.CompiledProgramCount - afterHave : -1;

            Observe("01 INT", t => [Invoke("FINDCHARA_TIMEID", candidate ? Bind("FINDCHARA_TIMEID", CompactSourceType.Integer) : default,
                T(CompactSourceType.Integer), [I(t,"0",()=>0)]).Integer], t => t.Order.SequenceEqual(["0"]));
            Observe("02 STR", t => [Invoke("COUNT_SPLIT", candidate ? Bind("COUNT_SPLIT", CompactSourceType.String,CompactSourceType.String,CompactSourceType.String) : default,
                T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String), [S(t,"0",()=>"a/b/a"),S(t,"1",()=>"/"),S(t,"2",()=>"a")]).Integer], t => t.Order.Count==3);
            var afterCount = owner?.CompiledProgramCount ?? -1;
            Observe("03 INT STR mixed", t => [Invoke("HAVE_SKILL", have, haveTypes,
                [I(t,"0",()=>-1),I(t,"1",()=>3),I(t,"2",()=>0),S(t,"3",()=>"")]).Integer], t => t.Order.Count==4);
            Observe("04 omitted int", t => [Invoke("FINDCHARA_TIMEID", candidate ? Bind("FINDCHARA_TIMEID") : default,
                [], []).Integer], t => t.Order.Count==0);
            Observe("05 omitted string", t => [Invoke("CHARA_SKILLCOUNT", candidate ? Bind("CHARA_SKILLCOUNT",CompactSourceType.Integer) : default,
                T(CompactSourceType.Integer), [I(t,"0",()=>0)]).Integer], t => t.Order.Count==1);
            Observe("06 explicit default", t => [Invoke("FINDCHARA_LINK", candidate ? Bind("FINDCHARA_LINK",CompactSourceType.Integer) : default,
                T(CompactSourceType.Integer), [I(t,"0",()=>0)]).Integer], t => t.Order.Count==1);
            Observe("07 INT to STR native TOSTR", t => [Invoke("COUNT_SPLIT", candidate ? Bind("COUNT_SPLIT",CompactSourceType.Integer,CompactSourceType.String,CompactSourceType.String) : default,
                T(CompactSourceType.Integer,CompactSourceType.String,CompactSourceType.String), [I(t,"0",()=>123),S(t,"1",()=>"/"),S(t,"2",()=>"123")]).Integer], t => t.Order.Count==3);
            Observe("08 changing input", t =>
            {
                var site=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                var values=new List<long?>(); for(var i=0;i<4;i++) values.Add(Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),
                    [S(t,"v",()=>++t.SideEffect%2==0?"x/x":"x"),S(t,"d",()=>"/"),S(t,"n",()=>"x")]).Integer); return values;
            }, t => t.Order.Count==12 && t.SideEffect==4);
            Observe("09 left to right", t => [Invoke("HAVE_SKILL",have,haveTypes,[I(t,"0",()=>-1),I(t,"1",()=>0),I(t,"2",()=>0),S(t,"3",()=>"")]).Integer],
                t => t.Order.SequenceEqual(["0","1","2","3"]));
            Observe("10 exactly once", t => [Invoke("HAVE_SKILL",have,haveTypes,[I(t,"0",()=>{t.SideEffect++;return -1;}),I(t,"1",()=>{t.SideEffect++;return 0;}),I(t,"2",()=>{t.SideEffect++;return 0;}),S(t,"3",()=>{t.SideEffect++;return "";})]).Integer],
                t => t.SideEffect==4 && t.Order.Count==4);
            Observe("11 nested eligible", t =>
            {
                var inner=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                var outer=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                return [Invoke("COUNT_SPLIT",outer,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),
                    [S(t,"outer0",()=>"a/b/a"),S(t,"outer1",()=>{t.SideEffect=(int)Invoke("COUNT_SPLIT",inner,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),[S(t,"inner0",()=>"z/z"),S(t,"inner1",()=>"/"),S(t,"inner2",()=>"z")]).Integer;return "/";}),S(t,"outer2",()=>"a")]).Integer];
            }, t => t.SideEffect==1 && t.Order.Count==6);
            Observe("12 nested noneligible oracle equivalent", t =>
            {
                if (candidate)
                {
                    try { _=owner!.Handle("BUST"); } catch (InvalidOperationException) { return [0]; }
                    t.SideEffect++; return [null];
                }
                var term=idDic.GetFunctionMethod(labelDic,"BUST",[SingleLongTerm.FromValue(-1)],true) as UserDefinedMethodTerm;
                return [term?.GetIntValue(exm)];
            }, t => candidate ? t.SideEffect==0 : true);
            Observe("13 middle argument exception", t =>
            {
                var site=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                _=Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),
                    [S(t,"0",()=>{t.SideEffect++;return "a";}),S(t,"1",()=>throw new ArgumentException("synthetic middle")),S(t,"2",()=>{t.SideEffect++;return "a";})]); return [null];
            }, t => t.SideEffect==1 && t.Order.SequenceEqual(["0","1"]));
            if (candidate)
            {
                var invalidationOwner=new CompactRuntimeOwner(this,exm);
                var invalidationSite=invalidationOwner.Bind(invalidationOwner.Handle("CHARA_SKILLCOUNT"),invalidationOwner,this,[CompactSourceType.Integer,CompactSourceType.String]);
                Observe("14 generation invalidation", t =>
                {
                    CompactArgumentFrame storage=default;Span<QueryValue> values=storage;
                    values[0]=I(t,"0",()=>{invalidationOwner.Revoke();return 0;})();
                    invalidationSite.EnsureLive(invalidationOwner,this);
                    values[1]=S(t,"1",()=>"")(); return [invalidationSite.Execute(invalidationOwner,this,values[..2]).Integer];
                }, t => t.Order.SequenceEqual(["0"]));
                var hostSite=Bind("CHARA_SKILLCOUNT",CompactSourceType.Integer,CompactSourceType.String);
                Observe("15 host invalidation", t =>
                {
                    var saved=GlobalStatic.EMediator;
                    try { GlobalStatic.EMediator=null!; var value=I(t,"0",()=>0)(); hostSite.EnsureLive(owner!,this); return [hostSite.Execute(owner!,this,[value,QueryValue.S("")]).Integer]; }
                    finally { GlobalStatic.EMediator=saved; }
                }, t => t.Order.SequenceEqual(["0"]));
            }
            else
            {
                rows.Add(new("14 generation invalidation",true,[null],["0"],1,0,0,"oracle boundary",0,0,0));
                rows.Add(new("15 host invalidation",true,[null],["0"],1,0,0,"oracle boundary",0,0,0));
            }
            Observe("16 RNG consuming argument", t => [Invoke("HAVE_SKILL",have,haveTypes,[I(t,"0",()=>-1),I(t,"1",()=>vEvaluator.GetNextRand(1000)),I(t,"2",()=>vEvaluator.GetNextRand(1000)),S(t,"3",()=>"")]).Integer], t => t.Order.Count==4);
            Observe("17 observable side effect", t => [Invoke("CHARA_SKILLCOUNT",candidate?Bind("CHARA_SKILLCOUNT",CompactSourceType.Integer,CompactSourceType.String):default,
                T(CompactSourceType.Integer,CompactSourceType.String),[I(t,"write",()=>{t.SideEffect=17;return 0;}),S(t,"read",()=>t.SideEffect.ToString())]).Integer], t => t.SideEffect==17&&t.Order.SequenceEqual(["write","read"]));
            Observe("18 reentry 256", t =>
            {
                var site=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                long Nested(int depth){t.Depth++;t.MaxDepth=Math.Max(t.MaxDepth,t.Depth);try{
                    if(!candidate&&depth>0)_=Nested(depth-1);
                    return Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),
                    [S(t,"v"+depth,()=>"a/a"),S(t,"d"+depth,()=>{if(candidate&&depth>0)_=Nested(depth-1);return "/";}),S(t,"n"+depth,()=>"a")]).Integer;}finally{t.Depth--;}}
                return [Nested(255)];
            }, t => t.MaxDepth==256&&t.Depth==0);
            Observe("19 frame cleanup after fault", t =>
            {
                var site=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                try{_=Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),[S(t,"f0",()=>"a/a"),S(t,"f1",()=>throw new ArgumentException("first")),S(t,"f2",()=>"a")]);}catch(ArgumentException){}
                return [Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),[S(t,"s0",()=>"a/a"),S(t,"s1",()=>"/"),S(t,"s2",()=>"a")]).Integer];
            }, t => t.Order.SequenceEqual(["f0","f1","s0","s1","s2"]));
            Observe("20 successful reuse after Flat fault", t =>
            {
                var site=candidate?Bind("COUNT_SPLIT",CompactSourceType.String,CompactSourceType.String,CompactSourceType.String):default;
                if(candidate)try{_=Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),[S(t,"f0",()=>"a/a"),S(t,"f1",()=>"/"),S(t,"f2",()=>"a")],0);}catch(InvalidOperationException){}
                return [Invoke("COUNT_SPLIT",site,T(CompactSourceType.String,CompactSourceType.String,CompactSourceType.String),[S(t,"s0",()=>"a/a"),S(t,"s1",()=>"/"),S(t,"s2",()=>"a")]).Integer];
            }, t => candidate?t.Order.Count==6:t.Order.Count==3);

            object? performance=null;
            if(candidate)
            {
                var directHandle=owner!.Handle("HAVE_SKILL");
                var values=new[]{QueryValue.I(-1),QueryValue.I(0),QueryValue.I(0),QueryValue.S("")};
                for(var i=0;i<1000;i++){_=directHandle.Execute(owner,this,values);_=have.Execute(owner,this,values);}
                const int iterations=20000;var directTicks=new long[5];var boundTicks=new long[5];long checksum=0;
                long Batch(bool bound){var start=Stopwatch.GetTimestamp();for(var i=0;i<iterations;i++)checksum+=bound?have.Execute(owner,this,values).Integer:directHandle.Execute(owner,this,values).Integer;return Stopwatch.GetTimestamp()-start;}
                for(var round=0;round<5;round++)if((round&1)==0){directTicks[round]=Batch(false);boundTicks[round]=Batch(true);}else{boundTicks[round]=Batch(true);directTicks[round]=Batch(false);}
                Array.Sort(directTicks);Array.Sort(boundTicks);
                performance=new{IterationsPerBatch=iterations,Rounds=5,DirectMedianTicks=directTicks[2],BoundMedianTicks=boundTicks[2],
                    BoundOverDirectRatio=(double)boundTicks[2]/directTicks[2],MaterialRegression=(double)boundTicks[2]/directTicks[2]>1.50,Checksum=checksum};
            }

            return new { Schema="emuera-r0e1b-callsite-v1",Mode=candidate?"CompactCandidate":"LegacyControl",
                Rows=rows,AllPassed=rows.Count==20&&rows.All(row=>row.Pass),
                Lazy=new{StartupCompiledProgramCount=startup,AfterHaveCompiledProgramCount=afterHave,
                    RepeatedHaveAdditionalCompiledProgramCount=repeatedHaveAdditional,AfterCountSplitCompiledProgramCount=afterCount},
                Flat=candidate?new{owner!.Attempts,owner.Completed,owner.Faults,LegacyRetryAfterFlat=0,ProductionBridgeAttempts=0}:null,
                Guard=candidate?R0E1AProof.GuardSnapshot():null,
                CallsiteSizeBytes=Unsafe.SizeOf<CompactBoundCallsite>(),RetainedCallsiteCount=5,
                Performance=performance,
                WholeProductSuperiority="NOT_YET_CLAIMED" };
        }
        finally
        {
            SetOption(nameof(RuntimeConfig.CompatiFuncArgOptional),oldOptional);
            SetOption(nameof(RuntimeConfig.CompatiFuncArgAutoConvert),oldConvert);
        }
    }

    internal object RunR0E1BBindNegatives()
    {
        var rows=new List<object>();
        void Reject(string name,Action action,string expected)
        {
            string? fault=null;try{action();}catch(Exception ex){fault=ex.GetType().Name+": "+ex.Message;}
            rows.Add(new{Name=name,Pass=fault?.Contains(expected,StringComparison.OrdinalIgnoreCase)==true,Fault=fault});
        }
        var optional=GetOption(nameof(RuntimeConfig.CompatiFuncArgOptional));
        var convert=GetOption(nameof(RuntimeConfig.CompatiFuncArgAutoConvert));
        try
        {
            SetOption(nameof(RuntimeConfig.CompatiFuncArgOptional),false);
            SetOption(nameof(RuntimeConfig.CompatiFuncArgAutoConvert),false);
            var owner=new CompactRuntimeOwner(this,exm);
            Reject("too many args",()=>owner.Bind(owner.Handle("FINDCHARA_TIMEID"),owner,this,[CompactSourceType.Integer,CompactSourceType.Integer]),"arity");
            Reject("STR to INT",()=>owner.Bind(owner.Handle("FINDCHARA_TIMEID"),owner,this,[CompactSourceType.String]),"STR to INT");
            Reject("REF",()=>owner.Bind(owner.Handle("FINDCHARA_TIMEID"),owner,this,[CompactSourceType.Reference]),"REF");
            Reject("optional disabled",()=>owner.Bind(owner.Handle("CHARA_SKILLCOUNT"),owner,this,[CompactSourceType.Integer]),"omitted");
            Reject("autoconvert disabled",()=>owner.Bind(owner.Handle("COUNT_SPLIT"),owner,this,[CompactSourceType.Integer,CompactSourceType.String,CompactSourceType.String]),"autoconvert");
            Reject("wrong owner",()=>owner.Bind(owner.Handle("FINDCHARA_TIMEID"),null!,this,[CompactSourceType.Integer]),"owner");
            Reject("unresolved metadata",()=>CompactBoundCallsite.Create(default,null,[]),"unresolved metadata");
            Reject("unsupported default",()=>CompactBoundCallsite.Create(default,[new QueryParameter(0,false,QueryValue.I(0),true)],[],false),"unsupported default");
            var stale=new CompactRuntimeOwner(this,exm);var staleHandle=stale.Handle("FINDCHARA_TIMEID");stale.Revoke();
            Reject("stale target",()=>stale.Bind(staleHandle,stale,this,[CompactSourceType.Integer]),"revoked");
            var blocked=new CompactRuntimeOwner(this,exm);blocked.BlockMaterializationForE1BSelfTest("COUNT_SPLIT");var evaluations=0;
            Reject("materialization failure before evaluation",()=>
            {
                _=blocked.Bind(blocked.Handle("COUNT_SPLIT"),blocked,this,[CompactSourceType.String]);
                evaluations++;
            },"Unsupported callee");
            return new{Schema="emuera-r0e1b-bind-negatives-v1",Rows=rows,AllPassed=rows.Count==10&&rows.All(row=>(bool)row.GetType().GetProperty("Pass")!.GetValue(row)!),
                MaterializationFailureArgumentEvaluations=evaluations,Guard=R0E1AProof.GuardSnapshot()};
        }
        finally{SetOption(nameof(RuntimeConfig.CompatiFuncArgOptional),optional);SetOption(nameof(RuntimeConfig.CompatiFuncArgAutoConvert),convert);}
    }

    private static bool GetOption(string name)=>(bool)typeof(RuntimeConfig).GetProperty(name)!.GetValue(null)!;
    private static void SetOption(string name,bool value)=>typeof(RuntimeConfig).GetProperty(name)!.SetValue(null,value);
}
#endif

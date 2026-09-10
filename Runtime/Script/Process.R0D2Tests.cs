#if R0_D2
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Utils;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    private sealed class D2Trace
    {
        internal readonly List<string> Order = [];
        internal int SideEffect, Depth, MaxDepth;
        internal bool Throw = true;
    }
    private sealed class D2Expression(Type type, Func<object> evaluate) : AExpression(type)
    {
        public override long GetIntValue(ExpressionMediator mediator) => (long)evaluate();
        public override string GetStrValue(ExpressionMediator mediator) => (string)evaluate();
    }
    private sealed record D2Scenario(string Name, Func<D2Trace, UserDefinedMethodTerm> Create,
        int Repeats = 1, bool AllowLegacyScratch = false, bool RejectBeforeAttempt = false);

    internal object RunR0D2Matrix(string root, string phase)
    {
        var registry = r0CRegistry ?? throw new InvalidOperationException("D2 registry missing");
        var map = registry.Map;
        var manifest = JsonSerializer.Deserialize<R0CManifest>(File.ReadAllText(Program.R0CRegistryManifestPath!))!;
        var rows = new List<object>();
        var mediator = GlobalStatic.EMediator;
        var scratch = new List<(VariableToken Token, Array Live, Array Copy)>();
        foreach (var label in R0CKeys.Select(key => labelDic.GetNonEventLabel(map.Functions[key].Function.Name)!)
            .Append(labelDic.GetNonEventLabel("BUST")!).Distinct())
        {
            foreach (var token in new[] { "ARG", "ARGS", "LOCAL", "LOCALS" }
                .Select(name => idDic.GetNextRuntimeLocalVariableToken(name, label))
                .Concat(label.GetNextRuntimePrivateVariables().Select(p => (VariableToken)p.Value))
                .Where(t => t is not null).Distinct())
                if (token.GetArray() is Array live) scratch.Add((token, live, (Array)live.Clone()));
        }
        var oldReturn = state.MethodReturnValue; var oldLine = state.CurrentLine; var oldLines = state.lineCount;
        var initialState = GetBenchmarkStateHash(); var initialRng = vEvaluator.GetR0C2RngHash();
        var randomField = vEvaluator.GetType().GetField("rand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldRandom = randomField.GetValue(vEvaluator)!;
        string ScratchHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(scratch.Select(p => p.Live.Cast<object?>().ToArray()).ToArray()))));
        var initialScratch = ScratchHash();
        void Restore()
        {
            foreach (var p in scratch) Array.Copy(p.Copy, p.Live, p.Copy.Length);
            state.MethodReturnValue = oldReturn; state.lineCount = oldLines;
            randomField.SetValue(vEvaluator, oldRandom);
            if (r0CRegistry?.Enabled != true)
            {
                r0CRegistry = new R0CRegistry(this, r0CGeneration, map, manifest);
                r0CRegistry.InstallBindings();
            }
        }
        static AExpression I(long value) => SingleLongTerm.FromValue(value);
        static AExpression S(string value) => SingleStrTerm.FromValue(value);
        UserDefinedMethodTerm Term(string name, params AExpression[] args)
            => UserDefinedMethodTerm.Create(labelDic.GetNonEventLabel(name)!, args.ToList(), out var error)
                ?? throw new InvalidOperationException("ConvertArg: " + error);
        AExpression Int(D2Trace t, string tag, Func<long> value) => new D2Expression(typeof(long), () =>
        { t.Order.Add(tag); return value(); });
        AExpression Str(D2Trace t, string tag, Func<string> value) => new D2Expression(typeof(string), () =>
        { t.Order.Add(tag); return value(); });
        UserDefinedMethodTerm DefaultTerm(string name, int index, SingleTerm? value, bool optional = false)
        {
            var label = labelDic.GetNonEventLabel(name)!; var saved = label.Def[index];
            var option = typeof(RuntimeConfig).GetProperty(nameof(RuntimeConfig.CompatiFuncArgOptional))!;
            var savedOption = option.GetValue(null);
            try { label.Def[index] = value!; option.SetValue(null, optional); return Term(name); }
            finally { label.Def[index] = saved; option.SetValue(null, savedOption); }
        }
        UserDefinedMethodTerm Nested(D2Trace trace, int levels, bool throws = false)
        {
            UserDefinedMethodTerm? child = levels > 0 ? Nested(trace, levels - 1, throws) : null;
            return Term("COUNT_SPLIT", S("a/b/a"), Str(trace, "nested-" + levels, () =>
            {
                trace.Depth++; trace.MaxDepth = Math.Max(trace.MaxDepth, trace.Depth);
                try
                {
                    if (child is not null) _ = child.GetIntValue(exm);
                    else if (throws && trace.Throw) throw new ArgumentException("D2 synthetic inner exception");
                    return "/";
                }
                finally { trace.Depth--; }
            }), S("a"));
        }
        var scenarios = new[]
        {
            new D2Scenario("01 INT argument", t => Term("CHARA_SKILLCOUNT", Int(t,"i0",()=>0))),
            new D2Scenario("02 STR argument", t => Term("COUNT_SPLIT",Str(t,"s0",()=>"a/b/a"),S("/"),S("a"))),
            new D2Scenario("03 INT STR mixed", t => Term("HAVE_SKILL",Int(t,"i0",()=>-1),Int(t,"i1",()=>3),Int(t,"i2",()=>0),Str(t,"s3",()=>""))),
            new D2Scenario("04 omitted int zero", t => DefaultTerm("CHARA_SKILLCOUNT",0,null,true)),
            new D2Scenario("05 omitted string empty", t => DefaultTerm("COUNT_SPLIT",0,null,true)),
            new D2Scenario("06 explicit default", t => DefaultTerm("HAVE_SKILL",0,SingleLongTerm.FromValue(-1))),
            new D2Scenario("07 native TOSTR conversion", t =>
            {
                var option=typeof(RuntimeConfig).GetProperty(nameof(RuntimeConfig.CompatiFuncArgAutoConvert))!;
                var saved=option.GetValue(null);
                try { option.SetValue(null,true); return Term("COUNT_SPLIT",Int(t,"convert",()=>123),S("/"),S("123")); }
                finally { option.SetValue(null,saved); }
            }),
            new D2Scenario("08 changing repeated inputs", t => Term("COUNT_SPLIT",Str(t,"change",()=>++t.SideEffect % 2 == 0 ? "x/x" : "x"),S("/"),S("x")),4),
            new D2Scenario("09 left to right", t => Term("HAVE_SKILL",Int(t,"0",()=>-1),Int(t,"1",()=>0),Int(t,"2",()=>0),Str(t,"3",()=>""))),
            new D2Scenario("10 exactly once", t => Term("HAVE_SKILL",Int(t,"0",()=>{t.SideEffect++;return -1;}),Int(t,"1",()=>{t.SideEffect++;return 0;}),Int(t,"2",()=>{t.SideEffect++;return 0;}),Str(t,"3",()=>{t.SideEffect++;return "";}))),
            new D2Scenario("11 nested eligible in later argument", t =>
            {
                var nested=Term("COUNT_SPLIT",S("z/z/z"),S("/"),S("z"));
                return Term("COUNT_SPLIT",Str(t,"outer0",()=>"a/b/a"),Str(t,"outer1",()=>{t.SideEffect=(int)nested.GetIntValue(exm);return "/";}),S("a"));
            }),
            new D2Scenario("12 nested noneligible Legacy", t =>
            {
                var nested=Term("BUST",I(-1));
                return Term("COUNT_SPLIT",S("a/b/a"),Str(t,"native",()=>{t.SideEffect=(int)nested.GetIntValue(exm);return "/";}),S("a"));
            },AllowLegacyScratch:true),
            new D2Scenario("13 second argument exception", t => Term("CHARA_SKILLCOUNT",Int(t,"0",()=>{t.SideEffect++;return 0;}),Str(t,"1",()=>throw new ArgumentException("D2 synthetic argument 1"))),RejectBeforeAttempt:true),
            new D2Scenario("14 generation invalidation", t => Term("CHARA_SKILLCOUNT",Int(t,"revoke",()=>{r0CGeneration++;return 0;})),RejectBeforeAttempt:true),
            new D2Scenario("15 host invalidation", t => Term("CHARA_SKILLCOUNT",Int(t,"host",()=>{GlobalStatic.EMediator=null!;GlobalStatic.EMediator=mediator;return 0;})),RejectBeforeAttempt:true),
            new D2Scenario("16 actual native RNG argument", t => Term("HAVE_SKILL",I(-1),Int(t,"rng0",()=>vEvaluator.GetNextRand(1000)),Int(t,"rng1",()=>vEvaluator.GetNextRand(1000)),S(""))),
            new D2Scenario("17 observable synthetic side effect", t => Term("CHARA_SKILLCOUNT",Int(t,"write",()=>{t.SideEffect=17;return 0;}),Str(t,"read",()=>t.SideEffect.ToString()))),
            new D2Scenario("18 deep argument reentry 256", t => Nested(t,255)),
            new D2Scenario("19 release after deep exception", t => Nested(t,31,true),2,RejectBeforeAttempt:false),
            new D2Scenario("20 repeated execution after exception", t => Term("COUNT_SPLIT",S("a/b/a"),Str(t,"retry",()=>t.Throw ? throw new ArgumentException("D2 first only") : "/"),S("a")),2)
        };
        PerformanceMetrics.Configure(Path.Combine(root,"raw",phase+".metrics.jsonl"));
        try
        {
            foreach (var scenario in scenarios)
            {
                var observations = new List<object>();
                foreach (var direct in new[] { false, true })
                {
                    Restore(); r0D1Prelinked = true; r0D2Direct = direct;
                    var t = new D2Trace(); var term = scenario.Create(t);
                    var current = r0CRegistry!;
                    randomField.SetValue(vEvaluator,new MTRandom(20260906));
                    var a=current.Attempts;var c=current.Completed;var f=current.Faults;var l=r0CLegacyEntries;
                    PerformanceMetrics.BeginMacro("D2 argument matrix");
                    var values=new List<long?>();var exceptions=new List<string?>();
                    for(var repeat=0;repeat<scenario.Repeats;repeat++)
                    {
                        try { values.Add(term.GetIntValue(exm)); exceptions.Add(null); }
                        catch(Exception ex) { values.Add(null); exceptions.Add(ex.GetType().FullName+": "+ex.Message); }
                        t.Throw=false;
                    }
                    var metric=PerformanceMetrics.FinishMacro(false);
                    if (t.Depth!=0 || !ReferenceEquals(state.CurrentLine,oldLine) || methodStack!=0 || state.functionCount!=0 ||
                        scratch.Any(p=>!ReferenceEquals(p.Token.GetArray(),p.Live)) || current.Faults!=f ||
                        FlatQueryProof.LegacyEntryAttempts!=0 || B1Proof.BridgeAttempts!=0)
                        throw new InvalidOperationException("D2 matrix lifetime/fault invariant: "+scenario.Name+"; "+JsonSerializer.Serialize(exceptions));
                    if (!scenario.AllowLegacyScratch && ScratchHash()!=initialScratch)
                        throw new InvalidOperationException("D2 candidate changed native scratch: "+scenario.Name);
                    if(scenario.RejectBeforeAttempt && (current.Attempts!=a || exceptions[0] is null))
                        throw new InvalidOperationException("D2 outer Flat began before valid arguments");
                    if(scenario.Name.StartsWith("18") && t.MaxDepth!=256)throw new InvalidOperationException("Deep case not exercised");
                    if(scenario.Name.StartsWith("10") && (t.SideEffect!=4 || t.Order.Count!=4))throw new InvalidOperationException("Exactly once failed");
                    observations.Add(new {
                        Values=values,Exceptions=exceptions,Order=t.Order,EvaluationCount=t.Order.Count,t.SideEffect,
                        t.MaxDepth,DepthAfter=t.Depth,RngHash=vEvaluator.GetR0C2RngHash(),
                        metric.RandomCallCount,metric.RandomTraceHash,
                        FlatAttempts=current.Attempts-a,FlatCompleted=current.Completed-c,FlatFaults=current.Faults-f,
                        LegacyEntries=r0CLegacyEntries-l,ScratchHash=ScratchHash(),
                        NativeStateHash=GetBenchmarkStateHash(),LegacyRetryAfterFlat=FlatQueryProof.LegacyEntryAttempts
                    });
                    Restore();
                }
                var equal=JsonSerializer.Serialize(observations[0])==JsonSerializer.Serialize(observations[1]);
                var row=new {scenario.Name,Pass=equal,A_D1=observations[0],B_D2=observations[1]};
                rows.Add(row);
                B1Proof.WriteJson(Path.Combine(root,"raw",phase+".matrix.json"),rows);
                if(!equal)throw new InvalidOperationException("D2 A/B argument mismatch: "+scenario.Name);
            }

            // Observe the transport values themselves, including defaults and conversion;
            // catches staging mistakes even when a callee ignores a particular argument.
            var transportRows=new List<object>();
            foreach(var scenario in scenarios.Take(12).Where(s=>!s.AllowLegacyScratch))
            {
                var outputs=new List<object>();
                foreach(var direct in new[]{false,true})
                {
                    Restore();r0D2Direct=direct;var trace=new D2Trace();var term=scenario.Create(trace);
                    var binding=term.Call.TopLabel.R0D1Binding!;
                    var parameters=binding.ParametersForD2Proof;
                    var frame=new QueryValue[4];
                    if(direct)binding.FillD2Arguments(this,term.Argument.Arguments,frame);
                    else
                    {
                        term.Argument.SetTransporter(exm);
                        for(int i=0;i<parameters.Length;i++)
                            frame[i]=parameters[i].String?QueryValue.S(term.Argument.TransporterStr[i]):QueryValue.I(term.Argument.TransporterInt[i]);
                    }
                    outputs.Add(new {Values=frame.Take(parameters.Length).ToArray(),trace.Order,trace.SideEffect});
                }
                bool equal=JsonSerializer.Serialize(outputs[0])==JsonSerializer.Serialize(outputs[1]);
                transportRows.Add(new {scenario.Name,Pass=equal,A_D1=outputs[0],B_D2=outputs[1]});
                if(!equal)throw new InvalidOperationException("D2 transport value mismatch");
            }
            Restore();r0D2Direct=true;
            var poisoned=Term("COUNT_SPLIT",S("a/b/a"),S("/"),S("a"));
            Array.Fill(poisoned.Argument.TransporterStr,"POISON");
            if(poisoned.GetIntValue(exm)!=2 || poisoned.Argument.TransporterStr.Any(v=>v!="POISON"))
                throw new InvalidOperationException("D2 read or wrote Legacy transporter");
            B1Proof.WriteJson(Path.Combine(root,"raw",phase+".transport.json"),transportRows);
            return new {Pass=true,Cases=rows.Count,Rows=rows,TransportValueComparisons=transportRows.Count,
                PoisonedNativeTransporterIgnored=true,NativeStateUnchanged=initialState==GetBenchmarkStateHash(),
                ScratchUnchanged=initialScratch==ScratchHash(),RngUnchanged=initialRng==vEvaluator.GetR0C2RngHash(),
                Frame=new{Kind="caller-local InlineArray",Cells=4,CellBytes=Unsafe.SizeOf<QueryValue>(),
                    PayloadBytes=Unsafe.SizeOf<R0D2ArgumentFrame>(),RetainedFrameArrays=0,PoolCapacity="not applicable",
                    VerifiedArgumentReentryDepth=256,Release="finally Clear; no shared depth state"},
                FlatFaults=0,LegacyRetryAfterFlat=0,ProductionBridgeUsed="NO"};
        }
        finally { Restore(); r0D2Direct=false; }
    }
}
#endif

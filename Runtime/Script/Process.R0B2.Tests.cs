#if R0_B2
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MinorShift.Emuera.Runtime.Diagnostics;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameProc;
internal sealed partial class Process
{
    private object[] B2GeneralTests(B1HotMap map)
    {
        var catalog=new FlatQueryCatalog(map);var rows=new List<object>();int sequence=0;
        if(!FlatQueryCatalog.FindExternalFrameAliases("LOCAL@METHOD:1 + LOCALS @ OTHER:0").SequenceEqual(new[]{"METHOD","OTHER"}))throw new InvalidOperationException("External frame alias detector");
        rows.Add(new{Name="Conservative external frame alias detector",Pass=true});
        int Compile(string body,string signature="",string declarations="",string? name=null)
        {
            name??="B2_TEST_"+(++sequence);
            return catalog.Synthetic(name,"@"+name+(signature.Length==0?"":"("+signature+")")+"\n#FUNCTION\n"+declarations+body+"\n");
        }
        long Flat(int id,params QueryValue[] args)
        {
            var context=new FlatQueryContext(catalog.Snapshot());FlatQueryProof.ForbidLegacy=true;
            try{return context.Execute(id,args).Integer;}finally{FlatQueryProof.ForbidLegacy=false;}
        }
        AExpression Native(string expression)=>ExpressionParser.ReduceExpressionTerm(
            LexicalAnalyzer.Analyse(new CharStream(expression),LexEndWith.EoL,LexAnalyzeFlag.None),TermEndWith.EoL);
        (long? Value,string? Type,string? Message) Outcome(Func<long> action)
        {
            try{return(action(),null,null);}
            catch(Exception ex)when(ex is CodeEE or IndexOutOfRangeException or ArgumentOutOfRangeException){return(null,ex.GetType().Name,ex.Message);}
        }
        void Differential(string name,string expression,string? expectedErrorOrigin=null)
        {
            int id=Compile("RETURNF "+expression);var native=Native(expression);
            var a=Outcome(()=>native.GetIntValue(exm));var c=Outcome(()=>Flat(id));
            bool pass=a.Value==c.Value&&a.Type==c.Type;
            if(expectedErrorOrigin!=null)pass&=a.Message?.Contains(expectedErrorOrigin)==true&&c.Message?.Contains(expectedErrorOrigin)==true;
            rows.Add(new{Name=name,Expression=expression,Legacy=a.Value,Flat=c.Value,LegacyFault=a.Type,FlatFault=c.Type,LegacyReason=a.Message,FlatReason=c.Message,Pass=pass});
            if(!pass)throw new InvalidOperationException("Builtin differential FAIL: "+name+" / "+a+" / "+c);
        }
        foreach(var key in new[]{"スキル1","","__B2_MISSING_KEY__"})
            Differential("GETNUM key/empty/missing","GETNUM(ABL, \""+key+"\")");
        Differential("GETNUM ignores invalid first indices","GETNUM(ABL:999999:999999, \"スキル1\")");
        Differential("GETNUM unsupported dictionary","GETNUM(TARGET, \"anything\")");
        var abl=idDic.GetVariableToken("ABL",null,false);long size=abl.GetLength(),chara=vEvaluator.CHARANUM;
        foreach(var (start,end) in new[]{(0L,0L),(0L,3L),(2L,1L),(size,size),(size-1,size),(-1L,1L),(0L,size+1),(size+1,size+1)})
            Differential("MATCH half-open/equal/reversed/bounds",$"MATCH(ABL:0:0, 0, {start}, {end})");
        Differential("MATCH not found","MATCH(ABL:0:0, 9223372036854775807)");
        Differential("MATCH validates prefix even empty","MATCH(ABL:999999:0, 0, 0, 0)");
        Differential("MATCH range faults before target","MATCH(ABL:0:0, CFLAG:999999:0, -1, 0)","MATCH");
        Differential("FINDCHARA not found","FINDCHARA(CFLAG:0:0, 9223372036854775807)");
        foreach(var (start,end) in new[]{(0L,0L),(2L,1L),(0L,chara),(chara-1,chara),(-1L,chara),(chara,chara),(0L,chara+1)})
            Differential("FINDCHARA range",$"FINDCHARA(CFLAG:0:0, 0, {start}, {end})");
        Differential("FINDCHARA ignores character selector","FINDCHARA(CFLAG:999999:0, 0)");
        Differential("FINDCHARA invalid data index skipped on empty","FINDCHARA(CFLAG:0:999999, 0, 0, 0)");
        Differential("FINDCHARA invalid data index read faults","FINDCHARA(CFLAG:0:999999, 0, 0, 1)");
        Differential("FINDCHARA range faults before target","FINDCHARA(CFLAG:0:0, CFLAG:999999:0, -1, 1)","第3引数");
        foreach(var (input,pattern) in new[]{("",""),("abc",""),("aaaa","aa"),("abc","missing"),("日本語日本","日本"),("abc","^|$"),("aaa","(?=a)"),("abc","["),("abc",".")})
            Differential("STRCOUNT regex/empty/invalid","STRCOUNT(\""+input+"\", \""+pattern+"\")");
        Differential("STRCOUNT invalid pattern before input","STRCOUNT(CSTR:999999:0, \"[\")","正規表現");
        void Check(string name,string body,long expected,string signature="",string declarations="",params QueryValue[] args)
        {
            long actual=Flat(Compile(body,signature,declarations),args);
            if(actual!=expected)throw new InvalidOperationException("General frame FAIL: "+name);
            rows.Add(new{Name=name,Expected=expected,Actual=actual,Pass=true});
        }
        Check("Indexed defaults","RETURNF ARG:2 + ARG",12,"ARG = 5, ARG:2 = 7");
        Check("Indexed string default","RETURNF ARGS:2 == \"日本語\"",1,"ARGS:2 = \"日本語\"");
        Check("Scratch branch definite writes","IF ARG\nLOCAL:1 = 3\nELSE\nLOCAL:1 = 4\nENDIF\nRETURNF LOCAL:1",4,"ARG","#LOCALSIZE 2\n",QueryValue.I(0));
        Check("Private small array","TMP:1 = 17\nRETURNF TMP:1",17,"","#DIM TMP,2\n");
        Check("Descending FOR","LOCAL:1 = 0\nFOR LOCAL,3,0,-1\nLOCAL:1 += LOCAL\nNEXT\nRETURNF LOCAL:1",6,"","#LOCALSIZE 2\n");
        Check("Zero-step FOR","LOCAL:1 = 9\nFOR LOCAL,0,4,0\nLOCAL:1 = 99\nNEXT\nRETURNF LOCAL:1",9,"","#LOCALSIZE 2\n");
        Compile("LOCAL = ARG + ARG:1\nRETURNF LOCAL","ARG,ARG:1=7","#LOCALSIZE 1\n","B2_NESTED_SUM");
        Check("Nested frames and defaults","LOCAL = 100\nRETURNF B2_NESTED_SUM(B2_NESTED_SUM(ARG,2)) + LOCAL",112,"ARG","#LOCALSIZE 1\n",QueryValue.I(3));
        Check("Missing middle argument","RETURNF B2_NESTED_SUM(,2)",2);
        Check("Short circuit protects fault","RETURNF 0 && CFLAG:999999:0",0);
        Check("Short circuit OR protects fault","RETURNF 1 || CFLAG:999999:0",1);
        int repeated=Compile("LOCAL = ARG + 1\nRETURNF LOCAL","ARG","#LOCALSIZE 1\n");
        var reusable=new FlatQueryContext(catalog.Snapshot());
        foreach(long value in new[]{1L,7L,-4L,0L})if(reusable.Execute(repeated,[QueryValue.I(value)]).Integer!=value+1)throw new InvalidOperationException("Reusable frame leaked");
        rows.Add(new{Name="Reusable frame changing inputs",Pass=true});
        int large=Compile(string.Join("\n",Enumerable.Repeat("SIF ARG == 999\nRETURNF 3",30))+"\nRETURNF 7","ARG");
        if(catalog.Programs[large]!.FastLeaf?.RegisterCount is not >64||Flat(large,QueryValue.I(0))!=7)throw new InvalidOperationException("Large reusable fast frame");
        rows.Add(new{Name="B1 fast leaf >64 registers, B2 fully validated",Pass=true});
        int tooDeep=Compile("RETURNF B2_NESTED_SUM(1)");
        bool depthFail=false,typeFail=false;
        try{new FlatQueryContext(catalog.Snapshot(),1).Execute(tooDeep,[]);}catch(InvalidOperationException e)when(e.Message.Contains("CallDepth")){depthFail=true;}
        try{reusable.Execute(repeated,[QueryValue.S("wrong type")]);}catch(InvalidOperationException e)when(e.Message.Contains("argument type")){typeFail=true;}
        if(!depthFail||!typeFail)throw new InvalidOperationException("Explicit boundary fault guard");
        rows.Add(new{Name="Call depth and root argument type fail explicitly",Pass=true});
        Compile("RETURNF ARG","ARG","","B2_FUEL_LEAF");
        int siblings=Compile("RETURNF B2_FUEL_LEAF(1) + B2_FUEL_LEAF(2)");
        bool fuelFail=false;
        try{new FlatQueryContext(catalog.Snapshot()).Execute(siblings,[],4);}catch(InvalidOperationException e)when(e.Message.Contains("StepLimit")){fuelFail=true;}
        if(!fuelFail||Flat(siblings)!=3)throw new InvalidOperationException("Shared leaf fuel guard");
        rows.Add(new{Name="Shared conservative fuel across sibling leaf calls",Pass=true});
        Compile("RETURNF 1","","","B2_DEPTH_0");
        for(int d=1;d<32;d++)Compile("RETURNF B2_DEPTH_"+(d-1)+"()","","","B2_DEPTH_"+d);
        string? depthBlocker=null;
        try{Compile("RETURNF B2_DEPTH_31()","","","B2_DEPTH_32");}catch(NotSupportedException e){depthBlocker=e.Message;}
        if(depthBlocker==null)throw new InvalidOperationException("33-frame static chain admitted");
        rows.Add(new{Name="33-frame static chain rejected at link",Reason=depthBlocker,Pass=true});
        foreach(var test in new[]{
            ("Persistent LOCAL","RETURNF LOCAL","","#LOCALSIZE 1\n"),
            ("Conditional uninitialized LOCAL","SIF ARG\nLOCAL = 1\nRETURNF LOCAL","ARG","#LOCALSIZE 1\n"),
            ("Zero-iteration uninitialized LOCAL","FOR LOCAL,0,0\nLOCAL:1 = 5\nNEXT\nRETURNF LOCAL:1","","#LOCALSIZE 2\n"),
            ("Persistent private","RETURNF TMP","","#DIM TMP\n"),
            ("Dynamic scratch index","LOCAL:ARG = 1\nRETURNF 0","ARG","#LOCALSIZE 2\n"),
            ("External write","CFLAG:0:0 = 1\nRETURNF 0","",""),
            ("RNG","RETURNF RAND:10","",""),
            ("Dynamic target","RETURNF EXISTFUNCTION(\"HAVE_SKILL\")","",""),
            ("Unapproved diagnostic builtin","RETURNF FINDELEMENT(ABL:0:0,0)","",""),
            ("Unsupported control","WHILE 0\nWEND\nRETURNF 0","","")
        }){
            string? reason=null;
            try{Compile(test.Item2,test.Item3,test.Item4);}catch(Exception ex)when(ex is NotSupportedException or CodeEE){reason=ex.Message;}
            if(reason==null)throw new InvalidOperationException("Fail-closed did not reject: "+test.Item1);
            rows.Add(new{Name=test.Item1,Rejected=true,Reason=reason,Pass=true});
        }
        string? recursion=null;
        try{Compile("RETURNF B2_RECURSE(ARG)","ARG","","B2_RECURSE");}catch(NotSupportedException ex){recursion=ex.Message;}
        if(recursion==null)throw new InvalidOperationException("Recursion accepted without proof");
        rows.Add(new{Name="Static recursion explicit rejection",Reason=recursion,Pass=true});
        return rows.ToArray();
    }
    private void B2Coverage(B1HotMap map,string root,string phase)
    {
        var catalog=new FlatQueryCatalog(map);var rows=new List<object>();var eligible=new HashSet<string>();var unjoined=new List<B1ProfileRow>();
        foreach(var row in map.Profile.Rows){
            string key=B1HotMap.Key(row.SourcePath,row.SourceLine,row.FunctionName);
            if(!map.Functions.ContainsKey(key)){unjoined.Add(row);continue;}
            int? id=null;string? blocker=null;
            try{id=catalog.Link(key);eligible.Add(key);}
            catch(Exception ex)when(ex is NotSupportedException or CodeEE or FormatException){blocker=ex.Message;}
            rows.Add(new{Key=key,row.FunctionName,row.SourcePath,row.SourceLine,row.CallCount,row.ExclusiveMilliseconds,Eligible=id!=null,ProgramId=id,Blocker=blocker});
        }
        var programs=catalog.Snapshot();
        var byKey=map.Profile.Rows.GroupBy(r=>B1HotMap.Key(r.SourcePath,r.SourceLine,r.FunctionName)).ToDictionary(g=>g.Key,g=>g.Sum(r=>r.ExclusiveMilliseconds));
        var roots=eligible.Select(key=>{
            var seen=new HashSet<int>();void Visit(int id){if(!seen.Add(id))return;foreach(var c in programs[id].Calls)Visit(c.Program);}
            Visit(catalog.Link(key));
            var keys=seen.Select(i=>programs[i].Key).Order().ToArray();
            return new{Key=key,DistinctNodes=keys,DistinctExclusiveUnionMs=keys.Sum(k=>byKey.GetValueOrDefault(k))};
        }).OrderByDescending(r=>r.DistinctExclusiveUnionMs).ToArray();
        double denominator=map.Profile.Rows.Sum(r=>r.ExclusiveMilliseconds),numerator=eligible.Sum(k=>byKey[k]);
        B1Proof.WriteJson(Path.Combine(root,"raw",phase+".coverage.json"),new{PriorProvenPercent=7.334337,ProvenClosedPercent=100*numerator/denominator,ExclusiveUnionMs=numerator,DenominatorExclusiveMs=denominator,ProfileRows=map.Profile.Rows.Length,EligibleProfileNodes=eligible.Count,ExternalFrameAliasTargets=catalog.ExternalFrameAliases.Order().ToArray(),Unmatched=unjoined,Rows=rows,Roots=roots,Rule="Actual compiler + unchanged hard flags + typed flat lowering + static linking + definite scratch assignment + explicit external frame alias screen over all fixed ERB/ERH. Distinct exact source/line/name nodes; unmatched excluded from numerator. Compile/link proof, not execution proof for every input or dynamically generated outside observers."});
        using var output=new StreamWriter(Path.Combine(root,"raw",phase+".coverage.tsv"));
        output.WriteLine("Key\tEligible\tExclusiveMs\tBlocker");
        foreach(var row in map.Profile.Rows){
            var key=B1HotMap.Key(row.SourcePath,row.SourceLine,row.FunctionName);
            output.WriteLine(key.Replace('\t','|')+"\t"+eligible.Contains(key)+"\t"+row.ExclusiveMilliseconds+"\t"+catalog.Blockers.GetValueOrDefault(key,unjoined.Contains(row)?"ExactJoinMissing":"").Replace('\t',' ').Replace('\n',' '));
        }
    }
}
#endif

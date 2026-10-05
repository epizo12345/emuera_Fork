using System.Reflection;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;

namespace Emuera.ConfigRegressionTests;
internal static partial class Program
{
	private static readonly FieldInfo stringsField=typeof(StrForm).GetField("strs",BindingFlags.Instance|BindingFlags.NonPublic)!;
	private static readonly FieldInfo termsField=typeof(StrForm).GetField("terms",BindingFlags.Instance|BindingFlags.NonPublic)!;
	private static StrForm Form(string[] strings, params AExpression[] expressions)
	{
		var form=(StrForm)Activator.CreateInstance(typeof(StrForm),true)!;
		stringsField.SetValue(form,strings);termsField.SetValue(form,expressions);return form;
	}
	private sealed class TraceExpression(string name,string value,List<string> trace,bool fold=false,bool throwRestructure=false) : AExpression(typeof(string))
	{
		public override AExpression Restructure(ExpressionMediator exm)
		{
			trace.Add("R"+name);if(throwRestructure)throw new InvalidOperationException("R"+name);
			return fold?new TraceConstant(name,value,trace):this;
		}
		public override string GetStrValue(ExpressionMediator exm){trace.Add("D"+name);return value;}
	}
	private sealed class TraceConstant(string name,string value,List<string> trace,bool throwGet=false) : SingleTerm(typeof(string))
	{
		public override AExpression Restructure(ExpressionMediator exm){trace.Add("R"+name);return this;}
		public override string GetStrValue(ExpressionMediator exm){trace.Add("G"+name);if(throwGet)throw new InvalidOperationException("G"+name);return value;}
	}
	private static void TestStrForm()
	{
		foreach(int n in new[]{0,1,2,8,64})foreach(bool alternating in new[]{false,true}){
			var trace=new List<string>();var expected="<";var fragments=new string[n+1];fragments[0]="<";
			var expressions=new AExpression[n];
			for(int i=0;i<n;i++){
				string value=i%3==0?"":i%3==1?"日本😀":"x";fragments[i+1]=i==n-1?">":"|";
				expressions[i]=new TraceExpression(i.ToString(),value,trace,!alternating||i%2==0);
				expected+=value+fragments[i+1];
			}
			var form=Form(fragments,expressions);form.Restructure(null!);
			var order=Enumerable.Range(0,n).Select(i=>"R"+i).Concat(Enumerable.Range(0,n).Where(i=>!alternating||i%2==0).Select(i=>"G"+i));
			Check($"{n}/{alternating}: all restructuring precedes constant values",trace.SequenceEqual(order));
			Check($"{n}/{alternating}: result",form.GetString(null!)==expected);
			var afterGet=Enumerable.Range(0,n).Where(i=>alternating&&i%2==1).Select(i=>"D"+i);
			Check($"{n}/{alternating}: runtime dynamic order",trace.SequenceEqual(order.Concat(afterGet)));
			Check($"{n}/{alternating}: exact remaining terms",((AExpression[])termsField.GetValue(form)!).Length==(alternating?n/2:0));
			trace.Clear();form.Restructure(null!);
			Check($"{n}/{alternating}: repeated restructure/result",form.GetString(null!)==expected);
		}
		var nested=Form(["a","z"],new StrFormTerm(Form(["b","d"],new SingleStrTerm("c"))));
		nested.Restructure(null!);Check("nested constant form",nested.IsConst&&nested.GetString(null!)=="abcdz");
		var nullValue=Form(["a","b"],new SingleStrTerm(null!));nullValue.Restructure(null!);Check("null constant matches String.Concat",nullValue.GetString(null!)=="ab");
		Check("all constant forms keep shared empty term array",ReferenceEquals(termsField.GetValue(nullValue),Array.Empty<AExpression>()));
		var groupsTrace=new List<string>();
		var groups=Form(Enumerable.Repeat("|",9).ToArray(),Enumerable.Range(0,8).Select(i=>(AExpression)new TraceExpression(i.ToString(),(i==2||i==5?"D":"C")+i,groupsTrace,i!=2&&i!=5)).ToArray());
		groups.Restructure(null!);
		Check("CC-D-CC-D-CC: complete first phase and ordered constant groups",groupsTrace.SequenceEqual(new[]{"R0","R1","R2","R3","R4","R5","R6","R7","G0","G1","G3","G4","G6","G7"}));
		Check("CC-D-CC-D-CC: builder flush/reset output",groups.GetString(null!)=="|C0|C1|D2|C3|C4|D5|C6|C7|");
		Check("CC-D-CC-D-CC: surviving dynamic order",groupsTrace.TakeLast(2).SequenceEqual(new[]{"D2","D5"}));
		var noConstants=Form(["a","b"],new TraceExpression("0","d",[],false));var sameStrings=stringsField.GetValue(noConstants);noConstants.Restructure(null!);
		Check("no constants keeps string array",ReferenceEquals(sameStrings,stringsField.GetValue(noConstants)));
		foreach(bool restructureException in new[]{false,true}){
			var trace=new List<string>();var texts=new[]{"a","b","c","d"};
			var terms=new AExpression[]{new TraceExpression("0","0",trace,true),restructureException?new TraceExpression("1","1",trace,true,true):new TraceConstant("1","1",trace,true),new TraceExpression("2","2",trace,true)};
			var form=Form(texts,terms);string? caught=null;try{form.Restructure(null!);}catch(InvalidOperationException e){caught=e.Message;}
			Check($"exception/{restructureException}: order",trace.SequenceEqual(restructureException?new[]{"R0","R1"}:new[]{"R0","R1","R2","G0","G1"}));
			Check($"exception/{restructureException}: type/message",caught==(restructureException?"R1":"G1"));
			Check($"exception/{restructureException}: uncommitted text and original term array",ReferenceEquals(texts,stringsField.GetValue(form))&&ReferenceEquals(terms,termsField.GetValue(form)));
			Check($"exception/{restructureException}: partial first phase",terms[0] is SingleTerm&&(restructureException?terms[2] is TraceExpression:terms[2] is SingleTerm));
		}
		long bytes=Allocation(()=>ConstantForm(512,false).Restructure(null!));results.Add(new{name="constant-fold-allocation",bytes});
		Check("512 constants avoid cumulative strings/list shifts",bytes<150000);
	}
	private static StrForm ConstantForm(int n,bool alternating)
	{
		var strings=Enumerable.Repeat("abc",n+1).ToArray();var expressions=new AExpression[n];
		for(int i=0;i<n;i++)expressions[i]=alternating&&i%2==1?new TraceExpression("","def",[]):new SingleStrTerm("def");
		return Form(strings,expressions);
	}
}

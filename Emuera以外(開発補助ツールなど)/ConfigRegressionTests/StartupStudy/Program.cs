using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.Json;
using MinorShift.Emuera;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;

namespace Emuera.ConfigRegressionTests;
internal static partial class Program
{
	private static int failures, checks;
	private static readonly List<object> results = [];
	[STAThread]
	private static int Main(string[] args)
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		Config.SetConfig(ConfigData.Instance);
		JSONConfig.Game = new JSONGameConfigData();
		Console.WriteLine($"SERVER_GC={GCSettings.IsServerGC}; PROCESSOR_COUNT={Environment.ProcessorCount}; ENGINE={typeof(Preload).Assembly.Location}");
		if (args[0] == "A") TestPreload(args[1]);
		else if (args[0] == "C") TestStrForm();
		else if (args[0] == "D") TestDiscard();
		else throw new ArgumentException("Unknown probe");
		Console.WriteLine("DATA=" + JsonSerializer.Serialize(results));
		Console.WriteLine($"RESULT: {checks - failures}/{checks}; failures={failures}; skips=0");
		return failures == 0 ? 0 : 1;
	}
	private static void Check(string name, bool condition)
	{
		checks++; if (!condition) failures++;
		Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {name}");
	}
	private static void TestPreload(string directory)
	{
		Directory.CreateDirectory(directory);
		// 末尾空行・CR除去・BOMだけの期待値はliteralで固定し、製品処理から作らない。
		var cases = new (string name, byte[] bytes, string[] expected)[] {
			("empty", [], [""]), ("bom-only", [239,187,191], [""]),
			("lf", Encoding.ASCII.GetBytes("a\nb\n"), ["a","b",""]),
			("no-final-newline", Encoding.ASCII.GetBytes("a\nb"), ["a","b"]),
			("crlf-empty", Encoding.ASCII.GetBytes("a\r\n\r\nb\r\n"), ["a","","b",""]),
			("lf-empty", Encoding.ASCII.GetBytes("\n\na\n\n"), ["","","a","",""]),
			("single-cr", [(byte)'\r'], [""]),
			("utf8", [239,187,191,..Encoding.UTF8.GetBytes("日本語😀\r\n末尾")], ["日本語😀","末尾"]),
			("sjis", Encoding.GetEncoding(932).GetBytes("日本語\n末尾"), ["日本語","末尾"]),
			("invalid-utf8", [239,187,191,255,10], ["�",""])
		};
		var queue = typeof(ParserMediator).GetField("warningList", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
		var clear = queue.GetType().GetMethod("Clear")!;
		typeof(Config).GetProperty("DisplayWarningLevel")!.SetValue(null, 0);
		Config.Encode = Encoding.GetEncoding(932);
		foreach (var c in cases)
		{
			string path = Path.Combine(directory, c.name + ".erb"); File.WriteAllBytes(path, c.bytes);
			foreach (bool request in new[] { false, true }) foreach (bool policy in new[] { false, true })
			{
				JSONConfig.Game.CheckUTF8withBOM = policy; clear.Invoke(queue, null);
				var lines = Preload.ReadFileLines(path, request);
				var warnings = ((System.Collections.IEnumerable)queue).Cast<object>().ToArray();
				bool bom = c.bytes.Length >= 3 && c.bytes[0] == 239 && c.bytes[1] == 187 && c.bytes[2] == 191;
				int expectedWarnings = c.bytes.Length != 0 && !bom && request && policy ? 1 : 0;
				Check($"{c.name}: contents/request={request}/policy={policy}", lines.SequenceEqual(c.expected));
				Check($"{c.name}: warnings/request={request}/policy={policy}", warnings.Length == expectedWarnings);
				results.Add(new { c.name, request, policy, lines, warnings = warnings.Select(w => w.GetType().GetField("WarningMes")!.GetValue(w)).ToArray() });
			}
		}
		JSONConfig.Game.CheckUTF8withBOM = false;
		// 割当削減のRED: 同じ入力・出力を持つ旧referenceより一時割当が小さい必要がある。
		string large = Path.Combine(directory, "allocation.erb");
		File.WriteAllText(large, string.Join('\n', Enumerable.Repeat("短い行", 2000)), new UTF8Encoding(true));
		long oldBytes = Allocation(() => ReferenceRead(large));
		long actualBytes = Allocation(() => Preload.ReadFileLines(large, false));
		results.Add(new { name = "allocation-red", oldBytes, actualBytes });
		Check("direct line storage removes temporary list backing", actualBytes < oldBytes - 10000);
	}
	private static long Allocation(Action action)
	{
		for (int i=0;i<5;i++) action();
		long start=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<10;i++)action();
		return (GC.GetAllocatedBytesForCurrentThread()-start)/10;
	}
	private static string[] ReferenceRead(string path)
	{
		ReadOnlySpan<byte> bytes=File.ReadAllBytes(path);
		if(bytes.IsEmpty)return [""];
		var encoding=Config.Encode;
		if(bytes.StartsWith([ (byte)239,(byte)187,(byte)191])){encoding=Encoding.UTF8;bytes=bytes[3..];}
		int count=1;foreach(byte b in bytes)if(b==10)count++;
		var lines=new List<string>(count);
		foreach(var range in bytes.Split((byte)10)){
			var line=bytes[range]; if(line.IsEmpty)lines.Add("");
			else if(line[^1]==13)lines.Add(encoding.GetString(line[..^1]));else lines.Add(encoding.GetString(line));
		}
		return [..lines];
	}
}

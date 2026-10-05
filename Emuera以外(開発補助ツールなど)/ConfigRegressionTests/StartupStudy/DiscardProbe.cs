using System.Reflection;
using System.Text;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Framework;

namespace Emuera.ConfigRegressionTests;
internal static partial class Program
{
	private static EraBinaryDataReader Reader(BinaryReader binary)
	{
		var type=typeof(EraBinaryDataReader).GetNestedType("EraBinaryDataReader1808",BindingFlags.NonPublic)!;
		return (EraBinaryDataReader)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{binary,1808,Array.Empty<uint>()},null)!;
	}
	private static byte[] ArrayData(int length,byte[] payload)
	{
		using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write(length);writer.Write(payload);return stream.ToArray();
	}
	private static byte[] Encode(long value)
	{
		using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
		if(value>=0&&value<=Ebdb.Byte)writer.Write((byte)value);
		else if(value>=short.MinValue&&value<=short.MaxValue){writer.Write(Ebdb.Int16);writer.Write((short)value);}
		else if(value>=int.MinValue&&value<=int.MaxValue){writer.Write(Ebdb.Int32);writer.Write((int)value);}
		else{writer.Write(Ebdb.Int64);writer.Write(value);}
		return stream.ToArray();
	}
	private static long ReferenceInt(BinaryReader reader)
	{
		byte b=reader.ReadByte();if(b<=Ebdb.Byte)return b;
		return b switch{Ebdb.Int16=>reader.ReadInt16(),Ebdb.Int32=>reader.ReadInt32(),Ebdb.Int64=>reader.ReadInt64(),_=>throw new FileEE(LocalizationManager.Error.AbnormalBinaryData)};
	}
	// 旧アルゴリズムは試験専用のreferenceだけに置く。例外/消費位置を配列書込みそのものと比較する。
	private static void ReferenceDiscard(BinaryReader reader,bool needInit)
	{
		int length=reader.ReadInt32();var array=new long[length];int x=0;
		while(true){byte b=reader.ReadByte();if(b==Ebdb.EoD)break;
			if(b==Ebdb.Zero){int count=(int)ReferenceInt(reader);if(needInit)for(int i=0;i<count;i++)array[x+i]=0;x+=count;continue;}
			if(b<=Ebdb.Byte)array[x]=b;else if(b==Ebdb.Int16)array[x]=reader.ReadInt16();else if(b==Ebdb.Int32)array[x]=reader.ReadInt32();else if(b==Ebdb.Int64)array[x]=reader.ReadInt64();else throw new FileEE(LocalizationManager.Error.AbnormalBinaryData);x++;
		}
		if(needInit)for(;x<length;x++)array[x]=0;
	}
	private static (string? type,string? message,long position,long? next) Outcome(byte[] bytes,bool init,bool reference)
	{
		using var stream=new MemoryStream(bytes);using var binary=new BinaryReader(stream,Encoding.Unicode,true);
			try{if(reference)ReferenceDiscard(binary,init);else{var reader=Reader(binary);reader.ReadIntArray(null!,init);}
			long position=stream.Position;long? next=stream.Length-position>=8?binary.ReadInt64():null;return(null,null,position,next);
		}catch(Exception e){return(e.GetType().FullName,e.Message,stream.Position,null);}
	}
	private static void TestDiscard()
	{
		var cases=new List<(string name,byte[] bytes)> {
			("empty",ArrayData(0,[Ebdb.EoD])),("zero-init",ArrayData(13,[Ebdb.Zero,..Encode(13),Ebdb.EoD])),
			("all-encodings",ArrayData(6,[0,207,..Encode(-123),..Encode(40000),..Encode(long.MinValue),..Encode(long.MaxValue),Ebdb.EoD])),
			("trailing-zeros",ArrayData(8,[3,Ebdb.EoD])),("negative-length",ArrayData(-1,[Ebdb.EoD])),
			("overrun-literal",ArrayData(0,[7,Ebdb.EoD])),("overrun-int16",ArrayData(0,[..Encode(-3),Ebdb.EoD])),
			("overrun-zero",ArrayData(2,[Ebdb.Zero,..Encode(4),Ebdb.EoD])),
			("negative-zero-tail",ArrayData(2,[Ebdb.Zero,..Encode(-1),Ebdb.EoD])),
			("negative-zero-value",ArrayData(2,[Ebdb.Zero,..Encode(-1),9,Ebdb.EoD])),
			("zero-count",ArrayData(2,[Ebdb.Zero,0,9,Ebdb.EoD])),
			("wrapped-zero-count",ArrayData(2,[Ebdb.Zero,..Encode(4294967297),9,Ebdb.EoD])),
			("bad-marker",ArrayData(2,[Ebdb.String])),("bad-zero-marker",ArrayData(2,[Ebdb.Zero,Ebdb.String]))
		};
		cases.Add(("unsupported-length",ArrayData(int.MaxValue,[Ebdb.EoD])));
		cases.Add(("index-overflow",ArrayData(2,[Ebdb.Zero,..Encode(int.MaxValue),Ebdb.Zero,2,Ebdb.EoD])));
		cases.Add(("truncated-oob-int16",ArrayData(0,[Ebdb.Int16,1])));
		cases.Add(("truncated-oob-int32",ArrayData(0,[Ebdb.Int32,1,2,3])));
		cases.Add(("truncated-oob-int64",ArrayData(0,[Ebdb.Int64,1,2,3,4,5,6,7])));
		cases.Add(("truncated-zero-int16",ArrayData(1,[Ebdb.Zero,Ebdb.Int16,1])));
		cases.Add(("truncated-zero-int32",ArrayData(1,[Ebdb.Zero,Ebdb.Int32,1,2,3])));
		cases.Add(("truncated-zero-int64",ArrayData(1,[Ebdb.Zero,Ebdb.Int64,1,2,3,4,5,6,7])));
		var complete=ArrayData(6,[0,207,..Encode(-123),..Encode(40000),..Encode(long.MinValue),..Encode(long.MaxValue),Ebdb.EoD]);
		for(int cut=0;cut<complete.Length;cut++)cases.Add(($"truncated-{cut}",complete[..cut]));
		foreach(var c in cases)foreach(bool init in new[]{false,true}){
			bool truncate=c.name.StartsWith("truncated-");var input=truncate?c.bytes:[..c.bytes,..BitConverter.GetBytes(123456789L)];
			var expected=Outcome(input,init,true);var actual=Outcome(input,init,false);
			Check($"{c.name}/{init}: exception/message/position/next",expected==actual);
			results.Add(new{c.name,init,expectedType=expected.type,expectedMessage=expected.message,expectedPosition=expected.position,expectedNext=expected.next,actualType=actual.type,actualMessage=actual.message,actualPosition=actual.position,actualNext=actual.next});
		}
		var large=ArrayData(1000000,[Ebdb.Zero,..Encode(1000000),Ebdb.EoD]);
		long bytes=Allocation(()=>{using var stream=new MemoryStream(large);using var binary=new BinaryReader(stream);using var reader=Reader(binary);reader.ReadIntArray(null!,true);});
		results.Add(new{name="discard-allocation",bytes});Check("null integer1D discards without payload array",bytes<2000);
	}
}

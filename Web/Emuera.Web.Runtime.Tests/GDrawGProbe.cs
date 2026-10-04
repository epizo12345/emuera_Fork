using MinorShift.Emuera.Web.Runtime;
using SkiaSharp;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class GDrawGProbe
{
    internal static readonly uint[] Source = [0xFFFF0000,0xFF00FF00,0xFF0000FF,0xFFFFFFFF,0,0x80FF0000,
        0xFFFFFF00,0xFF00FFFF,0xFFFF00FF,0xFF000000,0xFF4080C0,0xFF804020,
        0xFF102030,0xFF304050,0xFF506070,0xFF708090,0xFF90A0B0,0xFFB0C0D0,
        0xFFE0D0C0,0xFFC0B0A0,0xFFA09080,0xFF807060,0xFF605040,0xFF403020];
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static void Check(bool ok,string label) { if(!ok)throw new Exception(label); }
    internal static async Task Run(string[] args)
    {
        string name=args.Length==4?args[1]:"copy";
        var rects = new Dictionary<string,string> {
            ["copy"]="0,0,6,4,0,0,6,4",["crop"]="1,1,3,2,2,1,3,2",["scale"]="0,0,12,8,0,0,6,4",
            ["down"]="0,0,3,2,0,0,6,4",["outside-source"]="0,0,6,4,-1,-1,6,4",
            ["outside-dest"]="-2,-1,6,4,0,0,6,4",["negative"]="6,0,-6,4,0,0,6,4",
            ["source-right"]="0,0,6,4,2,0,6,4",["source-bottom"]="0,0,6,4,0,2,6,4",
            ["dest-right"]="10,0,6,4,0,0,6,4",["dest-bottom"]="0,6,6,4,0,0,6,4",
            ["identity"]="0,0,6,4,0,0,6,4",["swap"]="0,0,6,4,0,0,6,4",["alpha"]="0,0,6,4,0,0,6,4",
            ["bias"]="0,0,6,4,0,0,6,4",["overlap"]="1,0,5,4,0,0,5,4",
            ["zero"]="0,0,0,4,0,0,6,4",["large"]="2147483648,0,6,4,0,0,6,4"
        };
        string dest=name=="overlap"?"10":"11";
        string matrix=name is "identity" or "swap" or "alpha" or "bias"?", CM:0:0":"";
        string init="CM:0:0=256\nCM:1:1=256\nCM:2:2=256\nCM:3:3=256\nCM:4:4=256\n";
        if(name=="swap")init+="CM:0:0=0\nCM:2:2=0\nCM:0:2=256\nCM:2:0=256\n";
        if(name=="alpha")init+="CM:3:3=128\n";
        if(name=="bias")init+="CM:0:4=64\n";
        string operation = name switch {
            "missing-dest"=>"GDRAWG 99,10,0,0,6,4,0,0,6,4",
            "missing-source"=>"GDRAWG 11,99,0,0,6,4,0,0,6,4",
            "negative-id"=>"GDRAWG -9,10,0,0,6,4,0,0,6,4",
            "omitted"=>"GDRAWG 11,10,0,0,6,4,0,0,6",
            "omitted-inner"=>"GDRAWG 11,10,,0,6,4,0,0,6,4",
            "bad-matrix"=>"GDRAWG 11,10,0,0,6,4,0,0,6,4,CM:1:0",
            "wrong-matrix"=>"GDRAWG 11,10,0,0,6,4,0,0,6,4,1",
            "bare-matrix"=>"GDRAWG 11,10,0,0,6,4,0,0,6,4,CM",
            "expression"=>"RESULT = GDRAWG(11,10,0,0,6,4,0,0,6,4)",
            _=>"GDRAWG "+dest+",10,"+rects[name]+matrix
        };
        string root=Path.Combine(Path.GetTempPath(),"web-gdrawg",Guid.NewGuid().ToString("N"));
        foreach(string dir in new[]{"CSV","ERB","resources"})Directory.CreateDirectory(Path.Combine(root,dir));
        File.Copy(Path.Combine(AppContext.BaseDirectory,"p1a-fixture/CSV/GAMEBASE.CSV"),Path.Combine(root,"CSV/GAMEBASE.CSV"));
        File.WriteAllText(Path.Combine(root,"ERB/G.ERH"),"#DIM CM,5,5\n",new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(root,"ERB/G.ERB"),"@SYSTEM_TITLE\nGCREATE 10,6,4\nGDRAWSPRITE 10,\"SOURCE\"\nGCREATE 11,12,8\nGCLEAR 11,0xFF202040\nSPRITECREATE \"VIEW\","+dest+",0,0,"+(dest=="10"?"6,4":"12,8")+"\nSETANIMETIMER 20\nPRINT_IMG \"VIEW\"\nWAIT\n"+init+operation+"\nPRINTFORML DRAW_RESULT={RESULT}\nPRINTL AFTER_DRAW\nINPUT\nQUIT\n",new UTF8Encoding(true));
        using(var bitmap=new SKBitmap(new SKImageInfo(6,4,SKColorType.Rgba8888,SKAlphaType.Unpremul))){for(int y=0;y<4;y++)for(int x=0;x<6;x++)bitmap.SetPixel(x,y,new SKColor(Source[y*6+x]));using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(root,"resources/source.png"),png.ToArray());}
        File.WriteAllText(Path.Combine(root,"resources/sprites.csv"),"SOURCE,source.png,0,0,6,4\n",new UTF8Encoding(true));
        var runtime=await BrowserRuntimeSession.StartPersistentBootstrapAsync(root,null);runtime.StartTitle();
        Check(runtime.Status==BrowserRuntimeStatus.WaitingForInput,"initial image wait "+runtime.Output);
        Type contents=typeof(BrowserRuntimeSession).Assembly.GetType("MinorShift.Emuera.UI.Game.Image.AppContents")!;
        object graphics=contents.GetMethod("GetGraphics",Flags)!.Invoke(null,[long.Parse(dest)])!;
        SKImage Old()=> (SKImage)graphics.GetType().GetProperty("Image")!.GetValue(graphics)!;
        long Generation()=> (long)graphics.GetType().GetProperty("ContentGeneration",Flags)!.GetValue(graphics)!;
        string Url()=> (string)contents.GetMethod("GetSpriteDataUrl",Flags)!.Invoke(null,["VIEW"])!;
        uint[] Pixels(SKImage image){using var bitmap=SKBitmap.FromImage(image);return Enumerable.Range(0,image.Height).SelectMany(y=>Enumerable.Range(0,image.Width).Select(x=>(uint)bitmap.GetPixel(x,y))).ToArray();}
        SKImage old=Old();long generation=Generation();string before=Url();
        var src=contents.GetMethod("GetGraphics",Flags)!.Invoke(null,[10L])!;
        SKImage sourceBefore=(SKImage)src.GetType().GetProperty("Image")!.GetValue(src)!;uint[] sourcePixels=Pixels(sourceBefore);
        Check(runtime.Submit(new InputEnvelope(runtime.PendingInput!.RequestId,"")),"WAIT input accepted");
        bool runtimeFailure=name is "zero" or "large" or "bad-matrix" or "omitted" or "omitted-inner" or "wrong-matrix" or "bare-matrix";
        if(runtimeFailure){Check(runtime.Status==BrowserRuntimeStatus.Failed,"invalid runtime argument rejected");Check(ReferenceEquals(old,Old())&&generation==Generation(),"failed argument keeps destination");Check(old.Handle!=IntPtr.Zero,"failure retains destination ownership");}
        else {
            Check(runtime.Status==BrowserRuntimeStatus.WaitingForInput,"GDRAWG must reach next input: "+runtime.Output);
            bool missing=name is "missing-dest" or "missing-source" or "negative-id";
            Check(runtime.Output.Contains("DRAW_RESULT="+(missing?"0":"1")),"shared RESULT contract "+runtime.Output);
            if(missing){Check(ReferenceEquals(old,Old())&&generation==Generation(),"not-created returns without mutation");}
            else {
                Check(generation+1==Generation(),"one generation update");Check(old.Handle==IntPtr.Zero,"old destination disposed exactly at replacement");Check(!ReferenceEquals(before,Url()),"SpriteG data URL cache invalidated even for a no-op rectangle");
                Check(ReferenceEquals(Url(),Url()),"new generation URL reused");
                uint[] actual=Pixels(Old());
                if(name=="copy"||name=="expression")for(int y=0;y<4;y++)for(int x=0;x<6;x++){uint expected=Source[y*6+x];if(expected==0)expected=0xFF202040;if(expected==0x80FF0000)expected=0xFF901020;Check(actual[y*12+x]==expected,"independent known source-over pixel "+x+","+y);}
                if(name=="dest-right")Check(actual[10]==Source[0]&&actual[11]==Source[1]&&actual[9]==0xFF202040,"independent right clip boundary");
                if(name=="dest-bottom")Check(actual[6*12]==Source[0]&&actual[7*12]==Source[6]&&actual[5*12]==0xFF202040,"independent bottom clip boundary");
                if(args.Length==4){var oracle=JsonDocument.Parse(File.ReadAllText(args[2])).RootElement.EnumerateArray().Single(r=>r.GetProperty("name").GetString()==(name=="expression"?"copy":name));uint[] expected=oracle.GetProperty("pixels").EnumerateArray().Select(p=>p.GetUInt32()).ToArray();Check(actual.SequenceEqual(expected),"all pixels equal actual baseline Native GDRAWG "+name);}
                if(dest!="10"){Check(sourceBefore.Handle!=IntPtr.Zero,"borrowed source not disposed");Check(sourcePixels.SequenceEqual(Pixels(sourceBefore)),"source content unchanged");}
                // Submit後のPublishで既に更新された場合、追加Refreshはfalseでも正しい。
                runtime.RefreshAnimations();
                Check(Flatten(runtime.DisplayLines.SelectMany(l=>l.Parts)).Any(p=>p.ImageDataUrl==Url()),"already displayed SpriteG refreshed");
            }
        }
        if(args.Length==4)File.WriteAllText(args[3],JsonSerializer.Serialize(new{name,status=runtime.Status.ToString(),output=runtime.Output,generationBefore=generation,generationAfter=Generation(),pixels=Pixels(Old()),sourceAlive=sourceBefore.Handle!=IntPtr.Zero},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS actual ERB GDRAWG "+name);
    }
    static IEnumerable<BrowserDisplayPart> Flatten(IEnumerable<BrowserDisplayPart> parts){foreach(var p in parts){yield return p;if(p.Children is not null)foreach(var c in Flatten(p.Children))yield return c;}}
}

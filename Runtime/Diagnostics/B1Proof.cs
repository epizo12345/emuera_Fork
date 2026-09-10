#if R0_B1
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
namespace MinorShift.Emuera.Runtime.Diagnostics;
internal static class B1Proof
{
    internal static bool Active;
    internal static int BridgeAttempts;
    internal static void ForbidBridge(){if(Active){Interlocked.Increment(ref BridgeAttempts);throw new InvalidOperationException("B1 explicit FAIL: production bridge forbidden");}}
    internal static void WriteJson(string path,object value)
    {
        using var output=new FileStream(path,FileMode.Create);
        JsonSerializer.Serialize(output,value,new JsonSerializerOptions{WriteIndented=true});
    }
}
#endif

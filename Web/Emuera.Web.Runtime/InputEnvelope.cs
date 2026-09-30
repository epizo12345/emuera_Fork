namespace MinorShift.Emuera.Web.Runtime;

public sealed record InputEnvelope(
    long RequestId,
    string RawValue,
    BrowserInputSource Source = BrowserInputSource.Keyboard,
    long SessionGeneration = 0,
    long DisplayGeneration = 0);

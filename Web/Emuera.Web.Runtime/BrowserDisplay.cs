namespace MinorShift.Emuera.Web.Runtime;

public enum BrowserDisplayPartKind { Text, Button, NonButton, Image, Shape, Group, Break }
public enum BrowserDisplayMode { Relative, Absolute, AbsoluteLeftTop, AbsoluteLeftBottom }
public enum BrowserInputSource { Keyboard, DisplayButton }

public sealed record BrowserDisplayStyle(
    string Foreground,
    string ButtonColor,
    string? Background = null,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikeout = false,
    string? FontName = null,
    int FontSize = 0);

public sealed record BrowserDisplayLayout(
    BrowserDisplayMode Mode = BrowserDisplayMode.Relative,
    int X = 0,
    int Y = 0,
    int? Width = null,
    int? Height = null,
    int Padding = 0,
    int? BorderWidth = null,
    string? BorderColor = null,
    bool ExplicitPosition = false);

// Activation belongs to input/structure, independent of image-frame redraws.
public sealed record BrowserInputActivation(long SessionGeneration, long DisplayGeneration, long RequestId);

public sealed record BrowserDisplayPart(
    BrowserDisplayPartKind Kind,
    string Text = "",
    string? Input = null,
    bool IsInteger = false,
    string? ImageDataUrl = null,
    int Height = 0,
    int Width = 0,
    BrowserDisplayStyle? Style = null,
    BrowserDisplayLayout? Layout = null,
    string? Tooltip = null,
    BrowserInputActivation? Activation = null,
    IReadOnlyList<BrowserDisplayPart>? Children = null,
    long ButtonGeneration = -1,
    int? LockedX = null,
    double TextPaintScale = 1)
{
    public bool ShouldRenderForNativePaint(bool nativeLinePainted) =>
        nativeLinePainted || Layout?.ExplicitPosition != true;
}

public static class BrowserPartRenderPolicy
{
    public static bool ShouldRender(
        BrowserDisplayPart? renderedPart,
        BrowserDisplayPart part,
        bool callbackChanged,
        bool? renderedNativeLinePainted,
        bool nativeLinePainted,
        int renderedFlowOffsetY,
        int flowOffsetY) =>
        !ReferenceEquals(renderedPart, part)
        || callbackChanged
        || renderedNativeLinePainted != nativeLinePainted
        || renderedFlowOffsetY != flowOffsetY;
}

public sealed record BrowserDisplayLine(string Alignment, IReadOnlyList<BrowserDisplayPart> Parts, long DisplayGeneration = 0, long LineId = 0, bool IsTemporary = false, int IslandFlowOffsetY = 0, bool IsLogicalLine = true)
{
    // Image-only copies retain this creation-time summary; button generations never change on refresh.
    public long MaxButtonGeneration { get; } = MaxGeneration(Parts);
    static long MaxGeneration(IReadOnlyList<BrowserDisplayPart> parts)
    {
        long generation = -1;
        foreach (var part in parts)
        {
            if (part.Input is not null) generation = Math.Max(generation, part.ButtonGeneration);
            if (part.Children is not null) generation = Math.Max(generation, MaxGeneration(part.Children));
        }
        return generation;
    }
}

public enum BrowserPrimitiveInputKind { Timeout, Click, Key }

public sealed record PrimitiveInputEnvelope(
    long RequestId,
    BrowserPrimitiveInputKind Kind,
    string? ButtonValue = null,
    int Code = 0,
    int Result2 = 0,
    int X = 0,
    int Y = 0,
    int ButtonMapValue = -1,
    bool ButtonIsInteger = true,
    long SessionGeneration = 0,
    long DisplayGeneration = 0);

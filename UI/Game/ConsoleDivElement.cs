using System.Drawing;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.UI.Game;

class ConsoleDivElement : AConsoleDisplayNode
{
    readonly AConsoleDisplayNode[] _childNodes;
    public ConsoleDivElement(AConsoleDisplayNode[] childNode, string text)
    {
        _childNodes = childNode;
        Text = text;
    }

    public override bool CanDivide => true;

    public override void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode, bool isButton = false)
    {
        foreach (var childNode in _childNodes)
        {
            childNode.DrawTo(graph, pointY, isSelecting, isBackLog, mode, isButton);
        }
    }

    public override void SetWidth(StringMeasure sm, float subPixel)
    {
        foreach (var childNode in _childNodes)
        {
            childNode.SetWidth(sm, subPixel);
        }
    }
}
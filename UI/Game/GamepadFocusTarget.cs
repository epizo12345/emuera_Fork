using System.Drawing;
using MinorShift.Emuera.UI.Game;

namespace MinorShift.Emuera.GameView;

// [Emuera改修:GAMEPAD-V1]
// 通常Console、HTML、HTML Islandを同じNavigation単位へ変換するデータモデル。
internal enum GamepadFocusSourceType
{
    NormalDisplay = 0,
    HtmlIsland = 1,
}

internal enum GamepadFocusLayoutType
{
    Console = 0,
    Html = 1,
}

/// <summary>
/// ゲームパッドで選択できる1つの入力ボタンと、実際の描画/マウスヒット領域。
/// HTMLボタンは子ノードの矩形をUnionしてBoundsへまとめる。
/// </summary>
internal sealed class GamepadFocusTarget
{
    internal GamepadFocusTarget(
        ConsoleButtonString button,
        GamepadFocusSourceType sourceType,
        GamepadFocusLayoutType layoutType,
        int groupId,
        ConsoleDisplayLine parentLine,
        Rectangle bounds,
        RectangleF rawBounds,
        int order)
    {
        Button = button;
        SourceType = sourceType;
        LayoutType = layoutType;
        GroupId = groupId;
        ParentLine = parentLine;
        Bounds = bounds;
        RawBounds = rawBounds;
        Order = order;
    }

    internal ConsoleButtonString Button { get; }
    internal GamepadFocusSourceType SourceType { get; }
    internal GamepadFocusLayoutType LayoutType { get; set; }
    internal int GroupId { get; }
    internal ConsoleDisplayLine ParentLine { get; }
    internal int LineNo => ParentLine?.LineNo ?? -1;
    internal Rectangle Bounds { get; set; }
    internal RectangleF RawBounds { get; set; }
    internal int Order { get; }
    internal int Row { get; set; }
    internal int Column { get; set; }
    internal int NavigationGroupId { get; set; }
    internal bool Enabled { get; set; } = true;
    internal bool IsBack { get; set; }
    internal GamepadFocusTarget Up { get; set; }
    internal GamepadFocusTarget Down { get; set; }
    internal GamepadFocusTarget Left { get; set; }
    internal GamepadFocusTarget Right { get; set; }

    internal int CenterX => Bounds.Left + Bounds.Width / 2;
    internal int CenterY => Bounds.Top + Bounds.Height / 2;
    internal string SourceName => SourceType == GamepadFocusSourceType.NormalDisplay
        ? "NormalDisplay"
        : "HtmlIsland";

    internal GamepadFocusTarget GetNeighbor(GamepadDirection direction)
    {
        return direction switch
        {
            GamepadDirection.Up => Up,
            GamepadDirection.Down => Down,
            GamepadDirection.Left => Left,
            GamepadDirection.Right => Right,
            _ => null,
        };
    }
}

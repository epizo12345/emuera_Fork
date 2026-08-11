#nullable enable

using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.UI.Game;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace MinorShift.Emuera.GameView;

// [Emuera改修:GAMEPAD-V1]
// 画面固有番号ではなく、現在描画されている行構造とBoundsから移動先を決定する。
/// <summary>
/// 描画済みボタンの実座標から、上下左右の移動先を一度だけ構築する。
/// 通常ConsoleはConsoleDisplayLineを構造上の行として優先し、HTML_PRINTは
/// 1つのDisplayLine内に複数の見た目上の行を持てるため実描画矩形を使う。
/// </summary>
internal sealed class GamepadNavigationGraph
{
    private readonly Dictionary<ConsoleButtonString, GamepadFocusTarget> byButton = [];
    private readonly Dictionary<(GamepadFocusSourceType Source, int Group), List<GamepadFocusTarget>> groups = [];
    private static bool selfTestsRun;

    internal void Build(List<GamepadFocusTarget> targets, Action<string>? diagnostic)
    {
        byButton.Clear();
        groups.Clear();

        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            target.Up = null;
            target.Down = null;
            target.Left = null;
            target.Right = null;
            target.Row = 0;
            target.Column = 0;
            target.NavigationGroupId = -1;
            if (target.IsDirectionalFocusExcluded)
                continue;
            byButton.TryAdd(target.Button, target);

            var key = (target.SourceType, target.GroupId);
            if (!groups.TryGetValue(key, out List<GamepadFocusTarget>? group))
            {
                group = [];
                groups.Add(key, group);
            }
            group.Add(target);
        }

        foreach (KeyValuePair<(GamepadFocusSourceType Source, int Group), List<GamepadFocusTarget>> pair in groups)
            BuildGroup(pair.Value);

        if (diagnostic != null)
        {
            if (!selfTestsRun)
            {
                selfTestsRun = true;
                RunSelfTests(diagnostic);
            }
            Validate(targets, diagnostic);
        }
    }

    internal GamepadFocusTarget? Find(ConsoleButtonString? button)
    {
        if (button == null)
            return null;
        byButton.TryGetValue(button, out GamepadFocusTarget? target);
        return target;
    }

    private static void BuildGroup(List<GamepadFocusTarget> targets)
    {
        targets.Sort(CompareVisualOrder);
        int tolerance = Math.Max(2, Config.LineHeight / 3);
        List<List<GamepadFocusTarget>> groupRows = [];
        List<GamepadFocusTarget>? currentRow = null;
        int rowAnchorY = int.MinValue;
        Rectangle rowAnchorBounds = Rectangle.Empty;
        GamepadFocusLayoutType rowAnchorLayout = GamepadFocusLayoutType.Console;
        int rowAnchorLineNo = -1;

        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            bool verticallyOverlaps = currentRow != null
                && Math.Min(rowAnchorBounds.Bottom, target.Bounds.Bottom)
                    - Math.Max(rowAnchorBounds.Top, target.Bounds.Top)
                    >= Math.Max(1, Math.Min(rowAnchorBounds.Height, target.Bounds.Height) / 2);
            bool bothStructuredConsoleRows = currentRow != null
                && rowAnchorLayout == GamepadFocusLayoutType.Console
                && target.LayoutType == GamepadFocusLayoutType.Console
                && rowAnchorLineNo >= 0 && target.LineNo >= 0;
            bool startsNewRow = currentRow == null
                || (bothStructuredConsoleRows
                    ? target.LineNo != rowAnchorLineNo
                    : Math.Abs(target.CenterY - rowAnchorY) > tolerance && !verticallyOverlaps);
            if (startsNewRow)
            {
                currentRow = [];
                groupRows.Add(currentRow);
                rowAnchorY = target.CenterY;
                rowAnchorBounds = target.Bounds;
                rowAnchorLayout = target.LayoutType;
                rowAnchorLineNo = target.LineNo;
            }
            currentRow!.Add(target);
        }

        for (int rowIndex = 0; rowIndex < groupRows.Count; rowIndex++)
        {
            List<GamepadFocusTarget> row = groupRows[rowIndex];
            row.Sort(CompareHorizontalOrder);
            for (int column = 0; column < row.Count; column++)
            {
                GamepadFocusTarget target = row[column];
                target.Row = rowIndex;
                target.Column = column;
                if (column > 0 && IsReasonableHorizontalNeighbor(row[column - 1], target))
                    target.Left = row[column - 1];
                if (column + 1 < row.Count && IsReasonableHorizontalNeighbor(target, row[column + 1]))
                    target.Right = row[column + 1];
            }
        }
        for (int rowIndex = 0; rowIndex < groupRows.Count; rowIndex++)
        {
            List<GamepadFocusTarget> row = groupRows[rowIndex];
            for (int column = 0; column < row.Count; column++)
            {
                GamepadFocusTarget target = row[column];
                if (rowIndex > 0)
                    target.Up = FindVerticalCandidate(target, groupRows, rowIndex - 1, -1);
                if (rowIndex + 1 < groupRows.Count)
                    target.Down = FindVerticalCandidate(target, groupRows, rowIndex + 1, 1);
            }
        }

        // Region IDs are connected components of the completed graph.  Assigning
        // them before vertical links exist incorrectly splits a grid by row.
        AssignNavigationRegions(targets);
    }

    private static void RunSelfTests(Action<string> diagnostic)
    {
        string[] inputs = ["7", "8", "9", "4", "5", "6", "1", "2", "3", "0"];
        List<GamepadFocusTarget> targets = [];
        for (int i = 0; i < inputs.Length; i++)
        {
            int row = i / 3;
            int column = i % 3;
            if (i == 9)
                column = 0;
            ConsoleButtonString button = new(null, [], inputs[i]);
            Rectangle bounds = new(column * 40, row * 30, 30, 20);
            targets.Add(new GamepadFocusTarget(button, GamepadFocusSourceType.NormalDisplay,
                GamepadFocusLayoutType.Html, 0, null, bounds, bounds, i));
        }

        GamepadNavigationGraph graph = new();
        graph.Build(targets, null);
        bool keypad = targets[0].Down == targets[3] && targets[3].Down == targets[6]
            && targets[1].Down == targets[4] && targets[4].Down == targets[7]
            && targets[2].Down == targets[5] && targets[5].Down == targets[8];
        bool horizontal = targets[0].Right == targets[1] && targets[1].Left == targets[0];
        bool rowsCorrect = targets[0].Row == 0 && targets[3].Row == 1
            && targets[6].Row == 2 && targets[9].Row == 3;
        diagnostic("Navigation graph self-test (list/grid/keypad): "
            + (keypad && horizontal && rowsCorrect ? "PASS" : "WARNING"));

        // Deliberately interleave three visual lanes. B1/B2/B3 occupy rows
        // 1/5/8, so vertical navigation must skip unrelated A/C rows while
        // continuing the forward row scan. C1/C2 verifies a sparse right lane.
        List<GamepadFocusTarget> panelTargets =
        [
            CreateSelfTestTarget("B1", 220, 10, 0),
            CreateSelfTestTarget("A1", 10, 35, 1),
            CreateSelfTestTarget("C1", 900, 60, 2),
            CreateSelfTestTarget("A2", 10, 85, 3),
            CreateSelfTestTarget("B2", 220, 110, 4),
            CreateSelfTestTarget("A3", 10, 135, 5),
            CreateSelfTestTarget("C2", 900, 160, 6),
            CreateSelfTestTarget("B3", 220, 185, 7),
        ];
        GamepadNavigationGraph panelGraph = new();
        List<string> panelDiagnostics = [];
        panelGraph.Build(panelTargets, panelDiagnostics.Add);
        GamepadFocusTarget b1 = panelTargets[0];
        GamepadFocusTarget b2 = panelTargets[4];
        GamepadFocusTarget b3 = panelTargets[7];
        GamepadFocusTarget c1 = panelTargets[2];
        GamepadFocusTarget c2 = panelTargets[6];
        bool skippedRows = b2.Row > b1.Row + 1 && b3.Row > b2.Row + 1;
        bool panelLanes = b1.Down == b2 && b2.Down == b3 && c1.Down == c2
            && b1.Down != panelTargets[1] && b1.Down != panelTargets[2]
            && b1.Down != panelTargets[3];
        bool rowSkipAccepted = true;
        for (int i = 0; i < panelDiagnostics.Count; i++)
        {
            if (panelDiagnostics[i].Contains("invalid Up link", StringComparison.Ordinal)
                || panelDiagnostics[i].Contains("invalid Down link", StringComparison.Ordinal))
            {
                rowSkipAccepted = false;
                break;
            }
        }

        // This is intentionally inverted: it must be detected as invalid.
        GamepadFocusTarget? originalDown = b2.Down;
        b2.Down = b1;
        bool reverseDetected = ValidateLink(b2, b2.Down, GamepadDirection.Down, _ => { }) == 1;
        b2.Down = originalDown;
        diagnostic("Navigation graph self-test (independent vertical lanes / row skip validation): "
            + (panelLanes && skippedRows && rowSkipAccepted && reverseDetected ? "PASS" : "WARNING"));
    }

    private static GamepadFocusTarget CreateSelfTestTarget(string input, int x, int y, int order)
    {
        ConsoleButtonString button = new(null, [], input);
        Rectangle bounds = new(x, y, 30, 12);
        return new GamepadFocusTarget(button, GamepadFocusSourceType.NormalDisplay,
            GamepadFocusLayoutType.Html, 0, null, bounds, bounds, order);
    }

    private static GamepadFocusTarget FindVerticalCandidate(GamepadFocusTarget current,
        List<List<GamepadFocusTarget>> groupRows, int startRow, int rowStep)
    {
        for (int rowIndex = startRow; rowIndex >= 0 && rowIndex < groupRows.Count; rowIndex += rowStep)
        {
            if (TryFindVerticalCandidateInRow(current, groupRows[rowIndex], out GamepadFocusTarget? candidate))
                return candidate!;
        }
        return null!;
    }

    private static bool TryFindVerticalCandidateInRow(GamepadFocusTarget current,
        List<GamepadFocusTarget> candidates, out GamepadFocusTarget? result)
    {
        GamepadFocusTarget? best = null;
        int bestLanePriority = -1;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            GamepadFocusTarget candidate = candidates[i];
            bool overlaps = candidate.Bounds.Left < current.Bounds.Right
                && current.Bounds.Left < candidate.Bounds.Right;
            int lanePriority = GetVerticalLanePriority(current, candidate, overlaps);
            if (lanePriority < 0)
                continue;
            int distance = Math.Abs(candidate.CenterX - current.CenterX);
            if (best == null || lanePriority > bestLanePriority
                || (lanePriority == bestLanePriority && distance < bestDistance)
                || (lanePriority == bestLanePriority && distance == bestDistance && candidate.Order < best.Order))
            {
                best = candidate;
                bestLanePriority = lanePriority;
                bestDistance = distance;
            }
        }
        result = best;
        return best != null;
    }

    private static int GetVerticalLanePriority(GamepadFocusTarget current,
        GamepadFocusTarget candidate, bool overlaps)
    {
        // A real horizontal overlap is the strongest indication that two
        // controls occupy the same vertical lane.  Do not let a nearby panel
        // win merely because its center happens to be closer.
        if (overlaps)
            return 3;

        int leftDelta = Math.Abs(candidate.Bounds.Left - current.Bounds.Left);
        int leftTolerance = Math.Max(2, Config.LineHeight / 3);
        if (leftDelta <= leftTolerance)
            return 2;

        // Some controls have different widths or are rendered a few pixels
        // off their lane anchor.  Keep a deliberately narrow CenterX fallback,
        // and also require the rectangles to be nearly adjacent.  The old
        // width/LineHeight-sized gap accepted separate training panels.
        int centerDistance = Math.Abs(candidate.CenterX - current.CenterX);
        int centerTolerance = Math.Max(4, Config.LineHeight / 3);
        int horizontalGap = Math.Max(0,
            Math.Max(current.Bounds.Left, candidate.Bounds.Left)
                - Math.Min(current.Bounds.Right, candidate.Bounds.Right));
        int gapTolerance = Math.Max(2, Config.LineHeight / 4);
        return centerDistance <= centerTolerance && horizontalGap <= gapTolerance ? 1 : -1;
    }

    private static bool IsReasonableHorizontalNeighbor(GamepadFocusTarget left, GamepadFocusTarget right)
    {
        int gap = Math.Max(0, Math.Max(left.Bounds.Left, right.Bounds.Left)
            - Math.Min(left.Bounds.Right, right.Bounds.Right));
        int threshold = Math.Max(Config.LineHeight * 8,
            Math.Max(left.Bounds.Width, right.Bounds.Width) * 4);
        return gap <= threshold;
    }

    private static void AssignNavigationRegions(List<GamepadFocusTarget> targets)
    {
        int nextRegion = 0;
        Queue<GamepadFocusTarget> queue = new();
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget start = targets[i];
            if (start.NavigationGroupId >= 0)
                continue;
            start.NavigationGroupId = nextRegion++;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                GamepadFocusTarget current = queue.Dequeue();
                AddRegionNeighbor(current, current.Up, queue);
                AddRegionNeighbor(current, current.Down, queue);
                AddRegionNeighbor(current, current.Left, queue);
                AddRegionNeighbor(current, current.Right, queue);
                // Asymmetrical nearest-column links are possible. Include incoming links too.
                for (int candidateIndex = 0; candidateIndex < targets.Count; candidateIndex++)
                {
                    GamepadFocusTarget candidate = targets[candidateIndex];
                    if (candidate.NavigationGroupId >= 0)
                        continue;
                    if (candidate.Up == current || candidate.Down == current
                        || candidate.Left == current || candidate.Right == current)
                        AddRegionNeighbor(current, candidate, queue);
                }
            }
        }
    }

    private static void AddRegionNeighbor(GamepadFocusTarget current, GamepadFocusTarget? candidate,
        Queue<GamepadFocusTarget> queue)
    {
        if (candidate == null || candidate.NavigationGroupId >= 0
            || candidate.SourceType != current.SourceType || candidate.GroupId != current.GroupId)
            return;
        candidate.NavigationGroupId = current.NavigationGroupId;
        queue.Enqueue(candidate);
    }

    private static int CompareVisualOrder(GamepadFocusTarget left, GamepadFocusTarget right)
    {
        int result = left.Bounds.Top.CompareTo(right.Bounds.Top);
        if (result != 0)
            return result;
        result = left.Bounds.Left.CompareTo(right.Bounds.Left);
        return result != 0 ? result : left.Order.CompareTo(right.Order);
    }

    private static int CompareHorizontalOrder(GamepadFocusTarget left, GamepadFocusTarget right)
    {
        int result = left.Bounds.Left.CompareTo(right.Bounds.Left);
        return result != 0 ? result : left.Order.CompareTo(right.Order);
    }

    private static void Validate(List<GamepadFocusTarget> targets, Action<string> diagnostic)
    {
        int warningCount = 0;
        HashSet<(GamepadFocusSourceType Source, int BaseGroup, int Group)> regions = [];
        for (int i = 0; i < targets.Count; i++)
        {
            GamepadFocusTarget target = targets[i];
            if (target.IsDirectionalFocusExcluded)
                continue;
            regions.Add((target.SourceType, target.GroupId, target.NavigationGroupId));
            warningCount += ValidateLink(target, target.Left, GamepadDirection.Left, diagnostic);
            warningCount += ValidateLink(target, target.Right, GamepadDirection.Right, diagnostic);
            warningCount += ValidateLink(target, target.Up, GamepadDirection.Up, diagnostic);
            warningCount += ValidateLink(target, target.Down, GamepadDirection.Down, diagnostic);
            if (targets.Count > 1 && target.Up == null && target.Down == null
                && target.Left == null && target.Right == null)
            {
                diagnostic($"Navigation graph warning: isolated enabled target {Describe(target)}");
                warningCount++;
            }
            string text = target.Button.ToString() ?? string.Empty;
            bool clearlyBack = text.Contains("CANCEL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("BACK", StringComparison.OrdinalIgnoreCase)
                || text.Contains("RETURN", StringComparison.OrdinalIgnoreCase)
                || text.Contains("戻る", StringComparison.Ordinal)
                || text.Contains("帰る", StringComparison.Ordinal)
                || text.Contains("店を出る", StringComparison.Ordinal);
            if (clearlyBack && !target.IsBack)
            {
                diagnostic($"Navigation graph warning: explicit back label was not classified as Back {Describe(target)}");
                warningCount++;
            }
        }
        diagnostic($"Navigation graph validation: targets={targets.Count}, groups={regions.Count}, warnings={warningCount}");
        ValidateNumericKeypad(targets, diagnostic);
    }

    private static int ValidateLink(GamepadFocusTarget from, GamepadFocusTarget? to,
        GamepadDirection direction, Action<string> diagnostic)
    {
        if (to == null)
            return 0;
        bool validGroup = from.SourceType == to.SourceType && from.GroupId == to.GroupId;
        bool validGeometry = direction switch
        {
            GamepadDirection.Left => to.Row == from.Row && to.CenterX < from.CenterX,
            GamepadDirection.Right => to.Row == from.Row && to.CenterX > from.CenterX,
            // FindVerticalCandidate deliberately scans beyond adjacent visual
            // rows when an intervening row has no candidate in this lane.
            // Validation must preserve that legitimate row skip.
            // Normal Console targets can share a display rectangle even when
            // their structured LineNo rows differ. Row is the canonical
            // vertical ordering for this graph, so do not reintroduce a
            // drawing-coordinate requirement in diagnostics.
            GamepadDirection.Up => to.Row < from.Row,
            GamepadDirection.Down => to.Row > from.Row,
            _ => true,
        };
        if (validGroup && validGeometry)
            return 0;
        diagnostic($"Navigation graph warning: invalid {direction} link {Describe(from)} -> {Describe(to)}");
        return 1;
    }

    private static void ValidateNumericKeypad(List<GamepadFocusTarget> targets, Action<string> diagnostic)
    {
        GamepadFocusTarget? seven = FindInput(targets, "7");
        GamepadFocusTarget? eight = FindInput(targets, "8");
        GamepadFocusTarget? nine = FindInput(targets, "9");
        GamepadFocusTarget? four = FindInput(targets, "4");
        GamepadFocusTarget? five = FindInput(targets, "5");
        GamepadFocusTarget? six = FindInput(targets, "6");
        GamepadFocusTarget? one = FindInput(targets, "1");
        GamepadFocusTarget? two = FindInput(targets, "2");
        GamepadFocusTarget? three = FindInput(targets, "3");
        if (seven == null || eight == null || nine == null || four == null || five == null || six == null
            || one == null || two == null || three == null)
            return;
        if (seven.Row != eight.Row || eight.Row != nine.Row || four.Row != five.Row || five.Row != six.Row
            || one.Row != two.Row || two.Row != three.Row)
            return;
        bool valid = seven.Down == four && four.Down == one
            && eight.Down == five && five.Down == two
            && nine.Down == six && six.Down == three;
        diagnostic("Navigation keypad consistency: " + (valid ? "PASS" : "WARNING vertical links do not follow 7-4-1 / 8-5-2 / 9-6-3"));
    }

    private static GamepadFocusTarget? FindInput(List<GamepadFocusTarget> targets, string input)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            ConsoleButtonString button = targets[i].Button;
            string value = button.IsInteger ? button.Input.ToString() : button.Inputs ?? string.Empty;
            if (string.Equals(value, input, StringComparison.Ordinal))
                return targets[i];
        }
        return null;
    }

    private static string Describe(GamepadFocusTarget target)
    {
        Rectangle rect = target.Bounds;
        return $"source={target.SourceName}/baseGroup={target.GroupId}/group={target.NavigationGroupId}/row={target.Row}/column={target.Column}/rect=({rect.X},{rect.Y},{rect.Width},{rect.Height})";
    }
}

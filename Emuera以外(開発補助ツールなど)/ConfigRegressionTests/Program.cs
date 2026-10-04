using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using MinorShift.Emuera;
using MinorShift.Emuera.Forms;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI;
using MinorShift.Emuera.UI.Framework;
using MinorShift.Emuera.UI.Game;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using RuntimeConfig = MinorShift.Emuera.Runtime.Config.Config;

namespace Emuera.ConfigRegressionTests;

// [Emuera改修:TOOLS-05]
// JSON責務、LazyERB path safety、設定UIの回帰を一時ディレクトリ上で再実行する補助テスト。
internal static class Program
{
    private static readonly List<string> Failures = [];
    private static int TotalCases;
    private static int SkippedCases;
    private static string? _themeConfigDirectory;
    private static string? _themeConfigPath;

    [STAThread]
    private static int Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Run("dark mode config defaults to YES, round-trips YES/NO, and preserves display colors and other settings", DarkModeConfigRoundTrips);
        Run("non-square vertical flip preserves four corners in three sprite paths", IntegratedVerticalFlip);
        Run("non-identity color matrix swaps red and blue channels", IntegratedColorMatrix);
        Run("mask rejects out-of-range coordinates without changing pixels", IntegratedMaskBounds);
        Run("mask composes alpha at offset and preserves outside pixels", IntegratedMaskAlpha);
        Run("file-backed mask output survives PNG save and load", IntegratedMaskFileBacked);
        Run("nonuniform self mask keeps four source pixels", IntegratedMaskSelf);
        Run("missing game block persists disabled-empty default and retains unknown properties", MissingGameBlockDefault);
        Run("HTML island preserves first, last, nested, and tooltip-only hit targets", IntegratedIslandHits);
        Run("timer tick cannot reenter through drawing DoEvents", TimerTickDoesNotReenter);
        Run("stale timer callback cannot stop the next timed input", StaleTimerCannotStopNextInput);
        Run("two distinct image frames render during timed input and after transition", () => AnimatedFramesContinueAcrossTimedInput(true));
        Run("two distinct image frames render during timed input without remaining text", () => AnimatedFramesContinueAcrossTimedInput(false));
        Run("fallback font runs and split fragments retain each Unicode scalar exactly once", FontFallbackRunsAndSplits);
        Run("fallback font retains the selected hinting and edging settings", FontFallbackKeepsAntialiasSettings);
        Run("PrintStringBuffer wraps at the fallback glyph's drawn advance", PrintBufferWrapsAtFallbackAdvance);
        Run("AAAA emoji stays on the first line while following BBBB wraps", EmojiBoundaryWrapsBeforeB);
        Run("splitting inside a surrogate pair keeps the whole fallback rune", SplitInsideSurrogateKeepsRune);
        Run("wrapped fallback buttons keep text, positions, hit ranges, and selected color", WrappedFallbackButtonsDrawAndHit);
        Run("backlog color changes without losing fallback runs", FontBacklogColorAndRuns);
        Run("large fallback font wraps by the drawn advance", LargeFallbackFontWrapsByDrawnAdvance);
        Run("splitting at the final code unit leaves an empty suffix", FontTailSplitHasEmptySuffix);
        Run("repeated splits preserve every fallback run", FontRepeatedSplitsPreserveRuns);
        Run("integer width bounds fractional run advances", FontIntegerWidthBoundsFractionalAdvance);
        Run("valid existing game block is unchanged by load", ExistingGameBlockIsNotRewritten);
        Run("new user file does not gain a LazyERB override", NewUserFileHasNoOverride);
        Run("absent user block uses the complete game value", MissingUserUsesGameValue);
        Run("present false user block completely overrides the game value", FalseUserValueOverridesGame);
        Run("normal save preserves game and user unknown properties", SavePreservesUnknownProperties);

        foreach (string malformed in new[] { "null", "7", "{\"有効\":\"invalid\"}", "{\"フォルダ\":[1]}" })
            Run($"malformed game block {malformed} is not rewritten", () => MalformedBlockIsNotRewritten("game", malformed));

        foreach (string malformed in new[] { "null", "7", "{\"有効\":\"invalid\"}", "{\"フォルダ\":[1]}" })
            Run($"malformed user block {malformed} is not rewritten", () => MalformedBlockIsNotRewritten("user", malformed));

        Run("user LazyERB override staging saves a complete object and retains nested unknown values", StagedOverrideRetainsUnknownValues);
        Run("reset deletes the complete user LazyERB block only", ResetRemovesUserBlock);
        Run("saved override and reset change runtime-effective settings only after reload", SavedOverrideIsRestartOnly);
        Run("partial user block uses its defaults instead of merging game values", PartialUserBlockDoesNotMerge);
        Run("legacy LazyErb migration preserves values and unknown properties", LegacyBlockMigrates);
        Run("current block wins over legacy known values while retaining legacy unknowns", CurrentBlockWinsOverLegacy);
        Run("legacy null is removed while a valid current block is preserved", LegacyNullWithCurrentBlockIsCompatible);
        Run("legacy null without a current block uses legacy migration defaults", LegacyNullWithoutCurrentBlockIsCompatible);
        Run("explicit-null known game fields are invalid and unchanged", () => MalformedBlockIsNotRewritten("game", "{\"有効\":null,\"フォルダ\":[]}"));
        Run("explicit-null known user fields are invalid and unchanged", () => MalformedBlockIsNotRewritten("user", "{\"有効\":null,\"フォルダ\":[]}"));
        Run("malformed legacy block is not rewritten", MalformedLegacyIsNotRewritten);
        Run("directory policy accepts safe ERB descendants and rejects lexical escapes", DirectoryPathSafetyRules);
        Run("directory policy rejects existing files and sibling-prefix escapes", DirectoryPathSafetyRejectsFilesAndSiblingPrefixes);
        Run("runtime policy uses the complete user override and rejects unselected folders", RuntimePolicyUsesEffectiveOverrideAndConfiguredPath);
        Run("current runtime gates still force eager loading", RuntimePolicyRetainsEagerGates);
        Run("folder selection normalizes paths and removes descendants of selected parents", FolderSelectionNormalization);
        Run("folder dialog lists directories only and commits checked folders only on OK", FolderDialogShowsDirectoriesAndCommitsOnOk);
        Run("parent checks cascade downward and partial child-off preserves siblings", FolderDialogCheckChangesCascade);
        Run("saved parent selection is shown on descendants but commits only the parent", FolderDialogSelectedParentExpandsAndCommits);
        Run("checkbox double-click suppression targets only state images", CheckboxDoubleClickSuppressionTargetsStateImages);
        Run("cancel returns no selected-folder result", FolderDialogCancelReturnsNoSelection);
        Run("settings dialog exposes LazyERB startup tab without saving radio edits", ConfigDialogLazyErbTabDoesNotSaveUntilConfirmation);
        RunReparsePointCase();

        if (_themeConfigDirectory is not null && Directory.Exists(_themeConfigDirectory))
            Directory.Delete(_themeConfigDirectory, recursive: true);

        Console.WriteLine($"RESULT: {TotalCases - Failures.Count - SkippedCases}/{TotalCases} passed; {Failures.Count} failed; {SkippedCases} skipped.");
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void FontFallbackRunsAndSplits()
    {
        var style = new StringStyle(Color.White, FontStyle.Bold | FontStyle.Italic, "Consolas");
        SKFont baseFont = FontFactory.GetFont(style);
        int codepoint = new[] { 0x1F600, 0x10348, 0x1F9EA, 0x4E2D }
            .FirstOrDefault(value => !baseFont.ContainsGlyph(value) &&
                SKFontManager.Default.MatchCharacter(value) is SKTypeface matched && matched != baseFont.Typeface);
        if (codepoint == 0)
            throw new TestUnavailableException("no actual fallback font for the tested Unicode scalars");

        string fallback = char.ConvertFromUtf32(codepoint);
        string source = "AAAA" + fallback + "BBBB";
        var styled = new ConsoleStyledString(source, style);
        var field = typeof(ConsoleStyledString).GetField("_texts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var runs = (List<TextsWithFont>)field.GetValue(styled)!;
        Console.WriteLine($"FONT_FALLBACK_CP=U+{codepoint:X} FAMILY={runs[1].Font.Typeface.FamilyName} RUNS={runs.Count}");
        SequenceEqual(new[] { "AAAA", fallback, "BBBB" }, runs.Select(run => run.Text), "A-fallback-A run boundaries");
        True(runs[1].Font.Typeface != baseFont.Typeface, "middle run uses an actual fallback typeface");

        ConsoleStyledString suffix = styled.DivideAt(4)!;
        ConsoleStyledString trailing = suffix.DivideAt(fallback.Length)!;
        SequenceEqual(new[] { "AAAA", fallback, "BBBB" }, new[] { styled.Text, suffix.Text, trailing.Text },
            "split text keeps the source exactly once");
        foreach (var fragment in new[] { styled, suffix, trailing })
        {
            var fragmentRuns = (List<TextsWithFont>)field.GetValue(fragment)!;
            Equal(fragment.Text, string.Concat(fragmentRuns.Select(run => run.Text)), "split retains fallback drawing runs");
        }
    }

    private static void EmojiBoundaryWrapsBeforeB()
    {
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas") { FontSize = 36 };
        var styled = new ConsoleStyledString("AAAA😀BBBB", style);
        var runs = (List<TextsWithFont>)typeof(ConsoleStyledString)
            .GetField("_texts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(styled)!;
        if (runs.Count != 3 || runs[1].Text != "😀" || runs[0].Font.Typeface == runs[1].Font.Typeface)
            throw new TestUnavailableException("emoji fallback did not occur with the installed fonts");

        float prefixWidth = runs[0].Font.GetGlyphWidths(runs[0].Text).Sum() +
            runs[1].Font.GetGlyphWidths(runs[1].Text).Sum();
        float nextWidth = runs[2].Font.GetGlyphWidths("B").Sum();
        int limit = Enumerable.Range(20, 900).FirstOrDefault(width =>
            prefixWidth <= width - 1 && prefixWidth + nextWidth > width - 1);
        if (limit == 0)
            throw new TestUnavailableException("no integer drawable width separates emoji from the following B");

        PropertyInfo widthProperty = typeof(RuntimeConfig).GetProperty("DrawableWidth", BindingFlags.Static | BindingFlags.Public)!;
        int previousWidth = (int)widthProperty.GetValue(null)!;
        try
        {
            widthProperty.GetSetMethod(true)!.Invoke(null, [limit]);
            using var window = new MainWindow([]);
            var console = (EmueraConsole)typeof(MainWindow)
                .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var buffer = new PrintStringBuffer(console);
            buffer.AppendButton("AAAA😀BBBB", style, 1L);
            ConsoleDisplayLine[] lines = buffer.Flush(null!, false);
            Equal(2, lines.Length, "boundary produces two display lines");
            Equal("AAAA😀", lines[0].ToString(), "emoji remains entirely on the first line");
            Equal("BBBB", lines[1].ToString(), "the following B begins the second line");
            Console.WriteLine($"FONT_EMOJI_BOUNDARY_LIMIT={limit} PREFIX_WIDTH={prefixWidth:F3} NEXT_B_WIDTH={nextWidth:F3}");
        }
        finally { widthProperty.GetSetMethod(true)!.Invoke(null, [previousWidth]); }
    }

    private static void FontFallbackKeepsAntialiasSettings()
    {
        FontAntialias previous = JSONConfig.Game.FontAntialias;
        try
        {
            foreach (FontAntialias setting in new[] { FontAntialias.None, FontAntialias.Full })
            {
                JSONConfig.Game.FontAntialias = setting;
                var style = new StringStyle(Color.White, FontStyle.Bold | FontStyle.Italic, "Consolas")
                {
                    FontSize = 67 + (int)setting
                };
                SKFont baseFont = FontFactory.GetFont(style);
                int codepoint = new[] { 0x1F600, 0x10348, 0x1F9EA, 0x4E2D }
                    .FirstOrDefault(value => !baseFont.ContainsGlyph(value) &&
                        SKFontManager.Default.MatchCharacter(value) is SKTypeface matched && matched != baseFont.Typeface);
                if (codepoint == 0)
                    throw new TestUnavailableException("no actual fallback font for the anti-alias test");
                var styled = new ConsoleStyledString("A" + char.ConvertFromUtf32(codepoint) + "A", style);
                var runs = (List<TextsWithFont>)typeof(ConsoleStyledString)
                    .GetField("_texts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(styled)!;
                SKFont fallbackFont = runs[1].Font;
                Console.WriteLine($"FONT_AA={setting} BASE={baseFont.Hinting}/{baseFont.Edging} " +
                    $"FALLBACK={fallbackFont.Hinting}/{fallbackFont.Edging} " +
                    $"BASE_SIZE={baseFont.Size:F2} FALLBACK_SIZE={fallbackFont.Size:F2}");
                Equal(baseFont.Hinting, fallbackFont.Hinting, "fallback hinting follows the selected setting");
                Equal(baseFont.Edging, fallbackFont.Edging, "fallback edging follows the selected setting");
                Equal(baseFont.SkewX, fallbackFont.SkewX, "italic skew survives fallback");
            }
        }
        finally
        {
            JSONConfig.Game.FontAntialias = previous;
        }
    }

    private static void PrintBufferWrapsAtFallbackAdvance()
    {
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas") { FontSize = 36 };
        SKFont baseFont = FontFactory.GetFont(style);
        int codepoint = new[] { 0x1F600, 0x10348, 0x1F9EA, 0x4E2D }
            .FirstOrDefault(value => !baseFont.ContainsGlyph(value) &&
                SKFontManager.Default.MatchCharacter(value) is SKTypeface matched && matched != baseFont.Typeface);
        if (codepoint == 0)
            throw new TestUnavailableException("no actual fallback font for the wrap test");

        string source = "AAAA" + char.ConvertFromUtf32(codepoint) + "BBBB";
        var styled = new ConsoleStyledString(source, style);
        var runs = (List<TextsWithFont>)typeof(ConsoleStyledString)
            .GetField("_texts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(styled)!;
        var runeAdvances = runs.SelectMany(run => run.Text.EnumerateRunes()
            .Select(rune => (Utf16Length: rune.Utf16SequenceLength,
                Advance: run.Font.GetGlyphWidths(rune.ToString()).Sum()))).ToArray();
        int limit = -1;
        int expectedIndex = -1;
        int baseBreak = -1;
        for (int candidateLimit = 20; candidateLimit < 350; candidateLimit++)
        {
            float advance = 0;
            int index = 0;
            foreach (var glyph in runeAdvances)
            {
                if (advance + glyph.Advance > candidateLimit - 1)
                    break;
                advance += glyph.Advance;
                index += glyph.Utf16Length;
            }
            int wrongBreak = baseFont.BreakText(source, candidateLimit);
            if (index > 0 && index < source.Length && wrongBreak != index)
            {
                limit = candidateLimit;
                expectedIndex = index;
                baseBreak = wrongBreak;
                break;
            }
        }
        if (limit < 0)
            throw new TestUnavailableException("installed fallback widths do not expose a wrap mismatch");
        Console.WriteLine($"FONT_WRAP_LIMIT={limit} ACTUAL_PREFIX={expectedIndex} BASE_BREAK={baseBreak} " +
            $"FALLBACK_FAMILY={runs[1].Font.Typeface.FamilyName}");

        PropertyInfo widthProperty = typeof(RuntimeConfig).GetProperty("DrawableWidth", BindingFlags.Static | BindingFlags.Public)!;
        PropertyInfo wrapProperty = typeof(RuntimeConfig).GetProperty("ButtonWrap", BindingFlags.Static | BindingFlags.Public)!;
        int oldWidth = (int)widthProperty.GetValue(null)!;
        bool oldButtonWrap = (bool)wrapProperty.GetValue(null)!;
        try
        {
            widthProperty.GetSetMethod(nonPublic: true)!.Invoke(null, [limit]);
            wrapProperty.GetSetMethod(nonPublic: true)!.Invoke(null, [false]);
            using var window = new MainWindow([]);
            var console = (EmueraConsole)typeof(MainWindow)
                .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var buffer = new PrintStringBuffer(console);
            buffer.AppendButton(source, style, 1L);
            ConsoleDisplayLine[] lines = buffer.Flush(null!, false);
            True(lines.Length >= 2, "fallback text reaches the real wrap path");
            Equal(source[..expectedIndex], lines[0].ToString(), "first line ends at the fallback glyph's measured boundary");
            Equal(source, string.Concat(lines.Select(line => line.ToString())), "wrap keeps every Unicode scalar exactly once");
        }
        finally
        {
            widthProperty.GetSetMethod(nonPublic: true)!.Invoke(null, [oldWidth]);
            wrapProperty.GetSetMethod(nonPublic: true)!.Invoke(null, [oldButtonWrap]);
        }
    }

    private static void SplitInsideSurrogateKeepsRune()
    {
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas");
        var styled = new ConsoleStyledString("A😀B", style);
        ConsoleStyledString? suffix = styled.DivideAt(2); // 高位・低位サロゲートの間を指定する。
        True(suffix is not null, "a safe split point exists before the supplementary rune");
        Equal("A", styled.Text, "left fragment stops before the complete rune");
        Equal("😀B", suffix!.Text, "right fragment starts with the complete rune");
        Equal("A😀B", styled.Text + suffix.Text, "split preserves the source text");
    }

    private static void WrappedFallbackButtonsDrawAndHit()
    {
        var style = new StringStyle(Color.White, false, Color.Lime,
            FontStyle.Bold | FontStyle.Italic, "Consolas") { FontSize = 34 };
        SKFont baseFont = FontFactory.GetFont(style);
        int codepoint = new[] { 0x1F600, 0x10348, 0x1F9EA, 0x4E2D }
            .FirstOrDefault(value => !baseFont.ContainsGlyph(value) &&
                SKFontManager.Default.MatchCharacter(value) is SKTypeface matched && matched != baseFont.Typeface);
        if (codepoint == 0)
            throw new TestUnavailableException("no actual fallback font for the wrapped-button test");
        string firstText = new string('A', 90) + char.ConvertFromUtf32(codepoint) + new string('B', 90);
        const string secondText = " 日本語 SECOND_BUTTON";
        var secondStyle = new StringStyle(Color.White, FontStyle.Regular, "Consolas") { FontSize = 24 };
        using var window = new MainWindow([]);
        var console = (EmueraConsole)typeof(MainWindow)
            .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var buffer = new PrintStringBuffer(console);
        buffer.AppendButton(firstText, style, 1L);
        buffer.AppendButton(secondText, secondStyle, 2L);
        ConsoleDisplayLine[] lines = buffer.Flush(null!, false);
        True(lines.Length > 1, "the real buffer wraps the long fallback button");
        Equal(firstText + secondText, string.Concat(lines.Select(line => line.ToString())), "wrap keeps both buttons' text");

        using var bitmap = new SKBitmap(1600, Math.Max(100, lines.Length * Math.Max(40, RuntimeConfig.LineHeight)));
        using var canvas = new SKCanvas(bitmap);
        bitmap.Erase(SKColors.Black);
        var displayLines = (DisplayLineBuffer)typeof(EmueraConsole)
            .GetField("displayLineList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(console)!;
        foreach (ConsoleDisplayLine line in lines)
            displayLines.Add(line);
        window.ScrollBar.Maximum = lines.Length;
        window.ScrollBar.Value = lines.Length;
        int renderedFragments = 0;
        int actualFallbackFragments = 0;
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            lines[lineIndex].DrawTo(canvas, lineIndex * Math.Max(40, RuntimeConfig.LineHeight), false, false,
                RuntimeConfig.TextDrawingMode);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            foreach (ConsoleButtonString button in lines[lineIndex].Buttons)
            {
                if (!button.IsButton || button.StrArray.Length == 0)
                    continue;
                foreach (AConsoleDisplayNode node in button.StrArray)
                {
                    if (node is ConsoleStyledString styled)
                    {
                        var runs = (List<TextsWithFont>?)typeof(ConsoleStyledString)
                            .GetField("_texts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(styled);
                        if (runs is not null && runs.Any(run => run.Font.Typeface != baseFont.Typeface))
                            actualFallbackFragments++;
                    }
                    for (int i = 0; i < node.Text.Length; i++)
                    {
                        if (char.IsHighSurrogate(node.Text[i]))
                            True(i + 1 < node.Text.Length && char.IsLowSurrogate(node.Text[i + 1]), "high surrogate remains paired");
                        if (char.IsLowSurrogate(node.Text[i]))
                            True(i > 0 && char.IsHighSurrogate(node.Text[i - 1]), "low surrogate remains paired");
                    }
                }
                var first = button.StrArray.First(node => node.Size.Width > 2);
                var last = button.StrArray.Last(node => node.Size.Width > 2);
                Console.WriteLine($"FONT_HIT_PROBE line={lineIndex} input={button.Input} first={first.Point} " +
                    $"size={first.Size} result={console.FindButton((int)first.Point.X + 1, (int)first.Point.Y + 1)?.Input} " +
                    $"pictureHeight={window.MainPicBox.Height} scroll={window.ScrollBar.Value}/{window.ScrollBar.Maximum}");
                True(ReferenceEquals(button, console.FindButton((int)first.Point.X + 1, (int)first.Point.Y + 1)),
                    "inside point hits the drawn button fragment");
                True(ReferenceEquals(button, console.FindButton((int)(last.Point.X + last.Size.Width) - 2,
                    (int)last.Point.Y + 1)), "right inside point hits the drawn fragment");
                True(!ReferenceEquals(button, console.FindButton((int)(last.Point.X + last.Size.Width) + 2,
                    (int)last.Point.Y + 1)), "outside point does not hit the same fragment");
                renderedFragments++;
            }
        }
        True(renderedFragments >= 2 && actualFallbackFragments > 0,
            "multiple button fragments were drawn with an actual fallback run");

        string DrawColorHash(bool selected)
        {
            using var image = new SKBitmap(500, 100);
            using var graph = new SKCanvas(image);
            image.Erase(SKColors.Black);
            var text = new ConsoleStyledString("A" + char.ConvertFromUtf32(codepoint) + "B", style);
            text.SetWidth(null!, 0);
            text.DrawTo(graph, new SKPoint(10, 10), selected, false, RuntimeConfig.TextDrawingMode);
            graph.Flush();
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Bytes));
        }
        string normalHash = DrawColorHash(false);
        string selectedHash = DrawColorHash(true);
        True(normalHash != selectedHash, "selection color changes the rendered fallback text");
        Console.WriteLine($"FONT_BUFFER_LINES={lines.Length} BUTTON_FRAGMENTS={renderedFragments} " +
            $"FALLBACK_FRAGMENTS={actualFallbackFragments} NORMAL={normalHash} SELECTED={selectedHash}");
    }

    private static int AvailableFallbackScalar(SKFont baseFont)
    {
        int codepoint = new[] { 0x1F600, 0x10348, 0x1F9EA, 0x4E2D }
            .FirstOrDefault(value => !baseFont.ContainsGlyph(value) &&
                SKFontManager.Default.MatchCharacter(value) is SKTypeface matched && matched != baseFont.Typeface);
        if (codepoint == 0)
            throw new TestUnavailableException("no actual fallback font for this test");
        return codepoint;
    }

    private static List<TextsWithFont> DrawingRuns(ConsoleStyledString text) =>
        (List<TextsWithFont>?)typeof(ConsoleStyledString)
            .GetField("_texts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(text)
        ?? throw new InvalidOperationException("test text did not use a fallback font");

    private static void FontBacklogColorAndRuns()
    {
        PropertyInfo fore = typeof(RuntimeConfig).GetProperty("ForeColor", BindingFlags.Static | BindingFlags.Public)!;
        PropertyInfo log = typeof(RuntimeConfig).GetProperty("LogColor", BindingFlags.Static | BindingFlags.Public)!;
        object? oldFore = fore.GetValue(null);
        object? oldLog = log.GetValue(null);
        try
        {
            fore.GetSetMethod(true)!.Invoke(null, [Color.White]);
            log.GetSetMethod(true)!.Invoke(null, [Color.Cyan]);
            var style = new StringStyle(Color.White, false, Color.Lime, FontStyle.Bold | FontStyle.Italic, "Consolas")
                { FontSize = 36 };
            string glyph = char.ConvertFromUtf32(AvailableFallbackScalar(FontFactory.GetFont(style)));
            var styled = new ConsoleStyledString("A" + glyph + "B", style);
            styled.SetWidth(null!, 0);
            string Render(bool backLog)
            {
                using var image = new SKBitmap(400, 100);
                using var canvas = new SKCanvas(image);
                image.Erase(SKColors.Black);
                styled.DrawTo(canvas, new SKPoint(10, 10), false, backLog, RuntimeConfig.TextDrawingMode);
                canvas.Flush();
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Bytes));
            }
            string normal = Render(false);
            string backlog = Render(true);
            True(normal != backlog, "backlog uses the configured log color for fallback text");
            Equal("A" + glyph + "B", string.Concat(DrawingRuns(styled).Select(run => run.Text)),
                "backlog drawing keeps every run's text");
            Console.WriteLine($"FONT_BACKLOG_NORMAL={normal} BACKLOG={backlog} GLYPH={glyph}");
        }
        finally
        {
            fore.GetSetMethod(true)!.Invoke(null, [oldFore]);
            log.GetSetMethod(true)!.Invoke(null, [oldLog]);
        }
    }

    private static void LargeFallbackFontWrapsByDrawnAdvance()
    {
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas") { FontSize = 96 };
        SKFont baseFont = FontFactory.GetFont(style);
        int scalar = AvailableFallbackScalar(baseFont);
        string glyph = char.ConvertFromUtf32(scalar);
        string source = "AAAA" + glyph + "BBBB";
        SKTypeface fallbackTypeface = SKFontManager.Default.MatchCharacter(scalar)!;
        // 実描画runは元書体も補完書体も高さを揃えてから幅を測る。元Fontの96px幅は期待値に使わない。
        using var normalizedBase = new SKFont(baseFont.Typeface, baseFont.Size);
        normalizedBase.Size *= baseFont.Size / normalizedBase.Spacing;
        using var fallback = new SKFont(fallbackTypeface, baseFont.Size);
        fallback.Size *= baseFont.Size / fallback.Spacing;
        float baseAdvance = normalizedBase.GetGlyphWidths("A")[0];
        float fallbackAdvance = fallback.GetGlyphWidths(glyph).Sum();
        int limit = -1;
        int expectedIndex = -1;
        for (int candidateLimit = 20; candidateLimit < 900; candidateLimit++)
        {
            float width = 0;
            int index = 0;
            for (int runeIndex = 0; runeIndex < source.EnumerateRunes().Count(); runeIndex++)
            {
                float advance = runeIndex == 4 ? fallbackAdvance : baseAdvance;
                int length = runeIndex == 4 ? glyph.Length : 1;
                if (width + advance > candidateLimit - 1) break;
                width += advance;
                index += length;
            }
            if (index > 0 && index < source.Length && baseFont.BreakText(source, candidateLimit) != index)
            {
                limit = candidateLimit;
                expectedIndex = index;
                break;
            }
        }
        if (limit < 0)
            throw new TestUnavailableException("installed fonts do not expose a large-size wrap mismatch");
        PropertyInfo widthProperty = typeof(RuntimeConfig).GetProperty("DrawableWidth", BindingFlags.Static | BindingFlags.Public)!;
        int oldWidth = (int)widthProperty.GetValue(null)!;
        try
        {
            widthProperty.GetSetMethod(true)!.Invoke(null, [limit]);
            using var window = new MainWindow([]);
            var console = (EmueraConsole)typeof(MainWindow)
                .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var buffer = new PrintStringBuffer(console);
            buffer.AppendButton(source, style, 1L);
            ConsoleDisplayLine[] lines = buffer.Flush(null!, false);
            True(lines.Length > 1, "large fallback font actually wraps");
            Equal(source[..expectedIndex], lines[0].ToString(), "large fallback wrap uses drawn advance");
            Equal(source, string.Concat(lines.Select(line => line.ToString())), "large wrap keeps all text");
            Console.WriteLine($"FONT_LARGE_WRAP_SIZE={style.FontSize} LIMIT={limit} EXPECTED={expectedIndex} " +
                $"BASE_BREAK={baseFont.BreakText(source, limit)} FALLBACK={fallbackTypeface.FamilyName}");
        }
        finally { widthProperty.GetSetMethod(true)!.Invoke(null, [oldWidth]); }
    }

    private static void FontTailSplitHasEmptySuffix()
    {
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas");
        string glyph = char.ConvertFromUtf32(AvailableFallbackScalar(FontFactory.GetFont(style)));
        string source = "AA" + glyph;
        var first = new ConsoleStyledString(source, style);
        ConsoleStyledString suffix = first.DivideAt(source.Length)!;
        Equal(source, first.Text, "tail split leaves source intact");
        Equal("", suffix.Text, "tail split produces an empty suffix");
        suffix.SetWidth(null!, 0);
        Equal(0, suffix.Width, "empty suffix has zero width");
        True(suffix.DivideAt(0) is null, "empty suffix cannot split further");
    }

    private static void FontRepeatedSplitsPreserveRuns()
    {
        var style = new StringStyle(Color.White, FontStyle.Bold | FontStyle.Italic, "Consolas");
        SKFont baseFont = FontFactory.GetFont(style);
        string glyph = char.ConvertFromUtf32(AvailableFallbackScalar(baseFont));
        string source = "AA" + glyph + "BB" + glyph + "CC";
        var a = new ConsoleStyledString(source, style);
        var b = a.DivideAt(2)!;
        var c = b.DivideAt(glyph.Length)!;
        var d = c.DivideAt(2)!;
        var e = d.DivideAt(glyph.Length)!;
        var fragments = new[] { a, b, c, d, e };
        SequenceEqual(new[] { "AA", glyph, "BB", glyph, "CC" }, fragments.Select(part => part.Text),
            "repeated splits preserve exact Unicode scalars");
        foreach (var part in fragments)
            Equal(part.Text, string.Concat(DrawingRuns(part).Select(run => run.Text)),
                "fragment drawing run matches its text");
        True(DrawingRuns(b)[0].Font.Typeface != baseFont.Typeface,
            "first fallback fragment still uses a fallback typeface");
        True(DrawingRuns(d)[0].Font.Typeface != baseFont.Typeface,
            "second fallback fragment still uses a fallback typeface");
    }

    private static void FontIntegerWidthBoundsFractionalAdvance()
    {
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas") { FontSize = 47 };
        string glyph = char.ConvertFromUtf32(AvailableFallbackScalar(FontFactory.GetFont(style)));
        var text = new ConsoleStyledString("A" + glyph + "B", style);
        text.SetWidth(null!, 0);
        float drawnAdvance = DrawingRuns(text).Sum(run => run.Width);
        Equal((int)drawnAdvance, text.Width, "click and wrap width follows the existing integer truncation");
        True(drawnAdvance - text.Width >= 0 && drawnAdvance - text.Width < 1,
            "float drawing advance exceeds integer width by less than one pixel");
        Console.WriteLine($"FONT_INTEGER_WIDTH={text.Width} DRAWN_ADVANCE={drawnAdvance:F4} " +
            $"FRACTION={drawnAdvance - text.Width:F4}");
    }

    private static void TimerTickDoesNotReenter()
    {
        using var window = new MainWindow([]);
        var console = (EmueraConsole)typeof(MainWindow)
            .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        _ = window.Handle;
        var request = new InputRequest { InputType = InputType.IntValue, Timelimit = 10000 };
        console.WaitInput(request);
        request.DisplayTime = true;
        var consoleType = typeof(EmueraConsole);
        consoleType.GetField("need_settimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(console, false);
        consoleType.GetMethod("setTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(console, null);
        var timer = (System.Timers.Timer)consoleType.GetField("genericTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(console)!;
        timer.Interval = 100000;
        var tick = consoleType.GetMethod("tickTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int maxDepth = 0;
        int queuedCallbacks = 0;
        void QueueNestedTick()
        {
            window.BeginInvoke((Action)(() =>
            {
                queuedCallbacks++;
                int depth = new StackTrace().GetFrames()!
                    .Count(frame => frame.GetMethod()?.Name == "tickTimer");
                maxDepth = Math.Max(maxDepth, depth);
                if (queuedCallbacks < 3) QueueNestedTick();
                tick.Invoke(console, [timer, EventArgs.Empty]);
            }));
        }
        QueueNestedTick();
        tick.Invoke(console, [timer, EventArgs.Empty]);
        Application.DoEvents();
        timer.Enabled = false;
        Equal(3, queuedCallbacks, "three queued timer notifications were delivered");
        True(maxDepth <= 1, $"timer body must not nest; observed depth={maxDepth}");
        Console.WriteLine($"TIMER_NESTED_CALLBACKS={queuedCallbacks} MAX_TICK_DEPTH={maxDepth}");
    }

    private static void StaleTimerCannotStopNextInput()
    {
        using var window = new MainWindow([]);
        var console = (EmueraConsole)typeof(MainWindow)
            .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        _ = window.Handle;
        var consoleType = typeof(EmueraConsole);
        var timerField = consoleType.GetField("genericTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldTimer = (System.Timers.Timer)timerField.GetValue(console)!;
        var request = new InputRequest { InputType = InputType.IntValue, Timelimit = 1 };
        console.WaitInput(request);
        consoleType.GetField("need_settimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(console, false);
        consoleType.GetMethod("setTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(console, null);
        var currentTimer = (System.Timers.Timer)timerField.GetValue(console)!;
        currentTimer.Interval = 100000;
        System.Threading.Thread.Sleep(5);
        consoleType.GetMethod("tickTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(console, [oldTimer, EventArgs.Empty]);
        True(currentTimer.Enabled, "old queued callback must not end current timed input");
        currentTimer.Enabled = false;
    }

    private static void AnimatedFramesContinueAcrossTimedInput(bool displayTime)
    {
        using var window = new MainWindow([]);
        var init = typeof(MainWindow).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic)!;
        window.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), window, init);
        window.ShowInTaskbar = false;
        window.Show();
        var console = (EmueraConsole)typeof(MainWindow)
            .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        using var red = new GraphicsImage();
        using var blue = new GraphicsImage();
        red.GCreate(32, 32, false);
        blue.GCreate(32, 32, false);
        red.GClear(Color.Red);
        blue.GClear(Color.Blue);
        using var animation = new SpriteAnime("TEST_FRAMES", new Size(32, 32));
        True(animation.AddFrame(red, new Rectangle(0, 0, 32, 32), Point.Empty, 120), "red frame added");
        True(animation.AddFrame(blue, new Rectangle(0, 0, 32, 32), Point.Empty, 120), "blue frame added");
        True(console.CBG_SetImage(animation, 40, 0, 1), "animated image added to actual paint path");
        var timedColors = new List<SKColor>();
        var ordinaryColors = new List<SKColor>();
        bool timedPhase = true;
        window.MainPicBox.PaintSurface += (_, e) =>
        {
            using var image = e.Surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            if (bitmap.Width > 48 && bitmap.Height > 16)
            {
                SKColor color = bitmap.GetPixel(48, bitmap.Height - 16);
                (timedPhase ? timedColors : ordinaryColors).Add(color);
            }
        };
        var consoleType = typeof(EmueraConsole);
        var timedRequest = new InputRequest { InputType = InputType.IntValue, Timelimit = 5000 };
        console.WaitInput(timedRequest);
        timedRequest.DisplayTime = displayTime;
        consoleType.GetField("need_settimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(console, false);
        consoleType.GetMethod("setTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(console, null);
        console.setRedrawTimer(50);
        window.Refresh();
        PumpWindowFor(1100);
        if (!displayTime)
        {
            console.setRedrawTimer(0);
            PumpWindowFor(150);
            int paintsAtStop = timedColors.Count;
            PumpWindowFor(400);
            Equal(paintsAtStop, timedColors.Count, "stopped animation timer does not request periodic paints during TINPUT");
        }
        consoleType.GetMethod("stopTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(console, null);
        timedPhase = false;
        console.WaitInput(new InputRequest { InputType = InputType.IntValue });
        if (!displayTime) console.setRedrawTimer(50);
        PumpWindowFor(800);
        console.setRedrawTimer(0);
        // 試験用窓を破棄する前に非同期redraw loopを終了させ、残ったInvokeを処理する。
        ((System.Threading.PeriodicTimer)consoleType.GetField("redrawTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(console)!).Dispose();
        PumpWindowFor(150);
        True(HasRedAndBlue(timedColors), $"timed input frames: paints={timedColors.Count}, red={timedColors.Count(IsRed)}, blue={timedColors.Count(IsBlue)}");
        True(HasRedAndBlue(ordinaryColors), $"ordinary input frames: paints={ordinaryColors.Count}, red={ordinaryColors.Count(IsRed)}, blue={ordinaryColors.Count(IsBlue)}");
        Console.WriteLine($"ANIM_DISPLAY_TIME={displayTime}; ANIM_TIMED_PAINTS={timedColors.Count} RED={timedColors.Count(IsRed)} BLUE={timedColors.Count(IsBlue)}; ANIM_AFTER_PAINTS={ordinaryColors.Count} RED={ordinaryColors.Count(IsRed)} BLUE={ordinaryColors.Count(IsBlue)}");

        static bool IsRed(SKColor color) => color.Red > 180 && color.Green < 80 && color.Blue < 80;
        static bool IsBlue(SKColor color) => color.Blue > 180 && color.Green < 80 && color.Red < 80;
        static bool HasRedAndBlue(List<SKColor> colors) => colors.Any(IsRed) && colors.Any(IsBlue);
    }

    private static void PumpWindowFor(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
        }
    }

    private static void DarkModeConfigRoundTrips()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Emuera.ConfigRegressionTests", Guid.NewGuid().ToString("N"));
        string csvDirectory = Path.Combine(directory, "Csv");
        Directory.CreateDirectory(csvDirectory);
        _themeConfigDirectory = directory;
        _themeConfigPath = Path.Combine(directory, "emuera.config");

        Type programType = typeof(MinorShift.Emuera.Program);
        PropertyInfo exeDir = programType.GetProperty("ExeDir", BindingFlags.Public | BindingFlags.Static)!;
        PropertyInfo csvDir = programType.GetProperty("CsvDir", BindingFlags.Public | BindingFlags.Static)!;
        object? oldExeDir = exeDir.GetValue(null);
        object? oldCsvDir = csvDir.GetValue(null);
        try
        {
            exeDir.GetSetMethod(nonPublic: true)!.Invoke(null, [directory + Path.DirectorySeparatorChar]);
            csvDir.GetSetMethod(nonPublic: true)!.Invoke(null, [csvDirectory + Path.DirectorySeparatorChar]);

            ConfigData config = ConfigData.Instance;
            True(config.GetConfigItem("ダークモードを使用する") is ConfigItem<bool>, "dark-mode config item is registered as a bool");
            Equal(true, config.GetConfigValue<bool>((ConfigCode)163), "missing setting uses the compatibility default YES");

            config.LoadConfig();
            True(File.ReadAllLines(_themeConfigPath!, Config.Encode).Contains("ダークモードを使用する:YES"), "missing setting is persisted as YES");

            const string yesConfig = "ダークモードを使用する:YES\r\n履歴ログの行数:4321\r\n文字色:11,22,33\r\n背景色:44,55,66\r\n選択中文字色:77,88,99\r\n履歴文字色:101,112,123\r\n";
            File.WriteAllText(_themeConfigPath!, yesConfig, Config.Encode);
            config.LoadConfig();
            Equal(true, ReadRuntimeConfig<bool>("UseDarkMode"), "YES enables dark mode when config is loaded at startup");
            Equal(4321, RuntimeConfig.MaxLog, "an existing unrelated config setting is loaded");
            Equal(Color.FromArgb(11, 22, 33), RuntimeConfig.ForeColor, "dark-mode setting leaves game text color unchanged");
            Equal(Color.FromArgb(44, 55, 66), RuntimeConfig.BackColor, "dark-mode setting leaves game background color unchanged");
            Equal(Color.FromArgb(77, 88, 99), RuntimeConfig.FocusColor, "dark-mode setting leaves game highlight color unchanged");
            Equal(Color.FromArgb(101, 112, 123), RuntimeConfig.LogColor, "dark-mode setting leaves game history color unchanged");

            True(config.SaveConfig(), "existing config saves successfully");
            string[] savedYes = File.ReadAllLines(_themeConfigPath!, Config.Encode);
            True(savedYes.Contains("ダークモードを使用する:YES"), "YES is saved through the existing config writer");
            True(savedYes.Contains("履歴ログの行数:4321"), "saving dark mode preserves existing config values");

            File.WriteAllText(_themeConfigPath!, yesConfig.Replace("ダークモードを使用する:YES", "ダークモードを使用する:NO", StringComparison.Ordinal), Config.Encode);
            config.LoadConfig();
            Equal(false, ReadRuntimeConfig<bool>("UseDarkMode"), "NO disables dark mode when config is reloaded at startup");
            Equal(Color.FromArgb(11, 22, 33), RuntimeConfig.ForeColor, "switching theme does not alter game text color");
            Equal(Color.FromArgb(44, 55, 66), RuntimeConfig.BackColor, "switching theme does not alter game background color");
        }
        finally
        {
            exeDir.GetSetMethod(nonPublic: true)!.Invoke(null, [oldExeDir]);
            csvDir.GetSetMethod(nonPublic: true)!.Invoke(null, [oldCsvDir]);
        }
    }

    private static void IntegratedVerticalFlip()
    {
        using var source = new GraphicsImage();
        source.GCreate(80, 120, false);
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 80; x++)
                source.Bitmap.SetPixel(x, y, y < 60
                    ? (x < 40 ? SKColors.Red : SKColors.Green)
                    : (x < 40 ? SKColors.Blue : SKColors.Yellow));
        using var sprite = new SpriteG("flip", source, new Rectangle(0, 0, 80, 120));
        using var target = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(target);
        using SKImage snapshot = SKImage.FromBitmap(source.Bitmap);
        for (int path = 0; path < 3; path++)
        {
            target.Erase(SKColors.Transparent);
            Rectangle destination = new(20, 150, 80, -120);
            if (path == 0) sprite.GraphicsDraw(canvas, destination);
            else if (path == 1) sprite.GraphicsDraw(canvas, destination, null!);
            else sprite.GraphicsDrawFromSnapshot(canvas, destination, snapshot);
            Equal(SKColors.Blue, target.GetPixel(30, 40), $"path {path} upper-left");
            Equal(SKColors.Yellow, target.GetPixel(90, 40), $"path {path} upper-right");
            Equal(SKColors.Red, target.GetPixel(30, 140), $"path {path} lower-left");
            Equal(SKColors.Green, target.GetPixel(90, 140), $"path {path} lower-right");
        }
    }

    private static void IntegratedIslandHits()
    {
        using var window = new MainWindow([]);
        var console = (EmueraConsole)typeof(MainWindow)
            .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var style = new StringStyle(Color.White, FontStyle.Regular, "Consolas");
        AConsoleDisplayNode[] parts = Enumerable.Range(0, 80).Select(index =>
            (AConsoleDisplayNode)new ConsoleStyledString("X", style)
            { Point = new SKPoint(index * 12, 0), Size = new SKSize(10, 10) }).ToArray();
        var button = new ConsoleButtonString(console, parts, 7) { Title = "help" };
        var islands = (SortedDictionary<int, List<ConsoleDisplayLine>>)typeof(EmueraConsole)
            .GetField("_htmlElementListDict", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(console)!;
        islands.Add(0, [new ConsoleDisplayLine([button], true, false)]);
        True(console.FindButton(9999, 9999) is null, "miss remains a miss");
        True(ReferenceEquals(button, console.FindButton(5, 5)), "first part hit");
        True(ReferenceEquals(button, console.FindButton(79 * 12 + 5, 5)), "last part hit");
        var tooltipPart = new ConsoleStyledString("T", style)
        { Point = new SKPoint(980, 0), Size = new SKSize(10, 10) };
        var tooltip = new ConsoleButtonString(console, [tooltipPart]) { Title = "tooltip without input" };
        True(!tooltip.IsButton, "tooltip target has no input value");
        islands.Add(1, [new ConsoleDisplayLine([tooltip], true, false)]);
        True(ReferenceEquals(tooltip, console.FindButton(985, 5)), "tooltip-only target hit");
        var nested = new ConsoleStyledString("N", style)
        { Point = new SKPoint(5, 0), Size = new SKSize(10, 10) };
        var nestedButton = new ConsoleButtonString(console, [nested], 13);
        var wrapper = new ConsoleButtonString(console,
            [new ConsoleDivElement([nestedButton], SKColors.Transparent)]);
        islands.Add(2, [new ConsoleDisplayLine([wrapper], true, false)]);
        True(ReferenceEquals(nestedButton, console.FindButton(5, 5)), "higher nested hit wins");
    }

    private static void IntegratedColorMatrix()
    {
        using var source = new GraphicsImage();
        using var destination = new GraphicsImage();
        source.GCreate(1, 1, false);
        destination.GCreate(1, 1, false);
        source.Bitmap.Erase(new SKColor(10, 20, 30, 255));
        float[][] swapRedBlue =
        [
            [0, 0, 1, 0, 0], [0, 1, 0, 0, 0], [1, 0, 0, 0, 0],
            [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]
        ];
        destination.GDrawG(source, new Rectangle(0, 0, 1, 1), new Rectangle(0, 0, 1, 1), swapRedBlue);
        Equal(new SKColor(30, 20, 10, 255), destination.Bitmap.GetPixel(0, 0), "non-identity matrix changes channels");
    }

    private static void IntegratedMaskBounds()
    {
        using var source = new GraphicsImage();
        using var mask = new GraphicsImage();
        using var destination = new GraphicsImage();
        source.GCreate(1, 1, false);
        mask.GCreate(1, 1, false);
        destination.GCreate(2, 2, false);
        source.Bitmap.Erase(SKColors.Crimson);
        mask.Bitmap.Erase(SKColors.White);
        foreach (Point invalid in new[]
        {
            new Point(-1, 0), new Point(-1, 1), new Point(0, -1),
            new Point(2, 0), new Point(0, 2), new Point(int.MaxValue, 0)
        })
        {
            destination.Bitmap.Erase(SKColors.Blue);
            bool rejected = false;
            try { destination.GDrawGWithMask(source, mask, invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            True(rejected, $"invalid {invalid} is rejected before writing");
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    Equal(SKColors.Blue, destination.Bitmap.GetPixel(x, y), $"invalid {invalid} leaves ({x},{y})");
        }
        foreach (Point edge in new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1) })
        {
            destination.Bitmap.Erase(SKColors.Blue);
            destination.GDrawGWithMask(source, mask, edge);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    Equal(x == edge.X && y == edge.Y ? SKColors.Crimson : SKColors.Blue,
                        destination.Bitmap.GetPixel(x, y), $"edge {edge} pixel ({x},{y})");
        }
    }

    private static void IntegratedMaskAlpha()
    {
        using var source = new GraphicsImage();
        using var mask = new GraphicsImage();
        using var destination = new GraphicsImage();
        source.GCreate(1, 1, false);
        mask.GCreate(1, 1, false);
        destination.GCreate(3, 3, false);
        source.Bitmap.Erase(new SKColor(200, 100, 50, 128));
        mask.Bitmap.Erase(new SKColor(0, 255, 128, 255));
        destination.Bitmap.Erase(new SKColor(20, 40, 60, 64));
        SKColor outside = destination.Bitmap.GetPixel(0, 0);
        destination.GDrawGWithMask(source, mask, new Point(1, 1));
        Equal(new SKColor(109, 69, 53, 96), destination.Bitmap.GetPixel(1, 1), "legacy blue-weight and premultiplied-alpha result");
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                if (x != 1 || y != 1) Equal(outside, destination.Bitmap.GetPixel(x, y), $"outside ({x},{y})");
    }

    private static void IntegratedMaskFileBacked()
    {
        using var srcBitmap = new SKBitmap(1, 1);
        using var maskBitmap = new SKBitmap(1, 1);
        using var destBitmap = new SKBitmap(3, 3);
        srcBitmap.Erase(SKColors.Crimson);
        maskBitmap.Erase(SKColors.White);
        destBitmap.Erase(SKColors.Blue);
        using var source = new GraphicsImage();
        using var mask = new GraphicsImage();
        using var destination = new GraphicsImage();
        source.GCreateFromF(SKImage.FromBitmap(srcBitmap), false);
        mask.GCreateFromF(SKImage.FromBitmap(maskBitmap), false);
        destination.GCreateFromF(SKImage.FromBitmap(destBitmap), false);
        bool rejected = false;
        try { destination.GDrawGWithMask(source, mask, new Point(-1, 1)); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        True(rejected, "file-backed invalid offset is rejected");
        True(destination.Bitmap is null, "rejection does not materialize destination");
        destination.GDrawGWithMask(source, mask, new Point(1, 1));
        string path = Path.Combine(Path.GetTempPath(), "integrated-mask-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            destination.SavePng(path);
            using SKBitmap saved = SKBitmap.Decode(path);
            Equal(SKColors.Crimson, saved.GetPixel(1, 1), "saved target");
            Equal(SKColors.Blue, saved.GetPixel(0, 0), "saved outside");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void IntegratedMaskSelf()
    {
        using var image = new GraphicsImage();
        using var mask = new GraphicsImage();
        image.GCreate(2, 2, false);
        mask.GCreate(2, 2, false);
        SKColor[] colors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Yellow];
        image.Bitmap.SetPixel(0, 0, colors[0]);
        image.Bitmap.SetPixel(1, 0, colors[1]);
        image.Bitmap.SetPixel(0, 1, colors[2]);
        image.Bitmap.SetPixel(1, 1, colors[3]);
        mask.Bitmap.Erase(SKColors.White);
        image.GDrawGWithMask(image, mask, Point.Empty);
        Equal(colors[0], image.Bitmap.GetPixel(0, 0), "self top-left");
        Equal(colors[1], image.Bitmap.GetPixel(1, 0), "self top-right");
        Equal(colors[2], image.Bitmap.GetPixel(0, 1), "self bottom-left");
        Equal(colors[3], image.Bitmap.GetPixel(1, 1), "self bottom-right");
    }

    private static void MissingGameBlockDefault()
    {
        WithConfig("{\"UnknownGame\":{\"kept\":5}}", "{}", (gamePath, _) =>
        {
            JSONConfig.Load();
            Equal(false, JSONConfig.Game.LazyErb.Enabled, "in-memory default is disabled");
            Equal(0, JSONConfig.Game.LazyErb.Directories.Length, "in-memory default has no folders");

            JsonObject saved = ReadObject(gamePath);
            JsonObject lazy = saved["起動時に読み込まないERBフォルダ"]!.AsObject();
            Equal(false, lazy["有効"]!.GetValue<bool>(), "persisted default is disabled");
            Equal(0, lazy["フォルダ"]!.AsArray().Count, "persisted default has no folders");
            Equal(5, saved["UnknownGame"]!["kept"]!.GetValue<int>(), "unknown game property is retained");
        });
    }

    private static void ExistingGameBlockIsNotRewritten()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/RPG\"],\"将来項目\":{\"値\":3}},\"未知\":\"保持\"}";
        WithConfig(game, "{}", (gamePath, _) =>
        {
            byte[] before = File.ReadAllBytes(gamePath);
            JSONConfig.Load();
            SequenceEqual(before, File.ReadAllBytes(gamePath), "valid current block is not rewritten on load");
        });
    }

    private static void NewUserFileHasNoOverride()
    {
        WithConfig("{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[]}}", null, (_, userPath) =>
        {
            JSONConfig.Load();
            JsonObject saved = ReadObject(userPath);
            True(!saved.ContainsKey("起動時に読み込まないERBフォルダ"), "new user settings has no override block");
        });
    }

    private static void MissingUserUsesGameValue()
    {
        WithConfig("{\"起動時に読み込まないERBフォルダ\":{\"有効\":true,\"フォルダ\":[\"ERB/Game\"]}}", "{}", (_, _) =>
        {
            JSONConfig.Load();
            (bool? enabled, string[] directories) = ReadEffectiveLazyErb();
            Equal(true, enabled, "missing user block uses game enabled value");
            SequenceEqual(["ERB/Game"], directories, "missing user block uses complete game directory list");
        });
    }

    private static void FalseUserValueOverridesGame()
    {
        WithConfig(
            "{\"起動時に読み込まないERBフォルダ\":{\"有効\":true,\"フォルダ\":[\"ERB/Game\"]}}",
            "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/User\"]}}",
            (_, _) =>
            {
                JSONConfig.Load();
                (bool? enabled, string[] directories) = ReadEffectiveLazyErb();
                Equal(false, enabled, "present false is authoritative");
                SequenceEqual(["ERB/User"], directories, "user directories replace, not merge with, game directories");
            });
    }

    private static void StagedOverrideRetainsUnknownValues()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/Game\"]}}";
        const string user = "{\"UserUnknown\":7,\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/Old\"],\"NestedUnknown\":{\"keep\":9}}}";
        WithConfig(game, user, (_, userPath) =>
        {
            JSONConfig.Load();
            InvokeStatic("SetUserLazyErbOverride", true, new[] { "ERB/New" });
            JSONConfig.Save();
            JsonObject saved = ReadObject(userPath);
            JsonObject lazy = saved["起動時に読み込まないERBフォルダ"]!.AsObject();
            Equal(true, lazy["有効"]!.GetValue<bool>(), "staged enabled value is written");
            SequenceEqual(["ERB/New"], lazy["フォルダ"]!.AsArray().Select(node => node!.GetValue<string>()), "staged list is a complete replacement");
            Equal(9, lazy["NestedUnknown"]!["keep"]!.GetValue<int>(), "nested user unknown survives local edit");
            Equal(7, saved["UserUnknown"]!.GetValue<int>(), "top-level user unknown survives local edit");
        });
    }

    private static void ResetRemovesUserBlock()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[]}}";
        const string user = "{\"UserUnknown\":7,\"起動時に読み込まないERBフォルダ\":{\"有効\":true,\"フォルダ\":[\"ERB/User\"],\"NestedUnknown\":{\"remove\":true}}}";
        WithConfig(game, user, (_, userPath) =>
        {
            JSONConfig.Load();
            InvokeStatic("ResetUserLazyErbOverride");
            JSONConfig.Save();
            JsonObject saved = ReadObject(userPath);
            True(!saved.ContainsKey("起動時に読み込まないERBフォルダ"), "entire user override including nested unknowns is removed");
            Equal(7, saved["UserUnknown"]!.GetValue<int>(), "unrelated user setting survives reset");
        });
    }

    private static void SavedOverrideIsRestartOnly()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/Game\"]}}";
        WithConfig(game, "{}", (_, _) =>
        {
            JSONConfig.Load();
            Equal(false, JSONConfig.EffectiveLazyErb.Enabled, "initial runtime uses game value");
            InvokeStatic("SetUserLazyErbOverride", true, new[] { "ERB/User" });
            JSONConfig.Save();
            Equal(false, JSONConfig.EffectiveLazyErb.Enabled, "saving override does not change current runtime behavior");
            True(ReadHasUserLazyErbOverride(), "saved user override is visible to the settings UI");
            (bool? configuredEnabled, string[] configuredDirectories) = ReadConfiguredUserLazyErb();
            Equal(true, configuredEnabled, "settings UI reads the saved user value");
            SequenceEqual(["ERB/User"], configuredDirectories, "settings UI reads the saved user directories");

            JSONConfig.Load();
            Equal(true, JSONConfig.EffectiveLazyErb.Enabled, "next load activates saved user value");
            InvokeStatic("ResetUserLazyErbOverride");
            JSONConfig.Save();
            Equal(true, JSONConfig.EffectiveLazyErb.Enabled, "saving reset does not change current runtime behavior");
            True(!ReadHasUserLazyErbOverride(), "saved reset selects game recommendation in the UI");
            JSONConfig.Load();
            Equal(false, JSONConfig.EffectiveLazyErb.Enabled, "next load activates the game value after reset");
        });
    }

    private static void PartialUserBlockDoesNotMerge()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/GameOnly\"]}}";
        const string user = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":true}}";
        WithConfig(game, user, (_, _) =>
        {
            JSONConfig.Load();
            (bool? enabled, string[] directories) = ReadEffectiveLazyErb();
            Equal(true, enabled, "omitted user enabled value uses its schema default, not game value");
            True(!directories.Contains("ERB/GameOnly"), "game directories are not merged into present user block");
        });
    }

    private static void LegacyBlockMigrates()
    {
        const string game = "{\"LazyErb\":{\"Enabled\":false,\"Directories\":[\"RPG/One\"],\"UnknownLegacy\":{\"keep\":4}}}";
        WithConfig(game, "{}", (gamePath, _) =>
        {
            JSONConfig.Load();
            JsonObject saved = ReadObject(gamePath);
            True(!saved.ContainsKey("LazyErb"), "legacy block is removed after migration");
            JsonObject lazy = saved["起動時に読み込まないERBフォルダ"]!.AsObject();
            Equal(false, lazy["有効"]!.GetValue<bool>(), "legacy enabled value migrates");
            Equal("ERB/RPG/One", lazy["フォルダ"]![0]!.GetValue<string>(), "legacy directory becomes DataDir-relative ERB path");
            Equal(4, lazy["UnknownLegacy"]!["keep"]!.GetValue<int>(), "legacy unknown child migrates");
        });
    }

    private static void CurrentBlockWinsOverLegacy()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/Current\"]},\"LazyErb\":{\"Enabled\":true,\"Directories\":[\"Legacy\"],\"UnknownLegacy\":1}}";
        WithConfig(game, "{}", (gamePath, _) =>
        {
            JSONConfig.Load();
            JsonObject lazy = ReadObject(gamePath)["起動時に読み込まないERBフォルダ"]!.AsObject();
            Equal(false, lazy["有効"]!.GetValue<bool>(), "current enabled value wins");
            Equal("ERB/Current", lazy["フォルダ"]![0]!.GetValue<string>(), "current directories win");
            Equal(1, lazy["UnknownLegacy"]!.GetValue<int>(), "nonconflicting legacy unknown survives migration");
        });
    }

    private static void LegacyNullWithCurrentBlockIsCompatible()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/Current\"],\"UnknownCurrent\":5},\"LazyErb\":null}";
        WithConfig(game, "{}", (gamePath, _) =>
        {
            JSONConfig.Load();
            JsonObject saved = ReadObject(gamePath);
            True(!saved.ContainsKey("LazyErb"), "legacy null key is removed");
            JsonObject lazy = saved["起動時に読み込まないERBフォルダ"]!.AsObject();
            Equal(false, lazy["有効"]!.GetValue<bool>(), "current enabled value is preserved");
            Equal("ERB/Current", lazy["フォルダ"]![0]!.GetValue<string>(), "current directories are preserved");
            Equal(5, lazy["UnknownCurrent"]!.GetValue<int>(), "current unknown property is preserved");
        });
    }

    private static void LegacyNullWithoutCurrentBlockIsCompatible()
    {
        const string game = "{\"LazyErb\":null,\"UnknownGame\":1}";
        WithConfig(game, "{}", (gamePath, _) =>
        {
            JSONConfig.Load();
            JsonObject saved = ReadObject(gamePath);
            True(!saved.ContainsKey("LazyErb"), "legacy null key is removed");
            JsonObject lazy = saved["起動時に読み込まないERBフォルダ"]!.AsObject();
            Equal(true, lazy["有効"]!.GetValue<bool>(), "legacy null uses the old LazyERB enabled default");
            SequenceEqual(
                new[] { "ERB/口上/口上まとめ", "ERB/RPG/依頼", "ERB/RPG/イベント" },
                lazy["フォルダ"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray(),
                "legacy null uses the old recommended folders");
            Equal(1, saved["UnknownGame"]!.GetValue<int>(), "unknown game property is retained");
        });
    }

    private static void MalformedLegacyIsNotRewritten()
    {
        WithConfig("{\"LazyErb\":7,\"UnknownGame\":1}", "{}", (gamePath, _) =>
        {
            byte[] before = File.ReadAllBytes(gamePath);
            bool threw = false;
            try
            {
                JSONConfig.Load();
            }
            catch (JsonException)
            {
                threw = true;
            }
            True(threw, "present malformed legacy block throws JsonException");
            SequenceEqual(before, File.ReadAllBytes(gamePath), "malformed legacy source remains byte-identical");
        });
    }

    private static void DirectoryPathSafetyRules()
    {
        WithTemporaryDirectory(dataRoot =>
        {
            string erbRoot = Path.Combine(dataRoot, "ERB");
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "依頼"));
            True(IsSafeDirectoryPath(dataRoot, erbRoot, "ERB/RPG"), "normal ERB directory is accepted");
            True(IsSafeDirectoryPath(dataRoot, erbRoot, "ERB/RPG/依頼"), "nested ERB directory is accepted");
            True(IsSafeDirectoryPath(dataRoot, erbRoot, "ERB/NotYetCreated"), "missing configured directory is non-targeting but safe");
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "ERB"), "ERB root cannot be selected");
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "../ERB/RPG"), "parent traversal is rejected");
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "ERB/../ERB/RPG"), "embedded parent traversal is rejected");
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "C:\\outside"), "drive-rooted path is rejected");
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "/outside"), "slash-rooted path is rejected");
        });
    }

    private static void DirectoryPathSafetyRejectsFilesAndSiblingPrefixes()
    {
        WithTemporaryDirectory(dataRoot =>
        {
            string erbRoot = Path.Combine(dataRoot, "ERB");
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG"));
            string filePath = Path.Combine(erbRoot, "RPG", "file.ERB");
            File.WriteAllText(filePath, "");
            string sibling = Path.Combine(dataRoot, "ERB-extra", "RPG");
            Directory.CreateDirectory(sibling);
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "ERB/RPG/file.ERB"), "existing file is not a selectable directory");
            True(!IsSafeDirectoryPath(dataRoot, erbRoot, "ERB-extra/RPG"), "sibling path sharing the ERB prefix is rejected");
        });
    }

    private static void RuntimePolicyRetainsEagerGates()
    {
        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":true,\"フォルダ\":[\"ERB/RPG\"]}}";
        WithConfig(game, "{}", (_, _) =>
        {
            JSONConfig.Load();
            Type programType = typeof(MinorShift.Emuera.Program);
            Type configType = typeof(RuntimeConfig);
            string[] programNames = ["DebugMode", "AnalysisMode"];
            string[] configNames = ["IgnoreUncalledFunction", "NeedReduceArgumentOnLoad", "FunctionNotCalledWarning"];
            Dictionary<PropertyInfo, object?> oldValues = [];

            try
            {
                foreach (string name in programNames.Concat(configNames))
                {
                    Type owner = programNames.Contains(name) ? programType : configType;
                    PropertyInfo property = owner.GetProperty(name, BindingFlags.Public | BindingFlags.Static)!;
                    oldValues[property] = property.GetValue(null);
                }

                SetStaticProperty(programType, "DebugMode", false);
                SetStaticProperty(programType, "AnalysisMode", false);
                SetStaticProperty(configType, "IgnoreUncalledFunction", true);
                SetStaticProperty(configType, "NeedReduceArgumentOnLoad", false);
                PropertyInfo warning = configType.GetProperty("FunctionNotCalledWarning", BindingFlags.Public | BindingFlags.Static)!;
                SetStaticProperty(configType, "FunctionNotCalledWarning", Enum.Parse(warning.PropertyType, "IGNORE"));
                True(LazyErbPolicy.IsEnabledForCurrentMode, "eligible normal mode may use lazy loading");

                SetStaticProperty(programType, "DebugMode", true);
                True(!LazyErbPolicy.IsEnabledForCurrentMode, "Debug mode remains eager");
                SetStaticProperty(programType, "DebugMode", false);
                SetStaticProperty(programType, "AnalysisMode", true);
                True(!LazyErbPolicy.IsEnabledForCurrentMode, "Analysis mode remains eager");
                SetStaticProperty(programType, "AnalysisMode", false);
                SetStaticProperty(configType, "IgnoreUncalledFunction", false);
                True(!LazyErbPolicy.IsEnabledForCurrentMode, "IgnoreUncalledFunction prerequisite remains enforced");
                SetStaticProperty(configType, "IgnoreUncalledFunction", true);
                SetStaticProperty(configType, "NeedReduceArgumentOnLoad", true);
                True(!LazyErbPolicy.IsEnabledForCurrentMode, "argument reduction prerequisite remains enforced");
                SetStaticProperty(configType, "NeedReduceArgumentOnLoad", false);
                SetStaticProperty(configType, "FunctionNotCalledWarning", Enum.Parse(warning.PropertyType, "DISPLAY"));
                True(!LazyErbPolicy.IsEnabledForCurrentMode, "function warning prerequisite remains enforced");
            }
            finally
            {
                foreach ((PropertyInfo property, object? value) in oldValues)
                    property.GetSetMethod(nonPublic: true)!.Invoke(null, [value]);
            }
        });
    }

    private static void RuntimePolicyUsesEffectiveOverrideAndConfiguredPath()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string dataRoot = Path.Combine(tempRoot, "Data");
            string erbRoot = Path.Combine(dataRoot, "ERB");
            string selectedDirectory = Path.Combine(erbRoot, "RPG");
            string gameDirectory = Path.Combine(erbRoot, "GameOnly");
            Directory.CreateDirectory(selectedDirectory);
            Directory.CreateDirectory(gameDirectory);
            string selectedFile = Path.Combine(selectedDirectory, "selected.ERB");
            string gameFile = Path.Combine(gameDirectory, "game.ERB");
            File.WriteAllText(selectedFile, "");
            File.WriteAllText(gameFile, "");

            const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/GameOnly\"]}}";
            const string user = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":true,\"フォルダ\":[\"ERB/RPG\"]}}";
            WithConfig(game, user, (_, _) =>
            {
                JSONConfig.Load();
                Type programType = typeof(MinorShift.Emuera.Program);
                Type configType = typeof(RuntimeConfig);
                PropertyInfo exeDir = programType.GetProperty("ExeDir", BindingFlags.Public | BindingFlags.Static)!;
                PropertyInfo erbDir = programType.GetProperty("ErbDir", BindingFlags.Public | BindingFlags.Static)!;
                object? oldExeDir = exeDir.GetValue(null);
                object? oldErbDir = erbDir.GetValue(null);
                Dictionary<PropertyInfo, object?> oldMode = [];
                try
                {
                    SetStaticProperty(programType, "ExeDir", dataRoot);
                    SetStaticProperty(programType, "ErbDir", erbRoot);
                    foreach ((Type owner, string name) in new[]
                    {
                        (programType, "DebugMode"),
                        (programType, "AnalysisMode"),
                        (configType, "IgnoreUncalledFunction"),
                        (configType, "NeedReduceArgumentOnLoad"),
                        (configType, "FunctionNotCalledWarning"),
                    })
                    {
                        PropertyInfo property = owner.GetProperty(name, BindingFlags.Public | BindingFlags.Static)!;
                        oldMode[property] = property.GetValue(null);
                    }
                    SetStaticProperty(programType, "DebugMode", false);
                    SetStaticProperty(programType, "AnalysisMode", false);
                    SetStaticProperty(configType, "IgnoreUncalledFunction", true);
                    SetStaticProperty(configType, "NeedReduceArgumentOnLoad", false);
                    PropertyInfo warning = configType.GetProperty("FunctionNotCalledWarning", BindingFlags.Public | BindingFlags.Static)!;
                    SetStaticProperty(configType, "FunctionNotCalledWarning", Enum.Parse(warning.PropertyType, "IGNORE"));

                    True(LazyErbPolicy.IsActiveTarget(selectedFile), "user block enables its selected ERB folder despite game false");
                    True(!LazyErbPolicy.IsActiveTarget(gameFile), "game-only folder is not merged into present user block");
                }
                finally
                {
                    foreach ((PropertyInfo property, object? value) in oldMode)
                        property.GetSetMethod(nonPublic: true)!.Invoke(null, [value]);
                    exeDir.GetSetMethod(nonPublic: true)!.Invoke(null, [oldExeDir]);
                    erbDir.GetSetMethod(nonPublic: true)!.Invoke(null, [oldErbDir]);
                }
            });
        });
    }

    private static void FolderSelectionNormalization()
    {
        Type dialogType = typeof(JSONConfig).Assembly.GetType("MinorShift.Emuera.Forms.LazyErbDirectoryDialog")
            ?? throw new InvalidOperationException("LazyErbDirectoryDialog is not implemented");
        MethodInfo normalize = dialogType.GetMethod("NormalizeSelectedDirectories", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeSelectedDirectories is not implemented");
        string[] selected = (string[])normalize.Invoke(null, [new[]
        {
            "ERB/RPG\\依頼",
            "ERB/RPG",
            "ERB/Other",
            "ERB/RPG",
        }])!;
        SequenceEqual(["ERB/Other", "ERB/RPG"], selected, "parent selection removes descendants, duplicates are removed, and separators normalize");
    }

    private static void FolderDialogShowsDirectoriesAndCommitsOnOk()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string dataRoot = Path.Combine(tempRoot, "Data");
            string erbRootOnDisk = Path.Combine(dataRoot, "ERB");
            string erbRoot = Path.Combine(dataRoot, "erb");
            string nested = Path.Combine(erbRootOnDisk, "RPG", "依頼");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(Path.Combine(erbRootOnDisk, "口上"));
            File.WriteAllText(Path.Combine(erbRootOnDisk, "not-a-folder.ERB"), "");

            Type dialogType = typeof(JSONConfig).Assembly.GetType("MinorShift.Emuera.Forms.LazyErbDirectoryDialog")!;
            ConstructorInfo constructor = dialogType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(string), typeof(string), typeof(IEnumerable<string>)], null)!;
            Form dialog = (Form)constructor.Invoke([dataRoot, erbRoot, Array.Empty<string>()]);
            try
            {
                TreeView tree = (TreeView)dialogType.GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                TreeNode root = tree.Nodes[0];
                Equal("ERB", root.Text, "ERB is shown as the root");
                True(!root.Checked, "ERB root is not selected");
                True(!root.Nodes.Cast<TreeNode>().Any(node => node.Text == "not-a-folder.ERB"), "files are not listed");
                TreeNode rpg = root.Nodes.Cast<TreeNode>().Single(node => node.Text == "RPG");
                TreeNode request = rpg.Nodes.Cast<TreeNode>().Single(node => node.Text == "依頼");
                root.Checked = true;
                True(!root.Checked, "root cannot be checked");
                rpg.Checked = true;
                request.Checked = true;
                TreeNode greeting = root.Nodes.Cast<TreeNode>().Single(node => node.Text == "口上");
                greeting.Checked = true;

                dialogType.GetMethod("CommitSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
                Equal(DialogResult.OK, dialog.DialogResult, "accept records an OK result");
                string[] committed = (string[])dialogType.GetProperty("SelectedDirectories", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                SequenceEqual(["ERB/RPG", "ERB/口上"], committed, "independent selections remain, parent removes its selected child, and ERB casing is canonical");
            }
            finally
            {
                dialog.Dispose();
            }

            Form canceled = (Form)constructor.Invoke([dataRoot, erbRoot, new[] { "ERB/口上" }]);
            try
            {
                object? selectedBeforeOk = dialogType.GetProperty("SelectedDirectories", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canceled);
                True(selectedBeforeOk is null, "selection result is unavailable before OK");
                Equal(DialogResult.None, canceled.DialogResult, "unaccepted dialog has no OK result");
            }
            finally
            {
                canceled.Dispose();
            }
        });
    }

    private static void FolderDialogCheckChangesCascade()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string dataRoot = Path.Combine(tempRoot, "Data");
            string erbRoot = Path.Combine(dataRoot, "ERB");
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "イベント", "サブイベント"));
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "依頼"));
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "戦闘"));

            Type dialogType = typeof(JSONConfig).Assembly.GetType("MinorShift.Emuera.Forms.LazyErbDirectoryDialog")!;
            ConstructorInfo constructor = dialogType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(string), typeof(string), typeof(IEnumerable<string>)], null)!;
            Form dialog = (Form)constructor.Invoke([dataRoot, erbRoot, Array.Empty<string>()]);
            try
            {
                TreeView tree = (TreeView)dialogType.GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                TreeNode root = tree.Nodes[0];
                TreeNode rpg = root.Nodes.Cast<TreeNode>().Single(node => node.Text == "RPG");
                TreeNode events = rpg.Nodes.Cast<TreeNode>().Single(node => node.Text == "イベント");
                TreeNode subevents = events.Nodes.Cast<TreeNode>().Single(node => node.Text == "サブイベント");
                TreeNode request = rpg.Nodes.Cast<TreeNode>().Single(node => node.Text == "依頼");
                TreeNode battle = rpg.Nodes.Cast<TreeNode>().Single(node => node.Text == "戦闘");

                events.Checked = true;
                request.Checked = true;
                battle.Checked = true;
                True(!rpg.Checked, "checking every child individually does not promote its parent");

                rpg.Checked = true;
                True(events.Checked && subevents.Checked && request.Checked && battle.Checked,
                    "checking a parent checks every descendant");
                rpg.Checked = false;
                True(!events.Checked && !subevents.Checked && !request.Checked && !battle.Checked,
                    "unchecking a parent unchecks every descendant");

                rpg.Checked = true;
                events.Checked = false;
                True(!rpg.Checked, "turning one child off clears the parent check");
                True(!events.Checked && !subevents.Checked, "turning a child off also clears its descendants");
                True(request.Checked && battle.Checked, "other checked siblings remain checked");

                dialogType.GetMethod("CommitSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
                string[] committed = (string[])dialogType.GetProperty("SelectedDirectories", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                SequenceEqual(["ERB/RPG/依頼", "ERB/RPG/戦闘"], committed,
                    "partial child selection saves only the selected sibling paths");
            }
            finally
            {
                dialog.Dispose();
            }
        });
    }

    private static void FolderDialogSelectedParentExpandsAndCommits()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string dataRoot = Path.Combine(tempRoot, "Data");
            string erbRoot = Path.Combine(dataRoot, "ERB");
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "イベント", "サブイベント"));
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "依頼"));
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "戦闘"));

            Type dialogType = typeof(JSONConfig).Assembly.GetType("MinorShift.Emuera.Forms.LazyErbDirectoryDialog")!;
            ConstructorInfo constructor = dialogType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(string), typeof(string), typeof(IEnumerable<string>)], null)!;
            Form dialog = (Form)constructor.Invoke([dataRoot, erbRoot, new[] { "ERB/RPG" }]);
            try
            {
                TreeView tree = (TreeView)dialogType.GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                TreeNode rpg = tree.Nodes[0].Nodes.Cast<TreeNode>().Single(node => node.Text == "RPG");
                True(rpg.Checked, "the saved parent is checked");
                True(rpg.Nodes.Cast<TreeNode>().All(node => node.Checked), "saved parent is visibly applied to its children");
                True(rpg.Nodes.Cast<TreeNode>().SelectMany(node => node.Nodes.Cast<TreeNode>()).All(node => node.Checked),
                    "saved parent is visibly applied to nested descendants");

                dialogType.GetMethod("CommitSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
                string[] committed = (string[])dialogType.GetProperty("SelectedDirectories", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                SequenceEqual(["ERB/RPG"], committed, "parent selection does not redundantly save child paths");
            }
            finally
            {
                dialog.Dispose();
            }
        });
    }

    private static void FolderDialogCancelReturnsNoSelection()
    {
        WithTemporaryDirectory(tempRoot =>
        {
            string dataRoot = Path.Combine(tempRoot, "Data");
            string erbRoot = Path.Combine(dataRoot, "ERB");
            Directory.CreateDirectory(Path.Combine(erbRoot, "RPG", "依頼"));

            Type dialogType = typeof(JSONConfig).Assembly.GetType("MinorShift.Emuera.Forms.LazyErbDirectoryDialog")!;
            ConstructorInfo constructor = dialogType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(string), typeof(string), typeof(IEnumerable<string>)], null)!;
            Form dialog = (Form)constructor.Invoke([dataRoot, erbRoot, new[] { "ERB/RPG" }]);
            try
            {
                dialog.Show();
                Application.DoEvents();
                Button cancel = dialog.Controls.OfType<FlowLayoutPanel>()
                    .SelectMany(panel => panel.Controls.OfType<Button>())
                    .Single(button => button.DialogResult == DialogResult.Cancel);
                cancel.PerformClick();
                Application.DoEvents();

                Equal(DialogResult.Cancel, dialog.DialogResult, "cancel closes without confirming");
                object? selected = dialogType.GetProperty("SelectedDirectories", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog);
                True(selected is null, "cancel returns no selected-directory value");
            }
            finally
            {
                dialog.Dispose();
            }
        });
    }

    private static void CheckboxDoubleClickSuppressionTargetsStateImages()
    {
        using Form host = new() { ClientSize = new System.Drawing.Size(320, 180) };
        using TreeView tree = new() { CheckBoxes = true, Dock = DockStyle.Fill };
        TreeNode node = tree.Nodes.Add("RPG");
        host.Controls.Add(tree);
        host.Show();
        Application.DoEvents();
        node.EnsureVisible();
        Application.DoEvents();

        Point? stateImagePoint = null;
        for (int y = Math.Max(0, node.Bounds.Top); y < node.Bounds.Bottom && stateImagePoint is null; y++)
        {
            for (int x = 0; x < node.Bounds.Left; x++)
            {
                TreeViewHitTestInfo hit = tree.HitTest(x, y);
                if (ReferenceEquals(hit.Node, node) && (hit.Location & TreeViewHitTestLocations.StateImage) != 0)
                {
                    stateImagePoint = new Point(x, y);
                    break;
                }
            }
        }
        True(stateImagePoint.HasValue, "a real checkbox hit-test position is available");
        if (!stateImagePoint.HasValue)
            return;

        Point labelPoint = new(node.Bounds.Left + Math.Max(1, node.Bounds.Width / 2), node.Bounds.Top + node.Bounds.Height / 2);
        True((tree.HitTest(labelPoint).Location & TreeViewHitTestLocations.Label) != 0, "label point is outside the checkbox");

        Equal(true, LazyErbDirectoryDialog.ShouldSuppressCheckboxDoubleClick(tree, PackClientPoint(stateImagePoint.Value)),
            "state-image double-click is suppressed");
        Equal(false, LazyErbDirectoryDialog.ShouldSuppressCheckboxDoubleClick(tree, PackClientPoint(labelPoint)),
            "label double-click is not suppressed");
    }

    private static IntPtr PackClientPoint(Point point)
        => new(unchecked((point.Y << 16) | (point.X & 0xffff)));

    private static void ConfigDialogLazyErbTabDoesNotSaveUntilConfirmation()
    {
        string[] resourceKeys = [
            "ConfigDialog_LazyErb", "ConfigDialog_LazyErb_UseGameSettings", "ConfigDialog_LazyErb_UseLocalSettings",
            "ConfigDialog_LazyErb_Enabled", "ConfigDialog_LazyErb_Directories", "ConfigDialog_LazyErb_ChooseDirectories",
            "ConfigDialog_LazyErb_Reset", "ConfigDialog_LazyErb_Help", "LazyErbDirectoryDialog_Title",
            "LazyErbDirectoryDialog_Description", "LazyErbDirectoryDialog_Ok", "LazyErbDirectoryDialog_Cancel",
            "ConfigDialog_Display_UseDarkMode", "ConfigDialog_Display_DarkModeRestart",
        ];
        foreach (string key in resourceKeys)
        {
            string? english = FormLocalization.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"));
            string? japanese = FormLocalization.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("ja-JP"));
            string? chinese = FormLocalization.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("zh-Hans"));
            True(!string.IsNullOrWhiteSpace(english), $"English resource exists for {key}");
            True(!string.IsNullOrWhiteSpace(japanese) && japanese != english, $"Japanese resource exists for {key}");
            True(!string.IsNullOrWhiteSpace(chinese) && chinese != english, $"Chinese resource exists for {key}");
        }

        string englishDescription = FormLocalization.ResourceManager.GetString("LazyErbDirectoryDialog_Description", CultureInfo.GetCultureInfo("en"))!;
        string japaneseDescription = FormLocalization.ResourceManager.GetString("LazyErbDirectoryDialog_Description", CultureInfo.GetCultureInfo("ja-JP"))!;
        string chineseDescription = FormLocalization.ResourceManager.GetString("LazyErbDirectoryDialog_Description", CultureInfo.GetCultureInfo("zh-Hans"))!;
        True(englishDescription.Contains("all subfolders", StringComparison.OrdinalIgnoreCase), "English description explains descendant inclusion");
        True(japaneseDescription.Contains("サブフォルダもすべて対象"), "Japanese description explains descendant inclusion");
        True(chineseDescription.Contains("所有子文件夹"), "Chinese description explains descendant inclusion");

        const string game = "{\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/Game\"]}}";
        WithConfig(game, "{}", (gamePath, userPath) =>
        {
            JSONConfig.Load();
            byte[] gameBefore = File.ReadAllBytes(gamePath);
            byte[] userBefore = File.ReadAllBytes(userPath);
            EmueraConsole originalConsole = GlobalStatic.Console;
            EmueraConsole testConsole = (EmueraConsole)RuntimeHelpers.GetUninitializedObject(typeof(EmueraConsole));
            typeof(EmueraConsole).GetField("CBProc", BindingFlags.Public | BindingFlags.Instance)!
                .SetValue(testConsole, new ClipboardProcessor(null));
            GlobalStatic.Console = testConsole;
            try
            {
                using ConfigDialog dialog = new();
                dialog.SetConfig(null!);

                TabControl tabs = (TabControl)typeof(ConfigDialog)
                    .GetField("tabControl", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                True(tabs.TabPages.Cast<TabPage>().Any(page => page.Text == "Startup" || page.Text == "起動" || page.Text == "启动"),
                    "LazyERB startup tab is present in supported resources");
                RadioButton recommended = (RadioButton)typeof(ConfigDialog)
                    .GetField("_lazyUseGameSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                RadioButton local = (RadioButton)typeof(ConfigDialog)
                    .GetField("_lazyUseLocalSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                CheckBox useDarkMode = (CheckBox)typeof(ConfigDialog)
                    .GetField("_useDarkMode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                Equal(false, useDarkMode.Checked, "settings dialog displays the currently loaded dark-mode setting");
                True(recommended.Checked && !local.Checked, "missing user override initially selects game settings");

                local.Checked = true;
                True(!JSONConfig.HasUserLazyErbOverride, "changing the radio only edits the dialog draft");
                SequenceEqual(gameBefore, File.ReadAllBytes(gamePath), "opening/editing the dialog does not save game settings");
                SequenceEqual(userBefore, File.ReadAllBytes(userPath), "opening/editing the dialog does not save user settings");

                useDarkMode.Checked = true;
                recommended.Checked = true;
                typeof(ConfigDialog).GetMethod("SaveConfig", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(dialog, null);
                True(File.ReadAllLines(_themeConfigPath!, Config.Encode).Contains("ダークモードを使用する:YES"), "dialog save writes dark mode through emuera.config");
                Equal(false, ReadRuntimeConfig<bool>("UseDarkMode"), "dialog save leaves the running theme unchanged until restart");
            }
            finally
            {
                GlobalStatic.Console = originalConsole;
            }
        });
    }

    private static void RunReparsePointCase()
    {
        TotalCases++;
        try
        {
            WithTemporaryDirectory(dataRoot =>
            {
                string erbRoot = Path.Combine(dataRoot, "ERB");
                string outside = Path.Combine(dataRoot, "External");
                string link = Path.Combine(erbRoot, "Junction");
                Directory.CreateDirectory(erbRoot);
                Directory.CreateDirectory(outside);
                try
                {
                    try
                    {
                        Directory.CreateSymbolicLink(link, outside);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        if (!TryCreateJunction(link, outside, out string junctionFailure))
                        {
                            SkippedCases++;
                            Console.WriteLine($"SKIP/UNAVAILABLE: reparse fixture could not be created — {exception.Message}; {junctionFailure}");
                            return;
                        }
                    }
                    catch (IOException exception)
                    {
                        if (!TryCreateJunction(link, outside, out string junctionFailure))
                        {
                            SkippedCases++;
                            Console.WriteLine($"SKIP/UNAVAILABLE: reparse fixture could not be created — {exception.Message}; {junctionFailure}");
                            return;
                        }
                    }

                    True(!IsSafeDirectoryPath(dataRoot, erbRoot, "ERB/Junction"), "reparse-point directory is rejected");
                    MethodInfo candidateSafety = typeof(LazyErbPolicy).GetMethod("HasSafePathComponents", BindingFlags.NonPublic | BindingFlags.Static)
                        ?? throw new InvalidOperationException("candidate path reparse guard is missing");
                    True(!(bool)candidateSafety.Invoke(null, [erbRoot, Path.Combine(link, "outside.ERB"), false])!,
                        "candidate ERB path beneath a reparse point is rejected");

                    Type dialogType = typeof(JSONConfig).Assembly.GetType("MinorShift.Emuera.Forms.LazyErbDirectoryDialog")!;
                    ConstructorInfo constructor = dialogType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
                        [typeof(string), typeof(string), typeof(IEnumerable<string>)], null)!;
                    using Form dialog = (Form)constructor.Invoke([dataRoot, erbRoot, Array.Empty<string>()]);
                    TreeView tree = (TreeView)dialogType.GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                    True(tree.Nodes[0].Nodes.Cast<TreeNode>().All(node => node.Text != "Junction"),
                        "folder picker does not list reparse-point directories");
                    Console.WriteLine("PASS: reparse-point directory is rejected");
                }
                finally
                {
                    if (Directory.Exists(link))
                        Directory.Delete(link, recursive: false);
                }
            });
        }
        catch (Exception exception)
        {
            Failures.Add("reparse-point directory is rejected");
            Console.WriteLine($"FAIL: reparse-point directory is rejected — {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsSafeDirectoryPath(string dataRoot, string erbRoot, string configuredDirectory)
        => LazyErbPolicy.IsSafeDirectoryPath(dataRoot, erbRoot, configuredDirectory);

    private static bool TryCreateJunction(string link, string target, out string failure)
    {
        try
        {
            ProcessStartInfo startInfo = new("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(link);
            startInfo.ArgumentList.Add(target);
            using Process process = Process.Start(startInfo) ?? throw new IOException("cmd.exe could not be started");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            failure = $"junction creation exited {process.ExitCode}: {output} {error}";
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception exception)
        {
            failure = exception.Message;
            return false;
        }
    }

    private static void SetStaticProperty(Type owner, string name, object value)
    {
        PropertyInfo property = owner.GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{owner.Name}.{name} was not found");
        property.GetSetMethod(nonPublic: true)!.Invoke(null, [value]);
    }

    private static T ReadRuntimeConfig<T>(string propertyName)
    {
        PropertyInfo property = typeof(RuntimeConfig).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Config.{propertyName} was not found");
        return (T)property.GetValue(null)!;
    }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Emuera.ConfigRegressionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void SavePreservesUnknownProperties()
    {
        const string game = "{\"UnknownGame\":{\"kept\":5},\"起動時に読み込まないERBフォルダ\":{\"有効\":true,\"フォルダ\":[\"ERB/Game\"],\"UnknownNested\":{\"game\":1}}}";
        const string user = "{\"UnknownUser\":{\"kept\":8},\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[\"ERB/User\"],\"UnknownNested\":{\"user\":2}}}";
        WithConfig(game, user, (gamePath, userPath) =>
        {
            JSONConfig.Load();
            JSONConfig.Save();
            JsonObject savedGame = ReadObject(gamePath);
            JsonObject savedUser = ReadObject(userPath);
            JsonObject? userUnknown = savedUser["UnknownUser"] as JsonObject;
            JsonObject? userLazy = savedUser["起動時に読み込まないERBフォルダ"] as JsonObject;
            Equal(5, savedGame["UnknownGame"]!["kept"]!.GetValue<int>(), "game top-level unknown survives");
            Equal(1, savedGame["起動時に読み込まないERBフォルダ"]!["UnknownNested"]!["game"]!.GetValue<int>(), "game nested unknown survives");
            True(userUnknown is not null, "user top-level unknown object is retained");
            Equal(8, userUnknown!["kept"]!.GetValue<int>(), "user top-level unknown survives");
            True(userLazy is not null, "user LazyERB override is retained");
            Equal(2, userLazy!["UnknownNested"]!["user"]!.GetValue<int>(), "user nested unknown survives");
        });
    }

    private static void MalformedBlockIsNotRewritten(string side, string malformed)
    {
        string validGame = "{\"UnknownGame\":\"keep\",\"起動時に読み込まないERBフォルダ\":{\"有効\":false,\"フォルダ\":[]}}";
        string validUser = "{\"UnknownUser\":\"keep\"}";
        string game = side == "game"
            ? $"{{\"UnknownGame\":\"keep\",\"起動時に読み込まないERBフォルダ\":{malformed}}}"
            : validGame;
        string user = side == "user"
            ? $"{{\"UnknownUser\":\"keep\",\"起動時に読み込まないERBフォルダ\":{malformed}}}"
            : validUser;

        WithConfig(game, user, (gamePath, userPath) =>
        {
            byte[] badFileBefore = File.ReadAllBytes(side == "game" ? gamePath : userPath);
            bool threw = false;
            try
            {
                JSONConfig.Load();
            }
            catch (JsonException)
            {
                threw = true;
            }
            True(threw, $"present malformed {side} block throws JsonException");
            SequenceEqual(badFileBefore, File.ReadAllBytes(side == "game" ? gamePath : userPath), "malformed source bytes stay unchanged");
        });
    }

    private static (bool? Enabled, string[] Directories) ReadEffectiveLazyErb()
    {
        PropertyInfo property = typeof(JSONConfig).GetProperty("EffectiveLazyErb", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("JSONConfig.EffectiveLazyErb is not implemented");
        object value = property.GetValue(null) ?? throw new InvalidOperationException("EffectiveLazyErb returned null");
        Type type = value.GetType();
        bool? enabled = (bool?)type.GetProperty("Enabled")!.GetValue(value);
        string[] directories = (string[])type.GetProperty("Directories")!.GetValue(value)!;
        return (enabled, directories);
    }

    private static (bool? Enabled, string[] Directories) ReadConfiguredUserLazyErb()
    {
        PropertyInfo property = typeof(JSONConfig).GetProperty("UserLazyErbOverride", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("JSONConfig.UserLazyErbOverride is not implemented");
        object value = property.GetValue(null) ?? throw new InvalidOperationException("there is no configured user override");
        Type type = value.GetType();
        bool? enabled = (bool?)type.GetProperty("Enabled")!.GetValue(value);
        string[] directories = (string[])type.GetProperty("Directories")!.GetValue(value)!;
        return (enabled, directories);
    }

    private static bool ReadHasUserLazyErbOverride()
    {
        PropertyInfo property = typeof(JSONConfig).GetProperty("HasUserLazyErbOverride", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("JSONConfig.HasUserLazyErbOverride is not implemented");
        return (bool)property.GetValue(null)!;
    }

    private static void InvokeStatic(string methodName, params object?[] arguments)
    {
        MethodInfo method = typeof(JSONConfig).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"JSONConfig.{methodName} is not implemented");
        method.Invoke(null, arguments);
    }

    private static void WithConfig(string gameJson, string? userJson, Action<string, string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Emuera.ConfigRegressionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string gamePath = Path.Combine(directory, "setting.json");
        string userPath = Path.Combine(directory, "setting_user.json");
        string fieldGame = "_gameConfigFilePath";
        string fieldUser = "_userConfigFilePath";
        FieldInfo gameField = typeof(JSONConfig).GetField(fieldGame, BindingFlags.NonPublic | BindingFlags.Static)!;
        FieldInfo userField = typeof(JSONConfig).GetField(fieldUser, BindingFlags.NonPublic | BindingFlags.Static)!;
        object? oldGamePath = gameField.GetValue(null);
        object? oldUserPath = userField.GetValue(null);

        try
        {
            WriteJson(gamePath, gameJson);
            if (userJson is not null)
                WriteJson(userPath, userJson);
            gameField.SetValue(null, gamePath);
            userField.SetValue(null, userPath);
            test(gamePath, userPath);
        }
        finally
        {
            gameField.SetValue(null, oldGamePath);
            userField.SetValue(null, oldUserPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteJson(string path, string json)
        => File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static JsonObject ReadObject(string path)
        => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private sealed class TestUnavailableException(string message) : Exception(message);

    private static void Run(string name, Action test)
    {
        TotalCases++;
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (TestUnavailableException exception)
        {
            SkippedCases++;
            Console.WriteLine($"SKIP/UNAVAILABLE: {name} — {exception.Message}");
        }
        catch (Exception exception)
        {
            Failures.Add(name);
            Exception cause = exception.GetBaseException();
            Console.WriteLine($"FAIL: {name} — {cause.GetType().Name}: {cause.Message}");
            Console.WriteLine(exception);
        }
    }

    private static void True(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected <{expected}>, got <{actual}>");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"{message}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }
}

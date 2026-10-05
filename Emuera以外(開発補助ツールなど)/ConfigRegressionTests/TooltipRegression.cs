using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MinorShift.Emuera.Forms;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.UI.Game.Image;

namespace Emuera.ConfigRegressionTests;

// [Emuera改修:TOOLTIP-01] 通常の試験構成でCBG対象置換と製品callbackの終了順序を確認する。
// 順序固定hostは通常操作の自然再現とは区別する。製品側に試験hookを追加しない。
internal static class TooltipRegression
{
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<Action> work = new();
        public override void Post(SendOrPostCallback callback, object? state) => work.Enqueue(() => callback(state));
        public void WaitForWork()
        {
            if (!SpinWait.SpinUntil(() => !work.IsEmpty, 3000))
                throw new Exception("actual tooltip callback was not posted");
        }
        public void RunOne()
        {
            if (!work.TryDequeue(out var action)) throw new Exception("tooltip callback missing");
            action();
        }
    }

    internal static void SameNumberReplacementUsesNewTitle()
    {
        using var window = OpenWindow(out var console);
        using var image = new GraphicsImage();
        image.GCreate(80, 30, false); image.GClear(Color.Red);
        using var map = CreateMap(window);
        using var sprite = new SpriteG("tooltip-replacement", image, new Rectangle(0, 0, 80, 30));
        console.CBG_SetButtonMap(map);
        console.CBG_SetButtonImage(1, sprite, sprite, 20, 0, 2, "OLD_CBG_1");
        console.MoveMouse(new Point(40, 60));
        window.MainPicBox.Refresh();
        if (window.ToolTip.GetToolTip(window.MainPicBox) != "OLD_CBG_1")
            throw new Exception("old target tooltip was not established");
        console.CBG_ClearRange(2, 2);
        console.CBG_SetButtonImage(1, sprite, sprite, 20, 0, 2, "NEW_CBG_1");
        console.MoveMouse(new Point(40, 60));
        window.MainPicBox.Refresh();
        if (window.ToolTip.GetToolTip(window.MainPicBox) != "NEW_CBG_1")
            throw new Exception("same-number replacement retained old tooltip text");
    }

    internal static void DisposedWindowRejectsCallback() => VerifyShutdown(true);
    internal static void CallbackDoesNotPumpQueuedClose() => VerifyShutdown(false);

    private static void VerifyShutdown(bool disposeBeforeCallback)
    {
        var cursor = Cursor.Position;
        using var window = OpenWindow(out var console);
        using var image = new GraphicsImage();
        image.GCreate(80, 30, false); image.GClear(Color.Red);
        using var map = CreateMap(window);
        using var sprite = new SpriteG("tooltip-shutdown", image, new Rectangle(0, 0, 80, 30));
        console.CBG_SetButtonMap(map);
        console.CBG_SetButtonImage(1, sprite, sprite, 20, 0, 2, "SHUTDOWN_TOOLTIP");
        window.ToolTip.InitialDelay = 40;
        typeof(EmueraConsole).GetField("tooltip_duration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(console, 3000);
        var queued = new QueuedContext();
        var previous = SynchronizationContext.Current;
        try
        {
            Cursor.Position = window.MainPicBox.PointToScreen(new Point(40, 60));
            console.MoveMouse(new Point(40, 60));
            SynchronizationContext.SetSynchronizationContext(queued);
            window.MainPicBox.Refresh();
            queued.WaitForWork();
            SynchronizationContext.SetSynchronizationContext(previous);
            if (disposeBeforeCallback) window.Dispose();
            else
            {
                Cursor.Position = window.MainPicBox.PointToScreen(new Point(-30, -30));
                window.BeginInvoke(new Action(window.Close));
            }
            // 実際に製品のTask.Delay→Postが生成したcallbackを実行する。
            queued.RunOne();
            if (!disposeBeforeCallback && window.IsDisposed)
                throw new Exception("tooltip callback pumped the queued window close");
            Application.DoEvents();
            if (!window.IsDisposed) throw new Exception("queued window close was not processed");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            Cursor.Position = cursor;
        }
    }

    private static GraphicsImage CreateMap(MainWindow window)
    {
        var map = new GraphicsImage();
        map.GCreate(window.MainPicBox.Width, window.MainPicBox.Height, false);
        map.GClear(Color.FromArgb(unchecked((int)0xff000001)));
        return map;
    }

    private static MainWindow OpenWindow(out EmueraConsole console)
    {
        Config.SetConfig(ConfigData.Instance);
        JSONConfig.Game = new JSONGameConfigData(); JSONConfig.User = new JSONUserConfigData();
        var window = new MainWindow([]);
        var init = typeof(MainWindow).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic)!;
        window.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), window, init);
        window.ShowInTaskbar = false;
        window.Show();
        console = (EmueraConsole)typeof(MainWindow).GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        console.WaitInput(new InputRequest { InputType = InputType.IntValue });
        return window;
    }
}

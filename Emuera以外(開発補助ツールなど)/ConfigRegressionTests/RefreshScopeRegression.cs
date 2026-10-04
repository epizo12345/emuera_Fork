using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MinorShift.Emuera.Forms;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.UI.Game;

namespace Emuera.ConfigRegressionTests;

internal static class RefreshScopeRegression
{
    private static readonly MethodInfo DrawMethod =
        typeof(EmueraConsole).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static void Verify()
    {
        using var window = OpenWindow(out var console);
        int formPaints = 0;
        int surfacePaints = 0;
        int menuPaints = 0;
        window.Paint += (_, _) => formPaints++;
        window.MainPicBox.PaintSurface += (_, _) => surfacePaints++;
        var menu = window.Controls.OfType<MenuStrip>().First();
        menu.Paint += (_, _) => menuPaints++;
        DrawMethod.Invoke(console, null);
        Console.WriteLine($"REFRESH_SCOPE form={formPaints} surface={surfacePaints} menu={menuPaints} textbox={window.TextBox.Visible}");
        if (surfacePaints != 1)
            throw new Exception("one animation update must repaint the game surface exactly once");
        if (formPaints != 0 || menuPaints != 0)
            throw new Exception("animation update repainted controls outside the game surface");
        if (!window.TextBox.Visible)
            throw new Exception("input field became invisible");
        window.TextBox.Text = "42";
        window.ToolTip.SetToolTip(window.MainPicBox, "image hint");
        window.WindowState = FormWindowState.Minimized;
        Application.DoEvents();
        window.WindowState = FormWindowState.Normal;
        Application.DoEvents();
        formPaints = surfacePaints = menuPaints = 0;
        DrawMethod.Invoke(console, null);
        if (surfacePaints != 1 || formPaints != 0 || menuPaints != 0 ||
            window.TextBox.Text != "42" || window.ToolTip.GetToolTip(window.MainPicBox) != "image hint")
            throw new Exception("restore, input field, menu, or tooltip state changed");
    }

    private static MainWindow OpenWindow(out EmueraConsole console)
    {
        Config.SetConfig(ConfigData.Instance);
        JSONConfig.Game = new JSONGameConfigData();
        JSONConfig.User = new JSONUserConfigData();
        var window = new MainWindow([]);
        var init = typeof(MainWindow).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic)!;
        window.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), window, init);
        window.ShowInTaskbar = false;
        window.Show();
        console = (EmueraConsole)typeof(MainWindow)
            .GetField("console", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        console.WaitInput(new InputRequest { InputType = InputType.IntValue });
        return window;
    }
}

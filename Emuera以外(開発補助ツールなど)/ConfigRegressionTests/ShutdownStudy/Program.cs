using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MinorShift.Emuera.Forms;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;

internal static class ShutdownTests
{
	private delegate bool EnumProc(IntPtr window, IntPtr data);
	[DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
	[DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr value, IntPtr data);
	private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static object Get(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
	private static Task Loop(EmueraConsole console) => (Task)Get(console, "redrawTask");
	private static void Require(bool success, string message) { if (!success) throw new Exception(message); }
	private static void Pump(int milliseconds)
	{
		var clock = Stopwatch.StartNew();
		while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); }
	}
	private static MainWindow Open(out EmueraConsole console)
	{
		Config.SetConfig(ConfigData.Instance);
		JSONConfig.Game = new JSONGameConfigData(); JSONConfig.User = new JSONUserConfigData();
		var window = new MainWindow([]);
		var init = typeof(MainWindow).GetMethod("Init", Private)!;
		window.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), window, init);
		window.ShowInTaskbar = false; window.Show();
		console = (EmueraConsole)Get(window, "console");
		return window;
	}
	[STAThread]
	private static int Main(string[] args)
	{
		System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
		AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("UNHANDLED=" + e.ExceptionObject);
		try
		{
			string test = args.Length == 0 ? "lifetime" : args[0];
			using var window = Open(out var console);
			try
			{
				switch (test)
				{
					case "lifetime":
						console.setRedrawTimer(10); Pump(100);
						Require(!Loop(console).IsCompleted, "redraw Task completed while the actual periodic loop was still running");
						console.Dispose(); console.Dispose(); Pump(80);
						Require(Loop(console).IsCompletedSuccessfully, "actual redraw loop did not finish successfully after disposal");
						console.setRedrawTimer(10); Pump(40);
						Require(Loop(console).IsCompletedSuccessfully, "disposed loop restarted");
						break;
					case "immediate-dispose":
						for (int attempt = 0; attempt < 30; attempt++)
						{
							using var shortWindow = Open(out var shortConsole);
							shortConsole.setRedrawTimer(10); shortWindow.Dispose(); Pump(30);
							Require(Loop(shortConsole).IsCompletedSuccessfully, $"immediate disposal: task={Loop(shortConsole).Status}, error={Loop(shortConsole).Exception}");
						}
						// 外側の窓も通常通り後始末する。
						console.setRedrawTimer(10); console.Dispose(); Pump(50);
						break;
					case "direct-dispose":
						console.setRedrawTimer(10); Pump(50);
						window.Dispose(); Pump(100);
						Require(!(bool)Get(console, "isRedrawEnabled"), "direct MainWindow.Dispose left redraw enabled");
						Require(Loop(console).IsCompletedSuccessfully, "direct Dispose left the periodic loop running");
						break;
					case "menu-cancel":
						console.WaitInput(new InputRequest { InputType = InputType.IntValue });
						int menuPaints = 0; bool foundDialog = false;
						window.MainPicBox.PaintSurface += (_, _) => menuPaints++;
						console.setRedrawTimer(10);
						var cancelDialog = Task.Run(() =>
						{
							var deadline = Stopwatch.StartNew();
							while (deadline.ElapsedMilliseconds < 3000 && !foundDialog)
							{
								EnumWindows((handle, _) =>
								{
									GetWindowThreadProcessId(handle, out uint owner);
									var name = new System.Text.StringBuilder(100); GetClassName(handle, name, 100);
									if (owner != Environment.ProcessId || name.ToString() != "#32770") return true;
									Thread.Sleep(300); foundDialog = true;
									PostMessage(handle, 0x0111, (IntPtr)2, IntPtr.Zero); return false;
								}, IntPtr.Zero);
								Thread.Sleep(5);
							}
							if (!foundDialog) window.BeginInvoke(new Action(window.Close));
						});
						// 既存メニューhandlerそのものが開く終了確認を、通常のIDCANCEL通知でキャンセルする。
						typeof(MainWindow).GetMethod("exitToolStripMenuItem_Click", Private)!.Invoke(window, [window, EventArgs.Empty]);
						Pump(100);
						Require(foundDialog && !window.IsDisposed && menuPaints > 3 && !Loop(console).IsCompleted,
							$"menu cancel failed: dialog={foundDialog}, paints={menuPaints}, disposed={window.IsDisposed}");
						console.Dispose(); Pump(50);
						Console.WriteLine($"MENU_CANCEL paints={menuPaints} dialog={foundDialog}");
						break;
					case "cancel-close":
						console.setRedrawTimer(10); Pump(50);
						FormClosingEventHandler cancel = (_, e) => e.Cancel = true;
						window.FormClosing += cancel; window.Close(); Pump(50);
						Require(!window.IsDisposed && (bool)Get(console, "isRedrawEnabled"), "cancelled close stopped animation");
						var timer = (System.Timers.Timer)Get(console, "genericTimer");
						timer.Enabled = true; timer.Enabled = false;
						Require(!Loop(console).IsCompleted, "cancelled close ended actual redraw loop");
						window.FormClosing -= cancel; window.Close(); Pump(100);
						Require(Loop(console).IsCompletedSuccessfully, "accepted close left the periodic loop alive");
						break;
					case "queued-dispose":
						console.WaitInput(new InputRequest { InputType = InputType.IntValue });
						console.setRedrawTimer(10);
						// UIを短時間busyにし、通常でも起こる「配送待ち→直接Dispose」の順序を固定する。
						Thread.Sleep(100); window.Dispose(); Pump(120);
						Require(Loop(console).IsCompletedSuccessfully, "queued redraw did not cancel/finish on disposal");
						break;
					case "paint-close":
						console.WaitInput(new InputRequest { InputType = InputType.IntValue });
						int closing = 0;
						window.MainPicBox.PaintSurface += (_, _) => { if (++closing == 1) window.Close(); };
						console.setRedrawTimer(10); Pump(150);
						Require(window.IsDisposed && Loop(console).IsCompletedSuccessfully, "close during dispatched paint left loop alive");
						break;
					case "slow-paint":
						console.WaitInput(new InputRequest { InputType = InputType.IntValue });
						int paints = 0, depth = 0, maximumDepth = 0;
						window.MainPicBox.PaintSurface += (_, _) =>
						{
							maximumDepth = Math.Max(maximumDepth, ++depth); paints++;
							Thread.Sleep(40); depth--;
							if (paints == 8) console.setRedrawTimer(0);
						};
						console.setRedrawTimer(1); Pump(700);
						Require(maximumDepth == 1 && paints == 8, $"slow paint reentry/backlog: depth={maximumDepth}, paints={paints}");
						Pump(100); Require(paints == 8, "stopped slow paint still received queued refreshes");
						console.Dispose(); Pump(50);
						Require(Loop(console).IsCompletedSuccessfully, "slow drawing prevented disposal completion");
						Console.WriteLine($"SLOW_PAINT paints={paints} maxDepth={maximumDepth}");
						break;
					case "unexpected-error":
						console.WaitInput(new InputRequest { InputType = InputType.IntValue });
						Exception? reported = null;
						ThreadExceptionEventHandler report = (_, e) => reported = e.Exception;
						Application.ThreadException += report;
						try
						{
							window.MainPicBox.PaintSurface += (_, _) => throw new InvalidOperationException("DRAW_TEST_NORMAL_ERROR");
							console.setRedrawTimer(10); Pump(150);
							// WM_PAINTの例外はWinFormsが直接ThreadExceptionへ送る場合があり、TaskのFaultedは必須ではない。
							Console.WriteLine($"NORMAL_ERROR task={Loop(console).Status} reported={reported}");
							Require(reported?.Message == "DRAW_TEST_NORMAL_ERROR", "ordinary error was not reported through UI exception path");
							console.Dispose();
						}
						finally { Application.ThreadException -= report; }
						break;
					default: throw new Exception("unknown test " + test);
				}
				Console.WriteLine($"PASS {test}; task={Loop(console).Status}; runtime={Environment.Version}; serverGC={System.Runtime.GCSettings.IsServerGC}");
				return 0;
			}
			finally
			{
				// 基準版の失敗後も、そのrunで作ったtimerだけを止めて次の試験へ漏らさない。
				console.setRedrawTimer(0);
				((PeriodicTimer)Get(console, "redrawTimer")).Dispose(); Pump(50);
			}
		}
		catch (Exception error) { Console.Error.WriteLine("FAIL " + error); return 1; }
	}
}

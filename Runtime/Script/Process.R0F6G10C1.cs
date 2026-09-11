#if R0_F6G10C1
#nullable enable
using System;
namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
    internal object R0F6G10C1RunDisplayPath(bool newGame)
    {
        if (!console.R0F6G10C1StartProgram())
            throw new InvalidOperationException("initial production console run was rejected");
        var title = console.R0F6G10C1DisplayModel();
        var accepted = false;
        object? next = null;
        if (newGame)
        {
            accepted = console.R0F6G10C1AcceptControlledInput("0");
            if (!accepted) throw new InvalidOperationException("controlled NEW GAME response was rejected");
            next = console.R0F6G10C1DisplayModel();
        }
        return new
        {
            RuntimeMode = Program.RuntimeMode.ToString(),
            Title = title,
            NewGameResponseAccepted = accepted,
            NewGame = next,
            CompactCounters = compactProduction?.Evidence(),
            GuiLaunched = false,
            SendKeysUsed = false,
            MacroExecuted = false,
        };
    }
}
#endif

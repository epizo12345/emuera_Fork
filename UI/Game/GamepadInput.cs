#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using MinorShift.Emuera.Runtime.Config.JSON;

namespace MinorShift.Emuera.GameView;

// [Emuera改修:GAMEPAD-V1]
// XInput / WinMM / Raw Inputの物理入力を、UIから独立した論理Actionへ変換する。
internal enum GamepadDirection
{
    None = 0,
    Up,
    Down,
    Left,
    Right,
}

internal enum GamepadDirectionSource
{
    None = 0,
    DPad,
    LeftStick,
    Unknown,
}

internal enum GamepadDirectInputProfile
{
    Auto = 0,
    Disabled,
    Wasd,
    Numpad8462,
    ArrowKeys,
}

/// <summary>
/// Face button arrangement for non-XInput controllers. Auto is only a
/// configuration preference; an active backend always resolves to Xbox or
/// PlayStationWinMM before buttons are read.
/// </summary>
internal enum GamepadFaceButtonLayout
{
    Auto = 0,
    Xbox,
    PlayStationWinMM,
}

// [Emuera改修:GAMEPAD-CONFIG-V1]
// PS/Xboxのraw番号ではなく、物理位置を共通値として保持する。
internal enum GamepadPhysicalButton
{
    None = 0,
    FaceSouth,
    FaceEast,
    FaceWest,
    FaceNorth,
    LeftShoulder,
    RightShoulder,
    Start,
    LeftTrigger,
    RightTrigger,
}

[Flags]
internal enum GamepadPhysicalButtonMask : uint
{
    None = 0,
    FaceSouth = 1 << 0,
    FaceEast = 1 << 1,
    FaceWest = 1 << 2,
    FaceNorth = 1 << 3,
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    Start = 1 << 6,
    LeftTrigger = 1 << 7,
    RightTrigger = 1 << 8,
}

internal enum GamepadActionKind
{
    None = 0,
    Direction,
    Confirm,
    Cancel,
    Escape,
    OpenSettings,
    ScrollUp,
    ScrollDown,
    Macro1,
    Macro2,
    Macro3,
}

internal sealed class GamepadBindings
{
    internal static readonly GamepadActionKind[] ConfigurableActions =
    [
        GamepadActionKind.Confirm,
        GamepadActionKind.Cancel,
        GamepadActionKind.Escape,
        GamepadActionKind.ScrollUp,
        GamepadActionKind.ScrollDown,
        GamepadActionKind.OpenSettings,
        GamepadActionKind.Macro1,
        GamepadActionKind.Macro2,
        GamepadActionKind.Macro3,
    ];

    internal static readonly GamepadPhysicalButton[] ConfigurableButtons =
    [
        GamepadPhysicalButton.None,
        GamepadPhysicalButton.FaceSouth,
        GamepadPhysicalButton.FaceEast,
        GamepadPhysicalButton.FaceWest,
        GamepadPhysicalButton.FaceNorth,
        GamepadPhysicalButton.LeftShoulder,
        GamepadPhysicalButton.RightShoulder,
        GamepadPhysicalButton.LeftTrigger,
        GamepadPhysicalButton.RightTrigger,
        GamepadPhysicalButton.Start,
    ];

    internal GamepadPhysicalButton Confirm { get; private set; }
    internal GamepadPhysicalButton Cancel { get; private set; }
    internal GamepadPhysicalButton Escape { get; private set; }
    internal GamepadPhysicalButton PreviousPage { get; private set; }
    internal GamepadPhysicalButton NextPage { get; private set; }
    internal GamepadPhysicalButton OpenSettings { get; private set; }
    internal GamepadPhysicalButton Macro1 { get; private set; }
    internal GamepadPhysicalButton Macro2 { get; private set; }
    internal GamepadPhysicalButton Macro3 { get; private set; }

    internal static GamepadBindings Default()
    {
        return new GamepadBindings
        {
            Confirm = GamepadPhysicalButton.FaceSouth,
            Cancel = GamepadPhysicalButton.FaceEast,
            Escape = GamepadPhysicalButton.FaceNorth,
            PreviousPage = GamepadPhysicalButton.LeftShoulder,
            NextPage = GamepadPhysicalButton.RightShoulder,
            OpenSettings = GamepadPhysicalButton.FaceWest,
            Macro1 = GamepadPhysicalButton.None,
            Macro2 = GamepadPhysicalButton.None,
            Macro3 = GamepadPhysicalButton.None,
        };
    }

    internal static GamepadBindings FromUserConfig()
    {
        JSONUserConfigData user = JSONConfig.User ?? new JSONUserConfigData();
        GamepadBindings defaults = Default();
        return new GamepadBindings
        {
            Confirm = Parse(user.GamepadConfirm, defaults.Confirm),
            Cancel = Parse(user.GamepadCancel, defaults.Cancel),
            Escape = Parse(user.GamepadEscape, defaults.Escape),
            PreviousPage = Parse(user.GamepadPreviousPage, defaults.PreviousPage),
            NextPage = Parse(user.GamepadNextPage, defaults.NextPage),
            OpenSettings = Parse(user.GamepadOpenSettings, defaults.OpenSettings),
            Macro1 = Parse(user.GamepadMacro1, defaults.Macro1),
            Macro2 = Parse(user.GamepadMacro2, defaults.Macro2),
            Macro3 = Parse(user.GamepadMacro3, defaults.Macro3),
        };
    }

    internal GamepadBindings Clone()
    {
        return new GamepadBindings
        {
            Confirm = Confirm,
            Cancel = Cancel,
            Escape = Escape,
            PreviousPage = PreviousPage,
            NextPage = NextPage,
            OpenSettings = OpenSettings,
            Macro1 = Macro1,
            Macro2 = Macro2,
            Macro3 = Macro3,
        };
    }

    internal GamepadPhysicalButton Get(GamepadActionKind action)
    {
        return action switch
        {
            GamepadActionKind.Confirm => Confirm,
            GamepadActionKind.Cancel => Cancel,
            GamepadActionKind.Escape => Escape,
            GamepadActionKind.ScrollUp => PreviousPage,
            GamepadActionKind.ScrollDown => NextPage,
            GamepadActionKind.OpenSettings => OpenSettings,
            GamepadActionKind.Macro1 => Macro1,
            GamepadActionKind.Macro2 => Macro2,
            GamepadActionKind.Macro3 => Macro3,
            _ => GamepadPhysicalButton.None,
        };
    }

    internal void Assign(GamepadActionKind action, GamepadPhysicalButton button)
    {
        GamepadPhysicalButton previous = Get(action);
        if (previous == button)
            return;

        if (button != GamepadPhysicalButton.None)
        {
            foreach (GamepadActionKind otherAction in ConfigurableActions)
            {
                if (otherAction != action && Get(otherAction) == button)
                {
                    Set(otherAction, previous);
                    break;
                }
            }
        }
        Set(action, button);
    }

    internal void SaveTo(JSONUserConfigData user)
    {
        user.GamepadConfirm = Confirm.ToString();
        user.GamepadCancel = Cancel.ToString();
        user.GamepadEscape = Escape.ToString();
        user.GamepadPreviousPage = PreviousPage.ToString();
        user.GamepadNextPage = NextPage.ToString();
        user.GamepadOpenSettings = OpenSettings.ToString();
        user.GamepadMacro1 = Macro1.ToString();
        user.GamepadMacro2 = Macro2.ToString();
        user.GamepadMacro3 = Macro3.ToString();
    }

    internal static string GetDisplayName(GamepadPhysicalButton button)
    {
        return button switch
        {
            GamepadPhysicalButton.FaceSouth => "下ボタン（× / A）",
            GamepadPhysicalButton.FaceEast => "右ボタン（○ / B）",
            GamepadPhysicalButton.FaceWest => "左ボタン（□ / X）",
            GamepadPhysicalButton.FaceNorth => "上ボタン（△ / Y）",
            GamepadPhysicalButton.LeftShoulder => "L1 / LB",
            GamepadPhysicalButton.RightShoulder => "R1 / RB",
            GamepadPhysicalButton.LeftTrigger => "L2 / LT",
            GamepadPhysicalButton.RightTrigger => "R2 / RT",
            GamepadPhysicalButton.Start => "OPTIONS / Start",
            _ => "未割り当て",
        };
    }

    private void Set(GamepadActionKind action, GamepadPhysicalButton button)
    {
        switch (action)
        {
            case GamepadActionKind.Confirm: Confirm = button; break;
            case GamepadActionKind.Cancel: Cancel = button; break;
            case GamepadActionKind.Escape: Escape = button; break;
            case GamepadActionKind.ScrollUp: PreviousPage = button; break;
            case GamepadActionKind.ScrollDown: NextPage = button; break;
            case GamepadActionKind.OpenSettings: OpenSettings = button; break;
            case GamepadActionKind.Macro1: Macro1 = button; break;
            case GamepadActionKind.Macro2: Macro2 = button; break;
            case GamepadActionKind.Macro3: Macro3 = button; break;
        }
    }

    private static GamepadPhysicalButton Parse(string? value, GamepadPhysicalButton fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value, out _))
            return fallback;
        return Enum.TryParse(value, ignoreCase: true, out GamepadPhysicalButton result)
            && Enum.IsDefined(result)
            ? result
            : fallback;
    }
}

internal readonly struct GamepadAction
{
    internal GamepadAction(GamepadActionKind kind, GamepadDirection direction = GamepadDirection.None,
        GamepadDirectionSource directionSource = GamepadDirectionSource.None,
        GamepadPhysicalButtonMask physicalButtons = GamepadPhysicalButtonMask.None,
        GamepadPhysicalButtonMask pressedPhysicalButtons = GamepadPhysicalButtonMask.None)
    {
        Kind = kind;
        Direction = direction;
        DirectionSource = directionSource;
        PhysicalButtons = physicalButtons;
        PressedPhysicalButtons = pressedPhysicalButtons;
    }

    internal GamepadActionKind Kind { get; }
    internal GamepadDirection Direction { get; }
    internal GamepadDirectionSource DirectionSource { get; }
    internal GamepadPhysicalButtonMask PhysicalButtons { get; }
    internal GamepadPhysicalButtonMask PressedPhysicalButtons { get; }

    internal GamepadAction WithPhysicalButtons(GamepadPhysicalButtonMask physicalButtons,
        GamepadPhysicalButtonMask pressedPhysicalButtons)
    {
        return new GamepadAction(Kind, Direction, DirectionSource, physicalButtons, pressedPhysicalButtons);
    }
}

/// <summary>
/// XInputを優先し、XInputでコントローラーが見つからない場合はWinMM joystick APIへ
/// フォールバックするゲームパッド入力層。WinMMはjoy.cplで認識されるDirectInput/HID
/// 系ゲームパッドを、外部ライブラリなしで取得するために使用する。
/// </summary>
internal sealed class GamepadManager
{
    private const int DeadZone = 8000;
    private const byte TriggerPressThreshold = 64;
    private const byte TriggerReleaseThreshold = 48;
    private const int DirectionRepeatDelayMs = 280;
    private const int DirectionRepeatIntervalMs = 90;
    private const int ShoulderRepeatDelayMs = 400;
    private const int ShoulderRepeatIntervalMs = 120;
    private const int WinmmRescanIntervalMs = 750;
    private const uint WinmmPovCentered = 0xFFFF;
    private const uint JoyReturnAll = 0x000000FF;
    private const uint MaxXInputControllers = 4;

    [Flags]
    private enum XInputButtons : ushort
    {
        DPadUp = 0x0001,
        DPadDown = 0x0002,
        DPadLeft = 0x0004,
        DPadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000,
    }

    [Flags]
    private enum LogicalButtons : uint
    {
        None = 0,
        Confirm = 1 << 0,
        Cancel = 1 << 1,
        Escape = 1 << 2,
        OpenSettings = 1 << 3,
        LeftShoulder = 1 << 4,
        RightShoulder = 1 << 5,
        Macro1 = 1 << 7,
        Macro2 = 1 << 8,
        Macro3 = 1 << 9,
    }

    private enum GamepadBackend
    {
        None = 0,
        XInput,
        Winmm,
        RawInput,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public XInputButtons Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinmmJoyCaps
    {
        public ushort wMid;
        public ushort wPid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint wXmin;
        public uint wXmax;
        public uint wYmin;
        public uint wYmax;
        public uint wZmin;
        public uint wZmax;
        public uint wNumButtons;
        public uint wPeriodMin;
        public uint wPeriodMax;
        public uint wRmin;
        public uint wRmax;
        public uint wUmin;
        public uint wUmax;
        public uint wVmin;
        public uint wVmax;
        public uint wCaps;
        public uint wMaxAxes;
        public uint wNumAxes;
        public uint wMaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szRegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szOEMVxD;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinmmJoyInfoEx
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint XInputGetStateDelegate(uint userIndex, out XInputState state);

    [DllImport("winmm.dll", EntryPoint = "joyGetNumDevs", ExactSpelling = true)]
    private static extern uint JoyGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint JoyGetDevCaps(uint joyId, out WinmmJoyCaps caps, uint capsSize);

    [DllImport("winmm.dll", EntryPoint = "joyGetPosEx", ExactSpelling = true)]
    private static extern uint JoyGetPosEx(uint joyId, ref WinmmJoyInfoEx info);

    private readonly bool diagnosticsEnabled;
    private readonly string diagnosticLogPath;
    private readonly RawInputGamepad rawInputGamepad;
    private readonly uint[] previousXInputResults = [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue];
    private XInputGetStateDelegate? getState;
    private GamepadBindings bindings;
    private WinmmButtonMapping winmmButtonMapping;
    private WinmmJoyCaps winmmCaps;
    private bool xinputDisabled;
    private bool winmmAvailable;
    private bool connected;
    private bool hasPreviousSample;
    private GamepadBackend backend;
    private int xinputIndex = -1;
    private uint winmmId;
    private LogicalButtons previousButtons;
    private GamepadPhysicalButtonMask previousPhysicalButtons;
    private GamepadDirection previousDPadDirection;
    private GamepadDirection previousStickDirection;
    private long nextDPadDirectionRepeat;
    private long nextStickDirectionRepeat;
    private LogicalButtons repeatShoulder;
    private long nextShoulderRepeat;
    private long nextWinmmRescan;
    private uint previousRawButtons;
    private uint previousRawPov;
    private int previousRawX;
    private int previousRawY;
    private uint previousRawZ = uint.MaxValue;
    private uint previousRawR = uint.MaxValue;
    private uint previousRawU = uint.MaxValue;
    private uint previousRawV = uint.MaxValue;
    private int previousRawLeftTrigger = -1;
    private int previousRawRightTrigger = -1;
    private bool suppressInputUntilRelease;
    private bool xinputDiagnosticInitialized;
    private bool xinputLeftTriggerActive;
    private bool xinputRightTriggerActive;
    private bool winmmDiagnosticLogged;
    private uint winmmDiagnosticDeviceCount;
    private uint winmmLiveDeviceCount;
    private WinmmButtonMapping rawInputButtonMapping;
    private GamepadFaceButtonLayout winmmFaceButtonLayout = GamepadFaceButtonLayout.Xbox;
    private GamepadFaceButtonLayout rawInputFaceButtonLayout = GamepadFaceButtonLayout.Xbox;
    private bool firstPollAfterActivation;
    private long rawInputReceivedCount;
    private long rawInputParsedCount;
    private long rawInputSkippedCount;
    private long rawInputParseTotalTicks;
    private long rawInputParseMaxTicks;
    private long rawInputLastDiagnosticCount;
    private string rawInputLastSkipReason = "<none>";

    internal GamepadManager(bool diagnosticsEnabled)
    {
        this.diagnosticsEnabled = diagnosticsEnabled;
        diagnosticLogPath = Path.Combine(AppContext.BaseDirectory, "gamepad-debug.log");
        bindings = GamepadBindings.FromUserConfig();
        winmmButtonMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.Xbox, bindings);
        rawInputButtonMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.Xbox, bindings, false);
        rawInputGamepad = new RawInputGamepad(diagnosticsEnabled, WriteDiagnostic);
        LoadXInput();
        winmmAvailable = ProbeWinmm();

        Status = "Gamepad: Not Connected";
        WriteDiagnostic("Gamepad input initialized. XInput=" + (getState != null ? "available" : "unavailable")
            + ", WinMM=" + (winmmAvailable ? "available" : "unavailable"));
        WriteDiagnostic(Status);
        if (diagnosticsEnabled)
            RunGamepadInputSelfTests();
    }

    internal bool IsAvailable => getState != null || winmmAvailable || rawInputGamepad.IsAvailable;
    internal bool IsConnected => connected;
    internal bool DiagnosticsEnabled => diagnosticsEnabled;
    internal string Status { get; private set; }
    internal string DiagnosticLogPath => diagnosticLogPath;
    internal string ActiveBackend => backend.ToString();

    internal GamepadBindings GetBindingsForEditing() => bindings.Clone();

    internal void ReloadBindings()
    {
        bindings = GamepadBindings.FromUserConfig();
        winmmButtonMapping = WinmmButtonMapping.Create(winmmFaceButtonLayout, bindings);
        rawInputButtonMapping = WinmmButtonMapping.Create(rawInputFaceButtonLayout, bindings, false);
        ResetInputState();
        rawInputGamepad.ResetTransientState();
        WriteDiagnostic("Gamepad bindings reloaded; transient input state reset.");
        WriteDiagnostic("Logical mapping: " + DescribeLogicalMapping());
    }

    internal void ResetForConfiguration()
    {
        ResetInputState();
        suppressInputUntilRelease = true;
        rawInputGamepad.ResetTransientState();
    }

    internal void LogLifecycleDiagnostic(string message)
    {
        WriteDiagnostic(message);
    }

    internal bool RegisterRawInput(nint windowHandle)
    {
        return rawInputGamepad.Register(windowHandle);
    }

    internal void ProcessRawInput(nint rawInputHandle)
    {
        if (!ShouldProcessRawInputReports(backend))
        {
            if (diagnosticsEnabled)
            {
                rawInputReceivedCount++;
                rawInputSkippedCount++;
                rawInputLastSkipReason = $"active backend={backend}";
                MaybeLogRawInputCounters();
            }
            return;
        }

        if (!diagnosticsEnabled)
        {
            rawInputGamepad.Process(rawInputHandle);
            return;
        }

        rawInputReceivedCount++;
        long started = Stopwatch.GetTimestamp();
        rawInputGamepad.Process(rawInputHandle);
        long elapsed = Stopwatch.GetTimestamp() - started;
        rawInputParsedCount += rawInputGamepad.LastParsedReportCount;
        rawInputParseTotalTicks += elapsed;
        rawInputParseMaxTicks = Math.Max(rawInputParseMaxTicks, elapsed);
        MaybeLogRawInputCounters();
    }

    private static bool ShouldProcessRawInputReports(GamepadBackend currentBackend)
    {
        return currentBackend is GamepadBackend.None or GamepadBackend.RawInput;
    }

    private void MaybeLogRawInputCounters()
    {
        if (!diagnosticsEnabled || rawInputReceivedCount - rawInputLastDiagnosticCount < 256)
            return;
        rawInputLastDiagnosticCount = rawInputReceivedCount;
        WriteRawInputCounterSummary("periodic");
    }

    private void WriteRawInputCounterSummary(string reason)
    {
        if (!diagnosticsEnabled || rawInputReceivedCount == 0)
            return;
        double milliseconds = 1000.0 / Stopwatch.Frequency;
        double totalMilliseconds = rawInputParseTotalTicks * milliseconds;
        double maxMilliseconds = rawInputParseMaxTicks * milliseconds;
        WriteDiagnostic($"Raw Input counters: reason={reason}, backend={backend}, "
            + $"received={rawInputReceivedCount}, parsed={rawInputParsedCount}, skipped={rawInputSkippedCount}, "
            + $"skipReason={rawInputLastSkipReason}, parseTotalMs={totalMilliseconds:F3}, parseMaxMs={maxMilliseconds:F3}");
    }

    internal void NotifyRawInputDeviceChange(uint change, nint deviceHandle)
    {
        winmmDiagnosticLogged = false;
        rawInputGamepad.NotifyDeviceChange(change, deviceHandle);
    }

    internal void OnWindowDeactivated()
    {
        ResetInputState();
        rawInputGamepad.ResetTransientState();
        firstPollAfterActivation = false;
        WriteDiagnostic($"Window Deactivated: backend={backend}, connected={connected}; transient state reset.");
    }

    internal void OnWindowActivated()
    {
        ResetInputState();
        rawInputGamepad.ResetTransientState();
        nextWinmmRescan = 0;
        firstPollAfterActivation = true;

        string winmmReacquire = "not attempted";
        if (backend == GamepadBackend.Winmm && connected)
        {
            bool success = TryGetWinmmState(winmmId, out WinmmJoyInfoEx info, out uint result);
            winmmReacquire = $"{DescribeWinmmResult(result)} (0x{result:X8})";
            if (success)
            {
                LogWinmmState(info);
                nextWinmmRescan = Environment.TickCount64 + WinmmRescanIntervalMs;
            }
        }

        WriteDiagnostic($"Window Activated: backend={backend}, connected={connected}; transient state reset; "
            + $"WinMM reacquire={winmmReacquire}; nextWinmmRescan={nextWinmmRescan}.");
    }

    internal void Dispose()
    {
        WriteRawInputCounterSummary("dispose");
        rawInputGamepad.Dispose();
    }

    internal GamepadAction Poll()
    {
        long now = Environment.TickCount64;

        if (TryGetXInputState(out XInputState xinputState, out int xinputUserIndex))
        {
            if (backend != GamepadBackend.XInput || xinputIndex != xinputUserIndex)
                ConnectXInput(xinputUserIndex);
            LogXInputState(xinputState.Gamepad);
            LogicalButtons xinputButtons = ToLogicalButtons(xinputState.Gamepad,
                out GamepadPhysicalButtonMask xinputPhysicalButtons);
            return ProcessSample(xinputButtons, xinputPhysicalButtons,
                GetXInputDPadDirection(xinputState.Gamepad),
                GetStickDirection(xinputState.Gamepad.ThumbLX, xinputState.Gamepad.ThumbLY), now);
        }

        if (backend == GamepadBackend.XInput)
            Disconnect();

        if (backend == GamepadBackend.Winmm)
        {
            if (TryGetWinmmState(out WinmmJoyInfoEx winmmState))
            {
                    LogWinmmState(winmmState);
                    int x = NormalizeAxis(winmmState.dwXpos, winmmCaps.wXmin, winmmCaps.wXmax, false);
                    int y = NormalizeAxis(winmmState.dwYpos, winmmCaps.wYmin, winmmCaps.wYmax, true);
                    LogicalButtons winmmButtons = GetWinmmButtons(winmmState.dwButtons,
                        out GamepadPhysicalButtonMask winmmPhysicalButtons);
                return ProcessSample(winmmButtons, winmmPhysicalButtons, GetPovDirection(winmmState.dwPOV),
                    GetStickDirection(x, y), now);
            }

            Disconnect();
            nextWinmmRescan = now;
        }

        if (winmmAvailable && now >= nextWinmmRescan)
        {
            nextWinmmRescan = now + WinmmRescanIntervalMs;
            if (TryFindWinmm(out uint joyId, out WinmmJoyCaps caps))
            {
                ConnectWinmm(joyId, caps);
                if (TryGetWinmmState(out WinmmJoyInfoEx winmmState))
                {
                    LogWinmmState(winmmState);
                    int x = NormalizeAxis(winmmState.dwXpos, winmmCaps.wXmin, winmmCaps.wXmax, false);
                    int y = NormalizeAxis(winmmState.dwYpos, winmmCaps.wYmin, winmmCaps.wYmax, true);
                    LogicalButtons winmmButtons = GetWinmmButtons(winmmState.dwButtons,
                        out GamepadPhysicalButtonMask winmmPhysicalButtons);
                    return ProcessSample(winmmButtons, winmmPhysicalButtons, GetPovDirection(winmmState.dwPOV),
                        GetStickDirection(x, y), now);
                }
                Disconnect();
            }
        }

        if (rawInputGamepad.TryGetLatest(out RawInputGamepadSample rawSample))
        {
            if (backend != GamepadBackend.RawInput)
                ConnectRawInput(rawSample);
            LogicalButtons rawInputButtons = GetRawInputButtons(rawSample.ButtonMask,
                out GamepadPhysicalButtonMask rawInputPhysicalButtons);
            return ProcessSample(rawInputButtons, rawInputPhysicalButtons, rawSample.DPadDirection,
                rawSample.LeftStickDirection, now);
        }

        if (firstPollAfterActivation)
        {
            WriteDiagnostic($"First Poll After Activate: backend={backend}, connected={connected}, state received=false.");
            firstPollAfterActivation = false;
        }
        return default;
    }

    private void LoadXInput()
    {
        string[] names = ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"];
        for (int i = 0; i < names.Length; i++)
        {
            if (!NativeLibrary.TryLoad(names[i], out nint library))
                continue;

            try
            {
                nint export = NativeLibrary.GetExport(library, "XInputGetState");
                getState = Marshal.GetDelegateForFunctionPointer<XInputGetStateDelegate>(export);
                return;
            }
            catch
            {
                NativeLibrary.Free(library);
            }
        }
    }

    private static bool ProbeWinmm()
    {
        try
        {
            return JoyGetNumDevs() > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetXInputState(out XInputState state, out int userIndex)
    {
        state = default;
        userIndex = -1;
        if (getState == null || xinputDisabled)
            return false;

        bool diagnosticChanged = !xinputDiagnosticInitialized;
        for (uint i = 0; i < MaxXInputControllers; i++)
        {
            uint result;
            try
            {
                result = getState(i, out XInputState candidate);
                if (result == 0)
                {
                    if (previousXInputResults[i] != result)
                        diagnosticChanged = true;
                    previousXInputResults[i] = result;
                    state = candidate;
                    userIndex = (int)i;
                    continue;
                }
            }
            catch
            {
                result = uint.MaxValue;
                previousXInputResults[i] = result;
                diagnosticChanged = true;
                xinputDisabled = true;
                WriteDiagnostic("XInput polling failed; XInput polling disabled.");
                return false;
            }

            if (previousXInputResults[i] != result)
                diagnosticChanged = true;
            previousXInputResults[i] = result;
        }

        xinputDiagnosticInitialized = true;
        if (diagnosticChanged && diagnosticsEnabled)
        {
            for (int i = 0; i < previousXInputResults.Length; i++)
            {
                uint result = previousXInputResults[i];
                WriteDiagnostic($"XInput #{i} = {DescribeXInputResult(result)} (0x{result:X8})");
            }
        }

        // The loop above must inspect all four user indexes for diagnostics, but
        // keep the first connected state for the normal input path.
        for (int i = 0; i < previousXInputResults.Length; i++)
        {
            if (previousXInputResults[i] == 0)
            {
                // Query the selected controller once more to obtain its state.
                try
                {
                    if (getState((uint)i, out state) == 0)
                    {
                        userIndex = i;
                        return true;
                    }
                }
                catch
                {
                    xinputDisabled = true;
                    return false;
                }
            }
        }
        return false;
    }

    private bool TryFindWinmm(out uint joyId, out WinmmJoyCaps caps)
    {
        joyId = 0;
        caps = default;
        if (!winmmAvailable)
            return false;

        uint deviceCount;
        try
        {
            deviceCount = JoyGetNumDevs();
        }
        catch
        {
            winmmAvailable = false;
            return false;
        }

        uint maxDevice = deviceCount;
        bool logEnumeration = diagnosticsEnabled && (!winmmDiagnosticLogged || winmmDiagnosticDeviceCount != deviceCount);
        if (logEnumeration)
        {
            WriteDiagnostic($"WinMM device count: {deviceCount} (checking indexes 0..{(maxDevice == 0 ? 0 : maxDevice - 1)})");
            winmmDiagnosticDeviceCount = deviceCount;
        }
        uint capsSize = (uint)Marshal.SizeOf<WinmmJoyCaps>();
        bool found = false;
        winmmLiveDeviceCount = 0;
        for (uint i = 0; i < maxDevice; i++)
        {
            uint capsResult;
            WinmmJoyCaps candidate = default;
            try
            {
                capsResult = JoyGetDevCaps(i, out candidate, capsSize);
            }
            catch
            {
                capsResult = uint.MaxValue;
            }

            if (logEnumeration)
                WriteDiagnostic($"WinMM #{i}: joyGetDevCaps = {DescribeWinmmResult(capsResult)} (0x{capsResult:X8})");
            if (logEnumeration && capsResult == 0)
            {
                string name = string.IsNullOrWhiteSpace(candidate.szPname) ? "(empty)" : candidate.szPname.Trim();
                WriteDiagnostic($"WinMM #{i}: name = {name}, axes = {candidate.wNumAxes}, buttons = {candidate.wNumButtons}, caps = 0x{candidate.wCaps:X8}");
            }

            // Probe every index even when joyGetDevCaps rejected it. Some
            // systems expose a ghost slot in the capabilities API while the
            // next slot is the live controller, and the diagnostic output
            // must show both API results for every index.
            bool positionSuccess = TryGetWinmmState(i, out WinmmJoyInfoEx info, out uint positionResult);
            if (logEnumeration)
            {
                WriteDiagnostic($"WinMM #{i}: joyGetPosEx = {DescribeWinmmResult(positionResult)} (0x{positionResult:X8})");
                if (positionSuccess)
                {
                    WriteDiagnostic($"WinMM #{i}: X={info.dwXpos}, Y={info.dwYpos}, Z={info.dwZpos}, R={info.dwRpos}, U={info.dwUpos}, V={info.dwVpos}, POV={info.dwPOV}, Buttons=0x{info.dwButtons:X8}");
                }
            }

            // A device name is not a reliable indication that the slot is a
            // placeholder. Windows may report a live HID gamepad through the
            // generic Microsoft joystick driver name. Require both successful
            // capability and position probes, then use the actual axes/buttons
            // counts to decide whether it is a usable gamepad.
            if (capsResult == 0 && !found && positionSuccess
                    && (candidate.wNumButtons > 0 || candidate.wNumAxes >= 2))
            {
                found = true;
                joyId = i;
                caps = candidate;
            }
            if (capsResult == 0 && positionSuccess
                && (candidate.wNumButtons > 0 || candidate.wNumAxes >= 2))
                winmmLiveDeviceCount++;
        }
        if (logEnumeration)
            winmmDiagnosticLogged = true;
        return found;
    }

    private bool TryGetWinmmState(out WinmmJoyInfoEx info)
    {
        return TryGetWinmmState(winmmId, out info, out _);
    }

    private bool TryGetWinmmState(uint joystickId, out WinmmJoyInfoEx info, out uint result)
    {
        info = new WinmmJoyInfoEx
        {
            dwSize = (uint)Marshal.SizeOf<WinmmJoyInfoEx>(),
            dwFlags = JoyReturnAll,
        };
        try
        {
            result = JoyGetPosEx(joystickId, ref info);
            return result == 0;
        }
        catch
        {
            result = uint.MaxValue;
            return false;
        }
    }

    private static string DescribeWinmmResult(uint result)
    {
        return result switch
        {
            0 => "JOYERR_NOERROR",
            165 => "JOYERR_PARMS",
            166 => "JOYERR_NOCANDO",
            167 => "JOYERR_UNPLUGGED",
            2 => "MMSYSERR_BADDEVICEID",
            6 => "MMSYSERR_NODRIVER",
            7 => "MMSYSERR_NOMEM",
            11 => "MMSYSERR_INVALPARAM",
            uint.MaxValue => "EXCEPTION_OR_API_UNAVAILABLE",
            _ => "UNKNOWN",
        };
    }

    private static string DescribeXInputResult(uint result)
    {
        return result switch
        {
            0 => "ERROR_SUCCESS",
            1167 => "ERROR_DEVICE_NOT_CONNECTED",
            uint.MaxValue => "EXCEPTION",
            _ => "UNKNOWN",
        };
    }

    private void ConnectXInput(int userIndex)
    {
        rawInputGamepad.ResetTransientState();
        backend = GamepadBackend.XInput;
        xinputIndex = userIndex;
        connected = true;
        ResetInputState();
        SetStatus($"Gamepad: XInput #{userIndex}");
        WriteDiagnostic("XInput mapping: " + DescribeLogicalMapping());
        WriteDiagnostic($"Gamepad layout: Backend=XInput; XInputIndex={userIndex}; FaceButtonLayout=Xbox; LayoutReason=XInput backend.");
    }

    private void ConnectWinmm(uint joyId, WinmmJoyCaps caps)
    {
        rawInputGamepad.ResetTransientState();
        backend = GamepadBackend.Winmm;
        xinputIndex = -1;
        winmmId = joyId;
        winmmCaps = caps;
        GamepadFaceButtonLayout layout = ResolveWinmmFaceButtonLayout(caps, out string layoutReason);
        winmmFaceButtonLayout = layout;
        winmmButtonMapping = WinmmButtonMapping.Create(layout, bindings);
        connected = true;
        ResetInputState();
        string name = string.IsNullOrWhiteSpace(caps.szPname) ? "Generic Joystick" : caps.szPname.Trim();
        RawInputCandidateSummary rawSummary = rawInputGamepad.GetCandidateSummary();
        SetStatus($"Gamepad: WinMM / {name}");
        WriteDiagnostic($"WinMM #{joyId} selected as active gamepad.");
        WriteDiagnostic($"WinMM joystick #{joyId}: {name}; axes={caps.wNumAxes}, buttons={caps.wNumButtons}, caps=0x{caps.wCaps:X8}; MID=0x{caps.wMid:X4}, PID=0x{caps.wPid:X4}, RegKey={FormatWinmmIdentity(caps.szRegKey)}, OEM={FormatWinmmIdentity(caps.szOEMVxD)}");
        WriteDiagnostic($"Gamepad layout: Backend=WinMM; JoyId={joyId}; DeviceName={name}; RawCandidateCount={rawSummary.CandidateCount}; RawPlayStationCandidateCount={rawSummary.PlayStationCandidateCount}; FaceButtonLayout={DescribeFaceButtonLayout(layout)}; LayoutReason={layoutReason}");
        WriteDiagnostic("WinMM logical mapping: " + DescribeLogicalMapping());
        WriteDiagnostic("WinMM raw mapping: " + winmmButtonMapping.Describe());
    }

    private void ConnectRawInput(RawInputGamepadSample sample)
    {
        backend = GamepadBackend.RawInput;
        xinputIndex = -1;
        GamepadFaceButtonLayout layout = ResolveRawInputFaceButtonLayout(sample, out string layoutReason);
        rawInputFaceButtonLayout = layout;
        rawInputButtonMapping = WinmmButtonMapping.Create(layout, bindings, false);
        connected = true;
        ResetInputState();
        SetStatus($"Gamepad: Raw Input / {sample.DeviceName}");
        WriteDiagnostic($"Raw Input device: path={sample.DevicePath}, VID=0x{sample.VendorId:X4}, PID=0x{sample.ProductId:X4}, usagePage=0x{sample.UsagePage:X4}, usage=0x{sample.Usage:X4}");
        WriteDiagnostic($"Gamepad layout: Backend=RawInput; DeviceName={sample.DeviceName}; VID=0x{sample.VendorId:X4}; PID=0x{sample.ProductId:X4}; FaceButtonLayout={DescribeFaceButtonLayout(layout)}; LayoutReason={layoutReason}");
        WriteDiagnostic("Raw Input logical mapping: " + DescribeLogicalMapping());
        WriteDiagnostic("Raw Input raw mapping: " + rawInputButtonMapping.Describe());
    }

    private string DescribeLogicalMapping()
    {
        return $"Confirm={GamepadBindings.GetDisplayName(bindings.Confirm)}, "
            + $"Cancel={GamepadBindings.GetDisplayName(bindings.Cancel)}, "
            + $"Escape={GamepadBindings.GetDisplayName(bindings.Escape)}, "
            + $"OpenSettings={GamepadBindings.GetDisplayName(bindings.OpenSettings)}, "
            + $"PreviousPage={GamepadBindings.GetDisplayName(bindings.PreviousPage)}, "
            + $"NextPage={GamepadBindings.GetDisplayName(bindings.NextPage)}, "
            + $"Macro1={GamepadBindings.GetDisplayName(bindings.Macro1)}, "
            + $"Macro2={GamepadBindings.GetDisplayName(bindings.Macro2)}, "
            + $"Macro3={GamepadBindings.GetDisplayName(bindings.Macro3)}";
    }

    private GamepadFaceButtonLayout ResolveWinmmFaceButtonLayout(WinmmJoyCaps caps, out string reason)
    {
        return ResolveWinmmFaceButtonLayout(caps, winmmLiveDeviceCount,
            rawInputGamepad.GetCandidateSummary(), Program.GamepadFaceButtonLayoutOverride, out reason);
    }

    /// <summary>
    /// Resolves a WinMM mapping only from the selected device's own identity and
    /// a uniquely attributable Raw Input counterpart.  A globally detected PS
    /// device must never alter an unrelated WinMM/Xbox controller.
    /// </summary>
    private static GamepadFaceButtonLayout ResolveWinmmFaceButtonLayout(WinmmJoyCaps caps,
        uint liveWinmmDeviceCount, RawInputCandidateSummary rawSummary,
        GamepadFaceButtonLayout layoutOverride, out string reason)
    {
        if (layoutOverride != GamepadFaceButtonLayout.Auto)
        {
            reason = "GamepadLayout override";
            return layoutOverride;
        }

        if (HasExplicitPlayStationWinmmIdentity(caps))
        {
            reason = "WinMM device name/registry identity identifies a PlayStation controller";
            return GamepadFaceButtonLayout.PlayStationWinMM;
        }

        if (rawSummary.SinglePlayStationCandidate is RawInputDeviceIdentity rawIdentity)
        {
            if (MatchesRawInputIdentity(caps, rawIdentity))
            {
                reason = "WinMM identity matched the unique Raw Input PlayStation device";
                return GamepadFaceButtonLayout.PlayStationWinMM;
            }

            // WinMM's generic Microsoft joystick driver does not expose a device path.
            // The known DS4-compatible driver profile is accepted only when both APIs
            // have exactly one candidate, so another Raw Input device cannot change a
            // selected WinMM controller's mapping merely by being present.
            if (liveWinmmDeviceCount == 1 && rawSummary.CandidateCount == 1
                && HasKnownMicrosoftDs4WinmmProfile(caps))
            {
                reason = "unique WinMM/Raw Input pair with the known Microsoft DS4-compatible 6-axis/14-button profile";
                return GamepadFaceButtonLayout.PlayStationWinMM;
            }
        }

        reason = rawSummary.PlayStationCandidateCount > 0
            ? "no safe WinMM-to-Raw Input identity match; retained Xbox layout"
            : "WinMM device has no PlayStation identity";
        return GamepadFaceButtonLayout.Xbox;
    }

    private static GamepadFaceButtonLayout ResolveRawInputFaceButtonLayout(RawInputGamepadSample sample,
        out string reason)
    {
        if (Program.GamepadFaceButtonLayoutOverride != GamepadFaceButtonLayout.Auto)
        {
            reason = "GamepadLayout override";
            return Program.GamepadFaceButtonLayoutOverride;
        }
        if (sample.IsPlayStationCompatible)
        {
            reason = "Raw Input device VID/PID or product identity identifies a PlayStation controller";
            return GamepadFaceButtonLayout.PlayStationWinMM;
        }
        reason = "Raw Input device has no PlayStation identity";
        return GamepadFaceButtonLayout.Xbox;
    }

    private static bool HasExplicitPlayStationWinmmIdentity(WinmmJoyCaps caps)
    {
        return ContainsPlayStationName(caps.szPname)
            || ContainsPlayStationName(caps.szRegKey)
            || ContainsPlayStationName(caps.szOEMVxD);
    }

    private static bool ContainsPlayStationName(string? value)
    {
        return value?.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0
            || value?.IndexOf("PlayStation", StringComparison.OrdinalIgnoreCase) >= 0
            || value?.IndexOf("PS4", StringComparison.OrdinalIgnoreCase) >= 0
            || value?.IndexOf("Wireless Controller", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesRawInputIdentity(WinmmJoyCaps caps, RawInputDeviceIdentity raw)
    {
        if (caps.wMid == raw.VendorId && caps.wPid == raw.ProductId
            && caps.wMid != 0 && caps.wPid != 0)
            return true;

        string combined = $"{caps.szPname} {caps.szRegKey} {caps.szOEMVxD}";
        string vid = $"VID_{raw.VendorId:X4}";
        string pid = $"PID_{raw.ProductId:X4}";
        if (combined.IndexOf(vid, StringComparison.OrdinalIgnoreCase) >= 0
            && combined.IndexOf(pid, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return HasSpecificNameMatch(caps.szPname, raw.DeviceName)
            || HasSpecificNameMatch(caps.szRegKey, raw.DeviceName);
    }

    private static bool HasSpecificNameMatch(string? winmmName, string rawName)
    {
        if (string.IsNullOrWhiteSpace(winmmName) || string.IsNullOrWhiteSpace(rawName)
            || IsGenericMicrosoftJoystickDriver(winmmName))
            return false;
        string left = winmmName.Trim();
        string right = rawName.Trim();
        return left.Length >= 4 && right.Length >= 4
            && (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
                || left.IndexOf(right, StringComparison.OrdinalIgnoreCase) >= 0
                || right.IndexOf(left, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool HasKnownMicrosoftDs4WinmmProfile(WinmmJoyCaps caps)
    {
        return IsGenericMicrosoftJoystickDriver(caps.szPname)
            && caps.wNumAxes >= 6 && caps.wNumButtons >= 14;
    }

    private static bool IsGenericMicrosoftJoystickDriver(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string name = value.Trim();
        return string.Equals(name, "Microsoft PC ジョイスティック ドライバー", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Microsoft PC Joystick Driver", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatWinmmIdentity(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
    }

    private static string DescribeFaceButtonLayout(GamepadFaceButtonLayout layout)
    {
        return layout == GamepadFaceButtonLayout.PlayStationWinMM ? "PlayStation" : "Xbox";
    }

    /// <summary>
    /// Diagnostic-only regression checks for the two device-layer guarantees:
    /// Raw Input keeps D-pad and left-stick sources distinct, and a PS device
    /// seen by Raw Input cannot globally rewrite a generic WinMM controller.
    /// </summary>
    private void RunGamepadInputSelfTests()
    {
        RawInputDeviceIdentity ds4 = new("Wireless Controller", "\\\\?\\HID#VID_054C&PID_09CC",
            0x054C, 0x09CC, 0x0001, 0x0005, true);
        RawInputCandidateSummary onlyDs4 = new(1, 1, ds4);
        WinmmJoyCaps genericXboxCaps = new()
        {
            szPname = "Generic USB Joystick",
            wNumAxes = 6,
            wNumButtons = 14,
        };
        WinmmJoyCaps genericMicrosoftDs4Caps = new()
        {
            szPname = "Microsoft PC ジョイスティック ドライバー",
            wNumAxes = 6,
            wNumButtons = 14,
        };

        GamepadFaceButtonLayout genericXboxLayout = ResolveWinmmFaceButtonLayout(genericXboxCaps,
            1, onlyDs4, GamepadFaceButtonLayout.Auto, out _);
        GamepadFaceButtonLayout genericMicrosoftDs4Layout = ResolveWinmmFaceButtonLayout(
            genericMicrosoftDs4Caps, 1, onlyDs4, GamepadFaceButtonLayout.Auto, out _);
        GamepadBindings defaultBindings = GamepadBindings.Default();
        GamepadBindings swappedBindings = defaultBindings.Clone();
        swappedBindings.Assign(GamepadActionKind.Escape, GamepadPhysicalButton.FaceWest);
        GamepadBindings noneBindings = defaultBindings.Clone();
        foreach (GamepadActionKind action in GamepadBindings.ConfigurableActions)
            noneBindings.Assign(action, GamepadPhysicalButton.None);
        GamepadBindings macroBindings = defaultBindings.Clone();
        macroBindings.Assign(GamepadActionKind.Macro1, GamepadPhysicalButton.FaceWest);
        WinmmButtonMapping xboxMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.Xbox, defaultBindings);
        WinmmButtonMapping psMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.PlayStationWinMM, defaultBindings);
        WinmmButtonMapping rawInputPsMapping = WinmmButtonMapping.Create(
            GamepadFaceButtonLayout.PlayStationWinMM, defaultBindings, false);
        bool layouts = xboxMapping.Confirm == 0 && xboxMapping.Cancel == 1
            && psMapping.Confirm == 1 && psMapping.Cancel == 2
            && xboxMapping.Escape == 3 && psMapping.Escape == 3
            && xboxMapping.OpenSettings == 2 && psMapping.OpenSettings == 0
            && psMapping.LeftTrigger == 6 && psMapping.RightTrigger == 7
            && xboxMapping.LeftTrigger == -1 && xboxMapping.RightTrigger == -1
            && rawInputPsMapping.LeftTrigger == -1 && rawInputPsMapping.RightTrigger == -1
            && genericXboxLayout == GamepadFaceButtonLayout.Xbox
            && genericMicrosoftDs4Layout == GamepadFaceButtonLayout.PlayStationWinMM;
        bool defaultsUnchanged = defaultBindings.Confirm == GamepadPhysicalButton.FaceSouth
            && defaultBindings.Cancel == GamepadPhysicalButton.FaceEast
            && defaultBindings.Escape == GamepadPhysicalButton.FaceNorth
            && defaultBindings.PreviousPage == GamepadPhysicalButton.LeftShoulder
            && defaultBindings.NextPage == GamepadPhysicalButton.RightShoulder
            && defaultBindings.OpenSettings == GamepadPhysicalButton.FaceWest
            && defaultBindings.Macro1 == GamepadPhysicalButton.None
            && defaultBindings.Macro2 == GamepadPhysicalButton.None
            && defaultBindings.Macro3 == GamepadPhysicalButton.None;
        bool candidateOrder = Array.IndexOf(GamepadBindings.ConfigurableButtons,
                GamepadPhysicalButton.LeftTrigger) == 7
            && Array.IndexOf(GamepadBindings.ConfigurableButtons,
                GamepadPhysicalButton.RightTrigger) == 8
            && Array.IndexOf(GamepadBindings.ConfigurableButtons,
                GamepadPhysicalButton.Start) == 9;
        bool actionShape = GamepadBindings.ConfigurableActions.Length == 9
            && Array.IndexOf(GamepadBindings.ConfigurableActions, GamepadActionKind.OpenSettings) >= 0
            && (GamepadPhysicalButtonMask.Start & GamepadPhysicalButtonMask.Start) != 0;
        bool bindingSwap = swappedBindings.Escape == GamepadPhysicalButton.FaceWest
            && swappedBindings.OpenSettings == GamepadPhysicalButton.FaceNorth;
        bool allActionsCanBeUnassigned = true;
        foreach (GamepadActionKind action in GamepadBindings.ConfigurableActions)
            allActionsCanBeUnassigned &= noneBindings.Get(action) == GamepadPhysicalButton.None;
        GamepadBindings savedBindings = bindings;
        bindings = macroBindings;
        bool macroLogicalMapping = (GetLogicalButtons(GamepadPhysicalButton.FaceWest)
            & LogicalButtons.Macro1) != 0;
        GamepadBindings startConfirmBindings = defaultBindings.Clone();
        startConfirmBindings.Assign(GamepadActionKind.Confirm, GamepadPhysicalButton.Start);
        GamepadBindings startMacroBindings = defaultBindings.Clone();
        startMacroBindings.Assign(GamepadActionKind.Macro1, GamepadPhysicalButton.Start);
        GamepadBindings startSettingsBindings = defaultBindings.Clone();
        startSettingsBindings.Assign(GamepadActionKind.OpenSettings, GamepadPhysicalButton.Start);
        bindings = startConfirmBindings;
        bool startConfirmMapping = (GetLogicalButtons(GamepadPhysicalButton.Start)
            & LogicalButtons.Confirm) != 0;
        bindings = startMacroBindings;
        bool startMacroMapping = (GetLogicalButtons(GamepadPhysicalButton.Start)
            & LogicalButtons.Macro1) != 0;
        bindings = startSettingsBindings;
        bool startSettingsMapping = (GetLogicalButtons(GamepadPhysicalButton.Start)
            & LogicalButtons.OpenSettings) != 0;
        bindings = savedBindings;
        bool macroAssignment = macroBindings.Macro1 == GamepadPhysicalButton.FaceWest
            && macroBindings.OpenSettings == GamepadPhysicalButton.None;
        ResetInputState();
        ProcessSample(LogicalButtons.None, GamepadPhysicalButtonMask.None,
            GamepadDirection.None, GamepadDirection.None, 0);
        GamepadAction macroAction = ProcessSample(LogicalButtons.Macro1,
            GamepadPhysicalButtonMask.FaceWest, GamepadDirection.None, GamepadDirection.None, 1);
        bool macroDispatch = macroAction.Kind == GamepadActionKind.Macro1
            && macroAction.PressedPhysicalButtons == GamepadPhysicalButtonMask.FaceWest;

        GamepadBindings triggerBindings = defaultBindings.Clone();
        triggerBindings.Assign(GamepadActionKind.Escape, GamepadPhysicalButton.LeftTrigger);
        bool triggerEscape = triggerBindings.Escape == GamepadPhysicalButton.LeftTrigger;
        triggerBindings.Assign(GamepadActionKind.OpenSettings, GamepadPhysicalButton.RightTrigger);
        bool triggerSettings = triggerBindings.OpenSettings == GamepadPhysicalButton.RightTrigger;
        triggerBindings.Assign(GamepadActionKind.Escape, GamepadPhysicalButton.RightTrigger);
        bool triggerSwap = triggerBindings.Escape == GamepadPhysicalButton.RightTrigger
            && triggerBindings.OpenSettings == GamepadPhysicalButton.LeftTrigger;
        triggerBindings.Assign(GamepadActionKind.Escape, GamepadPhysicalButton.None);
        bool triggerNone = triggerBindings.Escape == GamepadPhysicalButton.None;

        bool leftTriggerState = false;
        bool rightTriggerState = false;
        bool triggerThresholds = !UpdateTriggerState(0, ref leftTriggerState)
            && !UpdateTriggerState(63, ref leftTriggerState)
            && UpdateTriggerState(64, ref leftTriggerState)
            && UpdateTriggerState(255, ref leftTriggerState)
            && UpdateTriggerState(49, ref leftTriggerState)
            && !UpdateTriggerState(48, ref leftTriggerState)
            && !UpdateTriggerState(0, ref rightTriggerState)
            && UpdateTriggerState(64, ref rightTriggerState)
            && !UpdateTriggerState(48, ref rightTriggerState);

        ResetInputState();
        ProcessSample(LogicalButtons.None, GamepadPhysicalButtonMask.None,
            GamepadDirection.None, GamepadDirection.None, 0);
        GamepadAction dpadAction = ProcessSample(LogicalButtons.None, GamepadPhysicalButtonMask.None,
            GamepadDirection.Up,
            GamepadDirection.None, 1);
        ResetInputState();
        ProcessSample(LogicalButtons.None, GamepadPhysicalButtonMask.None,
            GamepadDirection.None, GamepadDirection.None, 0);
        GamepadAction stickAction = ProcessSample(LogicalButtons.None, GamepadPhysicalButtonMask.None,
            GamepadDirection.None,
            GamepadDirection.Up, 1);
        bool directionSources = dpadAction.Kind == GamepadActionKind.Direction
            && dpadAction.DirectionSource == GamepadDirectionSource.DPad
            && stickAction.Kind == GamepadActionKind.Direction
            && stickAction.DirectionSource == GamepadDirectionSource.LeftStick;

        GamepadPhysicalButton liveConfirmBeforeCapture = bindings.Confirm;
        ResetInputState();
        ProcessSample(LogicalButtons.None, GamepadPhysicalButtonMask.None,
            GamepadDirection.None, GamepadDirection.None, 0);
        GamepadAction captureEntry = ProcessSample(LogicalButtons.Confirm,
            GamepadPhysicalButtonMask.FaceSouth, GamepadDirection.None, GamepadDirection.None, 1);
        GamepadAction captureHeld = ProcessSample(LogicalButtons.Confirm,
            GamepadPhysicalButtonMask.FaceSouth, GamepadDirection.None, GamepadDirection.None, 2);
        GamepadAction captureNeutral = ProcessSample(LogicalButtons.None,
            GamepadPhysicalButtonMask.None, GamepadDirection.None, GamepadDirection.None, 3);
        GamepadAction captureFaceEast = ProcessSample(LogicalButtons.Cancel,
            GamepadPhysicalButtonMask.FaceEast, GamepadDirection.None, GamepadDirection.None, 4);
        bool captureEdges = captureEntry.Kind == GamepadActionKind.Confirm
            && captureEntry.PressedPhysicalButtons == GamepadPhysicalButtonMask.FaceSouth
            && captureHeld.PressedPhysicalButtons == GamepadPhysicalButtonMask.None
            && captureNeutral.PhysicalButtons == GamepadPhysicalButtonMask.None
            && captureFaceEast.PressedPhysicalButtons == GamepadPhysicalButtonMask.FaceEast;
        GamepadPhysicalButtonMask[] captureMasks =
        [
            GamepadPhysicalButtonMask.FaceSouth,
            GamepadPhysicalButtonMask.FaceEast,
            GamepadPhysicalButtonMask.FaceWest,
            GamepadPhysicalButtonMask.FaceNorth,
            GamepadPhysicalButtonMask.LeftShoulder,
            GamepadPhysicalButtonMask.RightShoulder,
            GamepadPhysicalButtonMask.LeftTrigger,
            GamepadPhysicalButtonMask.RightTrigger,
            GamepadPhysicalButtonMask.Start,
        ];
        bool captureButtons = true;
        foreach (GamepadPhysicalButtonMask mask in captureMasks)
            captureButtons &= TryGetSinglePhysicalButton(mask, out _);
        bool captureRejectsMultiple = !TryGetSinglePhysicalButton(
            GamepadPhysicalButtonMask.FaceSouth | GamepadPhysicalButtonMask.FaceEast, out _);
        bool captureLeavesLiveBindings = bindings.Confirm == liveConfirmBeforeCapture;
        bool rawInputGating = ShouldProcessRawInputReports(GamepadBackend.None)
            && ShouldProcessRawInputReports(GamepadBackend.RawInput)
            && !ShouldProcessRawInputReports(GamepadBackend.Winmm)
            && !ShouldProcessRawInputReports(GamepadBackend.XInput);
        ResetInputState();

        WriteDiagnostic("Gamepad input self-test (layout / binding swap / capture / Raw D-pad-stick split / Raw Input gating): "
            + (layouts && defaultsUnchanged && candidateOrder && actionShape && bindingSwap && allActionsCanBeUnassigned
                && startConfirmMapping && startMacroMapping && startSettingsMapping
                && macroLogicalMapping && macroAssignment && macroDispatch && directionSources
                && triggerEscape && triggerSettings && triggerSwap && triggerNone && triggerThresholds
                && captureEdges && captureButtons && captureRejectsMultiple && captureLeavesLiveBindings
                && rawInputGating
                ? "PASS" : "WARNING"));
    }

    private void Disconnect()
    {
        if (backend is GamepadBackend.XInput or GamepadBackend.Winmm)
            rawInputGamepad.ResetTransientState();
        if (backend != GamepadBackend.None || connected)
            WriteDiagnostic(Status + " disconnected.");
        backend = GamepadBackend.None;
        xinputIndex = -1;
        connected = false;
        ResetInputState();
        SetStatus("Gamepad: Not Connected");
    }

    private void ResetInputState()
    {
        hasPreviousSample = false;
        previousButtons = LogicalButtons.None;
        previousPhysicalButtons = GamepadPhysicalButtonMask.None;
        previousDPadDirection = GamepadDirection.None;
        previousStickDirection = GamepadDirection.None;
        repeatShoulder = LogicalButtons.None;
        nextDPadDirectionRepeat = 0;
        nextStickDirectionRepeat = 0;
        nextShoulderRepeat = 0;
        previousRawButtons = 0;
        previousRawPov = WinmmPovCentered;
        previousRawX = int.MinValue;
        previousRawY = int.MinValue;
        previousRawZ = uint.MaxValue;
        previousRawR = uint.MaxValue;
        previousRawU = uint.MaxValue;
        previousRawV = uint.MaxValue;
        previousRawLeftTrigger = -1;
        previousRawRightTrigger = -1;
        xinputLeftTriggerActive = false;
        xinputRightTriggerActive = false;
    }

    private GamepadAction ProcessSample(LogicalButtons buttons, GamepadPhysicalButtonMask physicalButtons,
        GamepadDirection dpadDirection,
        GamepadDirection stickDirection, long now,
        GamepadDirectionSource dpadSource = GamepadDirectionSource.DPad)
    {
        if (!hasPreviousSample)
        {
            hasPreviousSample = true;
            previousButtons = buttons;
            previousPhysicalButtons = physicalButtons;
            previousDPadDirection = dpadDirection;
            previousStickDirection = stickDirection;
            if (firstPollAfterActivation)
            {
                WriteDiagnostic($"First Poll After Activate: backend={backend}, connected={connected}, state received=true.");
                firstPollAfterActivation = false;
            }
            return default;
        }

        if (suppressInputUntilRelease)
        {
            previousButtons = buttons;
            previousPhysicalButtons = physicalButtons;
            previousDPadDirection = dpadDirection;
            previousStickDirection = stickDirection;
            repeatShoulder = LogicalButtons.None;
            if (buttons == LogicalButtons.None
                && dpadDirection == GamepadDirection.None
                && stickDirection == GamepadDirection.None)
                suppressInputUntilRelease = false;
            return default;
        }

        LogicalButtons pressed = buttons & ~previousButtons;
        GamepadPhysicalButtonMask pressedPhysicalButtons = physicalButtons & ~previousPhysicalButtons;
        previousButtons = buttons;
        previousPhysicalButtons = physicalButtons;
        GamepadAction dpadAction = GetDirectionAction(dpadDirection, now, dpadSource,
            ref previousDPadDirection, ref nextDPadDirectionRepeat);
        GamepadAction stickAction = GetDirectionAction(stickDirection, now, GamepadDirectionSource.LeftStick,
            ref previousStickDirection, ref nextStickDirectionRepeat);
        GamepadAction shoulderAction = GetShoulderAction(buttons, pressed, now);

        GamepadAction action;
        if ((pressed & LogicalButtons.Cancel) != 0)
            action = new(GamepadActionKind.Cancel);
        else if ((pressed & LogicalButtons.Confirm) != 0)
            action = new(GamepadActionKind.Confirm);
        else if ((pressed & LogicalButtons.Escape) != 0)
            action = new(GamepadActionKind.Escape);
        else if ((pressed & LogicalButtons.OpenSettings) != 0)
            action = new(GamepadActionKind.OpenSettings);
        else if ((pressed & LogicalButtons.Macro1) != 0)
            action = new(GamepadActionKind.Macro1);
        else if ((pressed & LogicalButtons.Macro2) != 0)
            action = new(GamepadActionKind.Macro2);
        else if ((pressed & LogicalButtons.Macro3) != 0)
            action = new(GamepadActionKind.Macro3);
        else if (shoulderAction.Kind != GamepadActionKind.None)
            action = shoulderAction;
        else if (dpadAction.Kind != GamepadActionKind.None)
            action = dpadAction;
        else
            action = stickAction;
        return action.WithPhysicalButtons(physicalButtons, pressedPhysicalButtons);
    }

    private static GamepadAction GetDirectionAction(GamepadDirection direction, long now,
        GamepadDirectionSource source, ref GamepadDirection previousDirection, ref long nextDirectionRepeat)
    {
        if (direction == GamepadDirection.None)
        {
            previousDirection = GamepadDirection.None;
            return default;
        }

        if (direction != previousDirection)
        {
            previousDirection = direction;
            nextDirectionRepeat = now + DirectionRepeatDelayMs;
            return new(GamepadActionKind.Direction, direction, source);
        }

        if (now >= nextDirectionRepeat)
        {
            nextDirectionRepeat = now + DirectionRepeatIntervalMs;
            return new(GamepadActionKind.Direction, direction, source);
        }
        return default;
    }

    private GamepadAction GetShoulderAction(LogicalButtons buttons, LogicalButtons pressed, long now)
    {
        LogicalButtons shoulder = buttons & (LogicalButtons.LeftShoulder | LogicalButtons.RightShoulder);
        if (shoulder == LogicalButtons.None)
        {
            repeatShoulder = LogicalButtons.None;
            return default;
        }

        LogicalButtons newlyPressed = pressed & (LogicalButtons.LeftShoulder | LogicalButtons.RightShoulder);
        if (newlyPressed != LogicalButtons.None)
        {
            repeatShoulder = (newlyPressed & LogicalButtons.LeftShoulder) != 0
                ? LogicalButtons.LeftShoulder
                : LogicalButtons.RightShoulder;
            nextShoulderRepeat = now + ShoulderRepeatDelayMs;
            return new(repeatShoulder == LogicalButtons.LeftShoulder
                ? GamepadActionKind.ScrollUp
                : GamepadActionKind.ScrollDown);
        }

        if (repeatShoulder != LogicalButtons.None && (buttons & repeatShoulder) != 0 && now >= nextShoulderRepeat)
        {
            nextShoulderRepeat = now + ShoulderRepeatIntervalMs;
            return new(repeatShoulder == LogicalButtons.LeftShoulder
                ? GamepadActionKind.ScrollUp
                : GamepadActionKind.ScrollDown);
        }
        return default;
    }

    private LogicalButtons ToLogicalButtons(XInputGamepad gamepad,
        out GamepadPhysicalButtonMask physicalButtons)
    {
        LogicalButtons result = LogicalButtons.None;
        physicalButtons = GamepadPhysicalButtonMask.None;
        XInputButtons buttons = gamepad.Buttons;
        if ((buttons & XInputButtons.A) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.FaceSouth;
            result |= GetLogicalButtons(GamepadPhysicalButton.FaceSouth);
        }
        if ((buttons & XInputButtons.B) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.FaceEast;
            result |= GetLogicalButtons(GamepadPhysicalButton.FaceEast);
        }
        if ((buttons & XInputButtons.X) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.FaceWest;
            result |= GetLogicalButtons(GamepadPhysicalButton.FaceWest);
        }
        if ((buttons & XInputButtons.Y) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.FaceNorth;
            result |= GetLogicalButtons(GamepadPhysicalButton.FaceNorth);
        }
        if ((buttons & XInputButtons.LeftShoulder) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.LeftShoulder;
            result |= GetLogicalButtons(GamepadPhysicalButton.LeftShoulder);
        }
        if ((buttons & XInputButtons.RightShoulder) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.RightShoulder;
            result |= GetLogicalButtons(GamepadPhysicalButton.RightShoulder);
        }
        if ((buttons & XInputButtons.Start) != 0)
        {
            physicalButtons |= GamepadPhysicalButtonMask.Start;
            result |= GetLogicalButtons(GamepadPhysicalButton.Start);
        }
        if (UpdateTriggerState(gamepad.LeftTrigger, ref xinputLeftTriggerActive))
        {
            physicalButtons |= GamepadPhysicalButtonMask.LeftTrigger;
            result |= GetLogicalButtons(GamepadPhysicalButton.LeftTrigger);
        }
        if (UpdateTriggerState(gamepad.RightTrigger, ref xinputRightTriggerActive))
        {
            physicalButtons |= GamepadPhysicalButtonMask.RightTrigger;
            result |= GetLogicalButtons(GamepadPhysicalButton.RightTrigger);
        }
        return result;
    }

    private static bool UpdateTriggerState(byte value, ref bool active)
    {
        if (active)
        {
            if (value <= TriggerReleaseThreshold)
                active = false;
        }
        else if (value >= TriggerPressThreshold)
        {
            active = true;
        }
        return active;
    }

    private LogicalButtons GetLogicalButtons(GamepadPhysicalButton physicalButton)
    {
        LogicalButtons result = LogicalButtons.None;
        if (bindings.Confirm == physicalButton) result |= LogicalButtons.Confirm;
        if (bindings.Cancel == physicalButton) result |= LogicalButtons.Cancel;
        if (bindings.Escape == physicalButton) result |= LogicalButtons.Escape;
        if (bindings.OpenSettings == physicalButton) result |= LogicalButtons.OpenSettings;
        if (bindings.PreviousPage == physicalButton) result |= LogicalButtons.LeftShoulder;
        if (bindings.NextPage == physicalButton) result |= LogicalButtons.RightShoulder;
        if (bindings.Macro1 == physicalButton) result |= LogicalButtons.Macro1;
        if (bindings.Macro2 == physicalButton) result |= LogicalButtons.Macro2;
        if (bindings.Macro3 == physicalButton) result |= LogicalButtons.Macro3;
        return result;
    }

    private LogicalButtons GetWinmmButtons(uint buttons, out GamepadPhysicalButtonMask physicalButtons)
    {
        physicalButtons = winmmButtonMapping.GetPhysicalButtons(buttons);
        LogicalButtons result = LogicalButtons.None;
        if (IsButtonDown(buttons, winmmButtonMapping.Confirm)) result |= LogicalButtons.Confirm;
        if (IsButtonDown(buttons, winmmButtonMapping.Cancel)) result |= LogicalButtons.Cancel;
        if (IsButtonDown(buttons, winmmButtonMapping.Escape)) result |= LogicalButtons.Escape;
        if (IsButtonDown(buttons, winmmButtonMapping.OpenSettings)) result |= LogicalButtons.OpenSettings;
        if (IsButtonDown(buttons, winmmButtonMapping.LeftShoulder)) result |= LogicalButtons.LeftShoulder;
        if (IsButtonDown(buttons, winmmButtonMapping.RightShoulder)) result |= LogicalButtons.RightShoulder;
        if (IsButtonDown(buttons, winmmButtonMapping.LeftTrigger))
            result |= winmmButtonMapping.FilterOverridden(GetLogicalButtons(GamepadPhysicalButton.LeftTrigger));
        if (IsButtonDown(buttons, winmmButtonMapping.RightTrigger))
            result |= winmmButtonMapping.FilterOverridden(GetLogicalButtons(GamepadPhysicalButton.RightTrigger));
        if (IsButtonDown(buttons, winmmButtonMapping.Start))
            result |= GetLogicalButtons(GamepadPhysicalButton.Start);
        if (IsButtonDown(buttons, winmmButtonMapping.GetButtonIndex(bindings.Macro1)))
            result |= LogicalButtons.Macro1;
        if (IsButtonDown(buttons, winmmButtonMapping.GetButtonIndex(bindings.Macro2)))
            result |= LogicalButtons.Macro2;
        if (IsButtonDown(buttons, winmmButtonMapping.GetButtonIndex(bindings.Macro3)))
            result |= LogicalButtons.Macro3;
        return result;
    }

    private LogicalButtons GetRawInputButtons(uint buttons, out GamepadPhysicalButtonMask physicalButtons)
    {
        physicalButtons = rawInputButtonMapping.GetPhysicalButtons(buttons);
        LogicalButtons result = LogicalButtons.None;
        if (IsButtonDown(buttons, rawInputButtonMapping.Confirm)) result |= LogicalButtons.Confirm;
        if (IsButtonDown(buttons, rawInputButtonMapping.Cancel)) result |= LogicalButtons.Cancel;
        if (IsButtonDown(buttons, rawInputButtonMapping.Escape)) result |= LogicalButtons.Escape;
        if (IsButtonDown(buttons, rawInputButtonMapping.OpenSettings)) result |= LogicalButtons.OpenSettings;
        if (IsButtonDown(buttons, rawInputButtonMapping.LeftShoulder)) result |= LogicalButtons.LeftShoulder;
        if (IsButtonDown(buttons, rawInputButtonMapping.RightShoulder)) result |= LogicalButtons.RightShoulder;
        if (IsButtonDown(buttons, rawInputButtonMapping.LeftTrigger))
            result |= rawInputButtonMapping.FilterOverridden(GetLogicalButtons(GamepadPhysicalButton.LeftTrigger));
        if (IsButtonDown(buttons, rawInputButtonMapping.RightTrigger))
            result |= rawInputButtonMapping.FilterOverridden(GetLogicalButtons(GamepadPhysicalButton.RightTrigger));
        if (IsButtonDown(buttons, rawInputButtonMapping.Start))
            result |= GetLogicalButtons(GamepadPhysicalButton.Start);
        if (IsButtonDown(buttons, rawInputButtonMapping.GetButtonIndex(bindings.Macro1)))
            result |= LogicalButtons.Macro1;
        if (IsButtonDown(buttons, rawInputButtonMapping.GetButtonIndex(bindings.Macro2)))
            result |= LogicalButtons.Macro2;
        if (IsButtonDown(buttons, rawInputButtonMapping.GetButtonIndex(bindings.Macro3)))
            result |= LogicalButtons.Macro3;
        return result;
    }

    private static bool IsButtonDown(uint buttons, int buttonIndex)
    {
        return buttonIndex >= 0 && buttonIndex < 32 && (buttons & (1u << buttonIndex)) != 0;
    }

    private static GamepadDirection GetXInputDPadDirection(XInputGamepad gamepad)
    {
        if ((gamepad.Buttons & XInputButtons.DPadUp) != 0) return GamepadDirection.Up;
        if ((gamepad.Buttons & XInputButtons.DPadDown) != 0) return GamepadDirection.Down;
        if ((gamepad.Buttons & XInputButtons.DPadLeft) != 0) return GamepadDirection.Left;
        if ((gamepad.Buttons & XInputButtons.DPadRight) != 0) return GamepadDirection.Right;
        return GamepadDirection.None;
    }

    private static GamepadDirection GetPovDirection(uint pov)
    {
        if (pov == WinmmPovCentered)
            return GamepadDirection.None;
        if (pov <= 4500 || pov >= 31500)
            return GamepadDirection.Up;
        if (pov <= 13500)
            return GamepadDirection.Right;
        if (pov <= 22500)
            return GamepadDirection.Down;
        return GamepadDirection.Left;
    }

    internal static GamepadDirection GetStickDirection(int x, int y)
    {
        int absX = Math.Abs(x);
        int absY = Math.Abs(y);
        if (absX <= DeadZone && absY <= DeadZone)
            return GamepadDirection.None;
        if (absX >= absY)
            return x >= 0 ? GamepadDirection.Right : GamepadDirection.Left;
        return y >= 0 ? GamepadDirection.Up : GamepadDirection.Down;
    }

    internal static bool TryGetSinglePhysicalButton(GamepadPhysicalButtonMask mask,
        out GamepadPhysicalButton button)
    {
        button = mask switch
        {
            GamepadPhysicalButtonMask.FaceSouth => GamepadPhysicalButton.FaceSouth,
            GamepadPhysicalButtonMask.FaceEast => GamepadPhysicalButton.FaceEast,
            GamepadPhysicalButtonMask.FaceWest => GamepadPhysicalButton.FaceWest,
            GamepadPhysicalButtonMask.FaceNorth => GamepadPhysicalButton.FaceNorth,
            GamepadPhysicalButtonMask.LeftShoulder => GamepadPhysicalButton.LeftShoulder,
            GamepadPhysicalButtonMask.RightShoulder => GamepadPhysicalButton.RightShoulder,
            GamepadPhysicalButtonMask.LeftTrigger => GamepadPhysicalButton.LeftTrigger,
            GamepadPhysicalButtonMask.RightTrigger => GamepadPhysicalButton.RightTrigger,
            GamepadPhysicalButtonMask.Start => GamepadPhysicalButton.Start,
            _ => GamepadPhysicalButton.None,
        };
        return button != GamepadPhysicalButton.None;
    }

    private static int NormalizeAxis(uint value, uint min, uint max, bool invert)
    {
        if (max <= min)
            return 0;
        if (value < min) value = min;
        if (value > max) value = max;
        long normalized = ((long)value - min) * 65535 / ((long)max - min) - 32768;
        if (invert)
            normalized = -normalized;
        if (normalized < -32768) return -32768;
        if (normalized > 32767) return 32767;
        return (int)normalized;
    }

    private void LogXInputState(XInputGamepad gamepad)
    {
        if (!diagnosticsEnabled)
            return;
        uint buttons = (uint)gamepad.Buttons;
        int x = gamepad.ThumbLX;
        int y = gamepad.ThumbLY;
        bool triggerChanged = gamepad.LeftTrigger != previousRawLeftTrigger
            || gamepad.RightTrigger != previousRawRightTrigger;
        if (buttons == previousRawButtons && x == previousRawX && y == previousRawY && !triggerChanged)
            return;
        bool stateChanged = buttons != previousRawButtons || x != previousRawX || y != previousRawY;
        previousRawButtons = buttons;
        previousRawX = x;
        previousRawY = y;
        previousRawLeftTrigger = gamepad.LeftTrigger;
        previousRawRightTrigger = gamepad.RightTrigger;
        if (stateChanged)
            WriteDiagnostic($"XInput #{xinputIndex}: buttons=0x{buttons:X4}, LX={x}, LY={y}");
        if (triggerChanged)
            WriteDiagnostic($"XInput Trigger changed: LT={gamepad.LeftTrigger}, RT={gamepad.RightTrigger}");
    }

    private void LogWinmmState(WinmmJoyInfoEx info)
    {
        if (!diagnosticsEnabled)
            return;
        int x = NormalizeAxis(info.dwXpos, winmmCaps.wXmin, winmmCaps.wXmax, false);
        int y = NormalizeAxis(info.dwYpos, winmmCaps.wYmin, winmmCaps.wYmax, true);
        uint previousButtons = previousRawButtons;
        bool changed = info.dwButtons != previousRawButtons || info.dwPOV != previousRawPov
            || x != previousRawX || y != previousRawY || info.dwZpos != previousRawZ
            || info.dwRpos != previousRawR || info.dwUpos != previousRawU || info.dwVpos != previousRawV;
        if (!changed)
            return;
        uint pressed = info.dwButtons & ~previousButtons;
        uint released = previousButtons & ~info.dwButtons;
        previousRawButtons = info.dwButtons;
        previousRawPov = info.dwPOV;
        previousRawX = x;
        previousRawY = y;
        previousRawZ = info.dwZpos;
        previousRawR = info.dwRpos;
        previousRawU = info.dwUpos;
        previousRawV = info.dwVpos;
        WriteDiagnostic($"WinMM changed: PreviousButtons=0x{previousButtons:X8}, "
            + $"Buttons=0x{info.dwButtons:X8}, Pressed=0x{pressed:X8}, Released=0x{released:X8}, "
            + $"X={info.dwXpos}, Y={info.dwYpos}, Z={info.dwZpos}, R={info.dwRpos}, U={info.dwUpos}, V={info.dwVpos}, "
            + $"NormalizedX={x}, NormalizedY={y}, POV={info.dwPOV}");
    }

    private void SetStatus(string status)
    {
        if (string.Equals(Status, status, StringComparison.Ordinal))
            return;
        Status = status;
        WriteDiagnostic(status);
    }

    private void WriteDiagnostic(string message)
    {
        if (!diagnosticsEnabled)
            return;
        try
        {
            File.AppendAllText(diagnosticLogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never interfere with normal game execution.
        }
    }

    private readonly struct WinmmButtonMapping
    {
        private readonly GamepadFaceButtonLayout layout;
        private readonly bool mapPlayStationTriggers;
        internal readonly int Confirm;
        internal readonly int Cancel;
        internal readonly int Escape;
        internal readonly int OpenSettings;
        internal readonly int LeftShoulder;
        internal readonly int RightShoulder;
        internal readonly int LeftTrigger;
        internal readonly int RightTrigger;
        internal readonly int Start;
        private readonly bool confirmOverridden;
        private readonly bool cancelOverridden;
        private readonly bool escapeOverridden;
        private readonly bool openSettingsOverridden;
        private readonly bool leftShoulderOverridden;
        private readonly bool rightShoulderOverridden;

        private WinmmButtonMapping(GamepadFaceButtonLayout layout, bool mapPlayStationTriggers,
            int confirm, int cancel, int escape, int openSettings,
            int leftShoulder, int rightShoulder, int leftTrigger, int rightTrigger, int start,
            bool confirmOverridden, bool cancelOverridden, bool escapeOverridden,
            bool openSettingsOverridden, bool leftShoulderOverridden,
            bool rightShoulderOverridden)
        {
            this.layout = layout;
            this.mapPlayStationTriggers = mapPlayStationTriggers;
            Confirm = confirm;
            Cancel = cancel;
            Escape = escape;
            OpenSettings = openSettings;
            LeftShoulder = leftShoulder;
            RightShoulder = rightShoulder;
            LeftTrigger = leftTrigger;
            RightTrigger = rightTrigger;
            Start = start;
            this.confirmOverridden = confirmOverridden;
            this.cancelOverridden = cancelOverridden;
            this.escapeOverridden = escapeOverridden;
            this.openSettingsOverridden = openSettingsOverridden;
            this.leftShoulderOverridden = leftShoulderOverridden;
            this.rightShoulderOverridden = rightShoulderOverridden;
        }

        internal static WinmmButtonMapping Create(GamepadFaceButtonLayout layout, GamepadBindings bindings,
            bool mapPlayStationTriggers = true)
        {
            return new WinmmButtonMapping(
                layout, mapPlayStationTriggers,
                ReadOverride("EMUERA_GAMEPAD_CONFIRM_BUTTON", GetRawButtonIndex(layout, bindings.Confirm)),
                ReadOverride("EMUERA_GAMEPAD_CANCEL_BUTTON", GetRawButtonIndex(layout, bindings.Cancel)),
                ReadOverride("EMUERA_GAMEPAD_ESCAPE_BUTTON", GetRawButtonIndex(layout, bindings.Escape)),
                ReadOverride("EMUERA_GAMEPAD_SETTINGS_BUTTON", GetRawButtonIndex(layout, bindings.OpenSettings)),
                ReadOverride("EMUERA_GAMEPAD_LB_BUTTON", GetRawButtonIndex(layout, bindings.PreviousPage)),
                ReadOverride("EMUERA_GAMEPAD_RB_BUTTON", GetRawButtonIndex(layout, bindings.NextPage)),
                GetRawButtonIndex(layout, GamepadPhysicalButton.LeftTrigger, mapPlayStationTriggers),
                GetRawButtonIndex(layout, GamepadPhysicalButton.RightTrigger, mapPlayStationTriggers),
                GetRawButtonIndex(layout, GamepadPhysicalButton.Start, mapPlayStationTriggers),
                IsActiveOverride("EMUERA_GAMEPAD_CONFIRM_BUTTON"),
                IsActiveOverride("EMUERA_GAMEPAD_CANCEL_BUTTON"),
                IsActiveOverride("EMUERA_GAMEPAD_ESCAPE_BUTTON"),
                IsActiveOverride("EMUERA_GAMEPAD_SETTINGS_BUTTON"),
                IsActiveOverride("EMUERA_GAMEPAD_LB_BUTTON"),
                IsActiveOverride("EMUERA_GAMEPAD_RB_BUTTON"));
        }

        internal static bool HasActiveOverride()
        {
            return IsActiveOverride("EMUERA_GAMEPAD_CONFIRM_BUTTON")
                || IsActiveOverride("EMUERA_GAMEPAD_CANCEL_BUTTON")
                || IsActiveOverride("EMUERA_GAMEPAD_ESCAPE_BUTTON")
                || IsActiveOverride("EMUERA_GAMEPAD_SETTINGS_BUTTON")
                || IsActiveOverride("EMUERA_GAMEPAD_LB_BUTTON")
                || IsActiveOverride("EMUERA_GAMEPAD_RB_BUTTON");
        }

        private static bool IsActiveOverride(string variableName)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(raw, out int value) && value >= 0 && value < 32;
        }

        internal LogicalButtons FilterOverridden(LogicalButtons buttons)
        {
            if (confirmOverridden) buttons &= ~LogicalButtons.Confirm;
            if (cancelOverridden) buttons &= ~LogicalButtons.Cancel;
            if (escapeOverridden) buttons &= ~LogicalButtons.Escape;
            if (openSettingsOverridden) buttons &= ~LogicalButtons.OpenSettings;
            if (leftShoulderOverridden) buttons &= ~LogicalButtons.LeftShoulder;
            if (rightShoulderOverridden) buttons &= ~LogicalButtons.RightShoulder;
            return buttons;
        }

        internal GamepadPhysicalButtonMask GetPhysicalButtons(uint buttons)
        {
            GamepadPhysicalButtonMask result = GamepadPhysicalButtonMask.None;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.FaceSouth,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.FaceSouth;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.FaceEast,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.FaceEast;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.FaceWest,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.FaceWest;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.FaceNorth,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.FaceNorth;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.LeftShoulder,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.LeftShoulder;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.RightShoulder,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.RightShoulder;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.LeftTrigger,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.LeftTrigger;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.RightTrigger,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.RightTrigger;
            if (IsButtonDown(buttons, GetRawButtonIndex(layout, GamepadPhysicalButton.Start,
                    mapPlayStationTriggers)))
                result |= GamepadPhysicalButtonMask.Start;
            return result;
        }

        internal int GetButtonIndex(GamepadPhysicalButton button)
            => GetRawButtonIndex(layout, button, mapPlayStationTriggers);

        private static int GetRawButtonIndex(GamepadFaceButtonLayout layout, GamepadPhysicalButton button,
            bool mapPlayStationTriggers = true)
        {
            bool ps4Layout = layout == GamepadFaceButtonLayout.PlayStationWinMM;
            return button switch
            {
                GamepadPhysicalButton.FaceSouth => ps4Layout ? 1 : 0,
                GamepadPhysicalButton.FaceEast => ps4Layout ? 2 : 1,
                GamepadPhysicalButton.FaceWest => ps4Layout ? 0 : 2,
                GamepadPhysicalButton.FaceNorth => 3,
                GamepadPhysicalButton.LeftShoulder => 4,
                GamepadPhysicalButton.RightShoulder => 5,
                GamepadPhysicalButton.LeftTrigger => ps4Layout && mapPlayStationTriggers ? 6 : -1,
                GamepadPhysicalButton.RightTrigger => ps4Layout && mapPlayStationTriggers ? 7 : -1,
                GamepadPhysicalButton.Start => ps4Layout ? 9 : 7,
                _ => -1,
            };
        }

        internal string Describe()
        {
            return $"Confirm=button {Confirm}, Cancel=button {Cancel}, Escape=button {Escape}, Settings=button {OpenSettings}, LB=button {LeftShoulder}, RB=button {RightShoulder}, L2=button {LeftTrigger}, R2=button {RightTrigger}, Start=button {Start}";
        }

        private static int ReadOverride(string variableName, int defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(raw, out int value) && value >= 0 && value < 32 ? value : defaultValue;
        }
    }

    internal static bool HasActiveRawButtonOverride()
    {
        return WinmmButtonMapping.HasActiveOverride();
    }
}

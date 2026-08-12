#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

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

internal enum GamepadActionKind
{
    None = 0,
    Direction,
    Confirm,
    Cancel,
    Start,
    ScrollUp,
    ScrollDown,
}

internal readonly struct GamepadAction
{
    internal GamepadAction(GamepadActionKind kind, GamepadDirection direction = GamepadDirection.None,
        GamepadDirectionSource directionSource = GamepadDirectionSource.None)
    {
        Kind = kind;
        Direction = direction;
        DirectionSource = directionSource;
    }

    internal GamepadActionKind Kind { get; }
    internal GamepadDirection Direction { get; }
    internal GamepadDirectionSource DirectionSource { get; }
}

/// <summary>
/// XInputを優先し、XInputでコントローラーが見つからない場合はWinMM joystick APIへ
/// フォールバックするゲームパッド入力層。WinMMはjoy.cplで認識されるDirectInput/HID
/// 系ゲームパッドを、外部ライブラリなしで取得するために使用する。
/// </summary>
internal sealed class GamepadManager
{
    private const int DeadZone = 8000;
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
        LeftShoulder = 1 << 4,
        RightShoulder = 1 << 5,
        Start = 1 << 6,
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
    private bool xinputDiagnosticInitialized;
    private bool winmmDiagnosticLogged;
    private uint winmmDiagnosticDeviceCount;
    private uint winmmLiveDeviceCount;
    private WinmmButtonMapping rawInputButtonMapping;
    private bool firstPollAfterActivation;

    internal GamepadManager(bool diagnosticsEnabled)
    {
        this.diagnosticsEnabled = diagnosticsEnabled;
        diagnosticLogPath = Path.Combine(AppContext.BaseDirectory, "gamepad-debug.log");
        winmmButtonMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.Xbox);
        rawInputButtonMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.Xbox);
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
        rawInputGamepad.Process(rawInputHandle);
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
            return ProcessSample(ToLogicalButtons(xinputState.Gamepad.Buttons),
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
                return ProcessSample(GetWinmmButtons(winmmState.dwButtons), GetPovDirection(winmmState.dwPOV),
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
                    return ProcessSample(GetWinmmButtons(winmmState.dwButtons), GetPovDirection(winmmState.dwPOV),
                        GetStickDirection(x, y), now);
                }
                Disconnect();
            }
        }

        if (rawInputGamepad.TryGetLatest(out RawInputGamepadSample rawSample))
        {
            if (backend != GamepadBackend.RawInput)
                ConnectRawInput(rawSample);
            return ProcessSample(GetRawInputButtons(rawSample.ButtonMask), rawSample.DPadDirection,
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
        backend = GamepadBackend.XInput;
        xinputIndex = userIndex;
        connected = true;
        ResetInputState();
        SetStatus($"Gamepad: XInput #{userIndex}");
        WriteDiagnostic("XInput mapping: A=Confirm, B=Cancel, LB/RB=Scroll, Start=Enter.");
        WriteDiagnostic($"Gamepad layout: Backend=XInput; XInputIndex={userIndex}; FaceButtonLayout=Xbox; LayoutReason=XInput backend.");
    }

    private void ConnectWinmm(uint joyId, WinmmJoyCaps caps)
    {
        backend = GamepadBackend.Winmm;
        xinputIndex = -1;
        winmmId = joyId;
        winmmCaps = caps;
        GamepadFaceButtonLayout layout = ResolveWinmmFaceButtonLayout(caps, out string layoutReason);
        winmmButtonMapping = WinmmButtonMapping.Create(layout);
        connected = true;
        ResetInputState();
        string name = string.IsNullOrWhiteSpace(caps.szPname) ? "Generic Joystick" : caps.szPname.Trim();
        RawInputCandidateSummary rawSummary = rawInputGamepad.GetCandidateSummary();
        SetStatus($"Gamepad: WinMM / {name}");
        WriteDiagnostic($"WinMM #{joyId} selected as active gamepad.");
        WriteDiagnostic($"WinMM joystick #{joyId}: {name}; axes={caps.wNumAxes}, buttons={caps.wNumButtons}, caps=0x{caps.wCaps:X8}; MID=0x{caps.wMid:X4}, PID=0x{caps.wPid:X4}, RegKey={FormatWinmmIdentity(caps.szRegKey)}, OEM={FormatWinmmIdentity(caps.szOEMVxD)}");
        WriteDiagnostic($"Gamepad layout: Backend=WinMM; JoyId={joyId}; DeviceName={name}; RawCandidateCount={rawSummary.CandidateCount}; RawPlayStationCandidateCount={rawSummary.PlayStationCandidateCount}; FaceButtonLayout={DescribeFaceButtonLayout(layout)}; LayoutReason={layoutReason}");
        WriteDiagnostic("WinMM mapping: " + winmmButtonMapping.Describe());
    }

    private void ConnectRawInput(RawInputGamepadSample sample)
    {
        backend = GamepadBackend.RawInput;
        xinputIndex = -1;
        GamepadFaceButtonLayout layout = ResolveRawInputFaceButtonLayout(sample, out string layoutReason);
        rawInputButtonMapping = WinmmButtonMapping.Create(layout);
        connected = true;
        ResetInputState();
        SetStatus($"Gamepad: Raw Input / {sample.DeviceName}");
        WriteDiagnostic($"Raw Input device: path={sample.DevicePath}, VID=0x{sample.VendorId:X4}, PID=0x{sample.ProductId:X4}, usagePage=0x{sample.UsagePage:X4}, usage=0x{sample.Usage:X4}");
        WriteDiagnostic($"Gamepad layout: Backend=RawInput; DeviceName={sample.DeviceName}; VID=0x{sample.VendorId:X4}; PID=0x{sample.ProductId:X4}; FaceButtonLayout={DescribeFaceButtonLayout(layout)}; LayoutReason={layoutReason}");
        WriteDiagnostic("Raw Input mapping: " + rawInputButtonMapping.Describe());
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
        WinmmButtonMapping xboxMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.Xbox);
        WinmmButtonMapping psMapping = WinmmButtonMapping.Create(GamepadFaceButtonLayout.PlayStationWinMM);
        bool layouts = xboxMapping.Confirm == 0 && xboxMapping.Cancel == 1
            && psMapping.Confirm == 1 && psMapping.Cancel == 2
            && genericXboxLayout == GamepadFaceButtonLayout.Xbox
            && genericMicrosoftDs4Layout == GamepadFaceButtonLayout.PlayStationWinMM;

        ResetInputState();
        ProcessSample(LogicalButtons.None, GamepadDirection.None, GamepadDirection.None, 0);
        GamepadAction dpadAction = ProcessSample(LogicalButtons.None, GamepadDirection.Up,
            GamepadDirection.None, 1);
        ResetInputState();
        ProcessSample(LogicalButtons.None, GamepadDirection.None, GamepadDirection.None, 0);
        GamepadAction stickAction = ProcessSample(LogicalButtons.None, GamepadDirection.None,
            GamepadDirection.Up, 1);
        bool directionSources = dpadAction.Kind == GamepadActionKind.Direction
            && dpadAction.DirectionSource == GamepadDirectionSource.DPad
            && stickAction.Kind == GamepadActionKind.Direction
            && stickAction.DirectionSource == GamepadDirectionSource.LeftStick;
        ResetInputState();

        WriteDiagnostic("Gamepad input self-test (per-device layout / Raw D-pad-stick split): "
            + (layouts && directionSources ? "PASS" : "WARNING"));
    }

    private void Disconnect()
    {
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
    }

    private GamepadAction ProcessSample(LogicalButtons buttons, GamepadDirection dpadDirection,
        GamepadDirection stickDirection, long now,
        GamepadDirectionSource dpadSource = GamepadDirectionSource.DPad)
    {
        if (!hasPreviousSample)
        {
            hasPreviousSample = true;
            previousButtons = buttons;
            previousDPadDirection = dpadDirection;
            previousStickDirection = stickDirection;
            if (firstPollAfterActivation)
            {
                WriteDiagnostic($"First Poll After Activate: backend={backend}, connected={connected}, state received=true.");
                firstPollAfterActivation = false;
            }
            return default;
        }

        LogicalButtons pressed = buttons & ~previousButtons;
        previousButtons = buttons;
        GamepadAction dpadAction = GetDirectionAction(dpadDirection, now, dpadSource,
            ref previousDPadDirection, ref nextDPadDirectionRepeat);
        GamepadAction stickAction = GetDirectionAction(stickDirection, now, GamepadDirectionSource.LeftStick,
            ref previousStickDirection, ref nextStickDirectionRepeat);
        GamepadAction shoulderAction = GetShoulderAction(buttons, pressed, now);

        if ((pressed & LogicalButtons.Cancel) != 0)
            return new(GamepadActionKind.Cancel);
        if ((pressed & LogicalButtons.Confirm) != 0)
            return new(GamepadActionKind.Confirm);
        if ((pressed & LogicalButtons.Start) != 0)
            return new(GamepadActionKind.Start);
        if (shoulderAction.Kind != GamepadActionKind.None)
            return shoulderAction;
        if (dpadAction.Kind != GamepadActionKind.None)
            return dpadAction;
        return stickAction;
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

    private static LogicalButtons ToLogicalButtons(XInputButtons buttons)
    {
        LogicalButtons result = LogicalButtons.None;
        if ((buttons & XInputButtons.A) != 0) result |= LogicalButtons.Confirm;
        if ((buttons & XInputButtons.B) != 0) result |= LogicalButtons.Cancel;
        if ((buttons & XInputButtons.LeftShoulder) != 0) result |= LogicalButtons.LeftShoulder;
        if ((buttons & XInputButtons.RightShoulder) != 0) result |= LogicalButtons.RightShoulder;
        if ((buttons & XInputButtons.Start) != 0) result |= LogicalButtons.Start;
        return result;
    }

    private LogicalButtons GetWinmmButtons(uint buttons)
    {
        LogicalButtons result = LogicalButtons.None;
        if (IsButtonDown(buttons, winmmButtonMapping.Confirm)) result |= LogicalButtons.Confirm;
        if (IsButtonDown(buttons, winmmButtonMapping.Cancel)) result |= LogicalButtons.Cancel;
        if (IsButtonDown(buttons, winmmButtonMapping.LeftShoulder)) result |= LogicalButtons.LeftShoulder;
        if (IsButtonDown(buttons, winmmButtonMapping.RightShoulder)) result |= LogicalButtons.RightShoulder;
        if (IsButtonDown(buttons, winmmButtonMapping.Start)) result |= LogicalButtons.Start;
        return result;
    }

    private LogicalButtons GetRawInputButtons(uint buttons)
    {
        LogicalButtons result = LogicalButtons.None;
        if (IsButtonDown(buttons, rawInputButtonMapping.Confirm)) result |= LogicalButtons.Confirm;
        if (IsButtonDown(buttons, rawInputButtonMapping.Cancel)) result |= LogicalButtons.Cancel;
        if (IsButtonDown(buttons, rawInputButtonMapping.LeftShoulder)) result |= LogicalButtons.LeftShoulder;
        if (IsButtonDown(buttons, rawInputButtonMapping.RightShoulder)) result |= LogicalButtons.RightShoulder;
        if (IsButtonDown(buttons, rawInputButtonMapping.Start)) result |= LogicalButtons.Start;
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
        if (buttons == previousRawButtons && x == previousRawX && y == previousRawY)
            return;
        previousRawButtons = buttons;
        previousRawX = x;
        previousRawY = y;
        WriteDiagnostic($"XInput #{xinputIndex}: buttons=0x{buttons:X4}, LX={x}, LY={y}");
    }

    private void LogWinmmState(WinmmJoyInfoEx info)
    {
        if (!diagnosticsEnabled)
            return;
        int x = NormalizeAxis(info.dwXpos, winmmCaps.wXmin, winmmCaps.wXmax, false);
        int y = NormalizeAxis(info.dwYpos, winmmCaps.wYmin, winmmCaps.wYmax, true);
        if (info.dwButtons == previousRawButtons && info.dwPOV == previousRawPov && x == previousRawX && y == previousRawY)
            return;
        previousRawButtons = info.dwButtons;
        previousRawPov = info.dwPOV;
        previousRawX = x;
        previousRawY = y;
        WriteDiagnostic($"WinMM state: Buttons=0x{info.dwButtons:X8}, X={x}, Y={y}, POV={info.dwPOV}");
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
        internal readonly int Confirm;
        internal readonly int Cancel;
        internal readonly int LeftShoulder;
        internal readonly int RightShoulder;
        internal readonly int Start;

        private WinmmButtonMapping(int confirm, int cancel, int leftShoulder, int rightShoulder, int start)
        {
            Confirm = confirm;
            Cancel = cancel;
            LeftShoulder = leftShoulder;
            RightShoulder = rightShoulder;
            Start = start;
        }

        internal static WinmmButtonMapping Create(GamepadFaceButtonLayout layout)
        {
            bool ps4Layout = layout == GamepadFaceButtonLayout.PlayStationWinMM;
            return new WinmmButtonMapping(
                ReadOverride("EMUERA_GAMEPAD_CONFIRM_BUTTON", ps4Layout ? 1 : 0),
                ReadOverride("EMUERA_GAMEPAD_CANCEL_BUTTON", ps4Layout ? 2 : 1),
                ReadOverride("EMUERA_GAMEPAD_LB_BUTTON", 4),
                ReadOverride("EMUERA_GAMEPAD_RB_BUTTON", 5),
                ReadOverride("EMUERA_GAMEPAD_START_BUTTON", ps4Layout ? 9 : 7));
        }

        internal string Describe()
        {
            return $"Confirm=button {Confirm}, Cancel=button {Cancel}, LB=button {LeftShoulder}, RB=button {RightShoulder}, Start=button {Start}";
        }

        private static int ReadOverride(string variableName, int defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(raw, out int value) && value >= 0 && value < 32 ? value : defaultValue;
        }
    }
}

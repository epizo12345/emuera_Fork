#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace MinorShift.Emuera.GameView;

// [Emuera改修:GAMEPAD-V1]
// XInput / WinMMで状態取得できないWindows HIDゲームパッド用の最終フォールバック。
internal readonly struct RawInputGamepadSample
{
    internal RawInputGamepadSample(
        string deviceName,
        string devicePath,
        uint vendorId,
        uint productId,
        ushort usagePage,
        ushort usage,
        uint buttonMask,
        GamepadDirection dpadDirection,
        GamepadDirection leftStickDirection,
        int x,
        int y,
        uint pov,
        bool isPlayStationCompatible)
    {
        DeviceName = deviceName;
        DevicePath = devicePath;
        VendorId = vendorId;
        ProductId = productId;
        UsagePage = usagePage;
        Usage = usage;
        ButtonMask = buttonMask;
        DPadDirection = dpadDirection;
        LeftStickDirection = leftStickDirection;
        X = x;
        Y = y;
        Pov = pov;
        IsPlayStationCompatible = isPlayStationCompatible;
    }

    internal string DeviceName { get; }
    internal string DevicePath { get; }
    internal uint VendorId { get; }
    internal uint ProductId { get; }
    internal ushort UsagePage { get; }
    internal ushort Usage { get; }
    internal uint ButtonMask { get; }
    internal GamepadDirection DPadDirection { get; }
    internal GamepadDirection LeftStickDirection { get; }
    internal int X { get; }
    internal int Y { get; }
    internal uint Pov { get; }
    internal bool IsPlayStationCompatible { get; }
}

/// <summary>
/// Raw Inputが列挙した個々のHIDゲームパッドの識別情報。WinMMとの対応付けは
/// この情報を別デバイスへ流用せず、明示的に一意と判断できる場合だけ使用する。
/// </summary>
internal readonly struct RawInputDeviceIdentity
{
    internal RawInputDeviceIdentity(string deviceName, string devicePath, uint vendorId, uint productId,
        ushort usagePage, ushort usage, bool isPlayStationCompatible)
    {
        DeviceName = deviceName;
        DevicePath = devicePath;
        VendorId = vendorId;
        ProductId = productId;
        UsagePage = usagePage;
        Usage = usage;
        IsPlayStationCompatible = isPlayStationCompatible;
    }

    internal string DeviceName { get; }
    internal string DevicePath { get; }
    internal uint VendorId { get; }
    internal uint ProductId { get; }
    internal ushort UsagePage { get; }
    internal ushort Usage { get; }
    internal bool IsPlayStationCompatible { get; }
}

internal readonly struct RawInputCandidateSummary
{
    internal RawInputCandidateSummary(int candidateCount, int playStationCandidateCount,
        RawInputDeviceIdentity? singlePlayStationCandidate)
    {
        CandidateCount = candidateCount;
        PlayStationCandidateCount = playStationCandidateCount;
        SinglePlayStationCandidate = singlePlayStationCandidate;
    }

    internal int CandidateCount { get; }
    internal int PlayStationCandidateCount { get; }
    internal RawInputDeviceIdentity? SinglePlayStationCandidate { get; }
}

/// <summary>
/// Raw Input/HID fallback for gamepads which are visible in joy.cpl but are not
/// exposed by XInput or WinMM. Device enumeration is performed at registration
/// and on hot-plug notifications; WM_INPUT reports are parsed on the UI thread.
/// </summary>
internal sealed class RawInputGamepad : IDisposable
{
    private const uint RIM_TYPEHID = 2;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIDI_PREPARSEDDATA = 0x20000005;
    private const uint RIDI_DEVICENAME = 0x20000007;
    private const uint RIDI_DEVICEINFO = 0x2000000B;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_DEVNOTIFY = 0x00002000;
    private const uint GIDC_ARRIVAL = 1;
    private const uint GIDC_REMOVAL = 2;
    private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    private const ushort HID_USAGE_JOYSTICK = 0x04;
    private const ushort HID_USAGE_GAMEPAD = 0x05;
    private const ushort HID_USAGE_MULTI_AXIS = 0x08;
    private const ushort HID_USAGE_X = 0x30;
    private const ushort HID_USAGE_Y = 0x31;
    private const ushort HID_USAGE_HAT_SWITCH = 0x39;
    private const ushort HID_USAGE_PAGE_BUTTON = 0x09;
    private const uint HIDP_STATUS_SUCCESS = 0x00110000;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const int INVALID_HANDLE_VALUE = -1;

    private readonly bool diagnosticsEnabled;
    private readonly Action<string> log;
    private readonly Dictionary<nint, RawInputDeviceInfo> devices = new();
    private readonly HashSet<nint> unknownDeviceHandles = new();
    private RawInputGamepadSample latestSample;
    private bool hasLatestSample;
    private nint activeDevice;
    private bool registered;
    private bool disposed;
    private uint lastLoggedButtons;
    private int lastLoggedX = int.MinValue;
    private int lastLoggedY = int.MinValue;
    private uint lastLoggedPov = uint.MaxValue;

    internal RawInputGamepad(bool diagnosticsEnabled, Action<string> log)
    {
        this.diagnosticsEnabled = diagnosticsEnabled;
        this.log = log;
    }

    // Raw Input is part of user32.dll on every supported Windows target. The
    // actual registration result is logged by Register, while keeping the
    // polling timer alive permits controllers to be attached after startup.
    internal bool IsAvailable => OperatingSystem.IsWindows();
    internal int LastParsedReportCount { get; private set; }

    internal RawInputCandidateSummary GetCandidateSummary()
    {
        int candidateCount = 0;
        int playStationCandidateCount = 0;
        RawInputDeviceIdentity? singlePlayStationCandidate = null;
        foreach (RawInputDeviceInfo device in devices.Values)
        {
            if (!device.IsCandidate)
                continue;
            candidateCount++;
            bool playStationCompatible = IsDs4Compatible(device.VendorId, device.ProductId, device.ProductName);
            if (playStationCompatible)
            {
                playStationCandidateCount++;
                singlePlayStationCandidate = new RawInputDeviceIdentity(device.ProductName, device.DevicePath,
                    device.VendorId, device.ProductId, device.UsagePage, device.Usage, true);
            }
        }
        return new RawInputCandidateSummary(candidateCount, playStationCandidateCount,
            playStationCandidateCount == 1 ? singlePlayStationCandidate : null);
    }

    internal bool Register(nint windowHandle)
    {
        if (disposed || !OperatingSystem.IsWindows())
            return false;

        RawInputDeviceNative[] registrations =
        [
            new RawInputDeviceNative { UsagePage = HID_USAGE_PAGE_GENERIC, Usage = HID_USAGE_JOYSTICK, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = windowHandle },
            new RawInputDeviceNative { UsagePage = HID_USAGE_PAGE_GENERIC, Usage = HID_USAGE_GAMEPAD, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = windowHandle },
            new RawInputDeviceNative { UsagePage = HID_USAGE_PAGE_GENERIC, Usage = HID_USAGE_MULTI_AXIS, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = windowHandle },
        ];

        bool success;
        try
        {
            success = RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf<RawInputDeviceNative>());
        }
        catch (Exception ex)
        {
            Log($"Raw Input registration exception: {ex.GetType().Name}");
            return false;
        }

        registered = success;
        Log(success
            ? "Raw Input registration: SUCCESS (Joystick/Game Pad/Multi-axis Controller)"
            : $"Raw Input registration: FAILED (Win32Error={Marshal.GetLastWin32Error()})");
        Refresh(true);
        return success;
    }

    internal void Process(nint rawInputHandle)
    {
        LastParsedReportCount = 0;
        if (disposed || !registered || rawInputHandle == nint.Zero)
            return;

        nint buffer = nint.Zero;
        try
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            if (GetRawInputData(rawInputHandle, RID_INPUT, nint.Zero, ref size, headerSize) == uint.MaxValue
                || size < headerSize + 8 || size > 1024 * 1024)
                return;

            buffer = Marshal.AllocHGlobal((int)size);
            uint actualSize = size;
            if (GetRawInputData(rawInputHandle, RID_INPUT, buffer, ref actualSize, headerSize) == uint.MaxValue
                || actualSize < headerSize + 8)
                return;

            RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.dwType != RIM_TYPEHID || header.hDevice == nint.Zero)
                return;

            if (!devices.TryGetValue(header.hDevice, out RawInputDeviceInfo? device))
            {
                if (unknownDeviceHandles.Add(header.hDevice))
                    Log($"Raw Input report ignored for non-candidate or unenumerated device handle=0x{header.hDevice.ToInt64():X}");
                return;
            }

            if (!device.IsCandidate || device.PreparsedData == nint.Zero)
                return;

            uint reportSize = unchecked((uint)Marshal.ReadInt32(buffer, (int)headerSize));
            uint reportCount = unchecked((uint)Marshal.ReadInt32(buffer, (int)headerSize + 4));
            if (reportSize == 0 || reportCount == 0)
                return;

            ulong payloadStart = (ulong)headerSize + 8;
            ulong payloadEnd = actualSize;
            for (uint i = 0; i < reportCount && payloadStart + reportSize <= payloadEnd; i++)
            {
                if (reportSize > int.MaxValue)
                    return;
                byte[] report = new byte[(int)reportSize];
                Marshal.Copy(buffer + checked((int)payloadStart), report, 0, report.Length);
                if (TryParseReport(device, report, out RawInputGamepadSample sample))
                {
                    LastParsedReportCount++;
                    latestSample = sample;
                    hasLatestSample = true;
                    activeDevice = header.hDevice;
                    LogReport(sample);
                }
                payloadStart += reportSize;
            }
        }
        catch (Exception ex)
        {
            Log($"Raw Input report exception: {ex.GetType().Name}");
        }
        finally
        {
            if (buffer != nint.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    internal bool TryGetLatest(out RawInputGamepadSample sample)
    {
        sample = latestSample;
        return hasLatestSample;
    }

    /// <summary>
    /// Discard only the last report.  Device enumeration, registration and
    /// preparsed data remain intact so activation does not create duplicate
    /// native resources or make a connected device disappear.
    /// </summary>
    internal void ResetTransientState()
    {
        latestSample = default;
        hasLatestSample = false;
        lastLoggedButtons = 0;
        lastLoggedX = int.MinValue;
        lastLoggedY = int.MinValue;
        lastLoggedPov = uint.MaxValue;
    }

    internal void NotifyDeviceChange(uint change, nint deviceHandle)
    {
        if (disposed)
            return;

        string changeName = change switch
        {
            GIDC_ARRIVAL => "GIDC_ARRIVAL",
            GIDC_REMOVAL => "GIDC_REMOVAL",
            _ => "UNKNOWN",
        };
        Log($"Raw Input device change: {changeName} ({change}), handle=0x{deviceHandle.ToInt64():X}");
        if (change == GIDC_REMOVAL && deviceHandle == activeDevice)
        {
            hasLatestSample = false;
            activeDevice = nint.Zero;
        }
        Refresh(true);
    }

    private void Refresh(bool logAll)
    {
        if (disposed || !OperatingSystem.IsWindows())
            return;

        RawInputDeviceListNative[] nativeDevices;
        uint count = 0;
        try
        {
            uint listSize = (uint)Marshal.SizeOf<RawInputDeviceListNative>();
            uint result = GetRawInputDeviceList(nint.Zero, ref count, listSize);
            if (result == uint.MaxValue)
            {
                Log($"Raw Input device enumeration failed: GetRawInputDeviceList size (Win32Error={Marshal.GetLastWin32Error()})");
                return;
            }
            if (count > 1024)
            {
                Log($"Raw Input device enumeration rejected unusually large count: {count}");
                return;
            }
            nativeDevices = count == 0 ? Array.Empty<RawInputDeviceListNative>() : new RawInputDeviceListNative[(int)count];
            if (count > 0)
            {
                uint capacity = count;
                result = GetRawInputDeviceList(nativeDevices, ref capacity, listSize);
                if (result == uint.MaxValue)
                {
                    Log($"Raw Input device enumeration failed: GetRawInputDeviceList data (Win32Error={Marshal.GetLastWin32Error()})");
                    return;
                }
                count = capacity;
            }
        }
        catch (Exception ex)
        {
            Log($"Raw Input device enumeration exception: {ex.GetType().Name}");
            return;
        }

        Log($"Raw Input device count: {count}");
        Dictionary<nint, RawInputDeviceInfo> refreshed = new();
        for (int i = 0; i < nativeDevices.Length; i++)
        {
            RawInputDeviceListNative native = nativeDevices[i];
            RawInputDeviceInfo info = CreateDeviceInfo(native.hDevice, native.dwType);
            if (info.DeviceHandle == nint.Zero)
                continue;

            if (logAll)
            {
                Log($"Raw Input #{i}: type={DescribeDeviceType(info.DeviceType)} ({info.DeviceType}), path={info.DevicePath}, VID=0x{info.VendorId:X4}, PID=0x{info.ProductId:X4}, usagePage=0x{info.UsagePage:X4}, usage=0x{info.Usage:X4}, product={info.ProductName}, candidate={info.IsCandidate}, preparsed={(info.PreparsedData != nint.Zero ? "available" : "unavailable")}");
            }

            if (info.IsCandidate && info.PreparsedData != nint.Zero)
                refreshed[info.DeviceHandle] = info;
            else
                info.Dispose();
        }

        foreach (RawInputDeviceInfo oldInfo in devices.Values)
            oldInfo.Dispose();
        devices.Clear();
        unknownDeviceHandles.Clear();
        foreach (KeyValuePair<nint, RawInputDeviceInfo> pair in refreshed)
            devices[pair.Key] = pair.Value;

        if (activeDevice != nint.Zero && !devices.ContainsKey(activeDevice))
        {
            activeDevice = nint.Zero;
            hasLatestSample = false;
        }
    }

    private RawInputDeviceInfo CreateDeviceInfo(nint deviceHandle, uint deviceType)
    {
        RawInputDeviceInfo info = new()
        {
            DeviceHandle = deviceHandle,
            DeviceType = deviceType,
            DevicePath = GetDevicePath(deviceHandle),
            ProductName = string.Empty,
        };

        if (deviceType != RIM_TYPEHID)
        {
            info.ProductName = string.IsNullOrEmpty(info.DevicePath) ? "(not HID)" : info.DevicePath;
            return info;
        }

        if (TryGetDeviceInfo(deviceHandle, out RawInputDeviceInfoNative nativeInfo))
        {
            info.VendorId = nativeInfo.dwVendorId;
            info.ProductId = nativeInfo.dwProductId;
            info.UsagePage = nativeInfo.usUsagePage;
            info.Usage = nativeInfo.usUsage;
        }
        ParseVidPidFromPath(info.DevicePath, ref info.VendorId, ref info.ProductId);
        info.ProductName = GetProductName(info.DevicePath);
        if (string.IsNullOrWhiteSpace(info.ProductName))
            info.ProductName = string.IsNullOrWhiteSpace(info.DevicePath) ? "(unnamed HID device)" : info.DevicePath;

        info.IsCandidate = info.UsagePage == HID_USAGE_PAGE_GENERIC
            && (info.Usage == HID_USAGE_JOYSTICK || info.Usage == HID_USAGE_GAMEPAD || info.Usage == HID_USAGE_MULTI_AXIS);
        info.PreparsedData = GetPreparsedData(deviceHandle);
        return info;
    }

    private static string GetDevicePath(nint deviceHandle)
    {
        try
        {
            uint characterCount = 0;
            if (GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICENAME, null, ref characterCount) == uint.MaxValue || characterCount == 0)
                return string.Empty;
            StringBuilder builder = new((int)characterCount + 1);
            uint capacity = (uint)builder.Capacity;
            return GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICENAME, builder, ref capacity) == uint.MaxValue
                ? string.Empty
                : builder.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsDs4Compatible(uint vendorId, uint productId, string productName)
    {
        return vendorId == 0x054C && (productId == 0x09CC || productId == 0x05C4 || productId == 0x0BA0)
            || string.Equals(productName?.Trim(), "Wireless Controller", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDeviceInfo(nint deviceHandle, out RawInputDeviceInfoNative info)
    {
        info = default;
        nint memory = nint.Zero;
        try
        {
            int size = Marshal.SizeOf<RawInputDeviceInfoNative>();
            memory = Marshal.AllocHGlobal(size);
            RawInputDeviceInfoNative initial = new() { cbSize = (uint)size };
            Marshal.StructureToPtr(initial, memory, false);
            uint dataSize = (uint)size;
            if (GetRawInputDeviceInfo(deviceHandle, RIDI_DEVICEINFO, memory, ref dataSize) == uint.MaxValue)
                return false;
            info = Marshal.PtrToStructure<RawInputDeviceInfoNative>(memory);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (memory != nint.Zero)
                Marshal.FreeHGlobal(memory);
        }
    }

    private static nint GetPreparsedData(nint deviceHandle)
    {
        nint memory = nint.Zero;
        try
        {
            uint size = 0;
            if (GetRawInputDeviceInfo(deviceHandle, RIDI_PREPARSEDDATA, nint.Zero, ref size) == uint.MaxValue || size == 0 || size > 1024 * 1024)
                return nint.Zero;
            memory = Marshal.AllocHGlobal((int)size);
            uint actualSize = size;
            if (GetRawInputDeviceInfo(deviceHandle, RIDI_PREPARSEDDATA, memory, ref actualSize) == uint.MaxValue)
            {
                Marshal.FreeHGlobal(memory);
                return nint.Zero;
            }
            return memory;
        }
        catch
        {
            if (memory != nint.Zero)
                Marshal.FreeHGlobal(memory);
            return nint.Zero;
        }
    }

    private static string GetProductName(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
            return string.Empty;

        nint handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, nint.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, nint.Zero);
        if (handle == nint.Zero || handle.ToInt64() == INVALID_HANDLE_VALUE)
            return string.Empty;
        try
        {
            StringBuilder product = new(256);
            return HidD_GetProductString(handle, product, product.Capacity) ? product.ToString().TrimEnd('\0') : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private bool TryParseReport(RawInputDeviceInfo device, byte[] report, out RawInputGamepadSample sample)
    {
        sample = default;
        ushort usageLength = 64;
        ushort[] usages = new ushort[usageLength];
        uint status;
        try
        {
            status = HidP_GetUsages(HidpReportType.Input, HID_USAGE_PAGE_BUTTON, 0, usages, ref usageLength, device.PreparsedData, report, report.Length);
        }
        catch
        {
            return false;
        }

        if (status != HIDP_STATUS_SUCCESS && status != 0)
            usageLength = 0;

        uint buttonMask = 0;
        for (int i = 0; i < usageLength; i++)
        {
            ushort usage = usages[i];
            if (usage is >= 1 and <= 32)
                buttonMask |= 1u << (usage - 1);
        }

        bool hasX = TryGetUsageValue(device, HID_USAGE_X, report, out uint rawX);
        bool hasY = TryGetUsageValue(device, HID_USAGE_Y, report, out uint rawY);
        bool hasHat = TryGetUsageValue(device, HID_USAGE_HAT_SWITCH, report, out uint rawHat);
        int x = hasX ? NormalizeHidAxis(rawX) : 0;
        int y = hasY ? -NormalizeHidAxis(rawY) : 0;
        // D-pad/POV and the left stick stay separate through the input layer.
        // A Raw Input left-stick movement must not be promoted into a D-pad
        // action, otherwise MainWindow cannot route it to Direct Gameplay Input.
        GamepadDirection dpadDirection = hasHat && rawHat <= 7
            ? GetHatDirection(rawHat)
            : GamepadDirection.None;
        GamepadDirection leftStickDirection = hasX || hasY
            ? GamepadManager.GetStickDirection(x, y)
            : GamepadDirection.None;

        if (buttonMask == 0 && !hasX && !hasY && !hasHat)
            return false;

        sample = new RawInputGamepadSample(
            device.ProductName,
            device.DevicePath,
            device.VendorId,
            device.ProductId,
            device.UsagePage,
            device.Usage,
            buttonMask,
            dpadDirection,
            leftStickDirection,
            x,
            y,
            hasHat ? rawHat : uint.MaxValue,
            IsDs4Compatible(device.VendorId, device.ProductId, device.ProductName));
        return true;
    }

    private static bool TryGetUsageValue(RawInputDeviceInfo device, ushort usage, byte[] report, out uint value)
    {
        try
        {
            return HidP_GetUsageValue(HidpReportType.Input, HID_USAGE_PAGE_GENERIC, 0, usage, out value, device.PreparsedData, report, report.Length) == HIDP_STATUS_SUCCESS;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private void LogReport(RawInputGamepadSample sample)
    {
        if (!diagnosticsEnabled)
            return;
        if (sample.ButtonMask == lastLoggedButtons && sample.X == lastLoggedX && sample.Y == lastLoggedY && sample.Pov == lastLoggedPov)
            return;
        lastLoggedButtons = sample.ButtonMask;
        lastLoggedX = sample.X;
        lastLoggedY = sample.Y;
        lastLoggedPov = sample.Pov;
        Log($"Raw Input report: device={sample.DeviceName}, Buttons=0x{sample.ButtonMask:X8}, X={sample.X}, Y={sample.Y}, POV={sample.Pov}, DPad={sample.DPadDirection}, LeftStick={sample.LeftStickDirection}");
    }

    private void Log(string message)
    {
        if (diagnosticsEnabled)
            log(message);
    }

    private static string DescribeDeviceType(uint type)
    {
        return type switch
        {
            0 => "MOUSE",
            1 => "KEYBOARD",
            RIM_TYPEHID => "HID",
            _ => "UNKNOWN",
        };
    }

    private static void ParseVidPidFromPath(string path, ref uint vendorId, ref uint productId)
    {
        if (vendorId == 0 && TryGetPathHex(path, "VID_", out uint parsedVendor))
            vendorId = parsedVendor;
        if (productId == 0 && TryGetPathHex(path, "PID_", out uint parsedProduct))
            productId = parsedProduct;
    }

    private static bool TryGetPathHex(string path, string marker, out uint value)
    {
        value = 0;
        int start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;
        start += marker.Length;
        int length = 0;
        while (start + length < path.Length && length < 8 && Uri.IsHexDigit(path[start + length]))
            length++;
        return length > 0 && uint.TryParse(path.AsSpan(start, length), System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private static int NormalizeHidAxis(uint value)
    {
        long normalized;
        if (value <= 255)
            normalized = ((long)value - 128) * 512;
        else if (value <= 1023)
            normalized = ((long)value - 512) * 64;
        else if (value <= 4095)
            normalized = ((long)value - 2048) * 32;
        else
            normalized = (long)value - 32768;
        if (normalized < -32768) return -32768;
        if (normalized > 32767) return 32767;
        return (int)normalized;
    }

    private static GamepadDirection GetHatDirection(uint hat)
    {
        return hat switch
        {
            0 => GamepadDirection.Up,
            1 or 2 => GamepadDirection.Right,
            3 or 4 => GamepadDirection.Down,
            5 or 6 => GamepadDirection.Left,
            _ => GamepadDirection.None,
        };
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (RawInputDeviceInfo info in devices.Values)
            info.Dispose();
        devices.Clear();
        hasLatestSample = false;
        activeDevice = nint.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceListNative
    {
        internal nint hDevice;
        internal uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceNative
    {
        internal ushort UsagePage;
        internal ushort Usage;
        internal uint Flags;
        internal nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        internal uint dwType;
        internal uint dwSize;
        internal nint hDevice;
        internal nint wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceInfoNative
    {
        internal uint cbSize;
        internal uint dwType;
        internal uint dwVendorId;
        internal uint dwProductId;
        internal uint dwVersionNumber;
        internal ushort usUsagePage;
        internal ushort usUsage;
        // RID_DEVICE_INFO contains a union whose size is that of the
        // keyboard variant (24 bytes), even when dwType is HID. Keep the
        // trailing union padding so the native API receives the full 32-byte
        // structure instead of an undersized buffer.
        internal uint reserved0;
        internal uint reserved1;
    }

    private enum HidpReportType : int
    {
        Input = 0,
        Output = 1,
        Feature = 2,
    }

    private sealed class RawInputDeviceInfo
    {
        internal nint DeviceHandle;
        internal uint DeviceType;
        internal string DevicePath = string.Empty;
        internal string ProductName = string.Empty;
        internal uint VendorId;
        internal uint ProductId;
        internal ushort UsagePage;
        internal ushort Usage;
        internal bool IsCandidate;
        internal nint PreparsedData;

        internal void Dispose()
        {
            if (PreparsedData != nint.Zero)
            {
                Marshal.FreeHGlobal(PreparsedData);
                PreparsedData = nint.Zero;
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "RegisterRawInputDevices", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawInputDeviceNative[] devices, uint numDevices, uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceList", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(nint devices, ref uint numDevices, uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceList", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RawInputDeviceListNative[] devices, ref uint numDevices, uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);

    [DllImport("user32.dll", EntryPoint = "GetRawInputData", SetLastError = true)]
    private static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);

    [DllImport("hid.dll", EntryPoint = "HidD_GetProductString", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetProductString(nint hidDevice, StringBuilder buffer, int bufferLength);

    [DllImport("hidparse.dll", EntryPoint = "HidP_GetUsages", CallingConvention = CallingConvention.Winapi)]
    private static extern uint HidP_GetUsages(HidpReportType reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usageList, ref ushort usageLength, nint preparsedData, [In] byte[] report, int reportLength);

    [DllImport("hidparse.dll", EntryPoint = "HidP_GetUsageValue", CallingConvention = CallingConvention.Winapi)]
    private static extern uint HidP_GetUsageValue(HidpReportType reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint usageValue, nint preparsedData, [In] byte[] report, int reportLength);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}

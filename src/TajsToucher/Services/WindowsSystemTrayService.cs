using System.Runtime.InteropServices;

namespace TajsToucher.Services;

/// <summary>
/// Native notification-area host used by the WinUI dashboard. Keeping the tray surface in Win32
/// avoids bringing WinForms back just to keep the dashboard alive after its window is hidden.
/// </summary>
internal sealed class WindowsSystemTrayService : IDisposable
{
    private const uint TrayIconId = 1;
    private const uint TrayCallbackMessage = WmApp + 1;
    private const uint CommandOpen = 1001;
    private const uint CommandSettings = 1002;
    private const uint CommandEnabledFor = 1003;
    private const uint CommandExit = 1004;

    private readonly WindowProc windowProc;
    private readonly string windowClassName = $"TajsToucher.Tray.{Environment.ProcessId}.{Guid.NewGuid():N}";
    private nint windowHandle;
    private nint instanceHandle;
    private nint fallbackIconHandle;
    private uint taskbarCreatedMessage;
    private bool disposed;

    public WindowsSystemTrayService()
    {
        windowProc = HandleWindowMessage;
    }

    public event EventHandler? OpenDashboardRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenEnabledForRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (windowHandle != 0)
        {
            return;
        }

        instanceHandle = GetModuleHandleW(null);
        taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(windowProc),
            Instance = instanceHandle,
            ClassName = windowClassName,
        };

        if (RegisterClassExW(ref windowClass) == 0)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not register the TajsToucher tray window class.");
        }

        windowHandle = CreateWindowExW(
            0,
            windowClassName,
            "TajsToucher tray host",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            instanceHandle,
            0);
        if (windowHandle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            _ = UnregisterClassW(windowClassName, instanceHandle);
            instanceHandle = 0;
            throw new System.ComponentModel.Win32Exception(error, "Could not create the TajsToucher tray window.");
        }

        fallbackIconHandle = LoadIconW(0, IdiApplication);
        var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip | NifShowTip);
        data.CallbackMessage = TrayCallbackMessage;
        data.Icon = fallbackIconHandle;
        data.Tip = "TajsToucher";
        if (!Shell_NotifyIconW(NimAdd, ref data))
        {
            var error = Marshal.GetLastWin32Error();
            DestroyTrayHost();
            throw new System.ComponentModel.Win32Exception(error, "Could not add the TajsToucher notification-area icon.");
        }

        var version = CreateNotifyIconData(0);
        version.TimeoutOrVersion = NotifyIconVersion4;
        _ = Shell_NotifyIconW(NimSetVersion, ref version);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (windowHandle != 0)
        {
            var data = CreateNotifyIconData(0);
            _ = Shell_NotifyIconW(NimDelete, ref data);
        }

        DestroyTrayHost();
        GC.SuppressFinalize(this);
    }

    private nint HandleWindowMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (taskbarCreatedMessage != 0 && message == taskbarCreatedMessage)
        {
            ReAddTrayIcon();
            return 0;
        }

        if (message == TrayCallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFFu;
            if (mouseMessage == WmLButtonDblClk)
            {
                OpenDashboardRequested?.Invoke(this, EventArgs.Empty);
                return 0;
            }

            if (mouseMessage is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                return 0;
            }
        }

        if (message == WmCommand)
        {
            switch (unchecked((uint)wParam.ToUInt64()) & 0xFFFFu)
            {
                case CommandOpen:
                    OpenDashboardRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandSettings:
                    OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandEnabledFor:
                    OpenEnabledForRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case CommandExit:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void ReAddTrayIcon()
    {
        if (windowHandle == 0 || disposed)
        {
            return;
        }

        var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip | NifShowTip);
        data.CallbackMessage = TrayCallbackMessage;
        data.Icon = fallbackIconHandle;
        data.Tip = "TajsToucher";
        _ = Shell_NotifyIconW(NimAdd, ref data);
    }

    private void ShowContextMenu()
    {
        if (windowHandle == 0 || !GetCursorPos(out var point))
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = AppendMenuW(menu, MfString, CommandOpen, "Open dashboard");
            _ = AppendMenuW(menu, MfString, CommandSettings, "Settings");
            _ = AppendMenuW(menu, MfString, CommandEnabledFor, "Enabled for");
            _ = AppendMenuW(menu, MfSeparator, 0, null);
            _ = AppendMenuW(menu, MfString, CommandExit, "Exit TajsToucher");
            _ = SetForegroundWindow(windowHandle);
            _ = TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd | TpmNoNotify, point.X, point.Y, 0, windowHandle, 0);
            _ = PostMessageW(windowHandle, WmNull, 0, 0);
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private NotifyIconData CreateNotifyIconData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = windowHandle,
        Id = TrayIconId,
        Flags = flags,
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private void DestroyTrayHost()
    {
        if (windowHandle != 0)
        {
            _ = DestroyWindow(windowHandle);
            windowHandle = 0;
        }

        if (instanceHandle != 0)
        {
            _ = UnregisterClassW(windowClassName, instanceHandle);
            instanceHandle = 0;
        }

        fallbackIconHandle = 0;
    }

    private delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private const uint WmNull = 0x0000;
    private const uint WmCommand = 0x0111;
    private const uint WmApp = 0x8000;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;
    private static readonly nint IdiApplication = new(32512);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIconW(nint instance, nint iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenuW(nint menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rectangle);
}

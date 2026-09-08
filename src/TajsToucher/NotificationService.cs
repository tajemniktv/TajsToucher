using System.Drawing;
using System.Runtime.InteropServices;

namespace TajsToucher;

internal static class NotificationService
{
    private const int BalloonDurationMilliseconds = 10_000;

    public static bool TryLaunch(string? repositoryName, bool bypassCooldown = false)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            var arguments = new List<string> { bypassCooldown ? "--notify-test" : "--notify" };
            if (!string.IsNullOrWhiteSpace(repositoryName))
            {
                arguments.Add(repositoryName);
            }

            // Use bInheritHandles=false so a ten-second notification cannot keep Git's pipes alive
            // after GPG exits. Notifications are deliberately detached and fail-open.
            return DetachedProcessLauncher.TryLaunch(executable, arguments, HelperDispatch.Notification);
        }
        catch
        {
            return false;
        }
    }

    public static int Show(string? repositoryName, bool bypassCooldown = false)
    {
        var settings = new ConfigurationStore().LoadNotificationSettings();
        return ShowEvent(new OperationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
            OpenPgpOperation.Signing, OperationPhase.Requested), settings, repositoryName, bypassCooldown);
    }

    internal static int ShowEvent(OperationEvent operationEvent, NotificationSettings settings, string? repositoryName,
        bool bypassCooldown = false)
    {
        if (!operationEvent.IsValid) return 2;
        // An explicitly enabled failure alert should not be hidden by its own start prompt.
        bypassCooldown |= operationEvent.Phase == OperationPhase.Failed;
        using var customIcon = NotificationIconLoader.TryLoad(settings.IconPath);
        var presentation = FormatEvent(operationEvent, settings, repositoryName);
        using var host = new NativeNotificationHost(
            presentation.Title,
            presentation.Message,
            customIcon,
            settings.PlaySound);
        if (!NotificationCooldown.TryShow(bypassCooldown ? 0 : settings.CooldownSeconds, host.Show)) return 0;
        host.Run();
        return 0;
    }

    internal static (string Title, string Message) FormatEvent(OperationEvent operationEvent, NotificationSettings settings, string? repositoryName)
    {
        if (operationEvent.Phase == OperationPhase.Failed)
        {
            return ("OpenPGP operation failed",
                $"{OperationEvent.Describe(operationEvent.Operation)} exited with code {operationEvent.ExitCode}. Check the calling application's error output.");
        }

        var title = settings.Title == NotificationSettings.DefaultTitle && operationEvent.Operation != OpenPgpOperation.Signing
            ? "OpenPGP operation requested"
            : NotificationTemplate.RenderTitle(settings.Title, repositoryName, operationEvent.Operation);
        return (title, NotificationTemplate.Render(settings.Text, repositoryName, operationEvent.Operation));
    }

    private sealed class NativeNotificationHost : IDisposable
    {
        private const uint TrayIconId = 1;
        private const uint TimerId = 1;
        private const uint WmTimer = 0x0113;
        private const uint WmDestroy = 0x0002;
        private const uint WmApp = 0x8000;
        private const uint NimAdd = 0;
        private const uint NimModify = 1;
        private const uint NimDelete = 2;
        private const uint NifMessage = 0x00000001;
        private const uint NifIcon = 0x00000002;
        private const uint NifTip = 0x00000004;
        private const uint NifInfo = 0x00000010;
        private const uint NifShowTip = 0x00000080;
        private const uint NiifInfo = 1;
        private const uint NiifNoSound = 0x10;
        private static readonly nint IdiApplication = new(32512);

        private readonly string title;
        private readonly string message;
        private readonly Icon? customIcon;
        private readonly bool playSound;
        private readonly WindowProc windowProc;
        private readonly string windowClassName = $"TajsToucher.Notification.{Environment.ProcessId}.{Guid.NewGuid():N}";
        private nint windowHandle;
        private nint instanceHandle;
        private nint fallbackIconHandle;
        private bool iconAdded;

        public NativeNotificationHost(string title, string message, Icon? customIcon, bool playSound)
        {
            this.title = title;
            this.message = message;
            this.customIcon = customIcon;
            this.playSound = playSound;
            windowProc = HandleWindowMessage;
        }

        public void Show()
        {
            instanceHandle = GetModuleHandleW(null);
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProc = Marshal.GetFunctionPointerForDelegate(windowProc),
                Instance = instanceHandle,
                ClassName = windowClassName,
            };
            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not register the notification host.");
            }

            windowHandle = CreateWindowExW(
                0,
                windowClassName,
                "TajsToucher notification host",
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
                throw new System.ComponentModel.Win32Exception(error, "Could not create the notification host.");
            }

            fallbackIconHandle = LoadIconW(0, IdiApplication);
            var data = CreateNotifyIconData(NifMessage | NifIcon | NifTip | NifShowTip);
            data.CallbackMessage = WmApp + 1;
            data.Icon = customIcon?.Handle ?? fallbackIconHandle;
            data.Tip = "TajsToucher";
            if (!Shell_NotifyIconW(NimAdd, ref data))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not add the notification host icon.");
            }

            iconAdded = true;
            data = CreateNotifyIconData(NifInfo);
            data.Icon = customIcon?.Handle ?? fallbackIconHandle;
            data.InfoTitle = Truncate(title, 63);
            data.Info = Truncate(message, 255);
            data.InfoFlags = NiifInfo | (playSound ? 0 : NiifNoSound);
            if (!Shell_NotifyIconW(NimModify, ref data))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not display the notification.");
            if (SetTimer(windowHandle, TimerId, BalloonDurationMilliseconds, 0) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the notification timer.");
        }

        public void Run()
        {
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }

        public void Dispose()
        {
            if (iconAdded && windowHandle != 0)
            {
                var data = CreateNotifyIconData(0);
                _ = Shell_NotifyIconW(NimDelete, ref data);
                iconAdded = false;
            }

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
        }

        private nint HandleWindowMessage(nint window, uint message, nuint wParam, nint lParam)
        {
            if (message == WmTimer && wParam == TimerId)
            {
                _ = KillTimer(window, TimerId);
                PostQuitMessage(0);
                return 0;
            }

            if (message == WmDestroy)
            {
                PostQuitMessage(0);
                return 0;
            }

            return DefWindowProcW(window, message, wParam, lParam);
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

        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

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
        private struct NativeMessage
        {
            public nint Window;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public Point Point;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nuint SetTimer(nint window, nuint timerId, uint milliseconds, nint callback);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool KillTimer(nint window, nuint timerId);

        [DllImport("user32.dll")]
        private static extern int GetMessageW(out NativeMessage message, nint window, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessageW(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int exitCode);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    }
}

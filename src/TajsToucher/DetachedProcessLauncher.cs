using System.Runtime.InteropServices;

namespace TajsToucher;

internal static class DetachedProcessLauncher
{
    private const uint CreateNoWindow = 0x08000000;

    public static bool TryLaunch(string executable, IReadOnlyList<string> arguments, string helperMode)
    {
        var commandLine = new StringBuilder(WindowsArgumentQuoter.Quote(executable));
        foreach (var argument in arguments)
        {
            commandLine.Append(' ');
            commandLine.Append(WindowsArgumentQuoter.Quote(argument));
        }

        var startupInfo = new StartupInfo
        {
            Size = Marshal.SizeOf<StartupInfo>(),
        };

        var environment = Marshal.StringToHGlobalUni(HelperDispatch.CreateEnvironmentBlock(helperMode));
        try
        {
            if (!CreateProcess(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    bInheritHandles: false,
                    CreateNoWindow | 0x400, // CREATE_UNICODE_ENVIRONMENT
                    environment,
                    null,
                    ref startupInfo,
                    out var processInformation))
            {
                return false;
            }

            CloseHandle(processInformation.ProcessHandle);
            CloseHandle(processInformation.ThreadHandle);
            return true;
        }
        finally { Marshal.FreeHGlobal(environment); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(
        string applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool bInheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public int ProcessId;
        public int ThreadId;
    }
}

namespace TajsToucher;

internal static class WindowComposition
{
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmSystemBackdropMainWindow = 2;

    public static void Apply(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var backdropType = DwmSystemBackdropMainWindow;
            _ = DwmSetWindowAttribute(
                windowHandle,
                DwmwaSystemBackdropType,
                ref backdropType,
                Marshal.SizeOf<int>());

            // Let the system or a compatible window effect extend its material
            // behind the client area instead of stopping at the title bar.
            var margins = new Margins(-1, -1, -1, -1);
            _ = DwmExtendFrameIntoClientArea(windowHandle, ref margins);
        }
        catch (DllNotFoundException)
        {
            // DWM is available on supported Windows desktop versions. Keep the
            // app usable if it is started in an unusual compatibility environment.
        }
        catch (EntryPointNotFoundException)
        {
            // Older Windows versions simply keep the normal WinForms surface.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Margins
    {
        public Margins(int left, int right, int top, int bottom)
        {
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }

        public readonly int Left;
        public readonly int Right;
        public readonly int Top;
        public readonly int Bottom;
    }
}

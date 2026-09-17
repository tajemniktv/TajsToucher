using System.Drawing;

namespace TajsToucher;

internal static class AppIcon
{
    // A single process-lifetime icon. Windows and tray hosts borrow this handle.
    // Embedded data works in both folder and single-file deployments.
    private static readonly Lazy<Icon> icon = new(() =>
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("TajsToucher.AppIcon.ico")
            ?? throw new InvalidOperationException("The application icon is missing.");
        using var source = new Icon(stream, 32, 32);
        return (Icon)source.Clone();
    });

    internal static nint Handle => icon.Value.Handle;
}

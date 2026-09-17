using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace TajsToucher;

public sealed partial class TouchWaitCard : Window
{
    private readonly EventWaitHandle ended;
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    internal TouchWaitCard(EventWaitHandle ended)
    {
        this.ended = ended;
        InitializeComponent();
        AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(AppIcon.Handle));
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(true, false);
        Root.Loaded += (_, _) =>
        {
            // WinUI layout is in DIPs, AppWindow bounds are physical pixels.
            var scale = Root.XamlRoot.RasterizationScale;
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min((int)Math.Ceiling(440 * scale), area.Width);
            var height = Math.Min((int)Math.Ceiling(380 * scale), area.Height);
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2, width, height));
            // Apply after initial WinUI presentation and sizing, which can reset
            // the native topmost style during startup.
            presenter.IsAlwaysOnTop = true;
            DismissButton.Focus(FocusState.Programmatic);
            CheckEnded();
        };
        timer.Tick += (_, _) => CheckEnded();
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private void CheckEnded()
    {
        if (ended.WaitOne(0) || lifetime.Elapsed >= TimeSpan.FromSeconds(30)) Close();
    }

    private void DismissClicked(object sender, RoutedEventArgs args) => Close();
    private void DismissInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TajsToucher.Devices;

namespace TajsToucher.Pages;

public sealed class DevicesPage : Page
{
    private readonly ComboBox selector = new() { Header = "Connected key", DisplayMemberPath = nameof(ConnectedKey.Label), MinWidth = 360 };
    private readonly TextBlock inventory = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly Button connect = new() { Content = "Discover / refresh keys" };
    private readonly Button read = new() { Content = "Read application status", IsEnabled = false };
    private readonly Button identify = new() { Content = "Touch / identify test", IsEnabled = false };
    private readonly Button cancel = new() { Content = "Cancel test", IsEnabled = false };
    private IDeviceService? service;
    private CancellationTokenSource? testCancellation;
    private bool busy;
    private bool active;

    public DevicesPage()
    {
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InkBrush"];
        var panel = new StackPanel { Padding = new Thickness(32), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "YubiKey devices", FontSize = 28 });
        panel.Children.Add(new TextBlock { Text = "Optional, local SDK diagnostics. Discovery starts only when requested and stays active until app exit. Application reads and touch tests run only when clicked. This does not monitor other apps' touch requests.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(connect);
        panel.Children.Add(selector);
        panel.Children.Add(inventory);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(read); actions.Children.Add(identify); actions.Children.Add(cancel);
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "No PIN entry, credential enumeration, code generation, or key changes. Direct FIDO access may be denied by Windows; the app never elevates itself.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(status);
        Content = new ScrollViewer { Content = panel };
        connect.Click += Discover;
        read.Click += ReadStatus;
        identify.Click += Identify;
        cancel.Click += (_, _) => testCancellation?.Cancel();
        selector.SelectionChanged += (_, _) => { status.Text = ""; ShowSelection(); };
        Loaded += (_, _) =>
        {
            active = true;
            service = (App.DeviceFeatureLifetime as DesktopDeviceFeature)?.Service;
            if (service is not null) Subscribe();
        };
        Unloaded += (_, _) =>
        {
            active = false;
            testCancellation?.Cancel();
            if (service is not null) { service.InventoryChanged -= OnInventory; service.Signal -= OnSignal; }
        };
    }

    private void Subscribe()
    {
        service!.InventoryChanged -= OnInventory; service.Signal -= OnSignal;
        service.InventoryChanged += OnInventory; service.Signal += OnSignal;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IDeviceService CreateService() => new YubiKeyService();

    private async void Discover(object sender, RoutedEventArgs args)
    {
        SetBusy(true);
        try
        {
            if (service is null)
            {
                service = CreateService();
                App.DeviceFeatureLifetime = new DesktopDeviceFeature(service);
                Subscribe();
            }
            OnInventory(await service.RefreshInventoryAsync());
        }
        catch { status.Text = "Optional SDK unavailable. Git/GPG forwarding is unaffected."; }
        finally { SetBusy(false); }
    }

    private void OnInventory(InventoryResult result) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!active) return;
        var selectedId = (selector.SelectedItem as ConnectedKey)?.Id;
        selector.ItemsSource = result.Keys;
        selector.SelectedItem = result.Keys.FirstOrDefault(key => key.Id == selectedId);
        if (selector.SelectedItem is null && result.Keys.Count == 1) selector.SelectedIndex = 0;
        if (result.Outcome != DeviceOutcome.Ready) inventory.Text = $"Discovery: {result.Outcome}. Retry discovery; this is not proof no key is attached.";
        else if (result.Keys.Count == 0) inventory.Text = "No connected YubiKey found.";
        else if (selector.SelectedItem is null) inventory.Text = "Select a key explicitly. Labels are local to this discovery session; no serial numbers are logged.";
        ShowSelection();
    });

    private void ShowSelection()
    {
        if (selector.SelectedItem is ConnectedKey key)
            inventory.Text = $"Firmware: {key.Firmware}\nUSB available: {key.UsbAvailable}\nUSB enabled: {key.UsbEnabled}\nNFC available: {key.NfcAvailable}\nNFC enabled: {key.NfcEnabled}";
        read.IsEnabled = !busy && selector.SelectedItem is ConnectedKey;
        identify.IsEnabled = !busy && selector.SelectedItem is ConnectedKey { CanIdentify: true };
    }

    private async void ReadStatus(object sender, RoutedEventArgs args)
    {
        if (service is null || selector.SelectedItem is not ConnectedKey key) return;
        SetBusy(true);
        status.Text = "Reading metadata; no credentials will be requested…";
        try
        {
            var results = await service.ReadStatusAsync(key.Id);
            if (active) status.Text = string.Join("\n\n", results.Select(result =>
                $"{result.Application}: {result.Outcome} — {result.Detail}" + (result.RetriesRemaining is int retries ? $" Remaining: {retries}." : "")));
        }
        catch { if (active) status.Text = "Device read unavailable. Retry after other applications release the key."; }
        finally { SetBusy(false); }
    }

    private async void Identify(object sender, RoutedEventArgs args)
    {
        if (service is null || selector.SelectedItem is not ConnectedKey key) return;
        SetBusy(true);
        using var cancellation = new CancellationTokenSource();
        testCancellation = cancellation;
        cancel.IsEnabled = true;
        status.Text = "Starting TajsToucher's own identify test…";
        try
        {
            var result = await service.IdentifyAsync(key.Id, cancellation.Token);
            if (active) status.Text = result == DeviceOutcome.Ready ? "Touch confirmed for the selected key. No credential was created or signature made." : $"Identify test: {result}.";
        }
        catch { if (active) status.Text = "Identify test unavailable."; }
        finally { testCancellation = null; cancel.IsEnabled = false; SetBusy(false); }
    }

    private void OnSignal(DeviceSignal signal) => DispatcherQueue.TryEnqueue(() =>
    {
        if (active && signal.Kind == DeviceSignalKind.TouchRequested && testCancellation is not null)
            status.Text = "Touch the selected flashing key for TajsToucher's identify test. Cancel to stop.";
    });

    private void SetBusy(bool value)
    {
        busy = value; connect.IsEnabled = !value; selector.IsEnabled = !value; ShowSelection();
    }
}

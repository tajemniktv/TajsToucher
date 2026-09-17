using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TajsToucher.Devices;

namespace TajsToucher.Pages;

public sealed partial class DevicesPage : Page
{
    private readonly ComboBox selector = new() { Header = "SDK-accessible key", DisplayMemberPath = nameof(ConnectedKey.Label), MinWidth = 360 };
    private readonly TextBlock inventory = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock presence = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel attachedKeys = new() { Spacing = 8 };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
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
        InitializeComponent();
        var panel = new StackPanel { Padding = new Thickness(32), Spacing = 16, MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = "YubiKey devices", FontSize = 28 });
        panel.Children.Add(new TextBlock { Text = "Connected hardware is shown independently of SDK access. Git/GPG signing and its touch-wait prompt do not depend on these diagnostics.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(connect);
        panel.Children.Add(presence);
        panel.Children.Add(attachedKeys);
        var diagnostics = new StackPanel { Spacing = 12 };
        diagnostics.Children.Add(new TextBlock { Text = "Optional application reads and identify tests. No PIN entry, credentials, signatures, or key changes. Access can be unavailable while GPG owns the connection or Windows restricts FIDO.", TextWrapping = TextWrapping.Wrap });
        diagnostics.Children.Add(selector);
        diagnostics.Children.Add(inventory);
        actions.Children.Add(read); actions.Children.Add(identify); actions.Children.Add(cancel);
        diagnostics.Children.Add(actions);
        diagnostics.Children.Add(status);
        panel.Children.Add(new Expander { Header = "Optional SDK diagnostics", Content = diagnostics, HorizontalAlignment = HorizontalAlignment.Stretch });
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
            if (!busy) Discover(this, new RoutedEventArgs());
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
        presence.Text = result.AttachedUsbDevices switch
        {
            > 0 => $"Windows detects {result.AttachedUsbDevices} attached Yubico USB device(s). SDK-accessible keys: {result.Keys.Count}.",
            0 => $"Windows detects no attached Yubico USB devices. SDK-accessible keys (including other transports): {result.Keys.Count}.",
            _ => "Windows USB presence is unavailable; this does not mean the key is disconnected.",
        };
        attachedKeys.Children.Clear();
        for (var i = 0; i < result.AttachedUsbDevices.GetValueOrDefault(); i++)
            attachedKeys.Children.Add(new InfoBar
            {
                IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Success,
                Title = $"Yubico USB device {i + 1} · Connected",
                Message = "Detected by Windows. This does not depend on SDK access.",
            });
        // These are OS presence cards, not SDK handles or inferred matches to keys.
        selector.Visibility = actions.Visibility = result.Keys.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var selectedId = (selector.SelectedItem as ConnectedKey)?.Id;
        selector.ItemsSource = result.Keys;
        selector.SelectedItem = result.Keys.FirstOrDefault(key => key.Id == selectedId);
        if (selector.SelectedItem is null && result.Keys.Count == 1) selector.SelectedIndex = 0;
        if (result.Outcome != DeviceOutcome.Ready) inventory.Text = $"Discovery: {result.Outcome}. Retry discovery; this is not proof no key is attached.";
        else if (result.Keys.Count == 0) inventory.Text = result.AttachedUsbDevices > 0
            ? "Diagnostics currently unavailable. GPG may retain the smart-card connection between signatures; Windows may also restrict FIDO access. Your connected hardware remains shown above. Signing is unaffected—no replug is required for normal use."
            : "No SDK-accessible key for diagnostics. Connect a key and refresh, or check the USB presence status above. Signing notifications operate independently.";
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

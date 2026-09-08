using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using TajsToucher;
using WinRT.Interop;

namespace TajsToucher.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly ConfigurationStore configurationStore = new();

    public SettingsPage()
    {
        InitializeComponent();
        LoadSettings(configurationStore.LoadNotificationSettings());
    }

    public event EventHandler? SettingsSaved;

    internal void ReloadSettings()
    {
        LoadSettings(configurationStore.LoadNotificationSettings());
    }

    private void LoadSettings(NotificationSettings settings)
    {
        TitleTextBox.Text = settings.Title;
        TextTextBox.Text = settings.Text;
        IconPathTextBox.Text = settings.IconPath;
        SoundToggle.IsOn = settings.PlaySound;
        CooldownNumberBox.Value = settings.CooldownSeconds;
        SigningToggle.IsOn = settings.NotifyOnSigning;
        EncryptionToggle.IsOn = settings.NotifyOnEncryption;
        DecryptionToggle.IsOn = settings.NotifyOnDecryption;
        FailureToggle.IsOn = settings.NotifyOnFailure;
        DiagnosticsToggle.IsOn = settings.RecordDiagnostics;
        DevicePresenceToggle.IsOn = settings.NotifyOnDevicePresence;
        LowRetriesToggle.IsOn = settings.NotifyOnLowRetries;
        UpdateIconPreview();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings(NotificationSettings.Defaults);
        StatusLabel.Text = "Defaults restored. Click Save settings to keep them.";
    }

    private void ClearIcon_Click(object sender, RoutedEventArgs e)
    {
        IconPathTextBox.Text = string.Empty;
        UpdateIconPreview();
    }

    private async void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".ico");
        InitializeWithWindow.Initialize(picker, App.GetMainWindowHandle());
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        IconPathTextBox.Text = file.Path;
        UpdateIconPreview();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveSettingsAsync(testAfterSave: false);
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveSettingsAsync(testAfterSave: true);
    }

    private async Task SaveSettingsAsync(bool testAfterSave)
    {
        if (!TryReadSettings(out var settings))
        {
            return;
        }

        try
        {
            configurationStore.SaveNotificationSettings(settings);
            StatusLabel.Text = testAfterSave
                ? NotificationService.TryLaunch("TajsToucher", bypassCooldown: true)
                    ? "Settings saved and test notification launched."
                    : "Settings saved, but the notification helper could not be started."
                : "Settings saved.";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            StatusLabel.Text = $"Settings could not be saved: {exception.Message}";
            await ShowMessageAsync(exception.Message, "TajsToucher Settings");
        }
    }

    private bool TryReadSettings(out NotificationSettings settings)
    {
        if (!double.IsFinite(CooldownNumberBox.Value) || CooldownNumberBox.Value < 0 ||
            CooldownNumberBox.Value > 300 || CooldownNumberBox.Value != Math.Truncate(CooldownNumberBox.Value))
        {
            settings = NotificationSettings.Defaults;
            StatusLabel.Text = "Cooldown must be a whole number from 0 to 300 seconds.";
            return false;
        }

        var rawSettings = new NotificationSettings(TitleTextBox.Text, TextTextBox.Text, IconPathTextBox.Text,
            SoundToggle.IsOn, (int)CooldownNumberBox.Value)
        {
            NotifyOnSigning = SigningToggle.IsOn,
            NotifyOnEncryption = EncryptionToggle.IsOn,
            NotifyOnDecryption = DecryptionToggle.IsOn,
            NotifyOnFailure = FailureToggle.IsOn,
            RecordDiagnostics = DiagnosticsToggle.IsOn,
            NotifyOnDevicePresence = DevicePresenceToggle.IsOn,
            NotifyOnLowRetries = LowRetriesToggle.IsOn,
        };
        if (string.IsNullOrWhiteSpace(rawSettings.Title))
        {
            settings = NotificationSettings.Defaults;
            _ = ShowMessageAsync("Notification title cannot be empty.", "TajsToucher Settings");
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawSettings.Text))
        {
            settings = NotificationSettings.Defaults;
            _ = ShowMessageAsync("Notification text cannot be empty.", "TajsToucher Settings");
            return false;
        }

        settings = rawSettings.Normalize();

        if (!string.IsNullOrWhiteSpace(settings.IconPath))
        {
            using var icon = NotificationIconLoader.TryLoad(settings.IconPath);
            if (icon is null)
            {
                _ = ShowMessageAsync("The selected icon could not be loaded. Choose a valid .ico file or use Default.", "TajsToucher Settings");
                return false;
            }
        }

        return true;
    }

    private void UpdateIconPreview()
    {
        IconPreview.Source = null;
        var path = IconPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            IconPreview.Source = bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UriFormatException)
        {
            IconPreview.Source = null;
        }
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        if (XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}

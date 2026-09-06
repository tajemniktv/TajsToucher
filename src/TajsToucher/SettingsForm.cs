namespace TajsToucher;

internal sealed class SettingsPageControl : UserControl
{
    private readonly TextBox titleTextBox = new();
    private readonly TextBox textTextBox = new();
    private readonly TextBox iconPathTextBox = new();
    private readonly PictureBox iconPreview = new();
    private readonly Label statusLabel = new();
    private readonly ConfigurationStore configurationStore = new();

    public event EventHandler? SettingsSaved;

    public SettingsPageControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
        Padding = new Padding(24, 18, 24, 24);

        titleTextBox.Dock = DockStyle.Fill;
        titleTextBox.Margin = new Padding(3, 5, 3, 5);

        textTextBox.Dock = DockStyle.Fill;
        textTextBox.AcceptsReturn = true;
        textTextBox.Multiline = true;
        textTextBox.ScrollBars = ScrollBars.Vertical;
        textTextBox.Margin = new Padding(3, 5, 3, 5);

        iconPreview.Size = new Size(48, 48);
        iconPreview.SizeMode = PictureBoxSizeMode.CenterImage;
        iconPreview.BorderStyle = BorderStyle.None;
        iconPreview.BackColor = AppColors.Surface;
        iconPreview.Margin = new Padding(3, 3, 10, 3);

        var saveButton = new Button { Text = "Save settings", AutoSize = true };
        saveButton.Click += (_, _) => SaveSettings();
        var resetButton = new Button { Text = "Reset defaults", AutoSize = true };
        resetButton.Click += (_, _) => ResetDefaults();
        var testButton = new Button { Text = "Test notification", AutoSize = true };
        testButton.Click += (_, _) => TestNotification();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 0),
        };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(testButton);
        buttons.Controls.Add(resetButton);

        statusLabel.AutoSize = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.ForeColor = SystemColors.GrayText;

        var heading = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Personalize notifications",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(23, 32, 51),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var description = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Choose how TajsToucher presents Git signing requests.",
            ForeColor = Color.FromArgb(85, 96, 113),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(description, 0, 1);
        layout.SetColumnSpan(description, 2);
        layout.Controls.Add(CreateLabel("Notification title"), 0, 2);
        layout.Controls.Add(titleTextBox, 1, 2);
        layout.Controls.Add(CreateLabel("Notification text"), 0, 3);
        layout.Controls.Add(textTextBox, 1, 3);
        layout.Controls.Add(CreateLabel("Notification icon"), 0, 4);
        layout.Controls.Add(CreateIconEditor(), 1, 4);
        layout.Controls.Add(CreateLabel("Template help"), 0, 5);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Use {Repository} in the title or text. If omitted from the text, the repository is appended automatically.",
            TextAlign = ContentAlignment.MiddleLeft,
        }, 1, 5);
        layout.Controls.Add(statusLabel, 0, 6);
        layout.SetColumnSpan(statusLabel, 2);
        layout.Controls.Add(buttons, 0, 7);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        LoadSettings(configurationStore.LoadNotificationSettings());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var image = iconPreview.Image;
            iconPreview.Image = null;
            image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private Control CreateIconEditor()
    {
        var browseButton = new Button { Text = "Browse...", AutoSize = true };
        browseButton.Click += (_, _) => BrowseForIcon();
        var clearButton = new Button { Text = "Default", AutoSize = true };
        clearButton.Click += (_, _) =>
        {
            iconPathTextBox.Clear();
            UpdateIconPreview();
        };

        iconPathTextBox.Dock = DockStyle.Fill;
        iconPathTextBox.Margin = new Padding(0, 14, 6, 0);
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        editor.Controls.Add(iconPreview, 0, 0);
        editor.Controls.Add(iconPathTextBox, 1, 0);
        editor.Controls.Add(browseButton, 2, 0);
        editor.Controls.Add(clearButton, 3, 0);
        return editor;
    }

    private void LoadSettings(NotificationSettings settings)
    {
        titleTextBox.Text = settings.Title;
        textTextBox.Text = settings.Text;
        iconPathTextBox.Text = settings.IconPath;
        UpdateIconPreview();
    }

    private void ResetDefaults()
    {
        LoadSettings(NotificationSettings.Defaults);
        statusLabel.Text = "Defaults restored. Click Save to keep them.";
    }

    private void BrowseForIcon()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a notification icon",
            Filter = "Icon files (*.ico)|*.ico|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            iconPathTextBox.Text = dialog.FileName;
            UpdateIconPreview();
        }
    }

    private void UpdateIconPreview()
    {
        var oldImage = iconPreview.Image;
        iconPreview.Image = null;
        oldImage?.Dispose();

        using var icon = NotificationIconLoader.TryLoad(iconPathTextBox.Text.Trim());
        iconPreview.Image = icon?.ToBitmap();
    }

    private bool TryReadSettings(out NotificationSettings settings)
    {
        settings = new NotificationSettings(
            titleTextBox.Text,
            textTextBox.Text,
            iconPathTextBox.Text);

        if (string.IsNullOrWhiteSpace(settings.Title))
        {
            ShowValidationError("Notification title cannot be empty.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Text))
        {
            ShowValidationError("Notification text cannot be empty.");
            return false;
        }

        settings = settings.Normalize();
        if (!string.IsNullOrWhiteSpace(settings.IconPath))
        {
            using var icon = NotificationIconLoader.TryLoad(settings.IconPath);
            if (icon is null)
            {
                ShowValidationError("The selected icon could not be loaded. Choose a valid .ico file or use Default.");
                return false;
            }
        }

        return true;
    }

    private bool TrySave(out NotificationSettings settings)
    {
        if (!TryReadSettings(out settings))
        {
            return false;
        }

        try
        {
            configurationStore.SaveNotificationSettings(settings);
            statusLabel.Text = "Settings saved.";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            ShowValidationError($"Settings could not be saved: {exception.Message}");
            return false;
        }
    }

    private void SaveSettings()
    {
        TrySave(out _);
    }

    private void TestNotification()
    {
        if (!TrySave(out _))
        {
            return;
        }

        statusLabel.Text = NotificationService.TryLaunch("TajsToucher")
            ? "Test notification launched."
            : "The notification helper could not be started.";
    }

    private void ShowValidationError(string message)
    {
        statusLabel.Text = message;
        MessageBox.Show(this, message, "TajsToucher Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

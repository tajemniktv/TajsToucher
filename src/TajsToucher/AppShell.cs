namespace TajsToucher;

internal enum AppPage
{
    Home,
    Settings,
    EnabledFor,
}

internal static class AppShellLauncher
{
    public static int Show(AppPage initialPage = AppPage.Home)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new AppShellForm(initialPage);
        Application.Run(form);
        return 0;
    }
}

internal sealed class AppShellForm : Form
{
    private readonly Panel contentPanel = new();
    private readonly NotifyIcon trayIcon = new()
    {
        Icon = SystemIcons.Application,
        Text = "TajsToucher",
        Visible = true,
    };
    private readonly ContextMenuStrip trayMenu = new();
    private readonly Button homeButton;
    private readonly Button settingsButton;
    private readonly Button enabledForButton;
    private readonly HomePageControl homePage = new();
    private readonly SettingsPageControl settingsPage = new();
    private readonly EnabledForPageControl enabledForPage = new();
    private bool exitRequested;

    public AppShellForm(AppPage initialPage)
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        Text = "TajsToucher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 560);
        ClientSize = new Size(980, 650);
        BackColor = Color.Transparent;

        homeButton = CreateNavigationButton("Home");
        settingsButton = CreateNavigationButton("Settings");
        enabledForButton = CreateNavigationButton("Enabled for");
        homeButton.Click += (_, _) => Navigate(AppPage.Home);
        settingsButton.Click += (_, _) => Navigate(AppPage.Settings);
        enabledForButton.Click += (_, _) => Navigate(AppPage.EnabledFor);

        homePage.OpenSettingsRequested += (_, _) => Navigate(AppPage.Settings);
        homePage.OpenEnabledForRequested += (_, _) => Navigate(AppPage.EnabledFor);
        settingsPage.SettingsSaved += (_, _) => homePage.RefreshStatus();
        ConfigureTrayIcon();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSidebar(), 0, 0);
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.BackColor = Color.Transparent;
        root.Controls.Add(contentPanel, 1, 0);
        Controls.Add(root);

        Navigate(initialPage);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowComposition.Apply(Handle);
    }

    private void ConfigureTrayIcon()
    {
        var openDashboard = new ToolStripMenuItem("Open dashboard");
        openDashboard.Click += (_, _) => ShowFromTray(AppPage.Home);
        var openSettings = new ToolStripMenuItem("Settings");
        openSettings.Click += (_, _) => ShowFromTray(AppPage.Settings);
        var openEnabledFor = new ToolStripMenuItem("Enabled for");
        openEnabledFor.Click += (_, _) => ShowFromTray(AppPage.EnabledFor);
        var exit = new ToolStripMenuItem("Exit TajsToucher");
        exit.Click += (_, _) =>
        {
            exitRequested = true;
            Close();
        };

        trayMenu.Items.Add(openDashboard);
        trayMenu.Items.Add(openSettings);
        trayMenu.Items.Add(openEnabledFor);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(exit);
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.DoubleClick += (_, _) => ShowFromTray(AppPage.Home);
    }

    private void ShowFromTray(AppPage page)
    {
        Navigate(page);
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HideToTray()
    {
        Hide();
        trayIcon.ShowBalloonTip(
            1500,
            "TajsToucher",
            "TajsToucher is still running in the system tray.",
            ToolTipIcon.Info);
    }

    private Control CreateSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.Sidebar,
            Padding = new Padding(18, 22, 18, 18),
        };

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            AutoSize = false,
            Text = "Local-first  •  Windows",
            ForeColor = Color.FromArgb(156, 168, 190),
            TextAlign = ContentAlignment.BottomLeft,
        };

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 170,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = AppColors.Sidebar,
        };
        navigation.Controls.Add(homeButton);
        navigation.Controls.Add(settingsButton);
        navigation.Controls.Add(enabledForButton);

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 95,
            BackColor = AppColors.Sidebar,
        };
        brand.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            AutoSize = false,
            Text = "TajsToucher",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        });
        brand.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            AutoSize = false,
            Text = "Your signing companion",
            ForeColor = Color.FromArgb(174, 185, 204),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        sidebar.Controls.Add(footer);
        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(brand);
        return sidebar;
    }

    private static Button CreateNavigationButton(string text)
    {
        return new Button
        {
            Width = 184,
            Height = 44,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = AppColors.Sidebar,
            ForeColor = Color.FromArgb(196, 205, 219),
            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 6),
            UseVisualStyleBackColor = false,
        };
    }

    private void Navigate(AppPage page)
    {
        var activeButton = page switch
        {
            AppPage.Home => homeButton,
            AppPage.Settings => settingsButton,
            AppPage.EnabledFor => enabledForButton,
            _ => homeButton,
        };

        foreach (var button in new[] { homeButton, settingsButton, enabledForButton })
        {
            button.BackColor = button == activeButton ? AppColors.SidebarActive : AppColors.Sidebar;
            button.ForeColor = button == activeButton ? Color.White : Color.FromArgb(196, 205, 219);
        }

        contentPanel.Controls.Clear();
        Control pageControl = page switch
        {
            AppPage.Home => homePage,
            AppPage.Settings => settingsPage,
            AppPage.EnabledFor => enabledForPage,
            _ => homePage,
        };
        pageControl.Dock = DockStyle.Fill;
        contentPanel.Controls.Add(pageControl);
        if (page == AppPage.Home)
        {
            homePage.RefreshStatus();
        }
    }
}

internal sealed class HomePageControl : UserControl
{
    private readonly Label statusDot = new();
    private readonly Label statusTitle = new();
    private readonly Label statusDescription = new();
    private readonly Label signingValue = new();
    private readonly Label signingDetail = new();
    private readonly Label notificationValue = new();
    private readonly Label gpgValue = new();
    private readonly Label actionStatus = new();

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? OpenEnabledForRequested;

    public HomePageControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
        Padding = new Padding(26, 24, 26, 24);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            AutoSize = false,
            Text = "Your signing setup at a glance",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = AppColors.Ink,
        });
        heading.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            AutoSize = false,
            Text = "A quick view of your local signing setup.",
            ForeColor = AppColors.Muted,
        });

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(CreateStatusCard(), 0, 1);
        layout.Controls.Add(CreateMetricCards(), 0, 2);
        layout.Controls.Add(CreateActions(), 0, 3);
        layout.Controls.Add(new Panel(), 0, 4);
        Controls.Add(layout);
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        AppStatus status;
        try
        {
            status = AppStatus.Load();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            status = new AppStatus(false, false, false, null);
        }

        statusDot.ForeColor = status.IsReady ? AppColors.Success : AppColors.Warning;
        statusTitle.Text = status.IsReady ? "Ready for Git signing" : "Needs setup";
        statusDescription.Text = status.IsReady
            ? "Git is connected and TajsToucher can reach your real GnuPG installation."
            : "Run the install command to connect Git, then come back here to check the status.";

        signingValue.Text = status.IsReady ? "Enabled" : "Not enabled";
        signingValue.ForeColor = status.IsReady ? AppColors.Success : AppColors.Warning;
        signingDetail.Text = status.GitConfigurationMatches ? "Git is using TajsToucher" : "Git is not connected yet";
        notificationValue.Text = "Ready";
        notificationValue.ForeColor = AppColors.Success;
        gpgValue.Text = status.RealGpgAvailable ? "Available" : "Unavailable";
        gpgValue.ForeColor = status.RealGpgAvailable ? AppColors.Success : AppColors.Warning;
    }

    private Control CreateStatusCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.Surface,
            Padding = new Padding(18),
            BorderStyle = BorderStyle.None,
        };

        statusDot.Text = "●";
        statusDot.Font = new Font("Segoe UI", 19F, FontStyle.Regular);
        statusDot.Dock = DockStyle.Fill;
        statusDot.TextAlign = ContentAlignment.MiddleCenter;

        statusTitle.AutoSize = false;
        statusTitle.Dock = DockStyle.Top;
        statusTitle.Height = 34;
        statusTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        statusTitle.ForeColor = AppColors.Ink;

        statusDescription.AutoSize = false;
        statusDescription.Dock = DockStyle.Fill;
        statusDescription.ForeColor = AppColors.Muted;

        var copy = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 8, 0) };
        copy.Controls.Add(statusDescription);
        copy.Controls.Add(statusTitle);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        details.Controls.Add(statusDot, 0, 0);
        details.Controls.Add(copy, 1, 0);

        var enabledButton = CreateSecondaryButton("See what’s enabled");
        enabledButton.Click += (_, _) => OpenEnabledForRequested?.Invoke(this, EventArgs.Empty);
        details.Controls.Add(enabledButton, 2, 0);
        card.Controls.Add(details);
        return card;
    }

    private Control CreateMetricCards()
    {
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cards.Controls.Add(CreateMetricCard("Git / OpenPGP signing", signingValue, signingDetail), 0, 0);
        cards.Controls.Add(CreateMetricCard("Notifications", notificationValue, "Personalized locally"), 1, 0);
        cards.Controls.Add(CreateMetricCard("Real GnuPG", gpgValue, "The underlying signer"), 2, 0);
        return cards;
    }

    private static Control CreateMetricCard(string title, Label value, string detail)
    {
        var detailLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = detail,
            ForeColor = AppColors.Muted,
        };
        return CreateMetricCard(title, value, detailLabel);
    }

    private static Control CreateMetricCard(string title, Label value, Label detailLabel)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.Surface,
            Padding = new Padding(14, 10, 14, 8),
            Margin = new Padding(0, 0, 8, 0),
            BorderStyle = BorderStyle.None,
        };
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            AutoSize = false,
            Text = title,
            ForeColor = AppColors.Muted,
        });
        value.Dock = DockStyle.Top;
        value.Height = 29;
        value.AutoSize = false;
        value.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        card.Controls.Add(value);
        card.Controls.Add(detailLabel);
        return card;
    }

    private Control CreateActions()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
        };
        var settingsButton = CreatePrimaryButton("Personalize notifications");
        settingsButton.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        var testButton = CreateSecondaryButton("Test notification");
        testButton.Click += (_, _) =>
        {
            actionStatus.Text = NotificationService.TryLaunch("TajsToucher")
                ? "Test notification launched."
                : "The notification helper could not be started.";
        };
        actionStatus.AutoSize = true;
        actionStatus.ForeColor = AppColors.Muted;
        actionStatus.Padding = new Padding(8, 7, 0, 0);
        actions.Controls.Add(settingsButton);
        actions.Controls.Add(testButton);
        actions.Controls.Add(actionStatus);
        return actions;
    }

    private static Button CreatePrimaryButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = AppColors.Accent,
            ForeColor = Color.White,
            Padding = new Padding(12, 0, 12, 0),
            UseVisualStyleBackColor = false,
        };
    }

    private static Button CreateSecondaryButton(string text)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            BackColor = AppColors.Surface,
            ForeColor = AppColors.Ink,
            Padding = new Padding(10, 0, 10, 0),
            UseVisualStyleBackColor = false,
        };
    }
}

internal sealed class EnabledForPageControl : UserControl
{
    public EnabledForPageControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;
        Padding = new Padding(26, 24, 26, 24);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            AutoSize = false,
            Text = "Enabled for",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = AppColors.Ink,
        });
        heading.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            AutoSize = false,
            Text = "The adapters currently connected to TajsToucher.",
            ForeColor = AppColors.Muted,
        });
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(CreateFeatureCard("Git / OpenPGP signing", "Enabled", "Shows a notification when Git asks GnuPG to create a signature. Verification operations stay quiet."), 0, 1);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Coming later",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = AppColors.Muted,
            TextAlign = ContentAlignment.BottomLeft,
        }, 0, 2);
        layout.Controls.Add(CreateFutureCards(), 0, 3);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 30,
            Text = "This screen is ready to grow as more signing adapters are added.",
            ForeColor = AppColors.Muted,
            Padding = new Padding(0, 14, 0, 0),
        }, 0, 4);
        Controls.Add(layout);
    }

    private static Control CreateFutureCards()
    {
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        cards.Controls.Add(CreateFeatureCard("OpenPGP operations", "Not enabled", "More key-management actions"), 0, 0);
        cards.Controls.Add(CreateFeatureCard("SSH / FIDO", "Not enabled", "Hardware-backed SSH flows"), 1, 0);
        cards.Controls.Add(CreateFeatureCard("PIV / OATH", "Not enabled", "Additional YubiKey services"), 2, 0);
        return cards;
    }

    private static Control CreateFeatureCard(string title, string status, string description)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppColors.Surface,
            Padding = new Padding(16, 12, 16, 10),
            Margin = new Padding(0, 0, 8, 0),
            BorderStyle = BorderStyle.None,
        };
        var statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            AutoSize = false,
            Text = $"●  {status}",
            ForeColor = status == "Enabled" ? AppColors.Success : AppColors.Disabled,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        };
        card.Controls.Add(statusLabel);
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            AutoSize = false,
            Text = title,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = status == "Enabled" ? AppColors.Ink : AppColors.Disabled,
        });
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = description,
            ForeColor = AppColors.Muted,
        });
        return card;
    }
}

internal static class AppColors
{
    // Alpha-backed colors leave the DWM/Windhawk material visible instead of
    // replacing it with opaque WinForms fills.
    public static readonly Color Background = Color.Transparent;
    public static readonly Color Surface = Color.FromArgb(180, 255, 255, 255);
    public static readonly Color Sidebar = Color.FromArgb(165, 23, 32, 51);
    public static readonly Color SidebarActive = Color.FromArgb(90, 255, 255, 255);
    public static readonly Color Ink = Color.FromArgb(23, 32, 51);
    public static readonly Color Muted = Color.FromArgb(85, 96, 113);
    public static readonly Color Accent = Color.FromArgb(59, 130, 246);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Warning = Color.FromArgb(202, 138, 4);
    public static readonly Color Disabled = Color.FromArgb(148, 163, 184);
}

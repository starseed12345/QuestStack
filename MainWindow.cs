using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace QuestStack;

internal enum UnlockMode
{
    Full,
    FastbootOnly
}

internal sealed class MainWindow : Window
{
    private readonly Dictionary<string, StageVisual> _stageVisuals = new(StringComparer.Ordinal);
    private readonly StackPanel _stagePanel = new() { Spacing = 6 };
    private readonly StackPanel _logPanel = new() { Spacing = 5 };
    private readonly ScrollViewer _logScroll = new();
    private readonly StackPanel _backupPanel = new() { Spacing = 10 };
    private readonly TextBlock _connectionText = Ui.Text("Checking ADB", 12, Ui.SecondaryText, FontWeight.SemiBold);
    private readonly TextBlock _deviceName = Ui.Text("No headset detected", 19, Ui.PrimaryText, FontWeight.SemiBold);
    private readonly TextBlock _deviceSerial = Ui.Text("Serial  Not available", 13, Ui.SecondaryText);
    private readonly TextBlock _deviceBuild = Ui.Text("Build  Not available", 13, Ui.SecondaryText);
    private readonly TextBlock _deviceSlots = Ui.Text("Slots  Not available", 13, Ui.SecondaryText);
    private readonly TextBlock _deviceFingerprint = Ui.Text("Fingerprint unavailable", 12, Ui.SecondaryText);
    private readonly TextBlock _supportText = Ui.Text("Connect the headset and allow USB debugging.", 13, Ui.SecondaryText);
    private readonly TextBlock _modeHelp = Ui.Text(string.Empty, 12, Ui.SecondaryText);
    private readonly TextBlock _actionText = Ui.Text(string.Empty, 13, Ui.SecondaryText);
    private readonly TextBlock _backupCount = Ui.Text("0 backups", 13, Ui.SecondaryText);
    private readonly Button _startButton = Ui.PrimaryButton("Start");
    private readonly Button _refreshButton = Ui.SecondaryButton("Check ADB");
    private readonly Button _refreshBackupsButton = Ui.SecondaryButton("Refresh");
    private readonly ProgressBar _progress = new() { Minimum = 0, Height = 3 };
    private Border _connectionBadge = null!;
    private Border _deviceCard = null!;

    private DeviceIdentity? _device;
    private UnlockMode _mode = UnlockMode.Full;
    private bool _running;

    public MainWindow()
    {
        Title = "QuestStack";
        Width = 1060;
        Height = 780;
        MinWidth = 900;
        MinHeight = 640;
        Background = Ui.Background;
        Content = BuildLayout();

        _startButton.Click += async (_, _) => await ConfirmAndRunAsync();
        _refreshButton.Click += async (_, _) => await RefreshDeviceAsync();
        _refreshBackupsButton.Click += async (_, _) => await RefreshBackupsAsync();

        SetMode(UnlockMode.Full);
        Logger.EntryWritten += OnLogEntry;
        Opened += async (_, _) =>
        {
            await RefreshDeviceAsync();
            await RefreshBackupsAsync();
        };
        Closed += (_, _) => Logger.EntryWritten -= OnLogEntry;
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("64,*") };
        root.Children.Add(BuildHeader());

        var tabs = new TabControl
        {
            Margin = new Thickness(22, 10, 22, 22),
            Background = Ui.Background,
            ItemsSource = new[]
            {
                new TabItem { Header = "Unlock", Content = BuildUnlockPage() },
                new TabItem { Header = "Backups", Content = BuildBackupsPage() }
            }
        };
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        return root;
    }

    private Control BuildHeader()
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(24, 0)
        };

        var name = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        name.Children.Add(Ui.Text("QuestStack", 18, Ui.PrimaryText, FontWeight.SemiBold));
        name.Children.Add(Ui.Text("3.0.4", 12, Ui.SecondaryText));
        grid.Children.Add(name);

        _connectionBadge = new Border
        {
            Background = Ui.Raised,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(11, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _connectionText
        };
        Grid.SetColumn(_connectionBadge, 1);
        grid.Children.Add(_connectionBadge);

        return new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private Control BuildUnlockPage()
    {
        var columns = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,340"),
            ColumnSpacing = 18
        };

        var left = new StackPanel { Spacing = 13 };
        left.Children.Add(Ui.Text("Quest 1 bootloader unlock", 25, Ui.PrimaryText, FontWeight.SemiBold));
        left.Children.Add(Ui.Text("Connect the headset and choose a mode.", 14, Ui.SecondaryText));
        _progress.Foreground = Ui.Accent;
        _progress.Background = Ui.Raised;
        left.Children.Add(_progress);
        left.Children.Add(_stagePanel);
        left.Children.Add(BuildLogPanel());

        columns.Children.Add(new ScrollViewer
        {
            Content = left,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });

        var right = new StackPanel { Spacing = 12 };
        right.Children.Add(BuildModePanel());
        _deviceCard = BuildDevicePanel();
        right.Children.Add(_deviceCard);
        right.Children.Add(BuildRunPanel());

        var rightScroll = new ScrollViewer
        {
            Content = right,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetColumn(rightScroll, 1);
        columns.Children.Add(rightScroll);
        return columns;
    }

    private Control BuildModePanel()
    {
        var full = new RadioButton
        {
            Content = "Full process",
            GroupName = "UnlockMode",
            IsChecked = true,
            Foreground = Ui.PrimaryText,
            FontWeight = FontWeight.SemiBold
        };
        var fastbootOnly = new RadioButton
        {
            Content = "Unlock only",
            GroupName = "UnlockMode",
            Foreground = Ui.PrimaryText,
            FontWeight = FontWeight.SemiBold
        };

        full.PropertyChanged += (_, args) =>
        {
            if (args.Property == RadioButton.IsCheckedProperty && full.IsChecked == true)
                SetMode(UnlockMode.Full);
        };
        fastbootOnly.PropertyChanged += (_, args) =>
        {
            if (args.Property == RadioButton.IsCheckedProperty && fastbootOnly.IsChecked == true)
                SetMode(UnlockMode.FastbootOnly);
        };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Ui.Text("Mode", 12, Ui.SecondaryText, FontWeight.SemiBold));
        stack.Children.Add(full);
        stack.Children.Add(fastbootOnly);
        stack.Children.Add(_modeHelp);
        return Ui.Card(stack, new Thickness(16));
    }

    private Border BuildDevicePanel()
    {
        _deviceFingerprint.TextTrimming = TextTrimming.CharacterEllipsis;
        _deviceFingerprint.MaxLines = 3;

        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(Ui.Text("ADB device", 12, Ui.SecondaryText, FontWeight.SemiBold));
        stack.Children.Add(_deviceName);
        stack.Children.Add(_deviceSerial);
        stack.Children.Add(_deviceBuild);
        stack.Children.Add(_deviceSlots);
        stack.Children.Add(new Border { Height = 1, Background = Ui.Border, Margin = new Thickness(0, 2) });
        stack.Children.Add(_deviceFingerprint);
        stack.Children.Add(_supportText);
        stack.Children.Add(_refreshButton);
        return Ui.Card(stack, new Thickness(16));
    }

    private Control BuildRunPanel()
    {
        _startButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(_actionText);
        stack.Children.Add(_startButton);
        return Ui.Card(stack, new Thickness(16));
    }

    private Control BuildStage(WorkflowStageDefinition stage, int number)
    {
        var marker = Ui.Text(number.ToString(), 13, Ui.SecondaryText, FontWeight.SemiBold);
        marker.Width = 26;
        marker.HorizontalAlignment = HorizontalAlignment.Center;

        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("34,*") };
        grid.Children.Add(marker);
        var title = Ui.Text(stage.Title, 14, Ui.PrimaryText, FontWeight.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var row = new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 9),
            Child = grid
        };
        _stageVisuals[stage.Id] = new StageVisual(row, marker, number.ToString());
        return row;
    }

    private Control BuildLogPanel()
    {
        _logScroll.Content = _logPanel;
        _logScroll.Height = 170;
        _logScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(Ui.Text("Log", 13, Ui.SecondaryText, FontWeight.SemiBold));
        stack.Children.Add(_logScroll);
        return Ui.Card(stack, new Thickness(14));
    }

    private Control BuildBackupsPage()
    {
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        var copy = new StackPanel { Spacing = 4 };
        copy.Children.Add(Ui.Text("Backups", 25, Ui.PrimaryText, FontWeight.SemiBold));
        copy.Children.Add(Ui.Text("Inactive-slot backups stored on this computer.", 14, Ui.SecondaryText));
        copy.Children.Add(_backupCount);
        header.Children.Add(copy);
        Grid.SetColumn(_refreshBackupsButton, 1);
        _refreshBackupsButton.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(_refreshBackupsButton);

        var path = Ui.Text(BackupStore.RootDirectory, 12, Ui.SecondaryText);
        path.TextTrimming = TextTrimming.CharacterEllipsis;

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(header);
        content.Children.Add(Ui.Card(path, new Thickness(13)));
        content.Children.Add(_backupPanel);
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    private void SetMode(UnlockMode mode)
    {
        if (_running)
            return;

        _mode = mode;
        _stageVisuals.Clear();
        _stagePanel.Children.Clear();
        IReadOnlyList<WorkflowStageDefinition> stages =
            mode == UnlockMode.Full ? QuestWorkflow.Stages : UnlockOnlyWorkflow.Stages;
        for (int index = 0; index < stages.Count; index++)
            _stagePanel.Children.Add(BuildStage(stages[index], index + 1));

        _progress.Maximum = stages.Count;
        _progress.Value = 0;
        _deviceCard.IsVisible = mode == UnlockMode.Full;

        if (mode == UnlockMode.Full)
        {
            _modeHelp.Text = "Starts from Android and handles the backup, downgrade, unlock, reset, and reboot.";
            _actionText.Text = "This mode erases user data.";
            _startButton.Content = "Start";
            RenderDevice(_device);
        }
        else
        {
            _modeHelp.Text = "For a headset already running the vulnerable ABL in fastboot.";
            _actionText.Text = "No partitions are flashed. The slot is not changed and the headset is not wiped or rebooted.";
            _startButton.Content = "Unlock bootloader";
            _connectionText.Text = "Unlock only";
            _connectionText.Foreground = Ui.SecondaryText;
            _connectionBadge.BorderBrush = Ui.Border;
            _startButton.IsEnabled = true;
        }
    }

    private async Task RefreshDeviceAsync()
    {
        if (_running)
            return;

        _refreshButton.IsEnabled = false;
        if (_mode == UnlockMode.Full)
            _connectionText.Text = "Checking ADB";
        try
        {
            _device = await Task.Run(DeviceProbe.Read);
            if (_mode == UnlockMode.Full)
                RenderDevice(_device);
        }
        catch (Exception ex)
        {
            _device = null;
            if (_mode == UnlockMode.Full)
                RenderDevice(null);
            Logger.Error($"Device check failed: {ex.Message}");
        }
        finally
        {
            _refreshButton.IsEnabled = true;
        }
    }

    private void RenderDevice(DeviceIdentity? device)
    {
        if (_mode != UnlockMode.Full)
            return;

        if (device == null)
        {
            _connectionText.Text = "No ADB device";
            _connectionText.Foreground = Ui.SecondaryText;
            _connectionBadge.BorderBrush = Ui.Border;
            _deviceName.Text = "No headset detected";
            _deviceSerial.Text = "Serial  Not available";
            _deviceBuild.Text = "Build  Not available";
            _deviceSlots.Text = "Slots  Not available";
            _deviceFingerprint.Text = "Fingerprint unavailable";
            _supportText.Text = "Connect the headset and allow USB debugging.";
            _supportText.Foreground = Ui.SecondaryText;
            _startButton.IsEnabled = false;
            return;
        }

        _deviceName.Text = device.Model;
        _deviceSerial.Text = $"Serial  {device.Serial}";
        _deviceBuild.Text = $"Build  {device.Build}";
        _deviceSlots.Text = $"Active  {device.ActiveSuffix}     Target  {device.InactiveSuffix}";
        _deviceFingerprint.Text = device.Fingerprint;

        if (device.IsSupportedQuest1)
        {
            _connectionText.Text = "Quest 1 connected";
            _connectionText.Foreground = Ui.Success;
            _connectionBadge.BorderBrush = Ui.Success;
            _supportText.Text = "Ready";
            _supportText.Foreground = Ui.Success;
            _startButton.IsEnabled = !_running;
        }
        else
        {
            _connectionText.Text = "Unsupported";
            _connectionText.Foreground = Ui.Danger;
            _connectionBadge.BorderBrush = Ui.Danger;
            _supportText.Text = $"Quest 1 build {Firmware.RequiredBuild} is required.";
            _supportText.Foreground = Ui.Danger;
            _startButton.IsEnabled = false;
        }
    }

    private async Task ConfirmAndRunAsync()
    {
        if (_running)
            return;

        bool confirmed;
        if (_mode == UnlockMode.Full)
        {
            if (_device is not { IsSupportedQuest1: true } device)
                return;
            confirmed = await new DestructiveActionDialog(device).ShowDialog<bool>(this);
        }
        else
        {
            confirmed = await new UnlockOnlyDialog().ShowDialog<bool>(this);
        }

        if (confirmed)
            await RunWorkflowAsync();
    }

    private async Task RunWorkflowAsync()
    {
        UnlockMode runningMode = _mode;
        _running = true;
        _startButton.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        ResetStages();
        _logPanel.Children.Clear();
        SetConnection("Running", Ui.Accent);

        WorkflowResult result;
        if (runningMode == UnlockMode.Full)
        {
            var workflow = new QuestWorkflow();
            workflow.StageChanged += OnStageChanged;
            result = await Task.Run(() => workflow.Run());
            workflow.StageChanged -= OnStageChanged;
        }
        else
        {
            var workflow = new UnlockOnlyWorkflow();
            workflow.StageChanged += OnStageChanged;
            result = await Task.Run(() => workflow.Run());
            workflow.StageChanged -= OnStageChanged;
        }

        _running = false;
        _refreshButton.IsEnabled = true;
        if (result.Success)
        {
            SetConnection("Unlocked", Ui.Success);
            _startButton.IsEnabled = runningMode == UnlockMode.FastbootOnly;
        }
        else
        {
            SetConnection("Stopped", Ui.Danger);
            _startButton.IsEnabled = runningMode == UnlockMode.FastbootOnly ||
                                     _device?.IsSupportedQuest1 == true;
        }

        if (runningMode == UnlockMode.Full)
            await RefreshBackupsAsync();
    }

    private void SetConnection(string text, IBrush color)
    {
        _connectionText.Text = text;
        _connectionText.Foreground = color;
        _connectionBadge.BorderBrush = color;
    }

    private async Task RefreshBackupsAsync()
    {
        _refreshBackupsButton.IsEnabled = false;
        try
        {
            IReadOnlyList<BackupManifest> backups = await Task.Run(BackupStore.LoadVerifiedBackups);
            _backupPanel.Children.Clear();
            _backupCount.Text = $"{backups.Count} backup{(backups.Count == 1 ? string.Empty : "s")}";

            if (backups.Count == 0)
            {
                _backupPanel.Children.Add(Ui.Card(Ui.Text("No backups yet.", 14, Ui.SecondaryText)));
            }
            else
            {
                foreach (BackupManifest backup in backups)
                    _backupPanel.Children.Add(BuildBackupCard(backup));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not read backups: {ex.Message}");
        }
        finally
        {
            _refreshBackupsButton.IsEnabled = true;
        }
    }

    private Control BuildBackupCard(BackupManifest backup)
    {
        var title = Ui.Text(
            $"{backup.Serial}  •  {backup.SlotSuffix}",
            15,
            Ui.PrimaryText,
            FontWeight.SemiBold);
        var fingerprint = Ui.Text(backup.Fingerprint, 12, Ui.SecondaryText);
        fingerprint.TextTrimming = TextTrimming.CharacterEllipsis;
        fingerprint.MaxLines = 2;

        var open = Ui.SecondaryButton("Open folder");
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += (_, _) => OpenDirectory(backup.DirectoryPath);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(title);
        stack.Children.Add(Ui.Text(
            $"{backup.CreatedUtc.ToLocalTime():g}  •  {backup.Partitions.Count} partitions  •  {BackupStore.FormatBytes(backup.TotalBytes)}",
            12,
            Ui.SecondaryText));
        stack.Children.Add(fingerprint);
        stack.Children.Add(open);
        return Ui.Card(stack, new Thickness(15));
    }

    private void OnStageChanged(WorkflowStageUpdate update)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_stageVisuals.TryGetValue(update.Id, out StageVisual? visual))
                return;

            switch (update.State)
            {
                case WorkflowStageState.Running:
                    visual.Marker.Text = "•";
                    visual.Marker.Foreground = Ui.Accent;
                    visual.Row.BorderBrush = Ui.Accent;
                    visual.Row.Background = Ui.Brush("#1B1A16");
                    break;
                case WorkflowStageState.Complete:
                    visual.Marker.Text = "✓";
                    visual.Marker.Foreground = Ui.Success;
                    visual.Row.BorderBrush = Ui.Brush("#285742");
                    visual.Row.Background = Ui.Brush("#111D18");
                    break;
                case WorkflowStageState.Failed:
                    visual.Marker.Text = "×";
                    visual.Marker.Foreground = Ui.Danger;
                    visual.Row.BorderBrush = Ui.Danger;
                    visual.Row.Background = Ui.Brush("#261617");
                    break;
            }

            _progress.Value = _stageVisuals.Values.Count(item => item.Marker.Text == "✓");
        });
    }

    private void OnLogEntry(LogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IBrush color = entry.Level switch
            {
                LogLevel.Success => Ui.Success,
                LogLevel.Warning => Ui.Accent,
                LogLevel.Error => Ui.Danger,
                LogLevel.Step => Ui.Info,
                _ => Ui.SecondaryText
            };
            var line = Ui.Text($"{entry.Timestamp:HH:mm:ss}   {entry.Message}", 12, color);
            line.FontFamily = FontFamily.Parse("Cascadia Mono, Consolas, Menlo, monospace");
            _logPanel.Children.Add(line);
            while (_logPanel.Children.Count > 300)
                _logPanel.Children.RemoveAt(0);
            _logScroll.ScrollToEnd();
        });
    }

    private void ResetStages()
    {
        foreach (StageVisual visual in _stageVisuals.Values)
        {
            visual.Marker.Text = visual.DefaultMarker;
            visual.Marker.Foreground = Ui.SecondaryText;
            visual.Row.BorderBrush = Ui.Border;
            visual.Row.Background = Ui.Panel;
        }
        _progress.Value = 0;
    }

    private static void OpenDirectory(string path)
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
            }
            else
            {
                psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
            }
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not open the backup folder: {ex.Message}");
        }
    }

    private sealed record StageVisual(Border Row, TextBlock Marker, string DefaultMarker);
}

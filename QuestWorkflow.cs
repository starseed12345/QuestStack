namespace QuestStack;

internal enum WorkflowStageState
{
    Pending,
    Running,
    Complete,
    Failed
}

internal sealed record WorkflowStageDefinition(string Id, string Title);
internal sealed record WorkflowStageUpdate(string Id, WorkflowStageState State, string? Detail = null);
internal sealed record WorkflowResult(bool Success, DeviceIdentity? Device, BackupManifest? Backup, string? Error);

internal sealed class QuestWorkflow
{
    public static readonly IReadOnlyList<WorkflowStageDefinition> Stages =
    [
        new("identify", "Check device"),
        new("prepare", "Download files"),
        new("slots", "Read slots"),
        new("root", "Get root"),
        new("backup", "Back up inactive slot"),
        new("downgrade", "Flash v29"),
        new("activate", "Switch slot"),
        new("fastboot", "Enter fastboot"),
        new("unlock", "Unlock bootloader"),
        new("restore", "Restore original slot"),
        new("wipe", "Factory reset"),
        new("boot", "Reboot")
    ];

    public event Action<WorkflowStageUpdate>? StageChanged;

    public WorkflowResult Run(CancellationToken cancellationToken = default)
    {
        DeviceIdentity? device = null;
        BackupManifest? backup = null;
        string? currentStage = null;
        bool vulnerableSlotActive = false;
        bool rebootedToFastboot = false;

        try
        {
            currentStage = "identify";
            Start(currentStage);
            cancellationToken.ThrowIfCancellationRequested();
            device = DeviceProbe.ReadIdentity()
                ?? throw new InvalidOperationException("No authorized ADB headset was detected.");
            Logger.Info($"Connected: {device.Model}  {device.Serial}");
            Logger.Info($"Fingerprint: {device.Fingerprint}");
            if (!device.IsSupportedQuest1)
            {
                throw new InvalidOperationException(
                    $"Unsupported headset or firmware. Expected Quest 1 monterey build {Firmware.RequiredBuild}.");
            }
            Logger.Success("Supported Quest 1 fingerprint verified.");
            Complete(currentStage);

            currentStage = "prepare";
            Start(currentStage);
            cancellationToken.ThrowIfCancellationRequested();
            BundledAdb.ValidateEmbeddedResources();
            var (adbCode, adbVersion) = Adb.Run("version", 15_000);
            if (adbCode != 0)
                throw new InvalidOperationException($"ADB failed to start: {adbVersion}");
            var (fastbootCode, fastbootVersion) = FastbootCli.Run(["--version"], 15_000);
            if (fastbootCode != 0)
                throw new InvalidOperationException($"Fastboot failed to start: {fastbootVersion}");

            string cacheDirectory = GetCacheDirectory();
            string? firmwareDirectory = Firmware.DownloadAndExtract(cacheDirectory);
            if (firmwareDirectory == null)
                throw new InvalidOperationException("The verified firmware bundle could not be prepared.");
            string? ionstackPath = RootExploit.PrepareVerifiedBinary(cacheDirectory);
            if (ionstackPath == null)
                throw new InvalidOperationException("The verified ionstack binary could not be prepared.");
            GetBundlePaths(firmwareDirectory, out string bootctlPath, out string ablPath);
            Logger.Success("Files are ready.");
            Complete(currentStage);

            currentStage = "slots";
            Start(currentStage);
            int originalSlot = DeviceProbe.ReadCurrentSlot();
            if (originalSlot < 0)
                throw new InvalidOperationException("Could not read the active A/B slot from Android.");
            device = device with { ActiveSlot = originalSlot };
            int targetSlot = device.InactiveSlot;
            Logger.Success($"Original slot: {device.ActiveSuffix}. Downgrade target: {device.InactiveSuffix}.");
            Complete(currentStage);

            currentStage = "root";
            Start(currentStage);
            cancellationToken.ThrowIfCancellationRequested();
            if (!RootExploit.IsRooted())
            {
                if (!RootExploit.Push(ionstackPath) || !RootExploit.Run(maxAttempts: 15))
                    throw new InvalidOperationException("Temporary root could not be obtained.");
            }
            if (!RootExploit.IsRooted())
                throw new InvalidOperationException("The ADB shell did not retain uid 0.");
            Logger.Success("Temporary root is active.");
            Complete(currentStage);

            currentStage = "backup";
            Start(currentStage);
            cancellationToken.ThrowIfCancellationRequested();
            backup = BackupStore.CreateVerifiedBackup(device, targetSlot);
            Complete(currentStage, backup.DirectoryPath);

            currentStage = "downgrade";
            Start(currentStage);
            if (!Firmware.PushFiles(firmwareDirectory, bootctlPath))
                throw new InvalidOperationException("Firmware files could not be staged on the headset.");
            int rootedSlot = Firmware.GetCurrentSlot();
            if (rootedSlot != originalSlot)
                throw new InvalidOperationException("The active slot changed unexpectedly before flashing.");
            if (!Firmware.CheckImages(targetSlot) ||
                !Firmware.FlashPartitions(targetSlot) ||
                !Firmware.VerifyPartitions(targetSlot))
            {
                throw new InvalidOperationException("The inactive-slot downgrade did not pass verification.");
            }
            Complete(currentStage);

            currentStage = "activate";
            Start(currentStage);
            if (!Firmware.SwitchSlot(targetSlot))
                throw new InvalidOperationException("The vulnerable slot could not be activated.");
            vulnerableSlotActive = true;
            Complete(currentStage);

            currentStage = "fastboot";
            Start(currentStage);
            if (!Adb.RebootBootloader())
                throw new InvalidOperationException("ADB could not reboot the headset into fastboot.");
            rebootedToFastboot = true;
            Complete(currentStage);

            currentStage = "unlock";
            Start(currentStage);
            if (!BootloaderUnlocker.Unlock(ablPath, fastbootWaitSeconds: 180))
                throw new InvalidOperationException("The bootloader unlock could not be verified.");
            Complete(currentStage);

            currentStage = "restore";
            Start(currentStage);
            if (!FastbootCli.SetActiveSlot(device.Serial, originalSlot))
                throw new InvalidOperationException("The original slot could not be restored.");
            vulnerableSlotActive = false;
            Complete(currentStage);

            currentStage = "wipe";
            Start(currentStage);
            if (!FastbootCli.FactoryReset(device.Serial))
                throw new InvalidOperationException("fastboot -w did not complete.");
            Complete(currentStage);

            currentStage = "boot";
            Start(currentStage);
            if (!FastbootCli.Reboot(device.Serial))
                throw new InvalidOperationException("Fastboot could not reboot the headset.");
            Complete(currentStage);

            Logger.Success("Bootloader unlocked. The headset is rebooting.");
            return new WorkflowResult(true, device, backup, null);
        }
        catch (Exception ex)
        {
            if (currentStage != null)
                Fail(currentStage, ex.Message);
            Logger.Error(ex.Message);

            if (vulnerableSlotActive && device is { ActiveSlot: >= 0 })
            {
                Logger.Warn("Attempting to restore the original slot after the failure.");
                bool restored = !rebootedToFastboot && Adb.IsConnected()
                    ? Firmware.SwitchSlot(device.ActiveSlot)
                    : FastbootCli.SetActiveSlot(device.Serial, device.ActiveSlot);
                if (restored)
                {
                    vulnerableSlotActive = false;
                    Logger.Success("Original slot restored. The headset was left in fastboot.");
                }
                else
                {
                    Logger.Error("Automatic slot recovery failed. Do not select Boot Device.");
                    if (backup != null)
                        Logger.Error($"Verified recovery backup: {backup.DirectoryPath}");
                }
            }

            return new WorkflowResult(false, device, backup, ex.Message);
        }
    }

    private void Start(string id)
    {
        WorkflowStageDefinition stage = Stages.First(item => item.Id == id);
        Logger.Info(stage.Title);
        StageChanged?.Invoke(new WorkflowStageUpdate(id, WorkflowStageState.Running));
    }

    private void Complete(string id, string? detail = null)
    {
        StageChanged?.Invoke(new WorkflowStageUpdate(id, WorkflowStageState.Complete, detail));
    }

    private void Fail(string id, string detail)
    {
        StageChanged?.Invoke(new WorkflowStageUpdate(id, WorkflowStageState.Failed, detail));
    }

    private static string GetCacheDirectory()
    {
        string path = AppDataPaths.CacheDirectory;
        Directory.CreateDirectory(path);
        return path;
    }

    private static void GetBundlePaths(string firmwareDirectory, out string bootctlPath, out string ablPath)
    {
        string imagesPath = Path.Combine(firmwareDirectory, "images");
        bootctlPath = Path.Combine(firmwareDirectory, "bootctl_shim");
        ablPath = Path.Combine(imagesPath, "abl.img");
        if (!Directory.Exists(imagesPath) || !File.Exists(bootctlPath) || !File.Exists(ablPath))
            throw new InvalidDataException("The verified firmware bundle is incomplete.");
    }
}

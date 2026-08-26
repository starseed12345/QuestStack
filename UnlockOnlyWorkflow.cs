namespace QuestStack;

internal sealed class UnlockOnlyWorkflow
{
    public static readonly IReadOnlyList<WorkflowStageDefinition> Stages =
    [
        new("prepare", "Download files"),
        new("fastboot", "Wait for fastboot"),
        new("unlock", "Unlock bootloader")
    ];

    public event Action<WorkflowStageUpdate>? StageChanged;

    public WorkflowResult Run(CancellationToken cancellationToken = default)
    {
        string? currentStage = null;

        try
        {
            currentStage = "prepare";
            Start(currentStage);
            cancellationToken.ThrowIfCancellationRequested();
            BundledAdb.ValidateEmbeddedResources();

            var (fastbootCode, fastbootVersion) = FastbootCli.Run(["--version"], 15_000);
            if (fastbootCode != 0)
                throw new InvalidOperationException($"Fastboot failed to start: {fastbootVersion}");

            string cacheDirectory = AppDataPaths.CacheDirectory;
            Directory.CreateDirectory(cacheDirectory);
            string? firmwareDirectory = Firmware.DownloadAndExtract(cacheDirectory);
            if (firmwareDirectory == null)
                throw new InvalidOperationException("The firmware files could not be prepared.");

            string ablPath = Path.Combine(firmwareDirectory, "images", "abl.img");
            if (!File.Exists(ablPath))
                throw new InvalidDataException("abl.img is missing from the firmware files.");
            Complete(currentStage);

            currentStage = "fastboot";
            Start(currentStage);
            cancellationToken.ThrowIfCancellationRequested();
            if (!BootloaderUnlocker.Unlock(
                    ablPath,
                    fastbootWaitSeconds: 180,
                    fastbootDetected: () =>
                    {
                        Complete("fastboot");
                        currentStage = "unlock";
                        Start(currentStage);
                    }))
            {
                throw new InvalidOperationException("The bootloader unlock could not be verified.");
            }
            Complete(currentStage);

            Logger.Success("Bootloader unlocked.");
            Logger.Info("Install an update or switch back to a bootable slot before rebooting.");
            return new WorkflowResult(true, null, null, null);
        }
        catch (Exception ex)
        {
            if (currentStage != null)
                StageChanged?.Invoke(new WorkflowStageUpdate(currentStage, WorkflowStageState.Failed, ex.Message));
            Logger.Error(ex.Message);
            return new WorkflowResult(false, null, null, ex.Message);
        }
    }

    private void Start(string id)
    {
        WorkflowStageDefinition stage = Stages.First(item => item.Id == id);
        Logger.Info(stage.Title);
        StageChanged?.Invoke(new WorkflowStageUpdate(id, WorkflowStageState.Running));
    }

    private void Complete(string id)
    {
        StageChanged?.Invoke(new WorkflowStageUpdate(id, WorkflowStageState.Complete));
    }
}

namespace QuestStack;

internal static class Program
{
    private const string IonstackUrl = "https://files.catbox.moe/5wccdy";
    private const int FullFlowSteps = 8;

    private enum RunMode
    {
        Full,
        UnlockOnly
    }

    public static int Main(string[] args)
    {
        Console.Title = "QuestStack Quest 1 Root and Bootloader Unlock";
        PrintBanner();

        try
        {
            if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) || arg == "-h"))
            {
                PrintUsage();
                return 0;
            }

            if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
                return SelfTests.Run(ReadOption(args, "--abl")) ? 0 : 1;

            RunMode mode = ResolveMode(args);
            string? suppliedAblPath = ReadOption(args, "--abl");
            string workDirectory = Path.Combine(Path.GetTempPath(), "QuestStack");
            Directory.CreateDirectory(workDirectory);

            int exitCode = mode switch
            {
                RunMode.Full => RunFullFlow(workDirectory),
                RunMode.UnlockOnly => RunUnlockOnly(workDirectory, suppliedAblPath),
                _ => 1
            };

            if (args.Length == 0)
                Logger.Pause(exitCode == 0 ? "Finished." : "Stopped because an operation failed.");

            return exitCode;
        }
        catch (Exception ex)
        {
            Logger.Error($"Unexpected error: {ex.Message}");
            Logger.Info($"Details: {ex}");
            if (args.Length == 0)
                Logger.Pause("QuestStack stopped.");
            return 1;
        }
    }

    private static int RunFullFlow(string workDirectory)
    {
        Logger.Step(1, FullFlowSteps, "Checking the ADB connection...");
        if (!Adb.WaitForDevice())
            return 1;
        Logger.Success("ADB device connected and authorized.");

        Logger.Step(2, FullFlowSteps, "Verifying the starting firmware...");
        if (!Firmware.CheckFirmwareVersion())
            return 1;

        Logger.Step(3, FullFlowSteps, "Gaining root access...");
        if (!EnsureRoot(workDirectory))
            return 1;

        Logger.Step(4, FullFlowSteps, "Preparing the vulnerable v29 firmware bundle...");
        string? firmwareDirectory = Firmware.DownloadAndExtract(workDirectory);
        if (firmwareDirectory == null)
            return 1;

        if (!TryGetBundlePaths(firmwareDirectory, out string imagesPath, out string bootctlPath, out string ablPath))
            return 1;

        Logger.Step(5, FullFlowSteps, "Pushing firmware and tools to the device...");
        if (!Firmware.PushFiles(firmwareDirectory, bootctlPath))
            return 1;

        int currentSlot = Firmware.GetCurrentSlot();
        if (currentSlot < 0)
            return 1;

        int targetSlot = currentSlot == 0 ? 1 : 0;
        Logger.Info($"Current slot: {currentSlot} ({Firmware.SlotSuffix(currentSlot)})");
        Logger.Info($"Target slot:  {targetSlot} ({Firmware.SlotSuffix(targetSlot)})");

        Logger.Step(6, FullFlowSteps, $"Flashing v29 boot firmware to slot {targetSlot}...");
        if (!Firmware.CheckImages(targetSlot))
            return 1;

        string targetSuffix = Firmware.SlotSuffix(targetSlot);
        if (!Logger.Confirm($"Back up and flash the inactive slot {targetSuffix}? This overwrites its boot partitions."))
        {
            Logger.Info("Cancelled by user.");
            return 0;
        }

        if (!Firmware.BackupPartitions(targetSlot))
            return 1;

        if (!Firmware.FlashPartitions(targetSlot))
        {
            Logger.Error("Flashing failed. Do not reboot until the inactive slot has been repaired.");
            return 1;
        }

        if (!Firmware.VerifyPartitions(targetSlot))
        {
            Logger.Error("Verification failed. Do not switch slots or reboot.");
            return 1;
        }

        Logger.Step(7, FullFlowSteps, "Switching to the vulnerable slot...");
        if (!Logger.Confirm($"Set {targetSuffix} active and reboot into the downgraded boot firmware?"))
        {
            Logger.Info("Cancelled by user. The flashed slot was not activated.");
            return 0;
        }

        if (!Firmware.SwitchSlot(targetSlot))
            return 1;

        if (!Firmware.Reboot())
            return 1;

        Logger.Success("The headset is rebooting into the vulnerable slot.");
        Logger.Warn("Let the headset finish booting.");
        Logger.Pause("When the headset is stable and you are ready to enter fastboot manually.");

        Logger.Step(8, FullFlowSteps, "Unlocking the bootloader with CVE-2021-1931...");
        PrintManualFastbootInstructions();
        if (!BootloaderUnlocker.Unlock(ablPath))
        {
            Logger.Error("Bootloader unlock failed or could not be verified.");
            Logger.Info($"The firmware bundle was kept at: {firmwareDirectory}");
            return 1;
        }

        Logger.Success("Bootloader unlock completed and was verified.");
        Logger.Info($"Recovery files and the firmware bundle were kept at: {firmwareDirectory}");
        return 0;
    }

    private static int RunUnlockOnly(string workDirectory, string? suppliedAblPath)
    {
        Logger.Info("Unlock-only mode selected. No ADB, root exploit, partition flash, slot switch, or automatic reboot will run.");

        string? ablPath = suppliedAblPath;
        if (!string.IsNullOrWhiteSpace(ablPath))
        {
            ablPath = Path.GetFullPath(ablPath);
            if (!File.Exists(ablPath))
            {
                Logger.Error($"The supplied ABL image does not exist: {ablPath}");
                return 1;
            }
        }
        else
        {
            string? firmwareDirectory = Firmware.DownloadAndExtract(workDirectory);
            if (firmwareDirectory == null)
                return 1;

            ablPath = Path.Combine(firmwareDirectory, "images", "abl.img");
            if (!File.Exists(ablPath))
            {
                Logger.Error($"The firmware bundle does not contain images/abl.img: {firmwareDirectory}");
                return 1;
            }
        }

        PrintManualFastbootInstructions();
        if (!BootloaderUnlocker.Unlock(ablPath))
            return 1;

        Logger.Success("Bootloader unlock completed and was verified.");
        return 0;
    }

    private static bool EnsureRoot(string workDirectory)
    {
        if (RootExploit.IsRooted())
        {
            Logger.Success("ADB shell already has root.");
            return true;
        }

        if (!Logger.Confirm("Run the ionstack root exploit?"))
        {
            Logger.Info("Cancelled by user.");
            return false;
        }

        string ionstackPath = Path.Combine(workDirectory, "ionstack");
        if (!File.Exists(ionstackPath) && !DownloadIonstack(ionstackPath))
            return false;

        return RootExploit.Push(ionstackPath) && RootExploit.Run(maxAttempts: 15);
    }

    private static bool DownloadIonstack(string destinationPath)
    {
        Logger.Info("Downloading the ionstack exploit...");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("QuestStack/2.0");
            byte[] data = http.GetByteArrayAsync(IonstackUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(destinationPath, data);
            Logger.Success("Ionstack downloaded.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to download ionstack: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetBundlePaths(
        string firmwareDirectory,
        out string imagesPath,
        out string bootctlPath,
        out string ablPath)
    {
        imagesPath = Path.Combine(firmwareDirectory, "images");
        bootctlPath = Path.Combine(firmwareDirectory, "bootctl_shim");
        ablPath = Path.Combine(imagesPath, "abl.img");

        if (!Directory.Exists(imagesPath))
        {
            Logger.Error($"Firmware images directory not found: {imagesPath}");
            return false;
        }

        if (!File.Exists(bootctlPath))
        {
            Logger.Error($"bootctl_shim not found: {bootctlPath}");
            return false;
        }

        if (!File.Exists(ablPath))
        {
            Logger.Error($"abl.img not found: {ablPath}");
            return false;
        }

        return true;
    }

    private static RunMode ResolveMode(string[] args)
    {
        bool full = args.Any(arg => string.Equals(arg, "--full", StringComparison.OrdinalIgnoreCase));
        bool unlockOnly = args.Any(arg => string.Equals(arg, "--unlock-only", StringComparison.OrdinalIgnoreCase));

        if (full && unlockOnly)
            throw new ArgumentException("Choose either --full or --unlock-only, not both.");

        if (full)
            return RunMode.Full;
        if (unlockOnly)
            return RunMode.UnlockOnly;
        if (args.Length > 0)
            throw new ArgumentException("Unknown arguments. Run with --help to see the supported options.");

        Console.WriteLine("Choose a mode:");
        Console.WriteLine("  1. Full downgrade and unlock");
        Console.WriteLine("  2. Unlock only (already using the vulnerable ABL in fastboot)");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Selection [1/2]: ");
            string? choice = Console.ReadLine()?.Trim();
            if (choice == "1") return RunMode.Full;
            if (choice == "2") return RunMode.UnlockOnly;
            Logger.Warn("Please type 1 or 2, then press Enter.");
        }
    }

    private static string? ReadOption(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
                throw new ArgumentException($"{optionName} requires a file path.");

            return args[index + 1];
        }

        return null;
    }

    private static void PrintManualFastbootInstructions()
    {
        Logger.Info("Manual fastboot step:");
        Logger.Info("  1. Power the headset off completely.");
        Logger.Info("  2. Hold Volume Down and Power until the boot menu appears.");
        Logger.Info("  3. Leave the boot menu open and keep USB connected. Do not select Boot Device.");
        Logger.Info("QuestStack will wait for the fastboot USB interface. It will not reboot the headset automatically.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  QuestStack --full");
        Console.WriteLine("  QuestStack --unlock-only [--abl C:\\path\\to\\abl.img]");
        Console.WriteLine("  QuestStack --self-test [--abl C:\\path\\to\\abl.img]");
        Console.WriteLine();
        Console.WriteLine("Without arguments, QuestStack shows an interactive mode menu.");
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("QuestStack");
        Console.ResetColor();
        Console.WriteLine("Quest 1 root, vulnerable ABL downgrade, and verified bootloader unlock");
        Console.WriteLine(new string('-', 72));
        Console.WriteLine();
    }
}

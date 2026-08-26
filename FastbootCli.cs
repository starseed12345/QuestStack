using System.Diagnostics;

namespace QuestStack;

internal static class FastbootCli
{
    public static (int ExitCode, string Output) Run(IEnumerable<string> arguments, int timeoutMs = 30_000)
    {
        ProcessStartInfo psi;
        try
        {
            psi = CreateProcessStartInfo(arguments);
        }
        catch (Exception ex)
        {
            return (-1, $"failed to prepare fastboot: {ex.Message}");
        }

        using Process? process = Process.Start(psi);
        if (process == null)
            return (-1, "failed to start fastboot");

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "fastboot command timed out");
        }

        string output = (ReadTask(stdout) + Environment.NewLine + ReadTask(stderr)).Trim();
        return (process.ExitCode, output);
    }

    internal static ProcessStartInfo CreateProcessStartInfo(IEnumerable<string> arguments)
    {
        string executable = BundledAdb.FastbootExecutablePath;
        string toolsDirectory = BundledAdb.ToolsDirectory;
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = toolsDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string inheritedPath = psi.Environment.TryGetValue("PATH", out string? value) ? value ?? string.Empty : string.Empty;
        psi.Environment["PATH"] = string.IsNullOrWhiteSpace(inheritedPath)
            ? toolsDirectory
            : toolsDirectory + Path.PathSeparator + inheritedPath;
        psi.Environment["MKE2FS_CONFIG"] = Path.Combine(toolsDirectory, "mke2fs.conf");

        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);
        return psi;
    }

    public static bool WaitForDevice(string serial, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var (code, output) = Run(["devices"], 10_000);
            if (code == 0 && output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => line.StartsWith(serial + "\t", StringComparison.OrdinalIgnoreCase) ||
                             line.StartsWith(serial + " ", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    public static bool SetActiveSlot(string serial, int slot)
    {
        if (!WaitForDevice(serial, TimeSpan.FromMinutes(2)))
        {
            Logger.Error($"Fastboot device {serial} was not detected.");
            return false;
        }

        string suffix = Firmware.SlotSuffix(slot).TrimStart('_');
        Logger.Info($"Setting fastboot active slot to {Firmware.SlotSuffix(slot)}...");
        var (setCode, setOutput) = Run(["-s", serial, "set_active", suffix], 30_000);
        if (setCode != 0)
        {
            Logger.Error($"fastboot set_active failed: {setOutput}");
            return false;
        }

        var (slotCode, slotOutput) = Run(["-s", serial, "getvar", "current-slot"], 30_000);
        int activeSlot = slotCode == 0 ? ParseCurrentSlot(slotOutput) : -1;
        if (activeSlot != slot)
        {
            Logger.Error($"Could not verify slot {Firmware.SlotSuffix(slot)} as active: {slotOutput}");
            return false;
        }

        Logger.Success($"Slot {Firmware.SlotSuffix(slot)} is active.");
        return true;
    }

    public static bool FactoryReset(string serial)
    {
        if (!WaitForDevice(serial, TimeSpan.FromMinutes(1)))
        {
            Logger.Error($"Fastboot device {serial} was not detected for the factory reset.");
            return false;
        }

        Logger.Warn("Running fastboot -w. Userdata and cache will be erased.");
        var (wipeCode, wipeOutput) = Run(["-s", serial, "-w"], 600_000);
        if (wipeCode != 0)
        {
            Logger.Error($"fastboot -w failed: {wipeOutput}");
            return false;
        }
        Logger.Success("Factory reset completed with fastboot -w.");
        return true;
    }

    public static bool Reboot(string serial)
    {
        if (!WaitForDevice(serial, TimeSpan.FromMinutes(1)))
        {
            Logger.Error($"Fastboot device {serial} was not detected for reboot.");
            return false;
        }

        Logger.Info("Booting the headset from the original slot...");
        var (rebootCode, rebootOutput) = Run(["-s", serial, "reboot"], 30_000);
        if (rebootCode != 0)
        {
            Logger.Error($"fastboot reboot failed: {rebootOutput}");
            return false;
        }

        Logger.Success("The headset is rebooting into the operating system.");
        return true;
    }

    internal static int ParseCurrentSlot(string output)
    {
        foreach (string rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            int marker = line.IndexOf("current-slot:", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                continue;

            string value = line[(marker + "current-slot:".Length)..].Trim().TrimStart('_');
            if (value.StartsWith('a')) return 0;
            if (value.StartsWith('b')) return 1;
        }

        return -1;
    }

    private static string ReadTask(Task<string> task)
    {
        try { return task.GetAwaiter().GetResult(); }
        catch { return string.Empty; }
    }
}

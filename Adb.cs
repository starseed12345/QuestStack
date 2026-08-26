using System.Diagnostics;

namespace QuestStack;

internal static class Adb
{
    private static readonly object BackgroundProcessLock = new();
    private static readonly List<Process> BackgroundProcesses = new();

    public static (int exitCode, string output) Run(string args, int timeoutMs = 30_000)
    {
        ProcessStartInfo psi;
        try
        {
            psi = CreateProcessStartInfo();
            psi.Arguments = args;
        }
        catch (Exception ex)
        {
            return (-1, $"failed to prepare adb: {ex.Message}");
        }

        return RunProcess(psi, timeoutMs);
    }

    private static (int exitCode, string output) RunArguments(IEnumerable<string> arguments, int timeoutMs)
    {
        ProcessStartInfo psi;
        try
        {
            psi = CreateProcessStartInfo();
            foreach (string argument in arguments)
                psi.ArgumentList.Add(argument);
        }
        catch (Exception ex)
        {
            return (-1, $"failed to prepare adb: {ex.Message}");
        }

        return RunProcess(psi, timeoutMs);
    }

    private static (int exitCode, string output) RunProcess(ProcessStartInfo psi, int timeoutMs)
    {
        Process? startedProcess;
        try
        {
            startedProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return (-1, $"failed to start adb: {ex.Message}");
        }

        using var proc = startedProcess;
        if (proc == null) return (-1, "failed to start adb");

        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(timeoutMs))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { proc.WaitForExit(5_000); } catch { }
            string partial = ReadTask(stdout, 5_000) + ReadTask(stderr, 5_000);
            return (-1, partial.Trim());
        }

        string output = ReadTask(stdout, 5_000) + ReadTask(stderr, 5_000);
        return (proc.ExitCode, output.Trim());
    }

    public static bool IsConnected()
    {
        var (code, output) = Run("devices");
        if (code != 0)
            return false;

        return HasConnectedDevice(output);
    }

    internal static bool HasConnectedDevice(string output)
    {
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] columns = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 2 && string.Equals(columns[1], "device", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool WaitForDevice()
    {
        while (true)
        {
            var (code, output) = Run("devices");
            if (code != 0)
            {
                Logger.Error($"Could not run adb: {output}");
                return false;
            }

            if (HasConnectedDevice(output))
                return true;

            if (output.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                Logger.Warn("ADB is unauthorized. Approve the USB debugging prompt inside the headset.");
            else
                Logger.Warn("No ADB device found. Connect the Quest 1 and enable USB debugging.");

            Thread.Sleep(5_000);
        }
    }

    public static string? GetProp(string prop)
    {
        var (code, output) = Shell($"getprop {prop}");
        if (code != 0) return null;
        var val = output.Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    public static (int exitCode, string output) Shell(string command, int timeoutMs = 30_000)
    {
        return RunArguments(["shell", command], timeoutMs);
    }

    public static string? GetSerial()
    {
        var (code, output) = RunArguments(["get-serialno"], 10_000);
        string value = output.Trim();
        return code == 0 && value.Length > 0 && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
    }

    public static (int exitCode, string output) Push(string localPath, string remotePath, int timeoutMs = 300_000)
    {
        return RunArguments(["push", localPath, remotePath], timeoutMs);
    }

    public static bool ExecOutToFile(
        IEnumerable<string> remoteArguments,
        string destinationPath,
        int timeoutMs,
        out string output)
    {
        output = string.Empty;
        ProcessStartInfo psi;
        try
        {
            psi = CreateProcessStartInfo();
            psi.ArgumentList.Add("exec-out");
            foreach (string argument in remoteArguments)
                psi.ArgumentList.Add(argument);
        }
        catch (Exception ex)
        {
            output = $"failed to prepare adb: {ex.Message}";
            return false;
        }

        Process? startedProcess;
        try
        {
            startedProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            output = $"failed to start adb: {ex.Message}";
            return false;
        }

        using var process = startedProcess;
        if (process == null)
        {
            output = "failed to start adb";
            return false;
        }

        try
        {
            using FileStream destination = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(destination);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                output = "adb exec-out timed out";
                return false;
            }

            copyTask.GetAwaiter().GetResult();
            destination.Flush(flushToDisk: true);
            output = ReadTask(stderrTask, 5_000).Trim();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    public static bool RebootBootloader()
    {
        var (code, output) = RunArguments(["reboot", "bootloader"], 15_000);
        if (code == 0)
            return true;

        Logger.Error($"Could not reboot into fastboot: {output}");
        return false;
    }

    public static (int exitCode, string output) Remount(int timeoutMs = 15_000)
    {
        var (code, output) = Run("remount", timeoutMs);
        Thread.Sleep(1000);
        return (code, output);
    }

    public static bool RunStreamingShell(string command, string[] detectPatterns, int checkIntervalMs = 200, int maxWaitMs = 120_000)
    {
        ProcessStartInfo psi;
        try
        {
            psi = CreateProcessStartInfo(redirectInput: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to prepare adb: {ex.Message}");
            return false;
        }

        psi.ArgumentList.Add("shell");
        psi.ArgumentList.Add(command);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start adb shell: {ex.Message}");
            return false;
        }

        if (proc == null) return false;

        using var foundSignal = new ManualResetEventSlim(false);
        int found = 0;

        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null || Volatile.Read(ref found) != 0) return;
            foreach (var pat in detectPatterns)
            {
                if (e.Data.Contains(pat, StringComparison.Ordinal) && Interlocked.Exchange(ref found, 1) == 0)
                {
                    Logger.Success($"Detected: {e.Data.Trim()}");
                    foundSignal.Set();
                    return;
                }
            }
        };

        proc.ErrorDataReceived += (s, e) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref found) == 0 && sw.ElapsedMilliseconds < maxWaitMs && !proc.HasExited)
            foundSignal.Wait(checkIntervalMs);

        if (Volatile.Read(ref found) != 0)
        {
            Logger.Info("Leaving ionstack running on device (needed for root).");
            lock (BackgroundProcessLock)
                BackgroundProcesses.Add(proc);
            return true;
        }

        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { }

        proc.Dispose();
        return false;
    }

    private static string ReadTask(Task<string> task, int timeoutMs)
    {
        try
        {
            return task.Wait(timeoutMs) && task.IsCompletedSuccessfully ? task.Result : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(bool redirectInput = false)
    {
        string executable = BundledAdb.ExecutablePath;
        return new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}

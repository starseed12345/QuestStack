using System.Security.Cryptography;
using System.Text.Json;

namespace QuestStack;

internal sealed record PartitionBackupRecord(string Name, long Size, string Sha256);

internal sealed record BackupManifest(
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset CreatedUtc,
    string Serial,
    string Model,
    string Fingerprint,
    int Slot,
    string SlotSuffix,
    bool Verified,
    IReadOnlyList<PartitionBackupRecord> Partitions,
    string DirectoryPath)
{
    public long TotalBytes => Partitions.Sum(partition => partition.Size);
}

internal static class BackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string RootDirectory => AppDataPaths.BackupDirectory;

    public static BackupManifest CreateVerifiedBackup(DeviceIdentity device, int slot)
    {
        string serialDirectory = Path.Combine(RootDirectory, Sanitize(device.Serial));
        Directory.CreateDirectory(serialDirectory);

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string folderName = $"{timestamp}-slot-{Firmware.SlotSuffix(slot).TrimStart('_')}";
        string finalDirectory = Path.Combine(serialDirectory, folderName);
        string stagingDirectory = finalDirectory + $".incomplete-{Guid.NewGuid():N}";
        Directory.CreateDirectory(stagingDirectory);

        var partitions = new List<PartitionBackupRecord>();
        try
        {
            Logger.Info($"Saving a verified backup to {finalDirectory}");
            foreach (string partition in Firmware.Partitions)
            {
                string suffix = Firmware.SlotSuffix(slot);
                string blockDevice = $"/dev/block/bootdevice/by-name/{partition}{suffix}";
                string localPath = Path.Combine(stagingDirectory, partition + ".img");
                long expectedSize = ReadBlockDeviceSize(blockDevice);

                Logger.Info($"Backing up {partition}{suffix} to the computer...");
                if (!Adb.ExecOutToFile(
                        ["sh", "-c", BuildPartitionExportCommand(blockDevice)],
                        localPath,
                        600_000,
                        out string exportOutput))
                {
                    throw new IOException($"Could not read {partition}{suffix}: {exportOutput}");
                }

                long size = new FileInfo(localPath).Length;
                if (size != expectedSize)
                {
                    throw new InvalidDataException(
                        $"The backup of {partition}{suffix} has the wrong size. Expected {expectedSize} bytes, got {size}.");
                }

                string localHash = HashFile(localPath);
                var (hashCode, hashOutput) = Adb.Shell(
                    $"sha256sum {blockDevice} 2>/dev/null | cut -c1-64",
                    600_000);
                string deviceHash = hashOutput.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

                if (hashCode != 0 || deviceHash.Length != 64 ||
                    !string.Equals(localHash, deviceHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"SHA-256 verification failed for {partition}{suffix}. Host={localHash}, device={deviceHash}.");
                }

                partitions.Add(new PartitionBackupRecord(partition, size, localHash));
                Logger.Success($"{partition}{suffix} backed up and verified ({FormatBytes(size)}).");
            }

            var manifest = new BackupManifest(
                SchemaVersion: 1,
                AppVersion: "3.0.4",
                CreatedUtc: DateTimeOffset.UtcNow,
                Serial: device.Serial,
                Model: device.Model,
                Fingerprint: device.Fingerprint,
                Slot: slot,
                SlotSuffix: Firmware.SlotSuffix(slot),
                Verified: true,
                Partitions: partitions,
                DirectoryPath: finalDirectory);

            string manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            string manifestPath = Path.Combine(stagingDirectory, "manifest.json");
            File.WriteAllText(manifestPath, manifestJson);
            File.WriteAllText(Path.Combine(stagingDirectory, "manifest.sha256"), HashFile(manifestPath) + Environment.NewLine);

            if (Directory.Exists(finalDirectory))
                throw new IOException($"A backup already exists at {finalDirectory}.");

            Directory.Move(stagingDirectory, finalDirectory);
            Logger.Success($"Persistent backup verified: {finalDirectory}");
            return manifest with { DirectoryPath = finalDirectory };
        }
        catch
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(stagingDirectory, "INCOMPLETE.txt"),
                    "This backup did not complete verification and must not be restored." + Environment.NewLine);
            }
            catch { }

            throw;
        }
    }

    public static IReadOnlyList<BackupManifest> LoadVerifiedBackups()
    {
        if (!Directory.Exists(RootDirectory))
            return Array.Empty<BackupManifest>();

        var backups = new List<BackupManifest>();
        foreach (string manifestPath in Directory.EnumerateFiles(RootDirectory, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                string manifestHashPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "manifest.sha256");
                if (!File.Exists(manifestHashPath))
                    continue;
                string expectedManifestHash = File.ReadAllText(manifestHashPath).Trim();
                if (expectedManifestHash.Length != 64 ||
                    !string.Equals(HashFile(manifestPath), expectedManifestHash, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BackupManifest? manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
                string? directory = Path.GetDirectoryName(manifestPath);
                if (manifest == null || directory == null || !manifest.Verified)
                    continue;

                bool filesValid = manifest.Partitions.All(partition =>
                {
                    string imagePath = Path.Combine(directory, partition.Name + ".img");
                    return File.Exists(imagePath) &&
                           new FileInfo(imagePath).Length == partition.Size &&
                           string.Equals(HashFile(imagePath), partition.Sha256, StringComparison.OrdinalIgnoreCase);
                });

                if (filesValid)
                    backups.Add(manifest with { DirectoryPath = directory });
            }
            catch
            {
            }
        }

        return backups.OrderByDescending(backup => backup.CreatedUtc).ToArray();
    }

    public static string FormatBytes(long bytes)
    {
        const double mib = 1024d * 1024d;
        const double gib = mib * 1024d;
        return bytes >= gib ? $"{bytes / gib:0.00} GiB" : $"{bytes / mib:0.0} MiB";
    }

    internal static string BuildPartitionExportCommand(string blockDevice)
    {
        return $"dd if={blockDevice} bs=1048576 2>/dev/null";
    }

    internal static bool TryParseBlockDeviceSize(string output, out long size)
    {
        foreach (string token in output.Split(
                     [' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(token, out size) && size > 0)
                return true;
        }

        size = 0;
        return false;
    }

    private static long ReadBlockDeviceSize(string blockDevice)
    {
        var (code, output) = Adb.Shell($"blockdev --getsize64 {blockDevice}", 30_000);
        if (code != 0 || !TryParseBlockDeviceSize(output, out long size))
            throw new IOException($"Could not read the size of {blockDevice}: {output}");
        return size;
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}

namespace QuestStack;

internal static class Firmware
{
    internal const string RequiredBuild = "49845030443200410";

    private const string FirmwareUrl = "https://files.catbox.moe/fcpm6p.zip";
    private const string FirmwareArchiveSha256 = "7020F22D7CE788CE944E5CB04E5A89830B9A2F4D5AB11D5D08B01687FDC67501";
    private const string DeviceImagesDir = "/data/local/tmp/v16/images";
    private const string RemoteBootctl = "/data/local/tmp/bootctl_shim";

    internal static readonly string[] Partitions =
    {
        "boot", "modem", "pmic", "rpm", "tz", "hyp", "devcfg", "cmnlib",
        "cmnlib64", "keymaster", "ovrtz", "abl", "xbl"
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedBundleSha256 =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bootctl_shim"] = "A5A5A201C5D4318015B1F646729A5EC3AB8EEE3BC2B856F46ED6EA887D225FCF",
            ["images/abl.img"] = "C2CC1F173BEC2956FA5E068ABC98DB82E1CD2C651A4F4BB7AE31C79605E43707",
            ["images/boot.img"] = "A1BF0F8ABD84779F7EB8B26794209FA96290F66C5232609999A4D01AF39F986D",
            ["images/cmnlib.img"] = "DD186828E790F7DD547F002AD0FB46F76037E080BCD4E70C7CDDA901F68BD685",
            ["images/cmnlib64.img"] = "8E62871F97BF99BE78A92BA06B2F51C705C57C50DF1DDEC40E995B135F63A6E1",
            ["images/devcfg.img"] = "69A7D7A6656B077B511910E6EFAA31E771E7ED24BA80F0C1C694FB164F16575A",
            ["images/hyp.img"] = "E14251A9E7932C1FFD2C3C90C000382144A94CE51712177A24C853C58E3055CC",
            ["images/keymaster.img"] = "EBAA111A767E1E393133B9C88E54EFEC32AA143E1FFFB77297DCC3A317C22F3C",
            ["images/modem.img"] = "D8E94155770106D0A615B25E60DEC63B4B17384B442F51BD57E561F3A7ACE25D",
            ["images/ovrtz.img"] = "41AFD6068117DAD22315BEBC478F8749F2F505D25A6C27EA7ACE4A152F054F9B",
            ["images/pmic.img"] = "57DAEEB2903E01BBE932DAA8D15129DCF210275B200F696ED2F4C612A4698BF5",
            ["images/rpm.img"] = "85C4D8ED165A9295A46F20C9C8BD485C42B3B7CD60E832C9C0DA0B933DCC5B23",
            ["images/tz.img"] = "A1548108AD612DDE59F69ED85CEF99A312C761DD3B2EECCE3DF6602E96D28A10",
            ["images/xbl.img"] = "6ED2C6854F4837710B4B3BA31DA33820E80841C7F3E69678A680D821EFABCA63"
        };

    public static bool CheckFirmwareVersion()
    {
        Logger.Info("Checking firmware version...");
        var build = Adb.GetProp("ro.build.version.incremental");
        if (build == null)
        {
            Logger.Error("Could not read firmware version. Is the device connected?");
            return false;
        }

        Logger.Info($"Current firmware: {build}");

        if (build == RequiredBuild)
        {
            Logger.Success($"Firmware matches required version ({RequiredBuild}).");
            return true;
        }

        Logger.Error($"Firmware mismatch. Required: {RequiredBuild}, Found: {build}");
        Logger.Error("Your device must be on the required firmware version before continuing.");
        Logger.Error("Reboot into USB Update Mode and sideload the required firmware package:");
        Logger.Error("https://files.cocaine.trade/firmware/meta/Quest/q1_49845030443200410.zip");
        Logger.Error("After installing, boot normally and re-run QuestStack.");
        return false;
    }

    public static string? DownloadAndExtract(string workDir)
    {
        string zipPath = Path.Combine(workDir, "fcpm6p.zip");
        string partialZipPath = zipPath + ".partial";
        string extractDir = Path.Combine(workDir, "fcpm6p");

        if (IsBundleComplete(extractDir))
        {
            Logger.Success($"Using cached firmware bundle: {extractDir}");
            return extractDir;
        }

        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);

        if (File.Exists(zipPath))
        {
            if (HashMatches(zipPath, FirmwareArchiveSha256))
            {
                Logger.Info("Extracting the SHA-256 verified v29 firmware bundle...");
                if (TryExtractBundle(zipPath, extractDir))
                    return extractDir;
            }
            else
            {
                Logger.Warn("The cached firmware archive failed SHA-256 verification.");
            }

            Logger.Warn("The cached archive is invalid. Downloading it again.");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            File.Delete(zipPath);
        }

        if (File.Exists(partialZipPath))
            File.Delete(partialZipPath);

        Logger.Info($"Downloading v29 firmware from {FirmwareUrl}...");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var resp = http.GetAsync(FirmwareUrl, HttpCompletionOption.ResponseHeadersRead).Result;
            resp.EnsureSuccessStatusCode();

            long? totalBytes = resp.Content.Headers.ContentLength;
            using var stream = resp.Content.ReadAsStreamAsync().Result;
            using var fs = File.Create(partialZipPath);

            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int lastReportedPercent = -10;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                fs.Write(buffer, 0, read);
                downloaded += read;

                if (totalBytes.HasValue)
                {
                    int pct = (int)(downloaded * 100 / totalBytes.Value);
                    if (pct >= lastReportedPercent + 10)
                    {
                        Logger.Info($"Firmware download: {pct}% ({downloaded / 1024 / 1024} MiB)");
                        lastReportedPercent = pct;
                    }
                }
            }
            fs.Flush(flushToDisk: true);
            fs.Dispose();

            if (!HashMatches(partialZipPath, FirmwareArchiveSha256))
                throw new InvalidDataException("The downloaded firmware archive failed SHA-256 verification.");

            File.Move(partialZipPath, zipPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Download failed: {ex.Message}");
            try { if (File.Exists(partialZipPath)) File.Delete(partialZipPath); } catch { }
            return null;
        }

        Logger.Success("Download complete. Extracting...");
        if (!TryExtractBundle(zipPath, extractDir))
            return null;

        return extractDir;
    }

    private static bool TryExtractBundle(string zipPath, string extractDir)
    {
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);
            if (!ValidateBundle(extractDir, logErrors: true))
            {
                return false;
            }

            Logger.Success("Extraction complete.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Extraction failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsBundleComplete(string extractDir)
    {
        return ValidateBundle(extractDir, logErrors: false);
    }

    internal static bool ValidateBundle(string extractDir, bool logErrors)
    {
        foreach ((string relativePath, string expectedHash) in ExpectedBundleSha256)
        {
            string localPath = Path.Combine(extractDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                if (logErrors) Logger.Error($"Firmware bundle file is missing: {relativePath}");
                return false;
            }

            try
            {
                using FileStream stream = File.OpenRead(localPath);
                string actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (logErrors) Logger.Error($"Firmware bundle checksum mismatch: {relativePath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (logErrors) Logger.Error($"Could not validate {relativePath}: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    internal static bool ValidateArchive(string archivePath) => HashMatches(archivePath, FirmwareArchiveSha256);

    public static int GetCurrentSlot()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var (code, output) = Adb.Shell($"{RemoteBootctl} get-current-slot", 10_000);
            string trimmed = output.Trim();

            if (code == 0 && trimmed == "0") return 0;
            if (code == 0 && trimmed == "1") return 1;

            Logger.Warn($"Could not determine current slot (attempt {attempt}/5). Retrying...");
            Thread.Sleep(2000);
        }

        Logger.Error("Failed to determine current boot slot after 5 attempts.");
        Logger.Error("Cannot safely proceed without knowing the active slot.");
        return -1;
    }

    public static string SlotSuffix(int slot)
    {
        return slot switch
        {
            0 => "_a",
            1 => "_b",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "A/B slot must be 0 or 1.")
        };
    }

    public static bool PushFiles(string firmwareDir, string bootctlPath)
    {
        Logger.Info("Creating device temp directory...");
        var (mkdirCode, mkdirOutput) = Adb.Shell($"mkdir -p {DeviceImagesDir}");
        if (mkdirCode != 0)
        {
            Logger.Error($"Could not create {DeviceImagesDir}: {mkdirOutput}");
            return false;
        }

        Logger.Info("Pushing bootctl_shim...");
        var (c1, o1) = Adb.Push(bootctlPath, RemoteBootctl, 30_000);
        if (c1 != 0)
        {
            Logger.Error($"Failed to push bootctl_shim: {o1}");
            return false;
        }
        var (chmodCode, chmodOutput) = Adb.Shell($"chmod 755 {RemoteBootctl}");
        if (chmodCode != 0)
        {
            Logger.Error($"Could not make bootctl_shim executable: {chmodOutput}");
            return false;
        }

        string imagesDir = Path.Combine(firmwareDir, "images");
        string[] imgFiles = Partitions.Select(partition => Path.Combine(imagesDir, partition + ".img")).ToArray();

        string? missingImage = imgFiles.FirstOrDefault(path => !File.Exists(path));
        if (missingImage != null)
        {
            Logger.Error($"Required firmware image is missing: {missingImage}");
            return false;
        }

        Logger.Info($"Pushing {imgFiles.Length} firmware images...");

        foreach (string imgPath in imgFiles)
        {
            string fileName = Path.GetFileName(imgPath);
            Logger.Info($"  Pushing {fileName}...");
            var (c, o) = Adb.Push(imgPath, $"{DeviceImagesDir}/{fileName}", 300_000);
            if (c != 0)
            {
                Logger.Error($"Failed to push {fileName}: {o}");
                return false;
            }
        }

        Logger.Info("Verifying pushed files...");

        bool pushOk = true;

        foreach (string imgPath in imgFiles)
        {
            string fileName = Path.GetFileName(imgPath);
            string remotePath = $"{DeviceImagesDir}/{fileName}";

            string localHex = HashFile(imgPath);

            var (c, o) = Adb.Shell($"sha256sum {remotePath} 2>/dev/null | cut -c1-64", 30_000);
            string remoteHex = o.Trim().Split('\n')[0].Trim();

            if (c == 0 &&
                !string.IsNullOrEmpty(localHex) &&
                string.Equals(localHex, remoteHex, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"  {fileName}: OK");
            }
            else
            {
                Logger.Error($"  {fileName}: PUSH MISMATCH (local={localHex}, device={remoteHex})");
                pushOk = false;
            }
        }

        if (!pushOk)
        {
            Logger.Error("Push verification failed. Some files may be corrupted.");
            return false;
        }

        Logger.Success("All files pushed and verified.");
        return true;
    }

    public static bool CheckImages(int targetSlot)
    {
        string suffix = SlotSuffix(targetSlot);
        Logger.Info($"Checking images for slot {suffix.TrimStart('_')}...");

        foreach (string part in Partitions)
        {
            string imgFile = $"{DeviceImagesDir}/{part}.img";
            string blockDev = $"/dev/block/bootdevice/by-name/{part}{suffix}";

            var (code, output) = Adb.Shell($"[ -f {imgFile} ] && echo IMG_OK || echo IMG_MISSING", 5_000);
            if (code != 0 || !output.Contains("IMG_OK", StringComparison.Ordinal))
            {
                Logger.Error($"Missing image: {part}.img");
                return false;
            }

            var (c2, o2) = Adb.Shell($"[ -b {blockDev} ] && echo BLK_OK || echo BLK_MISSING", 5_000);
            if (c2 != 0 || !o2.Contains("BLK_OK", StringComparison.Ordinal))
            {
                Logger.Error($"Missing block device: {blockDev}");
                return false;
            }

            var (c3, o3) = Adb.Shell($"stat -c %s {imgFile} 2>/dev/null || echo 0", 5_000);
            var (c4, o4) = Adb.Shell($"cat /sys/class/block/$(basename $(readlink -f {blockDev}))/size 2>/dev/null || echo 0", 5_000);

            if (c3 != 0 || c4 != 0 ||
                !long.TryParse(o3.Trim(), out long imgSize) ||
                !long.TryParse(o4.Trim(), out long partSectors) ||
                imgSize <= 0 || partSectors <= 0)
            {
                Logger.Error($"Could not validate image and partition sizes for {part}.");
                return false;
            }

            long partSize = partSectors * 512;
            if (imgSize > partSize)
            {
                Logger.Error($"{part}.img ({imgSize} bytes) is larger than partition ({partSize} bytes).");
                return false;
            }

            Logger.Info($"  {part}: OK");
        }

        Logger.Success("All images checked.");
        return true;
    }

    public static bool FlashPartitions(int targetSlot)
    {
        string suffix = SlotSuffix(targetSlot);
        Logger.Info($"Flashing v29 firmware to slot {suffix.TrimStart('_')}...");

        foreach (string partition in Partitions)
        {
            Logger.Info($"  {partition}: writing...");
            bool written = false;

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                var (code, output) = Adb.Shell(BuildFlashPartitionCommand(partition, suffix), 300_000);
                if (code == 0 &&
                    output.Contains("WRITE_EXIT=0", StringComparison.Ordinal) &&
                    output.Contains("SYNC_EXIT=0", StringComparison.Ordinal))
                {
                    written = true;
                    Logger.Success($"  {partition}: write completed.");
                    break;
                }

                Logger.Warn($"  {partition}: write attempt {attempt}/2 failed.");
                if (!string.IsNullOrWhiteSpace(output))
                    Logger.Warn(output);
            }

            if (!written)
            {
                Logger.Error($"Could not write {partition}{suffix}. Stopping before the slot is activated.");
                return false;
            }
        }

        Logger.Success("Flash complete.");
        return true;
    }

    public static bool VerifyPartitions(int targetSlot)
    {
        string suffix = SlotSuffix(targetSlot);
        Logger.Info($"Verifying flashed partitions on slot {suffix.TrimStart('_')}...");

        foreach (string partition in Partitions)
        {
            Logger.Info($"  {partition}: reading back and hashing...");
            var (code, output) = Adb.Shell(BuildVerifyPartitionCommand(partition, suffix), 300_000);
            if (code != 0 || !output.Contains("VERIFY_OK", StringComparison.Ordinal))
            {
                Logger.Error($"  {partition}: verification failed.");
                Logger.Error(output);
                return false;
            }

            Logger.Success($"  {partition}: verified.");
        }

        Logger.Success("All partitions verified.");
        return true;
    }

    public static bool SwitchSlot(int targetSlot)
    {
        Logger.Info($"Switching active boot slot to {targetSlot}...");

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var (code, output) = Adb.Shell($"{RemoteBootctl} set-active-boot-slot {targetSlot}", 10_000);
            Logger.Info(output.Trim());

            if (code == 0)
            {
                Logger.Success($"Active boot slot set to {targetSlot} ({SlotSuffix(targetSlot)}).");
                return true;
            }

            Logger.Warn($"Slot switch attempt {attempt}/3 failed. Retrying...");
            Thread.Sleep(2000);
        }

        Logger.Error("Failed to switch boot slot after 3 attempts.");
        return false;
    }

    internal static string BuildFlashPartitionCommand(string partition, string suffix)
    {
        string image = $"{DeviceImagesDir}/{partition}.img";
        string block = $"/dev/block/bootdevice/by-name/{partition}{suffix}";
        return $"img='{image}'; blk='{block}'; " +
               "dd if=\"$img\" of=\"$blk\" bs=1M 2>&1; write_rc=$?; " +
               "sync; sync_rc=$?; echo WRITE_EXIT=$write_rc; echo SYNC_EXIT=$sync_rc; " +
               "[ \"$write_rc\" -eq 0 ] && [ \"$sync_rc\" -eq 0 ]";
    }

    internal static string BuildVerifyPartitionCommand(string partition, string suffix)
    {
        string image = $"{DeviceImagesDir}/{partition}.img";
        string block = $"/dev/block/bootdevice/by-name/{partition}{suffix}";
        return $"img='{image}'; blk='{block}'; " +
               "n=$(stat -c %s \"$img\" 2>/dev/null || wc -c < \"$img\"); " +
               "image_hash=$(sha256sum \"$img\" | cut -c1-64); " +
               "block_hash=$(dd if=\"$blk\" bs=4096 count=$(((n+4095)/4096)) 2>/dev/null | head -c \"$n\" | sha256sum | cut -c1-64); " +
               "echo IMAGE_HASH=$image_hash; echo BLOCK_HASH=$block_hash; " +
               "[ -n \"$image_hash\" ] && [ \"$image_hash\" = \"$block_hash\" ] && echo VERIFY_OK";
    }

    private static bool HashMatches(string path, string expectedSha256)
    {
        try
        {
            return string.Equals(HashFile(path), expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

}

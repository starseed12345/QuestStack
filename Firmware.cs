namespace QuestStack;

internal static class Firmware
{
    private const string RequiredBuild = "49845030443200410";

    private const string FirmwareUrl = "https://files.catbox.moe/fcpm6p.zip";
    private const string DeviceTmpDir = "/data/local/tmp/v16";
    private const string DeviceImagesDir = "/data/local/tmp/v16/images";
    private const string RemoteBootctl = "/data/local/tmp/bootctl_shim";

    private static readonly string[] NosystemPartitions = new[]
    {
        "boot", "modem", "pmic", "rpm", "tz", "hyp", "devcfg", "cmnlib",
        "cmnlib64", "keymaster", "ovrtz", "abl", "xbl"
    };

    private static readonly IReadOnlyDictionary<string, string> ExpectedBundleMd5 =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bootctl_shim"] = "a1d4f5eeeed927a821e8c730e8bbe061",
            ["images/abl.img"] = "a66adb03a16c792afb0db206acba3a42",
            ["images/boot.img"] = "b3eac238af7164e83a5c4eaf37dea51b",
            ["images/cmnlib.img"] = "09b36dd8a313fe8428b7f4b0c867ed34",
            ["images/cmnlib64.img"] = "e4d05008ffc2ff8fe185717f2da44017",
            ["images/devcfg.img"] = "8475c7edcb9c90e2ae0c5c2f03ce94e2",
            ["images/hyp.img"] = "9b1e3f6ceb0b7143b6f9708833dd7f4c",
            ["images/keymaster.img"] = "1067458d975d122923df9a1569464d53",
            ["images/modem.img"] = "20f9a23819b4172a730ce4b7311eb9f2",
            ["images/ovrtz.img"] = "3772e0505f2147820cb22a1de95cf394",
            ["images/pmic.img"] = "39d7931e21b214506c40af6cb3ba0d20",
            ["images/rpm.img"] = "344cb241834def92d9dc93c09435a146",
            ["images/tz.img"] = "b5ff998224d6348dfb4fdf1c79dad9e4",
            ["images/xbl.img"] = "287d1849a8d0c66d44ee59932f4ba73e"
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
        string zipPath = Path.Combine(workDir, "quest1_v16_new.zip");
        string partialZipPath = zipPath + ".partial";
        string extractDir = Path.Combine(workDir, "quest1_v16_new");

        if (IsBundleComplete(extractDir))
        {
            Logger.Success($"Using cached firmware bundle: {extractDir}");
            return extractDir;
        }

        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);

        if (File.Exists(zipPath))
        {
            Logger.Info("Extracting the cached v29 firmware bundle...");
            if (TryExtractBundle(zipPath, extractDir))
                return extractDir;

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
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                fs.Write(buffer, 0, read);
                downloaded += read;

                if (totalBytes.HasValue)
                {
                    int pct = (int)(downloaded * 100 / totalBytes.Value);
                    Console.Write($"\r[*] Downloaded {downloaded / 1024 / 1024}MB / {totalBytes.Value / 1024 / 1024}MB ({pct}%)");
                }
                else
                {
                    Console.Write($"\r[*] Downloaded {downloaded / 1024 / 1024}MB");
                }
            }
            Console.WriteLine();
            fs.Flush(flushToDisk: true);
            fs.Dispose();
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
        foreach ((string relativePath, string expectedHash) in ExpectedBundleMd5)
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
                string actualHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(stream)).ToLowerInvariant();
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

    public static string SlotSuffix(int slot) => slot == 0 ? "_a" : "_b";

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
        string[] imgFiles = Directory.GetFiles(imagesDir, "*.img");

        if (imgFiles.Length == 0)
        {
            Logger.Error($"No firmware images were found in {imagesDir}.");
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

        using var md5 = System.Security.Cryptography.MD5.Create();
        bool pushOk = true;

        foreach (string imgPath in imgFiles)
        {
            string fileName = Path.GetFileName(imgPath);
            string remotePath = $"{DeviceImagesDir}/{fileName}";

            byte[] localHash = md5.ComputeHash(File.ReadAllBytes(imgPath));
            string localHex = Convert.ToHexString(localHash).ToLowerInvariant();

            var (c, o) = Adb.Shell($"md5sum {remotePath} 2>/dev/null | cut -d' ' -f1", 10_000);
            string remoteHex = o.Trim().Split('\n')[0].Trim().ToLowerInvariant();

            if (c == 0 && localHex == remoteHex && !string.IsNullOrEmpty(localHex))
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

        foreach (string part in NosystemPartitions)
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

    public static bool BackupPartitions(int targetSlot)
    {
        string suffix = SlotSuffix(targetSlot);
        string bakDir = $"{DeviceTmpDir}/bak";
        var (mkdirCode, mkdirOutput) = Adb.Shell($"mkdir -p {bakDir}");
        if (mkdirCode != 0)
        {
            Logger.Error($"Could not create the backup directory: {mkdirOutput}");
            return false;
        }

        Logger.Info($"Backing up slot {suffix.TrimStart('_')} partitions...");

        foreach (string part in NosystemPartitions)
        {
            string blockDev = $"/dev/block/bootdevice/by-name/{part}{suffix}";
            string bakFile = $"{bakDir}/{part}{suffix}.img";

            Logger.Info($"  Backing up {part}...");
            var (code, output) = Adb.Shell(
                $"dd if={blockDev} of={bakFile} bs=1M 2>/dev/null && sync && [ -s {bakFile} ]",
                120_000);
            if (code != 0)
            {
                Logger.Error($"Backup failed for {part}: {output}");
                return false;
            }
        }

        Logger.Success("Backup complete.");
        return true;
    }

    public static bool FlashPartitions(int targetSlot)
    {
        string suffix = SlotSuffix(targetSlot);
        Logger.Info($"Flashing v29 firmware to slot {suffix.TrimStart('_')}...");

        string flashScript = Path.Combine(Path.GetTempPath(), "queststack", "flash.sh");
        File.WriteAllText(flashScript, @"#!/system/bin/sh
IMG_DIR=""$1""
BLOCK_DIR=""$2""
SLOT=""$3""
failed=0
for p in boot modem pmic rpm tz hyp devcfg cmnlib cmnlib64 keymaster ovrtz abl xbl; do
    img=""$IMG_DIR/$p.img""
    blk=""$BLOCK_DIR/${p}${SLOT}""
    [ -f ""$img"" ] || { echo ""FAIL $p missing image""; failed=1; continue; }
    [ -b ""$blk"" ] || { echo ""FAIL $p missing block device""; failed=1; continue; }
    echo ""FLASH $p""
    dd if=""$img"" of=""$blk"" bs=1M conv=fsync 2>&1
    if [ $? -ne 0 ]; then
        echo ""RETRY $p""
        dd if=""$img"" of=""$blk"" bs=1M 2>&1
        if [ $? -ne 0 ]; then
            echo ""FAIL $p write failed""
            failed=1
        fi
    fi
done
sync || failed=1
[ ""$failed"" -eq 0 ] || { echo ""FLASH_FAILED""; exit 1; }
echo ""FLASH_DONE""
");

        var (pushCode, pushOutput) = Adb.Push(flashScript, "/data/local/tmp/flash.sh", 10_000);
        if (pushCode != 0)
        {
            Logger.Error($"Could not push the flash script: {pushOutput}");
            return false;
        }

        var (chmodCode, chmodOutput) = Adb.Shell("chmod 755 /data/local/tmp/flash.sh");
        if (chmodCode != 0)
        {
            Logger.Error($"Could not make the flash script executable: {chmodOutput}");
            return false;
        }

        var (code, output) = Adb.Shell($"/data/local/tmp/flash.sh {DeviceImagesDir} /dev/block/bootdevice/by-name {suffix}", 600_000);

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("FLASH ", StringComparison.Ordinal) && !line.Contains("DONE", StringComparison.Ordinal))
                Logger.Info($"  {line.Substring(6)}: flashing...");
            else if (line.StartsWith("RETRY ", StringComparison.Ordinal))
                Logger.Warn($"  {line.Substring(6)}: retrying...");
            else if (line.StartsWith("FAIL ", StringComparison.Ordinal))
                Logger.Error($"  {line.Substring(5)}");
            else if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("No space", StringComparison.OrdinalIgnoreCase))
                Logger.Error($"  {line}");
        }

        if (code != 0 ||
            !output.Contains("FLASH_DONE", StringComparison.Ordinal) ||
            output.Contains("FLASH_FAILED", StringComparison.Ordinal) ||
            output.Split('\n').Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal)))
        {
            Logger.Error("Flash script did not complete.");
            Logger.Error(output);
            return false;
        }

        Logger.Success("Flash complete.");
        return true;
    }

    public static bool VerifyPartitions(int targetSlot)
    {
        string suffix = SlotSuffix(targetSlot);
        Logger.Info($"Verifying flashed partitions on slot {suffix.TrimStart('_')}...");

        string scriptPath = Path.Combine(Path.GetTempPath(), "queststack", "verify.sh");
        File.WriteAllText(scriptPath, @"#!/system/bin/sh
IMG_DIR=""$1""
BLOCK_DIR=""$2""
failed=0
for p in boot modem pmic rpm tz hyp devcfg cmnlib cmnlib64 keymaster ovrtz abl xbl; do
    img=""$IMG_DIR/$p.img""
    blk=""$BLOCK_DIR/${p}$3""
    [ -f ""$img"" ] || { echo ""MISSING $p image""; failed=1; continue; }
    [ -b ""$blk"" ] || { echo ""MISSING $p block device""; failed=1; continue; }
    n=$(stat -c%s ""$img"" 2>/dev/null || wc -c < ""$img"")
    a=$(md5sum ""$img"" | cut -d' ' -f1)
    b=$(dd if=""$blk"" bs=4096 count=$((n/4096+1)) 2>/dev/null | head -c ""$n"" | md5sum | cut -d' ' -f1)
    if [ ""$a"" = ""$b"" ]; then echo ""OK $p""
    else echo ""MISMATCH $p img=$a part=$b""; failed=1; fi
done
[ ""$failed"" -eq 0 ]
");

        var (pushCode, pushOutput) = Adb.Push(scriptPath, "/data/local/tmp/verify.sh", 10_000);
        if (pushCode != 0)
        {
            Logger.Error($"Could not push the verification script: {pushOutput}");
            return false;
        }

        var (chmodCode, chmodOutput) = Adb.Shell("chmod 755 /data/local/tmp/verify.sh");
        if (chmodCode != 0)
        {
            Logger.Error($"Could not make the verification script executable: {chmodOutput}");
            return false;
        }

        var (code, output) = Adb.Shell($"/data/local/tmp/verify.sh {DeviceImagesDir} /dev/block/bootdevice/by-name {suffix}", 300_000);

        bool allGood = code == 0;
        int verifiedCount = 0;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("OK ", StringComparison.Ordinal))
            {
                Logger.Info($"  {line.Substring(3)}: OK");
                verifiedCount++;
            }
            else if (line.StartsWith("MISMATCH ", StringComparison.Ordinal))
            {
                Logger.Error($"  {line.Substring(9)}");
                allGood = false;
            }
            else if (line.StartsWith("MISSING ", StringComparison.Ordinal))
            {
                Logger.Error($"  {line}");
                allGood = false;
            }
            else if (!string.IsNullOrWhiteSpace(line))
                Logger.Warn($"  {line}");
        }

        if (verifiedCount != NosystemPartitions.Length)
            allGood = false;

        if (allGood)
            Logger.Success("All partitions verified.");
        else
            Logger.Error("Some partitions failed verification.");

        return allGood;
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

    public static bool Reboot()
    {
        Logger.Info("Rebooting device...");
        var (code, output) = Adb.Run("reboot", 10_000);
        if (code != 0)
        {
            Logger.Error($"ADB reboot failed: {output}");
            return false;
        }

        return true;
    }
}

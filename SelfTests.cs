using System.Diagnostics;

namespace QuestStack;

internal static class SelfTests
{
    public static bool Run(string? realAblPath = null, string? ionstackPath = null, string? firmwareArchivePath = null)
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("Platform tools", TestBundledAdb),
            ("Fastboot wipe helpers", TestFastbootWipeHelpers),
            ("Native libusb", TestNativeLibUsb),
            ("Partition command construction", TestPartitionCommands),
            ("Backup export command", TestBackupExportCommand),
            ("Fastboot slot parsing", TestFastbootSlotParsing),
            ("Quest 1 fingerprint gate", TestQuest1FingerprintGate),
            ("ADB device list parsing", TestAdbDeviceParsing),
            ("Fastboot device-info parsing", TestFastbootInfoParsing),
            ("Consecutive Y confirmations", TestConsecutiveConfirmations),
            ("v29 LinuxLoader payload construction", TestPayloadConstruction)
        };

        if (!string.IsNullOrWhiteSpace(realAblPath))
            tests.Add(("Real ABL extraction and v29 payload", () => TestRealAbl(realAblPath)));
        if (!string.IsNullOrWhiteSpace(ionstackPath))
            tests.Add(("ionstack SHA-256", () => TestIonstackHash(ionstackPath)));
        if (!string.IsNullOrWhiteSpace(firmwareArchivePath))
            tests.Add(("Firmware archive SHA-256", () => TestFirmwareArchiveHash(firmwareArchivePath)));

        int passed = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Logger.Success($"PASS: {name}");
                passed++;
            }
            catch (Exception ex)
            {
                Logger.Error($"FAIL: {name}: {ex.Message}");
            }
        }

        Logger.Info($"Self-test result: {passed}/{tests.Count} passed.");
        return passed == tests.Count;
    }

    private static void TestBundledAdb()
    {
        BundledAdb.ValidateEmbeddedResources();
        (int exitCode, string output) = Adb.Run("version");
        Assert(exitCode == 0, $"ADB did not start: {output}");
        Assert(output.Contains($"Version {BundledAdb.Version}-", StringComparison.Ordinal),
            $"Expected ADB {BundledAdb.Version}, got: {output}");
        Logger.Info($"ADB version: {BundledAdb.Version}");

        (int fastbootCode, string fastbootOutput) = FastbootCli.Run(["--version"]);
        Assert(fastbootCode == 0, $"Fastboot did not start: {fastbootOutput}");
        Assert(fastbootOutput.Contains(BundledAdb.Version, StringComparison.Ordinal),
            $"Expected fastboot {BundledAdb.Version}, got: {fastbootOutput}");
        Logger.Info($"Fastboot version: {BundledAdb.Version}");
    }

    private static void TestFastbootWipeHelpers()
    {
        string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        string[] helpers = [$"mke2fs{extension}", "mke2fs.conf", $"make_f2fs{extension}", $"make_f2fs_casefold{extension}"];
        foreach (string helper in helpers)
            Assert(File.Exists(Path.Combine(BundledAdb.ToolsDirectory, helper)), $"Fastboot helper is missing: {helper}");

        ProcessStartInfo psi = FastbootCli.CreateProcessStartInfo(["--version"]);
        string path = psi.Environment.TryGetValue("PATH", out string? value) ? value ?? string.Empty : string.Empty;
        Assert(path.Split(Path.PathSeparator).FirstOrDefault() == BundledAdb.ToolsDirectory,
            "The fastboot helper directory is not first in PATH.");
        Assert(psi.Environment.TryGetValue("MKE2FS_CONFIG", out string? config) &&
               config == Path.Combine(BundledAdb.ToolsDirectory, "mke2fs.conf"),
            "The mke2fs configuration path is missing.");
    }

    private static void TestNativeLibUsb()
    {
        string version = NativeUsbRuntime.GetVersion();
        Assert(!string.IsNullOrWhiteSpace(version), "libusb returned no version.");
        Logger.Info($"libusb version: {version}");
    }

    private static void TestPartitionCommands()
    {
        string flash = Firmware.BuildFlashPartitionCommand("abl", "_a");
        Assert(flash.Contains("/abl_a", StringComparison.Ordinal), "The flash command targets the wrong slot.");
        Assert(flash.Contains("WRITE_EXIT=", StringComparison.Ordinal), "The flash command has no write status marker.");
        Assert(flash.Contains("SYNC_EXIT=", StringComparison.Ordinal), "The flash command has no sync status marker.");

        string verify = Firmware.BuildVerifyPartitionCommand("abl", "_b");
        Assert(verify.Contains("/abl_b", StringComparison.Ordinal), "The verification command targets the wrong slot.");
        Assert(verify.Contains("VERIFY_OK", StringComparison.Ordinal), "The verification command has no success marker.");
        Assert(verify.Contains("sha256sum", StringComparison.Ordinal), "The verification command is not using SHA-256.");
    }

    private static void TestBackupExportCommand()
    {
        const string device = "/dev/block/bootdevice/by-name/boot_a";
        string command = BackupStore.BuildPartitionExportCommand(device);
        Assert(command == $"dd if={device} bs=1048576 2>/dev/null",
            "The backup command does not suppress dd status output.");
        Assert(BackupStore.TryParseBlockDeviceSize("67108864\r\n", out long size) && size == 67_108_864,
            "The block device size parser failed.");
    }

    private static void TestFastbootSlotParsing()
    {
        string? infoValue = FastbootUsbDevice.ParseVariableResponse("INFOcurrent-slot: b\nOKAY", "current-slot");
        string? okayValue = FastbootUsbDevice.ParseVariableResponse("OKAYa", "current-slot");
        Assert(infoValue == "b", "The INFO current-slot response was parsed incorrectly.");
        Assert(okayValue == "a", "The OKAY current-slot response was parsed incorrectly.");
        Assert(DeviceProbe.ParseSlot("_a") == 0, "Slot _a was parsed incorrectly.");
        Assert(DeviceProbe.ParseSlot("b") == 1, "Slot b was parsed incorrectly.");
        Assert(DeviceProbe.ParseSlot("unknown") == -1, "An invalid slot was accepted.");
        Assert(FastbootCli.ParseCurrentSlot("(bootloader) current-slot: a") == 0, "fastboot slot a was parsed incorrectly.");
        Assert(FastbootCli.ParseCurrentSlot("current-slot: b\nFinished.") == 1, "fastboot slot b was parsed incorrectly.");
    }

    private static void TestAdbDeviceParsing()
    {
        Assert(!Adb.HasConnectedDevice("List of devices attached\r\n\r\n"), "An empty list was treated as connected.");
        Assert(!Adb.HasConnectedDevice("List of devices attached\r\nABC\tunauthorized\r\n"), "An unauthorized device was treated as connected.");
        Assert(!Adb.HasConnectedDevice("List of devices attached\r\nABC\toffline\r\n"), "An offline device was treated as connected.");
        Assert(Adb.HasConnectedDevice("List of devices attached\r\nABC\tdevice product:monterey\r\n"), "An authorized device was not detected.");
    }

    private static void TestQuest1FingerprintGate()
    {
        var quest1 = new DeviceIdentity(
            "1PASH9AZX90161",
            "Quest",
            "monterey",
            "monterey",
            Firmware.RequiredBuild,
            $"oculus/vr_monterey/monterey:10/QQ3A.200805.001/{Firmware.RequiredBuild}:user/release-keys",
            0);
        Assert(quest1.IsSupportedQuest1, "The supported Quest 1 fingerprint was rejected.");

        DeviceIdentity wrongDevice = quest1 with { Device = "hollywood", Product = "hollywood" };
        DeviceIdentity wrongBuild = quest1 with { Build = "99999999999999999" };
        Assert(!wrongDevice.IsSupportedQuest1, "A non-Quest 1 device was accepted.");
        Assert(!wrongBuild.IsSupportedQuest1, "An unsupported build was accepted.");
    }

    private static void TestFastbootInfoParsing()
    {
        const string response = "INFODevice unlocked: false\nINFOBuild number: 15849800125100000\nOKAY";
        Dictionary<string, string>? info = FastbootUsbDevice.ParseDeviceInfoResponse(response);
        Assert(info != null, "The response produced no fields.");
        Assert(info!["Device unlocked"] == "false", "The unlocked flag was parsed incorrectly.");
        Assert(info["Build number"] == "15849800125100000", "The build number was parsed incorrectly.");
    }

    private static void TestConsecutiveConfirmations()
    {
        TextReader originalInput = Console.In;
        try
        {
            Console.SetIn(new StringReader("Y\nY\n"));
            Assert(Logger.Confirm("First test confirmation?"), "The first Y was not accepted.");
            Assert(Logger.Confirm("Second test confirmation?"), "The second Y was not accepted.");
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }

    private static void TestPayloadConstruction()
    {
        const int peOffset = 0x2000;
        const int patchOffset = 0x3767c;
        byte[] ablImage = new byte[peOffset + patchOffset + 0x100];
        ablImage[peOffset] = (byte)'M';
        ablImage[peOffset + 1] = (byte)'Z';
        byte[] expected = [0xc9, 0x04, 0x00, 0x54];
        Array.Copy(expected, 0, ablImage, peOffset + patchOffset, expected.Length);

        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuestStackSelfTest-{Guid.NewGuid():N}");
        string ablPath = Path.Combine(testDirectory, "abl.img");
        Directory.CreateDirectory(testDirectory);

        try
        {
            File.WriteAllBytes(ablPath, ablImage);
            byte[]? payload = BootloaderUnlocker.BuildPayload(ablPath, "15849800125100000");
            Assert(payload != null, "No payload was produced.");
            Assert(payload!.Length == 0x100000 + patchOffset + 4, "The payload has the wrong length.");
            Assert(payload[0x100000] == (byte)'M' && payload[0x100001] == (byte)'Z', "The PE was copied from the wrong location.");

            byte[] patch = [0xb6, 0x00, 0x00, 0x14];
            for (int index = 0; index < patch.Length; index++)
                Assert(payload[0x100000 + patchOffset + index] == patch[index], "The v29 patch bytes are incorrect.");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
                Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static void TestRealAbl(string ablPath)
    {
        string fullPath = Path.GetFullPath(ablPath);
        Assert(File.Exists(fullPath), $"ABL image not found: {fullPath}");

        string? imagesDirectory = Path.GetDirectoryName(fullPath);
        string? bundleDirectory = imagesDirectory == null ? null : Directory.GetParent(imagesDirectory)?.FullName;
        Assert(bundleDirectory != null && Firmware.ValidateBundle(bundleDirectory, logErrors: false),
            "The real firmware bundle failed its checksum manifest.");

        byte[]? payload = BootloaderUnlocker.BuildPayload(fullPath, "16476800119700000");
        Assert(payload != null, "The real ABL did not produce a v29 payload.");
        Assert(payload!.Length == 0x100000 + 0x3777c + 4, "The real ABL payload has the wrong length.");

        byte[] patch = [0xb6, 0x00, 0x00, 0x14];
        for (int index = 0; index < patch.Length; index++)
            Assert(payload[0x100000 + 0x3777c + index] == patch[index], "The v29 patch bytes are incorrect.");
    }

    private static void TestIonstackHash(string ionstackPath)
    {
        string fullPath = Path.GetFullPath(ionstackPath);
        Assert(File.Exists(fullPath), $"ionstack file not found: {fullPath}");
        Assert(RootExploit.HashMatches(fullPath), "ionstack did not match the expected SHA-256 hash.");
    }

    private static void TestFirmwareArchiveHash(string archivePath)
    {
        string fullPath = Path.GetFullPath(archivePath);
        Assert(File.Exists(fullPath), $"Firmware archive not found: {fullPath}");
        Assert(Firmware.ValidateArchive(fullPath), "Firmware archive did not match the expected SHA-256 hash.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

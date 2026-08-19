namespace QuestStack;

internal static class SelfTests
{
    public static bool Run(string? realAblPath = null)
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("Bundled ADB", TestBundledAdb),
            ("Bundled native libusb", TestNativeLibUsb),
            ("ADB device list parsing", TestAdbDeviceParsing),
            ("Fastboot device-info parsing", TestFastbootInfoParsing),
            ("Consecutive Y confirmations", TestConsecutiveConfirmations),
            ("v29 LinuxLoader payload construction", TestPayloadConstruction)
        };

        if (!string.IsNullOrWhiteSpace(realAblPath))
            tests.Add(("Real ABL extraction and v29 payload", () => TestRealAbl(realAblPath)));

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
        Assert(exitCode == 0, $"Bundled ADB did not start: {output}");
        Assert(output.Contains($"Version {BundledAdb.Version}-", StringComparison.Ordinal),
            $"Expected ADB {BundledAdb.Version}, got: {output}");
        Logger.Info($"Bundled ADB version: {BundledAdb.Version}");
    }

    private static void TestNativeLibUsb()
    {
        string version = NativeUsbRuntime.GetVersion();
        Assert(!string.IsNullOrWhiteSpace(version), "The bundled libusb returned no version.");
        Logger.Info($"Bundled libusb version: {version}");
    }

    private static void TestAdbDeviceParsing()
    {
        Assert(!Adb.HasConnectedDevice("List of devices attached\r\n\r\n"), "An empty list was treated as connected.");
        Assert(!Adb.HasConnectedDevice("List of devices attached\r\nABC\tunauthorized\r\n"), "An unauthorized device was treated as connected.");
        Assert(!Adb.HasConnectedDevice("List of devices attached\r\nABC\toffline\r\n"), "An offline device was treated as connected.");
        Assert(Adb.HasConnectedDevice("List of devices attached\r\nABC\tdevice product:monterey\r\n"), "An authorized device was not detected.");
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

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

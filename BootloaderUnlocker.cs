namespace QuestStack;

internal static class BootloaderUnlocker
{
    private const int QuestVendorId = 0x2833;
    private const int FastbootProductId = 0x81;
    private const int OverflowSize = 0x100000;

    private static readonly IReadOnlyDictionary<string, PatchProfile> PatchProfiles =
        new Dictionary<string, PatchProfile>(StringComparer.Ordinal)
        {
            ["15849800125100000"] = new(
                BuildNumber: "15849800125100000",
                PatchOffset: 0x3767c,
                ExpectedBytes: [0xc9, 0x04, 0x00, 0x54],
                PatchBytes: [0xb6, 0x00, 0x00, 0x14]),
            ["16476800119700000"] = new(
                BuildNumber: "16476800119700000",
                PatchOffset: 0x3777c,
                ExpectedBytes: [0xc9, 0x04, 0x00, 0x54],
                PatchBytes: [0xb6, 0x00, 0x00, 0x14])
        };

    public static bool Unlock(string ablImagePath, int fastbootWaitSeconds = 180)
    {
        if (!File.Exists(ablImagePath))
        {
            Logger.Error($"ABL image not found: {ablImagePath}");
            return false;
        }

        Logger.Info("Waiting for the Quest fastboot USB interface...");
        using FastbootUsbDevice? device = WaitForDevice(TimeSpan.FromSeconds(fastbootWaitSeconds));
        if (device == null)
        {
            Logger.Error("Fastboot was not detected.");
            Logger.Error("Install the WinUSB/libusb driver for the Quest fastboot interface, then try again.");
            return false;
        }

        Dictionary<string, string>? deviceInfo = device.GetDeviceInfo();
        if (deviceInfo == null)
        {
            Logger.Error("Connected to fastboot, but could not read device information.");
            return false;
        }

        if (IsUnlocked(deviceInfo))
        {
            Logger.Success("Fastboot already reports Device unlocked: true.");
            return true;
        }

        if (!deviceInfo.TryGetValue("Build number", out string? buildNumber))
        {
            Logger.Error("Fastboot did not report a build number.");
            return false;
        }

        Logger.Info($"Fastboot build number: {buildNumber}");
        if (!PatchProfiles.TryGetValue(buildNumber, out PatchProfile? profile))
        {
            Logger.Error($"Build {buildNumber} is not a supported vulnerable Quest 1 ABL.");
            Logger.Error("Expected v28 build 15849800125100000 or v29 build 16476800119700000.");
            return false;
        }

        byte[]? payload = BuildPayload(ablImagePath, profile);
        if (payload == null)
            return false;

        Logger.Warn("The next command modifies the in-memory fastboot code and requests an unlock.");
        if (!Logger.Confirm("Send the verified exploit payload now?"))
        {
            Logger.Info("Unlock cancelled by user.");
            return false;
        }

        device.ClearReadBuffer();
        Logger.Info($"Sending {payload.Length:N0}-byte overflow payload...");
        if (!device.Write(payload, out string payloadError))
        {
            Logger.Error(payloadError);
            return false;
        }

        FastbootResponse overflowResponse = device.ReadResponse(2_000);
        LogResponse("Overflow response", overflowResponse);

        device.ClearReadBuffer();
        Logger.Info("Sending flash:unlock_token...");
        if (!device.WriteCommand("flash:unlock_token", out string commandError))
        {
            Logger.Error(commandError);
            return false;
        }

        FastbootResponse unlockResponse = device.ReadResponse(5_000);
        LogResponse("Unlock response", unlockResponse);
        if (unlockResponse.Status == FastbootResponseStatus.Failure)
        {
            Logger.Error("Fastboot rejected the unlock command.");
            return false;
        }

        device.Close();

        Logger.Info("If an unlock confirmation appears on the headset, approve it with the volume and power buttons.");
        Logger.Info("If the headset leaves fastboot, manually return to fastboot so the result can be verified.");

        if (!WaitForUnlockedState(TimeSpan.FromMinutes(3)))
        {
            Logger.Error("The unlock could not be confirmed. QuestStack will not report success without Device unlocked: true.");
            return false;
        }

        Logger.Success("Verified by fastboot: Device unlocked: true.");
        return true;
    }

    internal static byte[]? BuildPayload(string ablImagePath, string buildNumber)
    {
        if (!PatchProfiles.TryGetValue(buildNumber, out PatchProfile? profile))
            return null;

        return BuildPayload(ablImagePath, profile);
    }

    private static byte[]? BuildPayload(string ablImagePath, PatchProfile profile)
    {
        byte[] ablImage;
        try
        {
            ablImage = File.ReadAllBytes(ablImagePath);
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not read ABL image: {ex.Message}");
            return null;
        }

        byte[] loaderContainer = ablImage;
        int peOffset = FindLinuxLoaderPe(loaderContainer, profile);
        bool decompressedFromAbl = false;

        if (peOffset < 0)
        {
            foreach (byte[] decompressedSection in AblImageExtractor.DecompressGuidedLzmaSections(ablImage))
            {
                int candidateOffset = FindLinuxLoaderPe(decompressedSection, profile);
                if (candidateOffset < 0)
                    continue;

                loaderContainer = decompressedSection;
                peOffset = candidateOffset;
                decompressedFromAbl = true;
                break;
            }
        }

        if (peOffset < 0)
        {
            Logger.Error("Could not find the expected LinuxLoader PE inside abl.img.");
            Logger.Error($"The clean bytes for build {profile.BuildNumber} were not present at offset 0x{profile.PatchOffset:X}.");
            Logger.Error("Refusing to send an unverified payload.");
            return null;
        }

        int loaderBytesNeeded = profile.PatchOffset + profile.PatchBytes.Length;
        if (peOffset + loaderBytesNeeded > loaderContainer.Length)
        {
            Logger.Error("The LinuxLoader PE is truncated.");
            return null;
        }

        byte[] payload = Enumerable.Repeat((byte)0x0c, OverflowSize + loaderBytesNeeded).ToArray();
        Array.Copy(loaderContainer, peOffset, payload, OverflowSize, loaderBytesNeeded);
        Array.Copy(profile.PatchBytes, 0, payload, OverflowSize + profile.PatchOffset, profile.PatchBytes.Length);

        if (decompressedFromAbl)
            Logger.Success($"Extracted LinuxLoader PE from the ABL LZMA firmware volume at offset 0x{peOffset:X}.");
        else
            Logger.Success($"Found LinuxLoader PE at input offset 0x{peOffset:X}.");
        Logger.Info($"Applied build {profile.BuildNumber} patch at PE offset 0x{profile.PatchOffset:X}.");
        Logger.Success($"Built verified payload: {payload.Length:N0} bytes.");
        return payload;
    }

    private static int FindLinuxLoaderPe(byte[] image, PatchProfile profile)
    {
        int lastCandidate = image.Length - profile.PatchOffset - profile.ExpectedBytes.Length;
        for (int index = 0; index <= lastCandidate; index++)
        {
            if (image[index] != (byte)'M' || image[index + 1] != (byte)'Z')
                continue;

            int patchAddress = index + profile.PatchOffset;
            if (MatchesAt(image, patchAddress, profile.ExpectedBytes) ||
                MatchesAt(image, patchAddress, profile.PatchBytes))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool MatchesAt(byte[] source, int offset, byte[] expected)
    {
        if (offset < 0 || offset + expected.Length > source.Length)
            return false;

        for (int index = 0; index < expected.Length; index++)
        {
            if (source[offset + index] != expected[index])
                return false;
        }

        return true;
    }

    private static FastbootUsbDevice? WaitForDevice(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        int lastReportedSeconds = -1;

        while (DateTime.UtcNow < deadline)
        {
            var device = new FastbootUsbDevice(QuestVendorId, FastbootProductId);
            if (device.TryConnect())
            {
                Logger.Success("Quest fastboot interface detected.");
                return device;
            }

            device.Dispose();
            int remainingSeconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
            if (remainingSeconds != lastReportedSeconds && remainingSeconds % 10 == 0)
            {
                Logger.Info($"Waiting for fastboot... {remainingSeconds}s remaining");
                lastReportedSeconds = remainingSeconds;
            }

            Thread.Sleep(500);
        }

        return null;
    }

    private static bool WaitForUnlockedState(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        bool sawLockedState = false;

        while (DateTime.UtcNow < deadline)
        {
            using var device = new FastbootUsbDevice(QuestVendorId, FastbootProductId);
            if (device.TryConnect())
            {
                Dictionary<string, string>? info = device.GetDeviceInfo();
                if (info != null)
                {
                    if (IsUnlocked(info))
                        return true;

                    if (!sawLockedState)
                    {
                        Logger.Warn("Fastboot is still reporting Device unlocked: false. Waiting for confirmation...");
                        sawLockedState = true;
                    }
                }
            }

            Thread.Sleep(1_000);
        }

        return false;
    }

    private static bool IsUnlocked(IReadOnlyDictionary<string, string> deviceInfo)
    {
        return deviceInfo.TryGetValue("Device unlocked", out string? value) &&
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogResponse(string label, FastbootResponse response)
    {
        string text = string.IsNullOrWhiteSpace(response.Text) ? "<no response>" : response.Text.Replace('\n', ' ');
        switch (response.Status)
        {
            case FastbootResponseStatus.Success:
                Logger.Success($"{label}: {text}");
                break;
            case FastbootResponseStatus.Failure:
                Logger.Warn($"{label}: {text}");
                break;
            default:
                Logger.Warn($"{label}: timed out ({text})");
                break;
        }
    }

    private sealed record PatchProfile(
        string BuildNumber,
        int PatchOffset,
        byte[] ExpectedBytes,
        byte[] PatchBytes);
}

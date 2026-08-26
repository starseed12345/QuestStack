namespace QuestStack;

internal sealed record DeviceIdentity(
    string Serial,
    string Model,
    string Product,
    string Device,
    string Build,
    string Fingerprint,
    int ActiveSlot)
{
    public int InactiveSlot => ActiveSlot == 0 ? 1 : 0;
    public string ActiveSuffix => Firmware.SlotSuffix(ActiveSlot);
    public string InactiveSuffix => Firmware.SlotSuffix(InactiveSlot);

    public bool IsSupportedQuest1 =>
        Build == Firmware.RequiredBuild &&
        (Device.Equals("monterey", StringComparison.OrdinalIgnoreCase) ||
         Product.Equals("monterey", StringComparison.OrdinalIgnoreCase)) &&
        Fingerprint.StartsWith("oculus/vr_monterey/monterey:", StringComparison.OrdinalIgnoreCase);
}

internal static class DeviceProbe
{
    public static DeviceIdentity? Read()
    {
        DeviceIdentity? identity = ReadIdentity();
        if (identity == null)
            return null;

        int slot = ReadCurrentSlot();
        return slot < 0 ? null : identity with { ActiveSlot = slot };
    }

    public static DeviceIdentity? ReadIdentity()
    {
        if (!Adb.IsConnected())
            return null;

        string? serial = Adb.GetSerial();
        string? fingerprint = Adb.GetProp("ro.build.fingerprint");
        string? build = Adb.GetProp("ro.build.version.incremental");
        string? device = Adb.GetProp("ro.product.device");
        string? product = Adb.GetProp("ro.build.product") ?? Adb.GetProp("ro.product.name");
        string? model = Adb.GetProp("ro.product.model");

        if (serial == null || fingerprint == null || build == null || device == null || product == null)
            return null;

        return new DeviceIdentity(
            serial,
            model ?? "Meta Quest",
            product,
            device,
            build,
            fingerprint,
            -1);
    }

    public static int ReadCurrentSlot()
    {
        string? slotValue = Adb.GetProp("ro.boot.slot_suffix") ?? Adb.GetProp("ro.boot.slot");
        return ParseSlot(slotValue);
    }

    internal static int ParseSlot(string? value)
    {
        return value?.Trim() switch
        {
            "_a" or "a" or "0" => 0,
            "_b" or "b" or "1" => 1,
            _ => -1
        };
    }
}

using System.Reflection;
using System.Security.Cryptography;

namespace QuestStack;

internal static class BundledAdb
{
    public const string Version = "37.0.1";

    private const string ResourcePrefix = "QuestStack.PlatformTools.";
    private static readonly Lazy<PreparedTools> Prepared = new(Prepare, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly record struct ToolFile(string Name, string Sha256, bool Executable = false);
    private readonly record struct PreparedTools(string Directory, string AdbPath, string FastbootPath);

    public static string ExecutablePath => Prepared.Value.AdbPath;
    public static string FastbootExecutablePath => Prepared.Value.FastbootPath;
    public static string ToolsDirectory => Prepared.Value.Directory;

    public static void ValidateEmbeddedResources()
    {
        (_, ToolFile[] files) = GetRuntimeFiles();
        Assembly assembly = typeof(BundledAdb).Assembly;
        foreach (ToolFile file in files)
        {
            using Stream resource = assembly.GetManifestResourceStream(ResourcePrefix + file.Name)
                ?? throw new InvalidOperationException($"ADB resource is missing: {file.Name}");
            string actual = Convert.ToHexString(SHA256.HashData(resource));
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"ADB resource failed its checksum: {file.Name}");
        }
    }

    private static PreparedTools Prepare()
    {
        (string runtimeName, ToolFile[] files) = GetRuntimeFiles();
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.GetTempPath();

        string directory = Path.Combine(localData, "QuestStack", "platform-tools", Version, runtimeName);
        Directory.CreateDirectory(directory);

        foreach (ToolFile file in files)
            ExtractVerifiedFile(directory, file);

        string adb = Path.Combine(directory, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        string fastboot = Path.Combine(directory, OperatingSystem.IsWindows() ? "fastboot.exe" : "fastboot");
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                      UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            foreach (ToolFile file in files.Where(file => file.Executable))
                File.SetUnixFileMode(Path.Combine(directory, file.Name), mode);
        }

        return new PreparedTools(directory, adb, fastboot);
    }

    private static (string RuntimeName, ToolFile[] Files) GetRuntimeFiles()
    {
        if (OperatingSystem.IsWindows() && Environment.Is64BitProcess)
        {
            return ("win-x64",
            [
                new("adb.exe", "B4A6B455702684652CCCF7B46258B29E653538904359A58FD4931CF3EF286B3F", true),
                new("fastboot.exe", "B2D9CBFF4CE9AE7EB448CFC831BAFC867935F50F5BE38F8F81057FD7EB3B8D86", true),
                new("mke2fs.exe", "308304852B1F1A1449E9D9115332AB908B8A54B99F460ED1DF5D109DECEF5BE7", true),
                new("mke2fs.conf", "AD58A58DCDD24D85055814CA9CAC67DB89D4E67C434E96774BDCE0D0A007D067"),
                new("make_f2fs.exe", "A1D8E18474636972E4EBAF2617AC17C880E76642FAD7FA3E51AE5CD3C1FE5D17", true),
                new("make_f2fs_casefold.exe", "9AC076516C2A1F620DF24E148660BECF3E7D4102B46DDDA66970D35C59DB2962", true),
                new("AdbWinApi.dll", "C1D653030B4BDE65D3E07E4D0B0979E17BE56DF1436CDD15528630F27808050D"),
                new("AdbWinUsbApi.dll", "0710E894D9B40F71A670C13C694079D564C92C1279DA382CFE4850983AAEBE1B"),
                new("libwinpthread-1.dll", "F2044C755E39EFC5D47F47C5A942178DD6D79DC8945BB182DC8753D10E2A4269"),
                new("NOTICE.txt", "AE63BDEFB1C951689803E83619AD90D86BCF423323AB9906392555DE7338D4E7")
            ]);
        }

        if (OperatingSystem.IsLinux() && Environment.Is64BitProcess)
        {
            return ("linux-x64",
            [
                new("adb", "A902BE8F45C6C62E76C9EFAF6947A0FA747C9CABD89A2AC8E0D16ECB30B3ED01", true),
                new("fastboot", "A686E2C7E8DC9CF4CBA0CB8A2EEF05F7B2BD682C925ABD032FE203215D80B618", true),
                new("mke2fs", "78D622CB0BF46409E2C040B729BCBDA9F986E162CFD1FE8F98DD2B4EFEAC6EE9", true),
                new("mke2fs.conf", "AD58A58DCDD24D85055814CA9CAC67DB89D4E67C434E96774BDCE0D0A007D067"),
                new("make_f2fs", "16D06D185B62C7401E1B1468C1B7830C35F18F1CD78BE21333CF340C1FE78A4C", true),
                new("make_f2fs_casefold", "DDF5BDAD689A9AF9314865868E0FE9310B52F8DC0369EE3F01B8FFBB3B89A90F", true),
                new("NOTICE.txt", "4AB640FC5F3CE7114F0B75F14BD719D1F200331E898818CF25EFB58AE06539F3")
            ]);
        }

        if (OperatingSystem.IsMacOS() && Environment.Is64BitProcess)
        {
            return ("osx-universal",
            [
                new("adb", "1811E253B21B12CBFDA7201EBAF86C10E7DDCB5C606A7A81F7C82B4C429C2D3B", true),
                new("fastboot", "C97AA28970BA9A46023A8CA52A78DD6547C2C1C4133F9715D4FE551E1DFAE152", true),
                new("mke2fs", "29A460F471BDF5CB1C0666BC3AD8E0A1B02788A04E0865A6068AEDDC0065AFD0", true),
                new("mke2fs.conf", "AD58A58DCDD24D85055814CA9CAC67DB89D4E67C434E96774BDCE0D0A007D067"),
                new("make_f2fs", "5A8DB8FDD99910931BCDC8AC1184603C05F51EFE982A4E3B8940B803A5A60814", true),
                new("make_f2fs_casefold", "4DD12DDDE8C4758B056442BF3B3541B5E1CF7B6CEF19167E7C369DE1ED85505C", true),
                new("NOTICE.txt", "8E6D53255B372E55D46152097060353E1169B6CF3F8A7C9A262E6B8CD29176A1")
            ]);
        }

        throw new PlatformNotSupportedException("ADB supports Windows x64, Linux x64, and macOS x64/ARM64.");
    }

    private static void ExtractVerifiedFile(string directory, ToolFile file)
    {
        string destination = Path.Combine(directory, file.Name);
        if (File.Exists(destination) && HashMatches(destination, file.Sha256))
            return;

        Assembly assembly = typeof(BundledAdb).Assembly;
        using Stream resource = assembly.GetManifestResourceStream(ResourcePrefix + file.Name)
            ?? throw new InvalidOperationException($"ADB resource is missing: {file.Name}");

        string temporary = destination + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                resource.CopyTo(output);

            if (!HashMatches(temporary, file.Sha256))
                throw new InvalidDataException($"ADB resource failed its checksum: {file.Name}");

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool HashMatches(string path, string expected)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            string actual = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

using System.Reflection;
using System.Security.Cryptography;

namespace QuestStack;

internal static class BundledAdb
{
    public const string Version = "37.0.1";

    private const string ResourcePrefix = "QuestStack.PlatformTools.";
    private static readonly Lazy<string> PreparedExecutable = new(Prepare, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly record struct ToolFile(string Name, string Sha256);

    public static string ExecutablePath => PreparedExecutable.Value;

    public static void ValidateEmbeddedResources()
    {
        (_, ToolFile[] files) = GetRuntimeFiles();
        Assembly assembly = typeof(BundledAdb).Assembly;
        foreach (ToolFile file in files)
        {
            using Stream resource = assembly.GetManifestResourceStream(ResourcePrefix + file.Name)
                ?? throw new InvalidOperationException($"Bundled ADB resource is missing: {file.Name}");
            string actual = Convert.ToHexString(SHA256.HashData(resource));
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Bundled ADB resource failed its checksum: {file.Name}");
        }
    }

    private static string Prepare()
    {
        (string runtimeName, ToolFile[] files) = GetRuntimeFiles();
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.GetTempPath();

        string directory = Path.Combine(localData, "QuestStack", "platform-tools", Version, runtimeName);
        Directory.CreateDirectory(directory);

        foreach (ToolFile file in files)
            ExtractVerifiedFile(directory, file);

        string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                      UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                      UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(executable, mode);
        }

        return executable;
    }

    private static (string RuntimeName, ToolFile[] Files) GetRuntimeFiles()
    {
        if (OperatingSystem.IsWindows() && Environment.Is64BitProcess)
        {
            return ("win-x64",
            [
                new("adb.exe", "B4A6B455702684652CCCF7B46258B29E653538904359A58FD4931CF3EF286B3F"),
                new("AdbWinApi.dll", "C1D653030B4BDE65D3E07E4D0B0979E17BE56DF1436CDD15528630F27808050D"),
                new("AdbWinUsbApi.dll", "0710E894D9B40F71A670C13C694079D564C92C1279DA382CFE4850983AAEBE1B"),
                new("libwinpthread-1.dll", "F2044C755E39EFC5D47F47C5A942178DD6D79DC8945BB182DC8753D10E2A4269"),
                new("NOTICE.txt", "38EC8C6F5B7799C223FFEAB1F9E81C2D5FC67B5E56D6424F649630CA1EE1A811")
            ]);
        }

        if (OperatingSystem.IsLinux() && Environment.Is64BitProcess)
        {
            return ("linux-x64",
            [
                new("adb", "A902BE8F45C6C62E76C9EFAF6947A0FA747C9CABD89A2AC8E0D16ECB30B3ED01"),
                new("NOTICE.txt", "C29DA8F704720FA1D3D802B834B86D9C023F20AE2596A8F2A4E17ED5490B17AE")
            ]);
        }

        if (OperatingSystem.IsMacOS() && Environment.Is64BitProcess)
        {
            return ("osx-universal",
            [
                new("adb", "1811E253B21B12CBFDA7201EBAF86C10E7DDCB5C606A7A81F7C82B4C429C2D3B"),
                new("NOTICE.txt", "356CC66516060E4DA6BDC53E6F0BBB8E8101673F326B73C68D535EDDD04DD7B1")
            ]);
        }

        throw new PlatformNotSupportedException("Bundled ADB supports Windows x64, Linux x64, and macOS x64/ARM64.");
    }

    private static void ExtractVerifiedFile(string directory, ToolFile file)
    {
        string destination = Path.Combine(directory, file.Name);
        if (File.Exists(destination) && HashMatches(destination, file.Sha256))
            return;

        Assembly assembly = typeof(BundledAdb).Assembly;
        using Stream resource = assembly.GetManifestResourceStream(ResourcePrefix + file.Name)
            ?? throw new InvalidOperationException($"Bundled ADB resource is missing: {file.Name}");

        string temporary = destination + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                resource.CopyTo(output);

            if (!HashMatches(temporary, file.Sha256))
                throw new InvalidDataException($"Bundled ADB resource failed its checksum: {file.Name}");

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

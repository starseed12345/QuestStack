namespace QuestStack;

internal static class AppDataPaths
{
    public static string RootDirectory => Path.Combine(GetPersistentLocalData(), "QuestStack");
    public static string CacheDirectory => Path.Combine(RootDirectory, "cache");
    public static string BackupDirectory => Path.Combine(RootDirectory, "backups");

    private static string GetPersistentLocalData()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localData))
            return localData;

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
            throw new InvalidOperationException("A persistent user data directory is not available.");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(profile, "Library", "Application Support");
        if (OperatingSystem.IsLinux())
            return Path.Combine(profile, ".local", "share");

        throw new PlatformNotSupportedException("A persistent local application data path is required.");
    }
}

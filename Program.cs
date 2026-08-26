using Avalonia;

namespace QuestStack;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
            return SelfTests.Run(
                ReadOption(args, "--abl"),
                ReadOption(args, "--ionstack"),
                ReadOption(args, "--firmware")) ? 0 : 1;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<QuestStackApp>()
            .UsePlatformDetect();
    }

    private static string? ReadOption(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (index + 1 >= args.Length)
                return null;
            return args[index + 1];
        }

        return null;
    }
}

namespace QuestStack;

internal enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
    Step
}

internal sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Message);

internal static class Logger
{
    public static event Action<LogEntry>? EntryWritten;

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Success(string message) => Write(LogLevel.Success, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Step(int current, int total, string message)
    {
        Write(LogLevel.Step, $"{current}/{total}  {message}");
    }

    public static bool Confirm(string message)
    {
        Console.Write($"[?] {message} [Y/N] ");
        Console.Out.Flush();

        if (Console.IsInputRedirected)
            return ConfirmFromRedirectedInput();

        while (true)
        {
            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Y)
            {
                Console.WriteLine("Y");
                return true;
            }

            if (key is ConsoleKey.N or ConsoleKey.Escape)
            {
                Console.WriteLine("N");
                return false;
            }
        }
    }

    private static bool ConfirmFromRedirectedInput()
    {
        while (true)
        {
            string? answer = Console.ReadLine()?.Trim();
            if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrEmpty(answer) ||
                string.Equals(answer, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer, "no", StringComparison.OrdinalIgnoreCase))
                return false;
        }
    }

    private static void Write(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        Action<LogEntry>? handlers = EntryWritten;

        if (handlers == null)
        {
            try { Console.WriteLine($"{Prefix(level)} {message}"); } catch { }
            return;
        }

        foreach (Action<LogEntry> handler in handlers.GetInvocationList().Cast<Action<LogEntry>>())
        {
            try { handler(entry); } catch { }
        }
    }

    private static string Prefix(LogLevel level)
    {
        return level switch
        {
            LogLevel.Success => "[+]",
            LogLevel.Warning => "[!]",
            LogLevel.Error => "[-]",
            LogLevel.Step => "[>]",
            _ => "[*]"
        };
    }
}

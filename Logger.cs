namespace QuestStack;

internal static class Logger
{
    public static void Info(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("[*] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    public static void Success(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[+] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    public static void Warn(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[!] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    public static void Error(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[-] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    public static void Step(int current, int total, string msg)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write($"[{current}/{total}] ");
        Console.ResetColor();
        Console.WriteLine(msg);
    }

    public static bool Confirm(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"[?] {msg} [Y/N] ");
        Console.ResetColor();
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

    public static void Pause(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"[?] {msg} Press Enter to continue: ");
        Console.ResetColor();
        Console.Out.Flush();
        Console.ReadLine();
    }
}

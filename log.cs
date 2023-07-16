class Log
{
    private static string logfile = "logfile.txt";
    private static bool echo = false;
    private static int nr = 0;

    public static void SetLogFile(string file)
    {
        logfile = file;
    }

    public static void EchoToConsole(bool state)
    {
        echo = state;
    }

    public static void DoLog(string what)
    {
        File.AppendAllText(logfile, $"[{nr}] " + what + Environment.NewLine);

        nr++;

        if (echo)
            Console.WriteLine(what);
    }
}

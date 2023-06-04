class Log
{
    private static string logfile = "logfile.txt";
    private static bool echo = false;

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
        File.AppendAllText(logfile, what + Environment.NewLine);

        if (echo)
            Console.WriteLine(what);
    }
}

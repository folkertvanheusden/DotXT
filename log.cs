class Log
{
    private static string _logfile = "logfile.txt";
    private static bool _echo = false;
    private static int _nr = 0;
    private static uint _address = uint.MaxValue;

    public static void SetLogFile(string file)
    {
        _logfile = file;
    }

    public static void EchoToConsole(bool state)
    {
        _echo = state;
    }

    public static void SetAddress(uint addr)
    {
        _address = addr;
    }

    public static void DoLog(string what, bool is_meta = false)
    {
        File.AppendAllText(_logfile, $"[{_nr} | {_address:X6}] " + (is_meta ? "; " : "") + what + Environment.NewLine);

        _nr++;

        if (_echo)
            Console.WriteLine(what);
    }
}

class Log
{
    private static string _logfile = "logfile.txt";
    private static bool _echo = false;
    private static int _nr = 0;
    private static ushort _cs = 0;
    private static ushort _ip = 0;

    public static void SetLogFile(string file)
    {
        _logfile = file;
    }

    public static void EchoToConsole(bool state)
    {
        _echo = state;
    }

    public static void SetAddress(ushort cs, ushort ip)
    {
        _cs = cs;
        _ip = ip;
    }

    public static void DoLog(string what, bool is_meta = false)
    {
        File.AppendAllText(_logfile, $"[{_nr} | {_cs:X04}:{_ip:X04}] " + (is_meta ? "; " : "") + what + Environment.NewLine);

        _nr++;

        if (_echo)
            Console.WriteLine(what);
    }
}

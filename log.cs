using System.Collections.Generic;

class Log
{
    private static string _logfile = "logfile.txt";
    private static string _disassembly = null;
    private static bool _echo = false;
    private static int _nr = 0;
    private static ushort _cs = 0;
    private static ushort _ip = 0;
    private static SortedDictionary<string, Tuple<string, string> > disassembly = new();
    private static readonly System.Threading.Lock _disassembly_lock = new();

    public static void SetLogFile(string file)
    {
        _logfile = file;
    }

    public static void SetDisassemblyFile(string file)
    {
        _disassembly = file;
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

    public static void Disassemble(string prefix, string assembly)
    {
#if DEBUG
        lock(_disassembly_lock)
        {
            string addr = $"{_cs:X04}:{_ip:X04}";

            if (disassembly.ContainsKey(addr))
            {
                if (disassembly[addr].Item1 != prefix)
                {
                    disassembly[addr] = new Tuple<string, string>(null, assembly);
                }
            }
            else
            {
                disassembly.Add(addr, new Tuple<string, string>(prefix, assembly));
            }
        }
#endif

        DoLog(prefix + " " + assembly);
    }

    public static void EmitDisassembly()
    {
#if DEBUG
        if (_disassembly != null)
        {
            lock(_disassembly_lock)
            {
                foreach(KeyValuePair<string, Tuple<string, string> > entry in disassembly)
                {
                    // if (entry.Value.Item1 == null)
                    File.AppendAllText(_disassembly, $"{entry.Key} {entry.Value.Item2}" + Environment.NewLine);
                    //else
                    //File.AppendAllText(_disassembly, $"{entry.Key} {entry.Value.Item2} {entry.Value.Item1}" + Environment.NewLine);
                }
            }
        }
#endif
    }

    public static void DoLog(string what, bool is_meta = false)
    {
        File.AppendAllText(_logfile, $"[{_nr} | {_cs:X04}:{_ip:X04}] " + (is_meta ? "; " : "") + what + Environment.NewLine);
        _nr++;

        if (_echo)
            Console.WriteLine(what);
    }
}

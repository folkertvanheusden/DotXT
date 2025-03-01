using System.Collections.Generic;

internal enum LogLevel { TRACE, DEBUG, INFO, WARNING, ERRROR, FATAL };

class Log
{
    private static string _logfile = null;
    private static string _disassembly = null;
    private static bool _echo = false;
    private static SortedDictionary<string, Tuple<string, string> > disassembly = new();
    private static readonly System.Threading.Lock _disassembly_lock = new();
    private static readonly System.Threading.Lock _logging_lock = new();  // for windows

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

    public static void Disassemble(string prefix, string assembly)
    {
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
                    File.AppendAllText(_disassembly, $"{entry.Value.Item1} {entry.Value.Item2}" + Environment.NewLine);
                }
            }
        }
#endif
    }

    public static void DoLog(string what, LogLevel ll)
    {
        if (_logfile == null && _echo == false)
            return;

        //string output = $"{ll} " + (ll != LogLevel.TRACE ? "; " : "") + what + Environment.NewLine;
        string output = what + Environment.NewLine;

        lock(_logging_lock)
        {
            File.AppendAllText(_logfile, output);
        }

        if (_echo)
            Console.WriteLine(what);
    }

    public static void DoLog(string what, bool is_meta = false)
    {
        if (_logfile == null && _echo == false)
            return;

        //string output = $"{LogLevel.TRACE} " + (is_meta ? "; " : "") + what + Environment.NewLine;
        string output = what + Environment.NewLine;

        lock(_logging_lock)
        {
            File.AppendAllText(_logfile, output);
        }

        if (_echo)
            Console.WriteLine(what);
    }
}

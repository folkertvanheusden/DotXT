using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

internal enum LogLevel { TRACE, DEBUG, INFO, WARNING, ERROR, FATAL };

class Log
{
    private static string _logfile = null;
    private static string _disassembly = null;
    private static bool _echo = false;
    private static long _clock = 0;
    private static ushort _cs = 0;
    private static ushort _ip = 0;
    private static LogLevel _ll = LogLevel.INFO;
    private static SortedDictionary<string, Tuple<string, string> > disassembly = new();
    private static readonly System.Threading.Lock _disassembly_lock = new();
    private static readonly System.Threading.Lock _logging_lock = new();  // for windows
    private static BlockingCollection<string> _log_queue = new(1024);
    private static Thread _thread = null;

    public static void SetLogFile(string file)
    {
        _logfile = file;
        _thread = new Thread(Log.LogWriter);
        _thread.Name = "LogWriter";
        _thread.Start();
    }

    public static void EndLogging()
    {
        _log_queue.CompleteAdding();
        _thread.Join();
    }

    public static void SetLogLevel(LogLevel ll)
    {
        _ll = ll;
    }

    public static LogLevel StringToLogLevel(string name)
    {
        name = name.ToLower();
        if (name == "trace")
            return LogLevel.TRACE;
        if (name == "debug")
            return LogLevel.DEBUG;
        if (name == "info")
            return LogLevel.INFO;
        if (name == "warning")
            return LogLevel.WARNING;
        if (name == "error")
            return LogLevel.ERROR;
        if (name == "fatal")
            return LogLevel.FATAL;
        Log.Cnsl($"Loglevel \"{name}\" not understood, using debug instead");
        return LogLevel.DEBUG;
    }

    public static void SetMeta(long clock, ushort cs, ushort ip)
    {
        _clock = clock;
        _cs = cs;
        _ip = ip;
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
        DoLog(prefix + " " + assembly, LogLevel.DEBUG);
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

    public static void LogWriter()
    {
        FileStream _file_handle = new FileStream(_logfile, FileMode.Open, FileAccess.ReadWrite);

        while(_log_queue.IsCompleted == false || _log_queue.Count > 0)
        {
            string item;
            if (_log_queue.TryTake(out item, 1500))
            {
                if (item == null)
                    _file_handle.SetLength(0);
                else
                {
                    byte[] data = new UTF8Encoding(true).GetBytes(item + Environment.NewLine);
                    _file_handle.Write(data, 0, data.Length);
                }
            }
            else
            {
                _file_handle.Flush();
            }
        }

        _file_handle.Close();
    }

    public static void TruncateLogfile()
    {
        _log_queue.Add(null);
    }

    public static void Cnsl(string what)
    {
        Console.WriteLine(what);

        if (_logfile != null)
            _log_queue.Add(what);
    }

    public static void DoLog(string what, LogLevel ll)
    {
        if ((_logfile == null && _echo == false) || ll < _ll)
            return;

        string output = $"{_clock} {_cs:X4}:{_ip:X4} | {ll} | " + what;
        _log_queue.Add(output);

        if (_echo)
            Log.Cnsl(what);
    }
}

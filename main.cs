using DotXT;

string test = "";
List<string> floppies = new();

TMode mode = TMode.NotSet;

ushort initial_cs = 0;
ushort initial_ip = 0;
bool set_initial_ip = false;

bool run_IO = true;
uint load_test_at = 0xffffffff;

bool json_processing = false;
bool prompt = true;

uint ram_size = 1024;

List<Rom> roms = new();

string key_mda = "mda";
string key_cga = "cga";

List<string> ide = new();
Dictionary<string, List<Tuple<string, int> > > consoles = new();
FloppyDisk floppy_controller = null;

bool throttle = false;

bool use_midi = false;
bool use_rtc = false;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-h") {
        Console.WriteLine("-t file   load 'file'");
        Console.WriteLine("-T addr   sets the load-address for -t");
        Console.WriteLine("-x type   set type for -T: binary, blank");
        Console.WriteLine("-l file   log to file");
        Console.WriteLine("-L        set loglevel (trace, debug, ...)");
        Console.WriteLine("-R file,address   load rom \"file\" to address(xxxx:yyyy)");
        Console.WriteLine("          e.g. load the bios from f000:e000");
        Console.WriteLine("-s size   RAM size in kilobytes, decimal");
        Console.WriteLine("-F file   load floppy image (multiple for drive A-D)");
        Console.WriteLine("-D file   disassemble to file");
        Console.WriteLine("-I        disable I/O ports");
        Console.WriteLine("-S        try to run at real speed");
        Console.WriteLine("-P        skip prompt");
        Console.WriteLine("-O        enable option. currently: midi, rtc");
        Console.WriteLine("-X file   add an XT-IDE harddisk (must be 614/4/17 CHS)");
        Console.WriteLine($"-p device,type,port   port to listen on. type must be \"telnet\", \"http\" or \"vnc\" for now. device can be \"{key_cga}\" or \"{key_mda}\".");
        Console.WriteLine("-o cs,ip  start address (in hexadecimal)");
        System.Environment.Exit(0);
    }
    else if (args[i] == "-t")
        test = args[++i];
    else if (args[i] == "-S")
        throttle = true;
    else if (args[i] == "-T")
        load_test_at = (uint)GetValue(args[++i], true);
    else if (args[i] == "-O")
    {
        string what = args[++i];
        if (what == "midi")
            use_midi = true;
        else if (what == "rtc")
            use_rtc = true;
        else
        {
            Console.WriteLine($"{what} is not understood");
            System.Environment.Exit(1);
        }
    }
    else if (args[i] == "-p")
    {
        string[] parts = args[++i].Split(',');
        if (parts[0] != key_cga && parts[0] != key_mda)
        {
            Console.WriteLine($"{parts[0]} is not understood");
            System.Environment.Exit(1);
        }
        if (parts[1] != "telnet" && parts[1] != "http" && parts[1] != "vnc")
        {
            Console.WriteLine($"{parts[1]} is not understood");
            System.Environment.Exit(1);
        }

        var console_device = new Tuple<string, int>(parts[1], Convert.ToInt32(parts[2], 10));
        if (consoles.ContainsKey(parts[0]))
            consoles[parts[0]].Add(console_device);
        else
        {
            List<Tuple<string, int> > console_devices = new();
            console_devices.Add(console_device);
            consoles.Add(parts[0], console_devices);
        }
    }
    else if (args[i] == "-x") {
        string type = args[++i];

        if (type == "binary")
            mode = TMode.Binary;
        else if (type == "blank")
            mode = TMode.Blank;
        else
        {
            Console.WriteLine($"{type} is not understood");
            System.Environment.Exit(1);
        }
    }
    else if (args[i] == "-X")
        ide.Add(args[++i]);
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else if (args[i] == "-L")
        Log.SetLogLevel(Log.StringToLogLevel(args[++i]));
    else if (args[i] == "-D")
        Log.SetDisassemblyFile(args[++i]);
    else if (args[i] == "-I")
        run_IO = false;
    else if (args[i] == "-F")
        floppies.Add(args[++i]);
    else if (args[i] == "-d")
        json_processing = true;
    else if (args[i] == "-P")
        prompt = false;
    else if (args[i] == "-s")
        ram_size = (uint)GetValue(args[++i], false);
    else if (args[i] == "-R")
    {
        string[] parts = args[++i].Split(',');
        string file = parts[0];

        string[] aparts = parts[1].Split(':');
        uint seg = (uint)GetValue(aparts[0], true);
        uint ip = (uint)GetValue(aparts[1], true);
        uint addr = seg * 16 + ip;

        Console.WriteLine($"Loading {file} to {addr:X06}");

        roms.Add(new Rom(file, addr));
    }
    else if (args[i] == "-o")
    {
        string[] parts = args[++i].Split(',');

        initial_cs = (ushort)GetValue(parts[0], true);
        initial_ip = (ushort)GetValue(parts[1], true);

        set_initial_ip = true;
    }
    else
    {
        Console.WriteLine($"{args[i]} is not understood");

        System.Environment.Exit(1);
    }
}

if (test == "")
{
    Console.WriteLine("DotXT, (C) 2023-2025 by Folkert van Heusden");
    Console.WriteLine("Released in the public domain");
}

Console.TreatControlCAsInput = true;

#if DEBUG
Console.WriteLine("Debug build");
#endif

List<Device> devices = new();

if (mode != TMode.Blank)
{
    Keyboard kb = new();
    devices.Add(kb);  // still needed because of clock ticks
    devices.Add(new PPI(kb));

    foreach(KeyValuePair<string, List<Tuple<string, int> > > current_console in consoles)
    {
        List<EmulatorConsole> console_instances = new();
        foreach(var c in current_console.Value)
        {
            if (c.Item1 == "telnet")
                console_instances.Add(new TelnetServer(kb, c.Item2));
            else if (c.Item1 == "http")
                console_instances.Add(new HTTPServer(kb, c.Item2));
            else if (c.Item1 == "vnc")
                console_instances.Add(new VNCServer(kb, c.Item2, true));
        }

        if (current_console.Key == key_mda)
            devices.Add(new MDA(console_instances));
        else if (current_console.Key == key_cga)
            devices.Add(new CGA(console_instances));
    }

    devices.Add(new i8253());

    if (floppies.Count() > 0)
    {
        floppy_controller = new FloppyDisk(floppies);
        devices.Add(floppy_controller);
    }

    if (ide.Count() > 0)
        devices.Add(new XTIDE(ide));

    if (use_midi)
        devices.Add(new MIDI());

    if (use_rtc)
        devices.Add(new RTC());
}

// Bus gets the devices for memory mapped i/o
Bus b = new Bus(ram_size * 1024, ref devices, ref roms);

var p = new P8086(ref b, test, mode, load_test_at, ref devices, run_IO);

if (set_initial_ip)
    p.set_ip(initial_cs, initial_ip);

if (json_processing)
{
    for(;;)
    {
        if (prompt)
            Console.Write("==>");

        string line = Console.ReadLine();
        Log.DoLog($"CMDLINE: {line}", LogLevel.DEBUG);

        string[] parts = line.Split(' ');

        if (line == "s")
        {
            Disassemble(b, p);
            p.SetIgnoreBreakpoints();
            p.Tick();
        }
        else if (line == "S")
        {
            Disassemble(b, p);
            do
            {
                p.SetIgnoreBreakpoints();
                p.Tick();
            }
            while(p.IsProcessingRep());
        }
        else if (line == "q")
            break;
        else if (parts[0] == "dolog")
        {
            Log.DoLog(line, LogLevel.INFO);
        }
        else if (parts[0] == "reset")
        {
            b.ClearMemory();
            p.Reset();
        }
        else if (parts[0] == "set")
        {
            CmdSet(parts, p, b);
        }
        else if (parts[0] == "get")
        {
            CmdGet(parts, p, b);
        }
        else if (line == "c")
        {
            p.ResetCrashCounter();

            Runner(p);
        }
        else if (line != "")
        {
            Console.WriteLine($"\"{line}\" not understood");
        }

        Console.Out.Flush();
    }
}
else
{
    RunnerParameters runner_parameters = new();
    runner_parameters.cpu = p;
    runner_parameters.exit = new();
    runner_parameters.disassemble = false;

    Thread thread = null;

    bool running = false;

    bool echo_state = false;
    Log.EchoToConsole(echo_state);

    for(;;)
    {
        Console.Write("==>");

        string line = Console.ReadLine();
        Log.DoLog($"CMDLINE: {line}", LogLevel.DEBUG);
        if (line == "")
            continue;

        try
        {
            string [] parts = line.Split(" ");

            if (parts[0] == "quit" || parts[0] == "q")
                break;

            if (parts[0] == "help")
            {
                Console.WriteLine("quit / q       terminate application");
                Console.WriteLine("step / s / S   invoke 1 instruction, \"S\": step over loop");
                Console.WriteLine($"stop           stop emulation (running: {running})");
                Console.WriteLine("start / go     start emulation");
                Console.WriteLine("reset          reset emulator");
                Console.WriteLine("disassemble / da  toggle disassembly while emulating");
                Console.WriteLine("echo           toggle logging to console");
                Console.WriteLine("lsfloppy       list configured floppies");
                Console.WriteLine("setfloppy x y  set floppy unit x (0 based) to file y");
                Console.WriteLine("get [reg|ram] [regname|address]  get value from a register/memory location");
                Console.WriteLine("set [reg|ram] [regname|address] value   set registers/memory to a value");
                Console.WriteLine("get/set        value/address can be decimal or hexadecimal (prefix with 0x)");
                Console.WriteLine("hd x           hexdump of a few bytes starting at address x");
                Console.WriteLine("hd cs:ip       hexdump of a few bytes starting at address cs:ip");
                Console.WriteLine("dr             dump all registers");
                Console.WriteLine("gbp / lbp      list breakpoints");
                Console.WriteLine("sbp x          set breakpoint");
                Console.WriteLine("dbp x          delete breakpoint");
                Console.WriteLine("cbp            remove all breakpoints");
                Console.WriteLine("stats x        \"x\" must be \"cpu-speed\" currently");
                Console.WriteLine("setll x        set loglevel (trace, debug, ...)");
            }
            else if (parts[0] == "s" || parts[0] == "step" || parts[0] == "S")
            {
                bool rc = true;

                if (parts[0] == "S")
                {
                    do
                    {
                        if (runner_parameters.disassemble)
                            Disassemble(b, p);

                        if (p.Tick() == false)
                        {
                            rc = false;
                            break;
                        }
                    }
                    while(p.IsProcessingRep());
                }
                else
                {
                    if (runner_parameters.disassemble)
                        Disassemble(b, p);

                    rc = p.Tick();
                }

                if (rc == false)
                {
                    string stop_reason = p.GetStopReason();
                    if (stop_reason != "")
                        Console.WriteLine(stop_reason);
                }
            }
            else if (parts[0] == "disassemble" || parts[0] == "da")
            {
                runner_parameters.disassemble = !runner_parameters.disassemble;
                Console.WriteLine(runner_parameters.disassemble ? "disassembly on" : "disassembly off");
                if (running)
                    Console.WriteLine("Please stop+start emulation to activate tracing");
            }
            else if (parts[0] == "setll")
            {
                if (parts.Length != 2)
                    Console.WriteLine("Parameter missing");
                else
                    Log.SetLogLevel(Log.StringToLogLevel(parts[1]));
            }
            else if (parts[0] == "stats")
            {
                if (parts.Length < 2)
                    Console.WriteLine("Parameter missing");
                else if (parts[1] == "cpu-speed")
                {
                    if (running)
                        MeasureSpeed(p, parts.Length == 3 && parts[2] == "-c");
                    else
                        Console.WriteLine("Emulation not running");
                }
                else if (parts[1] == "i8253" || parts[1] == "timers")
                {
                    foreach(var device in devices)
                    {
                        if (device is i8253)
                        {
                            var state = device.GetState();
                            foreach(var state_line in state)
                                Console.WriteLine(state_line);
                        }
                    }
                }
            }
            else if (parts[0] == "echo")
            {
                echo_state = !echo_state;
                Console.WriteLine(echo_state ? "echo on" : "echo off");
                Log.EchoToConsole(echo_state);
            }
            else if (parts[0] == "start" || parts[0] == "go")
            {
                if (running)
                    Console.WriteLine("Already running");
                else
                {
                    runner_parameters.exit.set(false);
                    thread = CreateRunnerThread(runner_parameters);
                    Console.WriteLine("OK");
                    running = true;
                }
            }
            else if (parts[0] == "stop")
            {
                if (running)
                {
                    runner_parameters.exit.set(true);
                    thread.Join();
                    running = false;
                }
                else
                {
                    Console.WriteLine("Not running");
                }
            }
            else if (parts[0] == "reset")
            {
                if (running)
                {
                    runner_parameters.exit.set(true);
                    thread.Join();
                }

                b.ClearMemory();
                p.Reset();

                runner_parameters.exit.set(false);
                thread = CreateRunnerThread(runner_parameters);
                running = true;
            }
            else if (parts[0] == "lsfloppy")
            {
                if (floppy_controller == null)
                    Console.WriteLine("No floppy drive configured");
                else
                {
                    for(int i=0; i<floppy_controller.GetUnitCount(); i++)
                        Console.WriteLine($"{i}] {floppy_controller.GetUnitFilename(i)}");
                }
            }
            else if (parts[0] == "setfloppy")
            {
                if (floppy_controller == null)
                    Console.WriteLine("No floppy drive configured");
                else if (parts.Length != 3)
                    Console.WriteLine("Number of parameters is incorrect");
                else
                {
                    int unit = int.Parse(parts[1]);
                    if (floppy_controller.SetUnitFilename(unit, parts[2]))
                        Console.WriteLine("OK");
                    else
                        Console.WriteLine("Failed: invalid unit number or file does not exist");
                }
            }
            else if (parts[0] == "set")
            {
                CmdSet(parts, p, b);
            }
            else if (parts[0] == "get")
            {
                CmdGet(parts, p, b);
            }
            else if (parts[0] == "gbp" || parts[0] == "lbp")
            {
                GetBreakpoints(p);
            }
            else if (parts[0] == "sbp")
            {
                if (running)
                    Console.WriteLine("Please stop emulation first");
                else
                {
                    uint addr = (uint)GetValue(parts[1], false);
                    p.AddBreakpoint(addr);
                }
            }
            else if (parts[0] == "dbp")
            {
                if (running)
                    Console.WriteLine("Please stop emulation first");
                else
                {
                    uint addr = (uint)GetValue(parts[1], false);
                    p.DelBreakpoint(addr);
                }
            }
            else if (parts[0] == "cbp")
            {
                if (running)
                    Console.WriteLine("Please stop emulation first");
                else
                {
                    p.ClearBreakpoints();
                }
            }
            else if (parts[0] == "hd")
            {
                uint addr = (uint)GetValue(parts[1], true);

                for(int i=0; i<256; i+=16)
                    Console.WriteLine($"{addr + i:X6} {p.HexDump((uint)(addr + i))} {p.CharDump((uint)(addr + i))}");
            }
            else if (parts[0] == "dr")
            {
                Console.WriteLine($"AX: {p.GetAX():X04}, BX: {p.GetBX():X04}, CX: {p.GetCX():X04}, DX: {p.GetDX():X04}");
                Console.WriteLine($"DS: {p.GetDS():X04}, ES: {p.GetES():X04}");
                ushort ss = p.GetSS();
                ushort sp = p.GetSP();
                uint full_stack_addr = (uint)(ss * 16 + sp);
                Console.WriteLine($"SS: {ss:X04}, SP: {sp:X04} => ${full_stack_addr:X06}, {p.HexDump(full_stack_addr)}");
                Console.WriteLine($"BP: {p.GetBP():X04}, SI: {p.GetSI():X04}, DI: {p.GetDI():X04}");
                ushort cs = p.GetCS();
                ushort ip = p.GetIP();
                Console.WriteLine($"CS: {cs:X04}, IP: {ip:X04} => ${cs * 16 + ip:X06}");
                Console.WriteLine($"flags: {p.GetFlagsAsString()}");
            }
            else
            {
                Console.WriteLine($"\"{line}\" is not understood");
            }
        }
        catch(System.FormatException fe)
        {
            Console.WriteLine($"An error occured while processing \"{line}\": make sure you prefix hexadecimal values with \"0x\" and enter decimal values where required. Complete error message: \"{fe}\".");
        }
        catch(Exception e)
        {
            Console.WriteLine($"The error \"{e}\" occured while processing \"{line}\".");
        }
    }
}

Log.EmitDisassembly();

Log.EndLogging();

if (test != "" && mode == TMode.Binary)
    System.Environment.Exit(p.GetSI() == 0xa5ee ? 123 : 0);

System.Environment.Exit(0);

Thread CreateRunnerThread(RunnerParameters runner_parameters)
{
    Thread thread = new Thread(Runner);
    thread.Name = "runner";
    thread.Start(runner_parameters);
    return thread;
}

void Disassemble(Bus b, in P8086 p)
{
    State8086 state = p.GetState();
    ushort cs = state.cs;
    ushort ip = state.ip;
    Log.SetMeta(state.clock, cs, ip);

    P8086Disassembler d = new P8086Disassembler(b, state);

    string registers_str = d.GetRegisters();

    // instruction length, instruction string, additional info, hex-string
    (int length, string instruction, string meta, string hex) = d.Disassemble();
    System.Diagnostics.Debug.Assert(cs == state.cs && ip == state.ip, "Not modified by disassembler");

    Log.DoLog($"{d.GetRegisters()} | {instruction} | {hex} | {meta}", LogLevel.TRACE);
}

void Runner(object o)
{
    RunnerParameters runner_parameters = (RunnerParameters)o;
    P8086 p = runner_parameters.cpu;

    try
    {
        Console.WriteLine("Emulation started");

        long prev_time = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        long prev_clock = 0;
        for(;;)
        {
            if (runner_parameters.disassemble)
                Disassemble(b, p);

            if (p.Tick() == false || runner_parameters.exit.get() == true)
            {
                p.SetIgnoreBreakpoints();
                break;
            }

            if (!throttle)
                continue;

            long now_clock = p.GetClock();
            if (now_clock - prev_clock >= 4770000 / 50)
            {
                long now_time = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long diff_time = now_time - prev_time;
                if (diff_time < 20)
                    Thread.Sleep((int)(20 - diff_time));
                prev_time = now_time;
                prev_clock = now_clock;
            }
        }

        string stop_reason = p.GetStopReason();
        if (stop_reason != "")
            Console.WriteLine(stop_reason);
    }
    catch(Exception e)
    {
        string msg = $"An exception occured: {e.ToString()}";
        Console.WriteLine(msg);
        Log.DoLog(msg, LogLevel.WARNING);
    }

    Console.WriteLine("Emulation stopped");
}

int GetValue(string v, bool hex)
{
    string[] aparts = v.Split(":");
    if (aparts.Length == 2)
        return Convert.ToInt32(aparts[0], 16) * 16 + Convert.ToInt32(aparts[1], 16);

    if (v.Length > 2 && v[0] == '0' && v[1] == 'x')
        return Convert.ToInt32(v.Substring(2), 16);

    if (hex)
        return Convert.ToInt32(v, 16);

    return Convert.ToInt32(v, 10);
}

void CmdGet(string[] tokens, P8086 p, Bus b)
{
    if (tokens.Length != 3)
        Console.WriteLine("usage: get [reg|ram] [regname|address]");
    else if (tokens[1] == "reg")
    {
        try
        {
            string regname = tokens[2];
            ushort value   = 0;

            if (regname == "ax")
                value = p.GetAX();
            else if (regname == "bx")
                value = p.GetBX();
            else if (regname == "cx")
                value = p.GetCX();
            else if (regname == "dx")
                value = p.GetDX();
            else if (regname == "ss")
                value = p.GetSS();
            else if (regname == "cs")
                value = p.GetCS();
            else if (regname == "ds")
                value = p.GetDS();
            else if (regname == "es")
                value = p.GetES();
            else if (regname == "sp")
                value = p.GetSP();
            else if (regname == "bp")
                value = p.GetBP();
            else if (regname == "si")
                value = p.GetSI();
            else if (regname == "di")
                value = p.GetDI();
            else if (regname == "ip")
                value = p.GetIP();
            else if (regname == "flags")
                value = p.GetFlags();
            else
            {
                Console.WriteLine($"Register {regname} not known");
                return;
            }

            Console.WriteLine($">GET {regname} {value}");
        }
        catch (Exception e)
        {
            Console.WriteLine($">GET -1 -1 FAILED {e}");
        }
    }
    else if (tokens[1] == "ram" || tokens[1] == "mem")
    {
        try
        {
            uint addr = (uint)GetValue(tokens[2], false);
            ushort value = b.ReadByte(addr).Item1;

            Console.WriteLine($">GET {addr} {value}");
        }
        catch (Exception e)
        {
            Console.WriteLine($">GET -1 -1 FAILED {e}");
        }
    }
}

void CmdSet(string [] tokens, P8086 p, Bus b)
{
    if (tokens.Length != 4)
        Console.WriteLine("usage: set [reg|ram] [regname|address] value");
    else if (tokens[1] == "reg")
    {
        string regname = tokens[2];
        ushort value = (ushort)GetValue(tokens[3], false);

        try
        {
            if (regname == "ax")
                p.SetAX(value);
            else if (regname == "bx")
                p.SetBX(value);
            else if (regname == "cx")
                p.SetCX(value);
            else if (regname == "dx")
                p.SetDX(value);
            else if (regname == "ss")
                p.SetSS(value);
            else if (regname == "cs")
                p.SetCS(value);
            else if (regname == "ds")
                p.SetDS(value);
            else if (regname == "es")
                p.SetES(value);
            else if (regname == "sp")
                p.SetSP(value);
            else if (regname == "bp")
                p.SetBP(value);
            else if (regname == "si")
                p.SetSI(value);
            else if (regname == "di")
                p.SetDI(value);
            else if (regname == "ip")
                p.SetIP(value);
            else if (regname == "flags")
                p.SetFlags(value);
            else
            {
                Console.WriteLine($"Register {regname} not known");
                return;
            }

            Console.WriteLine($"<SET {regname} {value}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"<SET -1 -1 FAILED {e}");
        }
    }
    else if (tokens[1] == "ram" || tokens[1] == "mem")
    {
        try
        {
            uint addr = (uint)GetValue(tokens[2], false);
            byte value = (byte)GetValue(tokens[3], false);

            b.WriteByte(addr, value);

            Console.WriteLine($"<SET {addr} {value}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"<SET -1 -1 FAILED {e}");
        }
    }
}

void GetBreakpoints(P8086 p)
{
    Console.WriteLine("Breakpoints:");
    foreach(uint a in p.GetBreakpoints())
        Console.WriteLine($"\t{a:X06}");
}

void ClearConsoleInputBuffer()
{
    while(Console.KeyAvailable)
        Console.ReadKey(true);
}

void MeasureSpeed(P8086 p, bool continuously)
{
    if (continuously)
        Console.WriteLine("Press any key to stop measuring");
    do
    {
        long start_clock = p.GetClock();
        Thread.Sleep(1000);
        long end_clock = p.GetClock();

        Console.WriteLine($"Estimated emulation speed: {(end_clock - start_clock) * 100 / 4772730}%");
    }
    while(continuously && Console.KeyAvailable == false);

    if (continuously)
        ClearConsoleInputBuffer();
}

class ThreadSafe_Bool
{
    private readonly System.Threading.Lock _lock = new();
    private bool _state = false;

    public ThreadSafe_Bool()
    {
    }

    public bool get()
    {
        lock(_lock)
        {
            return _state;
        }
    }

    public void set(bool new_state)
    {
        lock(_lock)
        {
            _state = new_state;
        }
    }
}

struct RunnerParameters
{
    public P8086 cpu { get; set; }
    public ThreadSafe_Bool exit;
    public bool disassemble;
};

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
bool use_adlib = false;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-h") {
        Log.Cnsl("-t file   load 'file'");
        Log.Cnsl("-T addr   sets the load-address for -t");
        Log.Cnsl("-x type   set type for -T: binary, blank");
        Log.Cnsl("-l file   log to file");
        Log.Cnsl("-L        set loglevel (trace, debug, ...)");
        Log.Cnsl("-R file,address   load rom \"file\" to address(xxxx:yyyy)");
        Log.Cnsl("          e.g. load the bios from f000:e000");
        Log.Cnsl("-s size   RAM size in kilobytes, decimal");
        Log.Cnsl("-F file   load floppy image (multiple for drive A-D)");
        Log.Cnsl("-D file   disassemble to file");
        Log.Cnsl("-I        disable I/O ports");
        Log.Cnsl("-S        try to run at real speed");
        Log.Cnsl("-P        skip prompt");
        Log.Cnsl("-O        enable option. currently: adlib, midi, rtc");
        Log.Cnsl("-X file   add an XT-IDE harddisk (must be 614/4/17 CHS)");
        Log.Cnsl($"-p device,type,port   port to listen on. type must be \"telnet\", \"http\" or \"vnc\" for now. device can be \"{key_cga}\" or \"{key_mda}\".");
        Log.Cnsl("-o cs,ip  start address (in hexadecimal)");
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
        if (what == "adlib")
            use_adlib = true;
        else if (what == "midi")
            use_midi = true;
        else if (what == "rtc")
            use_rtc = true;
        else
        {
            Log.Cnsl($"{what} is not understood");
            System.Environment.Exit(1);
        }
    }
    else if (args[i] == "-p")
    {
        string[] parts = args[++i].Split(',');
        if (parts[0] != key_cga && parts[0] != key_mda)
        {
            Log.Cnsl($"{parts[0]} is not understood");
            System.Environment.Exit(1);
        }
        if (parts[1] != "telnet" && parts[1] != "http" && parts[1] != "vnc")
        {
            Log.Cnsl($"{parts[1]} is not understood");
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
            Log.Cnsl($"{type} is not understood");
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

        Log.Cnsl($"Loading {file} to {addr:X06}");

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
        Log.Cnsl($"{args[i]} is not understood");

        System.Environment.Exit(1);
    }
}

if (test == "")
{
    Log.Cnsl("DotXT, (C) 2023-2025 by Folkert van Heusden");
    Log.Cnsl("Released in the public domain");
}

Console.TreatControlCAsInput = true;

#if DEBUG
Log.Cnsl("Debug build");
#endif

RTSPServer audio = null;

List<Device> devices = new();

if (mode != TMode.Blank)
{
    Keyboard kb = new();
    devices.Add(kb);  // still needed because of clock ticks
    devices.Add(new PPI(kb));

    Adlib adlib = null;
    if (use_adlib)
    {
        adlib = new Adlib();
        devices.Add(adlib);
        audio = new RTSPServer(adlib, 5540);  // TODO port & instantiating; make optional
    }

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
                console_instances.Add(new VNCServer(kb, c.Item2, true, adlib));
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
var d = new P8086Disassembler(b);
var p = new P8086(ref b, test, mode, load_test_at, ref devices, run_IO);

if (set_initial_ip)
    p.set_ip(initial_cs, initial_ip);

if (json_processing)
{
    int cycle_count = 0;

    for(;;)
    {
        if (prompt)
            Console.Write("==>");

        string line = Console.ReadLine();
        Log.DoLog($"CMDLINE: {line}", LogLevel.DEBUG);

        string[] parts = line.Split(' ');

        if (line == "s")
        {
            Disassemble(d, p);
            p.SetIgnoreBreakpoints();
            cycle_count = p.Tick();
        }
        else if (line == "S")
        {
            Disassemble(d, p);
            do
            {
                p.SetIgnoreBreakpoints();
                int rc = p.Tick();
                if (rc == -1)
                    break;
                cycle_count += rc;
            }
            while(p.IsProcessingRep());
        }
        else if (line == "cycles")
            Console.WriteLine($">CYCLES {cycle_count}");
        else if (line == "q")
            break;
        else if (parts[0] == "dolog")
        {
            Log.DoLog(line, LogLevel.INFO);
        }
        else if (parts[0] == "reset")
        {
            cycle_count = 0;
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
            Log.Cnsl($"\"{line}\" not understood");
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
                Log.Cnsl("quit / q       terminate application");
                Log.Cnsl("step / s / S   invoke 1 instruction, \"S\": step over loop");
                Log.Cnsl($"stop           stop emulation (running: {running})");
                Log.Cnsl("start / go     start emulation");
                Log.Cnsl("reset          reset emulator");
                Log.Cnsl("disassemble / da  toggle disassembly while emulating");
                Log.Cnsl("echo           toggle logging to console");
                Log.Cnsl("lsfloppy       list configured floppies");
                Log.Cnsl("setfloppy x y  set floppy unit x (0 based) to file y");
                Log.Cnsl("get [reg|ram] [regname|address]  get value from a register/memory location");
                Log.Cnsl("set [reg|ram] [regname|address] value   set registers/memory to a value");
                Log.Cnsl("get/set        value/address can be decimal or hexadecimal (prefix with 0x)");
                Log.Cnsl("hd x           hexdump of a few bytes starting at address x");
                Log.Cnsl("hd cs:ip       hexdump of a few bytes starting at address cs:ip");
                Log.Cnsl("dr             dump all registers");
                Log.Cnsl("gbp / lbp      list breakpoints");
                Log.Cnsl("sbp x          set breakpoint");
                Log.Cnsl("dbp x          delete breakpoint");
                Log.Cnsl("cbp            remove all breakpoints");
                Log.Cnsl("stats x        \"x\" must be \"cpu-speed\" currently");
                Log.Cnsl("setll x        set loglevel (trace, debug, ...)");
                Log.Cnsl("trunclf        truncate logfile");
            }
            else if (parts[0] == "s" || parts[0] == "step" || parts[0] == "S")
            {
                bool rc = true;

                if (parts[0] == "S")
                {
                    do
                    {
                        if (runner_parameters.disassemble)
                            Disassemble(d, p);

                        if (p.Tick() == -1)
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
                        Disassemble(d, p);

                    rc = p.Tick() >= 0;
                }

                if (rc == false)
                {
                    string stop_reason = p.GetStopReason();
                    if (stop_reason != "")
                        Log.Cnsl(stop_reason);
                }
            }
            else if (parts[0] == "disassemble" || parts[0] == "da")
            {
                if (parts.Length == 2)
                    runner_parameters.disassemble = parts[1].ToLower() == "on";
                else
                    runner_parameters.disassemble = !runner_parameters.disassemble;
                Log.Cnsl(runner_parameters.disassemble ? "disassembly on" : "disassembly off");
                if (running)
                    Log.Cnsl("Please stop+start emulation to activate tracing");
            }
            else if (parts[0] == "trunclf")
            {
                Log.TruncateLogfile();
            }
            else if (parts[0] == "setll")
            {
                if (parts.Length != 2)
                    Log.Cnsl("Parameter missing");
                else
                    Log.SetLogLevel(Log.StringToLogLevel(parts[1]));
            }
            else if (parts[0] == "stats")
            {
                if (parts.Length < 2)
                    Log.Cnsl("Parameter missing");
                else if (parts[1] == "cpu-speed")
                {
                    if (running)
                        MeasureSpeed(p, parts.Length == 3 && parts[2] == "-c");
                    else
                        Log.Cnsl("Emulation not running");
                }
                else if (parts[1] == "i8253" || parts[1] == "timers")
                {
                    foreach(var device in devices)
                    {
                        if (device is i8253)
                        {
                            var state = device.GetState();
                            foreach(var state_line in state)
                                Log.Cnsl(state_line);
                        }
                    }
                }
            }
            else if (parts[0] == "echo")
            {
                echo_state = !echo_state;
                Log.Cnsl(echo_state ? "echo on" : "echo off");
                Log.EchoToConsole(echo_state);
            }
            else if (parts[0] == "start" || parts[0] == "go")
            {
                if (running)
                    Log.Cnsl("Already running");
                else
                {
                    runner_parameters.exit.set(false);
                    thread = CreateRunnerThread(runner_parameters);
                    Log.Cnsl("OK");
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
                    Log.Cnsl("Not running");
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
                    Log.Cnsl("No floppy drive configured");
                else
                {
                    for(int i=0; i<floppy_controller.GetUnitCount(); i++)
                        Log.Cnsl($"{i}] {floppy_controller.GetUnitFilename(i)}");
                }
            }
            else if (parts[0] == "setfloppy")
            {
                if (floppy_controller == null)
                    Log.Cnsl("No floppy drive configured");
                else if (parts.Length != 3)
                    Log.Cnsl("Number of parameters is incorrect");
                else
                {
                    string unit_str = parts[1].ToLower();
                    int unit = -1;
                    if (parts[1] == "a" || parts[1] == "a:")
                        unit = 0;
                    else if (parts[1] == "b" || parts[1] == "b:")
                        unit = 1;
                    else
                        unit = int.Parse(parts[1]);
                    if (floppy_controller.SetUnitFilename(unit, parts[2]))
                        Log.Cnsl("OK");
                    else
                        Log.Cnsl("Failed: invalid unit number or file does not exist");
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
                    Log.Cnsl("Please stop emulation first");
                else
                {
                    uint addr = (uint)GetValue(parts[1], false);
                    p.AddBreakpoint(addr);
                }
            }
            else if (parts[0] == "dbp")
            {
                if (running)
                    Log.Cnsl("Please stop emulation first");
                else
                {
                    uint addr = (uint)GetValue(parts[1], false);
                    p.DelBreakpoint(addr);
                }
            }
            else if (parts[0] == "cbp")
            {
                if (running)
                    Log.Cnsl("Please stop emulation first");
                else
                {
                    p.ClearBreakpoints();
                }
            }
            else if (parts[0] == "hd")
            {
                uint addr = (uint)GetValue(parts[1], true);

                for(int i=0; i<256; i+=16)
                    Log.Cnsl($"{addr + i:X6} {p.HexDump((uint)(addr + i))} {p.CharDump((uint)(addr + i))}");
            }
            else if (parts[0] == "dr")
            {
                Log.Cnsl($"AX: {p.GetAX():X04}, BX: {p.GetBX():X04}, CX: {p.GetCX():X04}, DX: {p.GetDX():X04}");
                Log.Cnsl($"DS: {p.GetDS():X04}, ES: {p.GetES():X04}");
                ushort ss = p.GetSS();
                ushort sp = p.GetSP();
                uint full_stack_addr = (uint)(ss * 16 + sp);
                Log.Cnsl($"SS: {ss:X04}, SP: {sp:X04} => ${full_stack_addr:X06}, {p.HexDump(full_stack_addr)}");
                Log.Cnsl($"BP: {p.GetBP():X04}, SI: {p.GetSI():X04}, DI: {p.GetDI():X04}");
                ushort cs = p.GetCS();
                ushort ip = p.GetIP();
                Log.Cnsl($"CS: {cs:X04}, IP: {ip:X04} => ${cs * 16 + ip:X06}");
                Log.Cnsl($"flags: {p.GetFlagsAsString()}");
            }
            else
            {
                Log.Cnsl($"\"{line}\" is not understood");
            }
        }
        catch(System.FormatException fe)
        {
            Log.Cnsl($"An error occured while processing \"{line}\": make sure you prefix hexadecimal values with \"0x\" and enter decimal values where required. Complete error message: \"{fe}\".");
        }
        catch(Exception e)
        {
            Log.Cnsl($"The error \"{e}\" occured while processing \"{line}\".");
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

void Disassemble(P8086Disassembler d, in P8086 p)
{
    State8086 state = p.GetState();
    ushort cs = state.cs;
    ushort ip = state.ip;

    Log.SetMeta(state.clock, cs, ip);

    d.SetCPUState(state);

    (int length, string instruction, string meta, string hex) = d.Disassemble();
    string registers_str = d.GetRegisters();
    Log.DoLog($"{d.GetRegisters()} | {instruction} | {hex} | {meta}", LogLevel.TRACE);

    System.Diagnostics.Debug.Assert(cs == state.cs && ip == state.ip, "Should not be modified by disassembler");
}

void Runner(object o)
{
    RunnerParameters runner_parameters = (RunnerParameters)o;
    P8086 p = runner_parameters.cpu;
    const int throttle_hz = 50;

    try
    {
        Log.Cnsl("Emulation started");

        long prev_time = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        long prev_clock = 0;
        for(;;)
        {
            if (runner_parameters.disassemble)
                Disassemble(d, p);

            if (p.Tick() == -1 || runner_parameters.exit.get() == true)
            {
                p.SetIgnoreBreakpoints();
                break;
            }

            if (!throttle)
                continue;

            long now_clock = p.GetClock();
            if (now_clock - prev_clock >= 4770000 / throttle_hz)
            {
                long now_time = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long diff_time = now_time - prev_time;
                if (diff_time < 1000 / throttle_hz)
                    Thread.Sleep((int)(1000 / throttle_hz - diff_time));
                prev_time = now_time;
                prev_clock = now_clock;
            }
        }

        string stop_reason = p.GetStopReason();
        if (stop_reason != "")
            Log.Cnsl(stop_reason);
    }
    catch(Exception e)
    {
        string msg = $"An exception occured: {e.ToString()}";
        Log.Cnsl(msg);
        Log.DoLog(msg, LogLevel.WARNING);
    }

    Log.Cnsl("Emulation stopped");
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
        Log.Cnsl("usage: get [reg|ram] [regname|address]");
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
                Log.Cnsl($"Register {regname} not known");
                return;
            }

            Log.Cnsl($">GET {regname} {value}");
        }
        catch (Exception e)
        {
            Log.Cnsl($">GET -1 -1 FAILED {e}");
        }
    }
    else if (tokens[1] == "ram" || tokens[1] == "mem")
    {
        try
        {
            uint addr = (uint)GetValue(tokens[2], false);
            ushort value = b.ReadByte(addr).Item1;

            Log.Cnsl($">GET {addr} {value}");
        }
        catch (Exception e)
        {
            Log.Cnsl($">GET -1 -1 FAILED {e}");
        }
    }
}

void CmdSet(string [] tokens, P8086 p, Bus b)
{
    if (tokens.Length != 4)
        Log.Cnsl("usage: set [reg|ram] [regname|address] value");
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
                Log.Cnsl($"Register {regname} not known");
                return;
            }

            Log.Cnsl($"<SET {regname} {value}");
        }
        catch (Exception e)
        {
            Log.Cnsl($"<SET -1 -1 FAILED {e}");
        }
    }
    else if (tokens[1] == "ram" || tokens[1] == "mem")
    {
        try
        {
            uint addr = (uint)GetValue(tokens[2], false);
            byte value = (byte)GetValue(tokens[3], false);

            b.WriteByte(addr, value);

            Log.Cnsl($"<SET {addr} {value}");
        }
        catch (Exception e)
        {
            Log.Cnsl($"<SET -1 -1 FAILED {e}");
        }
    }
}

void GetBreakpoints(P8086 p)
{
    Log.Cnsl("Breakpoints:");
    foreach(uint a in p.GetBreakpoints())
        Log.Cnsl($"\t{a:X06}");
}

void ClearConsoleInputBuffer()
{
    while(Console.KeyAvailable)
        Console.ReadKey(true);
}

void MeasureSpeed(P8086 p, bool continuously)
{
    if (continuously)
        Log.Cnsl("Press any key to stop measuring");
    do
    {
        long start_clock = p.GetClock();
        Thread.Sleep(1000);
        long end_clock = p.GetClock();

        Log.Cnsl($"Estimated emulation speed: {(end_clock - start_clock) * 100 / 4772730}%");
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

using DotXT;

string bin_file = "";
uint bin_file_addr = 0;
string xts_trace_file = "";

TMode mode = TMode.Normal;

ushort initial_cs = 0xf000;
ushort initial_ip = 0xfff0;

bool run_IO = true;

uint ram_size = 1024;

List<Rom> roms = new();

string key_mda = "mda";
string key_cga = "cga";

List<string> ide = new();
Dictionary<string, List<Tuple<string, int> > > consoles = new();

List<string> floppies = new();
FloppyDisk floppy_controller = null;

bool throttle = false;

bool use_midi = false;
bool use_rtc = false;
bool use_adlib = false;
bool use_lotechems = false;

string avi_file = null;
int avi_quality = 95;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-h") {
        Log.Cnsl("-m mode   \"normal\" (=default), \"json\", \"xtserver\", \"cc\" (count cycles) or \"empty\"");
        Log.Cnsl("-M mode-parameters");
        Log.Cnsl("          load-bin,<file>,<segment:offset>");
        Log.Cnsl("          set-start-addr,<segment:offset>");
        Log.Cnsl("          no-io");
        Log.Cnsl("          xts-trace,<file>");
        Log.Cnsl("-l file   log to file");
        Log.Cnsl("-L        set loglevel (trace, debug, ...)");
        Log.Cnsl("-R file,address   load rom \"file\" to address(xxxx:yyyy)");
        Log.Cnsl("          e.g. load the bios from f000:e000");
        Log.Cnsl("-s size   RAM size in kilobytes, decimal");
        Log.Cnsl("-F file   load floppy image (multiple for drive A-D)");
        Log.Cnsl("-D file   disassemble to file");
        Log.Cnsl("-S        try to run at real speed");
        Log.Cnsl("-O        enable option. currently: adlib, midi, rtc, lotechems");
        Log.Cnsl("-X file   add an XT-IDE harddisk (must be 614/4/17 CHS)");
        Log.Cnsl($"-p device,type,port   display output. type must be \"telnet\", \"http\" or \"vnc\". device can be \"{key_cga}\" or \"{key_mda}\".");
        Log.Cnsl("-P avi,quality    avi file to render display to, quality is 0...100");
        System.Environment.Exit(0);
    }
    else if (args[i] == "-P")
    {
        string[] parts = args[++i].Split(',');
        avi_file = parts[0];
        avi_quality = Convert.ToInt32(parts[1], 10);
    }
    else if (args[i] == "-M")
    {
        string[] parts = args[++i].Split(',');

        if (parts[0] == "load-bin")
        {
            bin_file = parts[1];
            bin_file_addr = (uint)GetValue(parts[2], true);
            Log.Cnsl($"Load {bin_file} at {bin_file_addr:X06}");
        }
        else if (parts[0] == "set-start-addr")
        {
            string[] aparts = parts[1].Split(":");
            initial_cs = (ushort)GetValue(aparts[0], true);
            initial_ip = (ushort)GetValue(aparts[1], true);
            Log.Cnsl($"Start running at {initial_cs:X04}:{initial_ip:X04}");
        }
        else if (parts[0] == "no-io")
        {
            Log.Cnsl("IO disabled");
            run_IO = false;
        }
        else if (parts[0] == "xts-trace")
        {
            xts_trace_file = parts[1];
            Log.Cnsl($"XT-Server emulation output will go to {xts_trace_file}");
        }
        else
        {
            Log.Cnsl($"{parts[0]} is not understood");
            System.Environment.Exit(1);
        }
    }
    else if (args[i] == "-m") {
        string type = args[++i];

        if (type == "normal")
            mode = TMode.Normal;
        else if (type == "empty")
            mode = TMode.Empty;
        else if (type == "json")
            mode = TMode.JSON;
        else if (type == "tests")
            mode = TMode.Tests;
        else if (type == "cc")
            mode = TMode.CC;
        else if (type == "xtserver")
            mode = TMode.XTServer;
        else
        {
            Log.Cnsl($"{type} is not understood");
            System.Environment.Exit(1);
        }
    }
    else if (args[i] == "-S")
        throttle = true;
    else if (args[i] == "-O")
    {
        string what = args[++i];
        if (what == "adlib")
            use_adlib = true;
        else if (what == "midi")
            use_midi = true;
        else if (what == "rtc")
            use_rtc = true;
        else if (what == "lotechems")
            use_lotechems = true;
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
    else if (args[i] == "-X")
        ide.Add(args[++i]);
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else if (args[i] == "-L")
        Log.SetLogLevel(Log.StringToLogLevel(args[++i]));
    else if (args[i] == "-D")
        Log.SetDisassemblyFile(args[++i]);
    else if (args[i] == "-F")
        floppies.Add(args[++i]);
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
    else
    {
        Log.Cnsl($"{args[i]} is not understood");

        System.Environment.Exit(1);
    }
}

if (mode == TMode.Normal)
{
    Log.Cnsl("DotXT, (C) 2023-2025 by Folkert van Heusden");
    Log.Cnsl("Released in the public domain");
}

//Console.TreatControlCAsInput = true;

#if DEBUG
Log.Cnsl("Debug build");
#endif

RTSPServer audio = null;
AVI avi = null;

List<Device> devices = new();

if (mode != TMode.Empty)
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

    Display display = null;
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
            display = new MDA(console_instances);
        else if (current_console.Key == key_cga)
            display = new CGA(console_instances);
        devices.Add(display);
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

    if ((bin_file != "" || display != null) && mode == TMode.XTServer)
        devices.Add(new XTServer(bin_file, bin_file_addr, display, xts_trace_file));

    if (avi_file != null)
        avi = new AVI(avi_file, 15, display, AVI_CODEC.JPEG, avi_quality);

    if (use_lotechems)
        devices.Add(new LotechEMS());
}

// Bus gets the devices for memory mapped i/o
Bus b = new Bus(ram_size * 1024, ref devices, ref roms);
var d = new P8086Disassembler(b);
var p = new P8086(ref b, ref devices, run_IO);

if (mode == TMode.Normal || mode == TMode.XTServer || mode == TMode.CC)
    p.GetState().SetIP(initial_cs, initial_ip);

if (mode == TMode.XTServer)
    AddXTServerBootROM(b);

if (mode == TMode.CC && bin_file != "")
    Tools.LoadBin(b, bin_file, bin_file_addr);

if (mode == TMode.JSON)
{
    int cycle_count = 0;

    for(;;)
    {
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
else if (mode == TMode.CC)
{
    for(;;)
    {
        int opcode = p.ReadMemByte(p.GetState().GetCS(), p.GetState().GetIP());
        int rc = p.Tick();
        if (rc == -1)
        {
            Log.Cnsl("Failed running program");
            break;
        }

        if (opcode == 0xf4)
        {
            Log.Cnsl($"Running program (including HLT) took {p.GetState().GetClock()} cycles");
            break;
        }
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
                Log.Cnsl("dump addr,size file  dump memory pointer to by addr (xxxx:yyyy) to file");
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
            else if (parts[0] == "dump")
            {
                string[] aparts = parts[1].Split(",");
                uint addr = (uint)GetValue(aparts[0], true);
                int size = GetValue(aparts[1], false);

                dump(b, addr, size, parts[2]);
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
                else
                {
                    Log.Cnsl($"{parts[1]} is not understood");
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

                if (parts.Length == 2 && parts[1] == "cold")
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
                Log.Cnsl($"AX: {p.GetState().GetAX():X04}, BX: {p.GetState().GetBX():X04}, CX: {p.GetState().GetCX():X04}, DX: {p.GetState().GetDX():X04}");
                Log.Cnsl($"DS: {p.GetState().GetDS():X04}, ES: {p.GetState().GetES():X04}");
                ushort ss = p.GetState().GetSS();
                ushort sp = p.GetState().GetSP();
                uint full_stack_addr = (uint)(ss * 16 + sp);
                Log.Cnsl($"SS: {ss:X04}, SP: {sp:X04} => ${full_stack_addr:X06}, {p.HexDump(full_stack_addr)}");
                Log.Cnsl($"BP: {p.GetState().GetBP():X04}, SI: {p.GetState().GetSI():X04}, DI: {p.GetState().GetDI():X04}");
                ushort cs = p.GetState().GetCS();
                ushort ip = p.GetState().GetIP();
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

if (avi != null)
{
    avi.Close();
}

if (mode == TMode.Tests)
    System.Environment.Exit(p.GetState().GetSI() == 0xa5ee ? 123 : 0);

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

            long now_clock = p.GetState().GetClock();
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
                value = p.GetState().GetAX();
            else if (regname == "bx")
                value = p.GetState().GetBX();
            else if (regname == "cx")
                value = p.GetState().GetCX();
            else if (regname == "dx")
                value = p.GetState().GetDX();
            else if (regname == "ss")
                value = p.GetState().GetSS();
            else if (regname == "cs")
                value = p.GetState().GetCS();
            else if (regname == "ds")
                value = p.GetState().GetDS();
            else if (regname == "es")
                value = p.GetState().GetES();
            else if (regname == "sp")
                value = p.GetState().GetSP();
            else if (regname == "bp")
                value = p.GetState().GetBP();
            else if (regname == "si")
                value = p.GetState().GetSI();
            else if (regname == "di")
                value = p.GetState().GetDI();
            else if (regname == "ip")
                value = p.GetState().GetIP();
            else if (regname == "flags")
                value = p.GetState().GetFlags();
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
                p.GetState().SetAX(value);
            else if (regname == "bx")
                p.GetState().SetBX(value);
            else if (regname == "cx")
                p.GetState().SetCX(value);
            else if (regname == "dx")
                p.GetState().SetDX(value);
            else if (regname == "ss")
                p.GetState().SetSS(value);
            else if (regname == "cs")
                p.GetState().SetCS(value);
            else if (regname == "ds")
                p.GetState().SetDS(value);
            else if (regname == "es")
                p.GetState().SetES(value);
            else if (regname == "sp")
                p.GetState().SetSP(value);
            else if (regname == "bp")
                p.GetState().SetBP(value);
            else if (regname == "si")
                p.GetState().SetSI(value);
            else if (regname == "di")
                p.GetState().SetDI(value);
            else if (regname == "ip")
                p.GetState().SetIP(value);
            else if (regname == "flags")
                p.GetState().SetFlags(value);
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
        long start_clock = p.GetState().GetClock();
        Thread.Sleep(1000);
        long end_clock = p.GetState().GetClock();

        Log.Cnsl($"Estimated emulation speed: {(end_clock - start_clock) * 100 / 4772730}%");
    }
    while(continuously && Console.KeyAvailable == false);

    if (continuously)
        ClearConsoleInputBuffer();
}

void AddXTServerBootROM(Bus b)
{
    uint start_addr = 0xd000 * 16 + 0x0000;
    uint addr = start_addr;

    byte [] option_rom = new byte[] {
        0x55, 0xaa, 0x01, 0xba, 0x01, 0xf0, 0xb0, 0xff, 0xee, 0x31, 0xc0, 0x8e,
        0xd8, 0xbe, 0x80, 0x01, 0xb9, 0x0b, 0x00, 0xc7, 0x04, 0x5a, 0x00, 0xc7,
        0x44, 0x02, 0x00, 0xd0, 0x83, 0xc6, 0x04, 0xe0, 0xf2, 0xbe, 0x80, 0x01,
        0xc7, 0x04, 0x5b, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xbe, 0x8c, 0x01,
        0xc7, 0x04, 0x71, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xbe, 0x90, 0x01,
        0xc7, 0x04, 0x7e, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xbe, 0x94, 0x01,
        0xc7, 0x04, 0x91, 0x00, 0xc7, 0x44, 0x02, 0x00, 0xd0, 0xea, 0x00, 0x00,
        0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0xcf, 0xa3, 0x56, 0x00, 0x89, 0x16,
        0x58, 0x00, 0xba, 0x01, 0xf0, 0xb8, 0x01, 0x60, 0xef, 0x8b, 0x16, 0x58,
        0x00, 0xa1, 0x56, 0x00, 0xcf, 0x89, 0x16, 0x58, 0x00, 0xba, 0x63, 0xf0,
        0xef, 0x8b, 0x16, 0x58, 0x00, 0xcf, 0x89, 0x16, 0x58, 0x00, 0xba, 0x64,
        0xf0, 0x8a, 0x04, 0x46, 0xee, 0x49, 0xe0, 0xf9, 0x8b, 0x16, 0x58, 0x00,
        0xcf, 0x89, 0x16, 0x58, 0x00, 0xba, 0x65, 0xf0, 0xee, 0x8b, 0x16, 0x58,
        0x00, 0xcf
    };

    for(int i=0; i<option_rom.Length; i++)
        b.WriteByte(addr++, option_rom[i]);

    byte checksum = 0;
    for(int i=0; i<512; i++)
        checksum += b.ReadByte((uint)(start_addr + i)).Item1;
    b.WriteByte(start_addr + 511, (byte)(~checksum));
}

void dump(Bus b, uint addr, int size, string filename)
{
    using (FileStream fs = File.Open(filename, FileMode.Create, FileAccess.Write, FileShare.None))
    {
        byte [] buffer = new byte[1];
        for(uint i=addr; i<(uint)(addr + size); i++)
        {
            buffer[0] = b.ReadByte(i).Item1;
            fs.Write(buffer);
        }
    }

    Log.Cnsl($"Wrote {size} bytes to {filename}");
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

internal enum TMode
{
    Normal,
    JSON,
    XTServer,
    CC,
    Tests,
    Empty
}

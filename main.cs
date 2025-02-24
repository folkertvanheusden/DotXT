using DotXT;

string test = "";
List<string> floppies = new();

TMode mode = TMode.NotSet;

ushort initial_cs = 0;
ushort initial_ip = 0;
bool set_initial_ip = false;

bool run_IO = true;
uint load_test_at = 0xffffffff;

bool debugger = false;
bool prompt = true;

uint ram_size = 1024;

List<Rom> roms = new();

string key_mda = "mda";
string key_cga = "cga";

Dictionary<string, List<Tuple<string, int> > > consoles = new();

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-h") {
        Console.WriteLine("-t file   load 'file'");
        Console.WriteLine("-T addr   sets the load-address for -t");
        Console.WriteLine("-x type   set type for -T: binary, blank");
        Console.WriteLine("-l file   log to file");
        Console.WriteLine("-L        log to screen");
        Console.WriteLine("-R file,address   load rom \"file\" to address(xxxx:yyyy)");
        Console.WriteLine("          e.g. load the bios from f000:e000");
        Console.WriteLine("-s size   RAM size in kilobytes, decimal");
        Console.WriteLine("-F file   load floppy image (multiple for drive A-D)");
        Console.WriteLine("-D file   disassemble to file");
        Console.WriteLine("-I        disable I/O ports");
        Console.WriteLine("-d        enable debugger");
        Console.WriteLine("-P        skip prompt");
        Console.WriteLine($"-p device,type,port   port to listen on. type must be \"telnet\", \"http\" or \"vnc\" for now. device can be \"{key_cga}\" or \"{key_mda}\".");
        Console.WriteLine("-o cs,ip  start address (in hexadecimal)");
        System.Environment.Exit(0);
    }
    else if (args[i] == "-t")
        test = args[++i];
    else if (args[i] == "-T")
        load_test_at = (uint)Convert.ToInt32(args[++i], 16);
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
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else if (args[i] == "-L")
        Log.EchoToConsole(true);
    else if (args[i] == "-D")
        Log.SetDisassemblyFile(args[++i]);
    else if (args[i] == "-I")
        run_IO = false;
    else if (args[i] == "-F")
        floppies.Add(args[++i]);
    else if (args[i] == "-d")
        debugger = true;
    else if (args[i] == "-P")
        prompt = false;
    else if (args[i] == "-s")
        ram_size = (uint)Convert.ToInt32(args[++i], 10);
    else if (args[i] == "-R")
    {
        string[] parts = args[++i].Split(',');
        string file = parts[0];

        string[] aparts = parts[1].Split(':');
        uint seg = (uint)Convert.ToInt32(aparts[0], 16);
        uint ip = (uint)Convert.ToInt32(aparts[1], 16);
        uint addr = seg * 16 + ip;

        Console.WriteLine($"Loading {file} to {addr:X06}");

        roms.Add(new Rom(file, addr));
    }
    else if (args[i] == "-o")
    {
        string[] parts = args[++i].Split(',');

        initial_cs = (ushort)Convert.ToInt32(parts[0], 16);
        initial_ip = (ushort)Convert.ToInt32(parts[1], 16);

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

Console.CancelKeyPress += delegate {
    Log.EmitDisassembly();
};

#if DEBUG
Console.WriteLine("Debug mode");
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
        devices.Add(new FloppyDisk(floppies));

    string [] drives = new string[] { "ide.img" };
    devices.Add(new XTIDE(drives));

    devices.Add(new MIDI());

    devices.Add(new RTC());
}

// Bus gets the devices for memory mapped i/o
Bus b = new Bus(ram_size * 1024, ref devices, ref roms);

var p = new P8086(ref b, test, mode, load_test_at, false, ref devices, run_IO);

if (set_initial_ip)
    p.set_ip(initial_cs, initial_ip);

if (debugger)
{
    bool echo_state = true;

    Log.EchoToConsole(echo_state);

    for(;;)
    {
        if (prompt)
            Console.Write("==>");

        string line = Console.ReadLine();

        //Log.DoLog(line);

        string[] parts = line.Split(' ');

        if (line == "s")
            p.Tick();
        else if (line == "S")
        {
            do {
                p.Tick();
            }
            while(p.IsProcessingRep());
        }
        else if (line == "q")
            break;
        else if (line == "echo")
        {
            echo_state = !echo_state;

            Console.WriteLine(echo_state ? "echo on" : "echo off");

            Log.EchoToConsole(echo_state);
        }
        else if (parts[0] == "ef")
        {
            if (parts.Length != 2)
                Console.WriteLine("usage: ef 0xhex_address");
            else
            {
                int address = Convert.ToInt32(parts[1], 16);

                Console.WriteLine($"{address:X6} {p.HexDump((uint)address)}");
            }
        }
        else if (parts[0] == "dolog")
        {
            Log.DoLog(line);
        }
        else if (parts[0] == "reset")
        {
            b.ClearMemory();
        }
        else if (parts[0] == "set")
        {
            if (parts.Length != 4)
                Console.WriteLine("usage: set [reg|ram] [regname|address] value");
            else if (parts[1] == "reg")
            {
                string regname = parts[2];
                ushort value = (ushort)Convert.ToInt32(parts[3], 10);

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
                        continue;
                    }

                    Console.WriteLine($"<SET {regname} {value}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"<SET -1 -1 FAILED {e}");
                }
            }
            else if (parts[1] == "ram" || parts[1] == "mem")
            {
                try
                {
                    uint addr  = (uint)Convert.ToInt32(parts[2], 10);
                    byte value = (byte)Convert.ToInt32(parts[3], 10);

                    b.WriteByte(addr, value);

                    Console.WriteLine($"<SET {addr} {value}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"<SET -1 -1 FAILED {e}");
                }
            }
        }
        else if (parts[0] == "get")
        {
            if (parts.Length != 3)
                Console.WriteLine("usage: get [reg|ram] [regname|address]");
            else if (parts[1] == "reg")
            {
                try
                {
                    string regname = parts[2];
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
                        continue;
                    }

                    Console.WriteLine($">GET {regname} {value}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($">GET -1 -1 FAILED {e}");
                }
            }
            else if (parts[1] == "ram" || parts[1] == "mem")
            {
                try
                {
                    uint   addr  = (uint)Convert.ToInt32(parts[2], 10);
                    ushort value = b.ReadByte(addr).Item1;

                    Console.WriteLine($">GET {addr} {value}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($">GET -1 -1 FAILED {e}");
                }
            }
        }
        else if (line == "c")
        {
            p.ResetCrashCounter();

            while(p.Tick())
            {
            }
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
    try
    {
        while(p.Tick())
        {
        }
    }
    catch(Exception e)
    {
        string msg = $"An exception occured: {e.ToString()}";
        Console.WriteLine(msg);
        Log.DoLog(msg);
    }
}

Log.EmitDisassembly();
    
if (test != "" && mode == TMode.Binary)
    System.Environment.Exit(p.GetSI() == 0xa5ee ? 123 : 0);

System.Environment.Exit(0);

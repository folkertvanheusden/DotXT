using DotXT;

string test = "";

TMode mode = TMode.NotSet;

bool intercept_int = false;

ushort initial_cs = 0;
ushort initial_ip = 0;
bool set_initial_ip = false;

bool load_bios = true;

uint load_test_at = 0xffffffff;

bool debugger = false;
bool prompt = true;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-h") {
        Console.WriteLine("-t file   load 'file' in RAM");
        Console.WriteLine("-T addr   sets the load-address for -t");
        Console.WriteLine("-x type   set type for -T: floppy, binary, blank");
        Console.WriteLine("-l file   log to file");
        Console.WriteLine("-i        intercept some of the BIOS calls");
        Console.WriteLine("-B        disable loading of the BIOS ROM images");
        Console.WriteLine("-d        enable debugger");
        Console.WriteLine("-P        skip prompt");
        Console.WriteLine("-o cs,ip  start address (in hexadecimal)");
        System.Environment.Exit(0);
    }
    else if (args[i] == "-t")
        test = args[++i];
    else if (args[i] == "-T")
        load_test_at = (uint)Convert.ToInt32(args[++i], 16);
    else if (args[i] == "-x") {
        string type = args[++i];

        if (type == "floppy")
            mode = TMode.Floppy;
        else if (type == "binary")
            mode = TMode.Binary;
        else if (type == "blank")
            mode = TMode.Blank;
        else {
            Console.WriteLine($"{type} is not understood");

            System.Environment.Exit(1);
        }
    }
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else if (args[i] == "-i")
        intercept_int = true;
    else if (args[i] == "-B")
        load_bios = false;
    else if (args[i] == "-d")
        debugger = true;
    else if (args[i] == "-P")
        prompt = false;
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
    Console.WriteLine("DotXT, (C) 2023 by Folkert van Heusden");
    Console.WriteLine("Released in the public domain");
}

#if DEBUG
Console.WriteLine("Debug mode");
#endif

List<Device> devices = new();

if (mode != TMode.Blank)
{
    devices.Add(new MDA());
    devices.Add(new CGA());
    devices.Add(new i8253());
    devices.Add(new FloppyDisk());
    devices.Add(new Keyboard());
}

uint ram_size = 256 * 1024;

if (test != "")
    ram_size = 640 * 1024;

if (mode == TMode.Blank)
    ram_size = 1024 * 1024;

// Bus gets the devices for memory mapped i/o
Bus b = new Bus(ram_size, load_bios, ref devices);

var p = new P8086(ref b, test, mode, load_test_at, intercept_int, !debugger, ref devices);

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

        string[] parts = line.Split(' ');

        if (line == "s")
            p.Tick();
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

                Console.WriteLine($"{address:X6} {p.HexDump((uint)address, false)}");
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

                    ushort value = b.ReadByte(addr);

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
            for (;;)
            {
                if (p.Tick() == false)
                    break;
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
    for (;;)
        p.Tick();
}

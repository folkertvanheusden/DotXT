using DotXT;

string test = "";
bool t_is_floppy = false;

bool intercept_int = false;

ushort initial_cs = 0;
ushort initial_ip = 0;
bool set_initial_ip = false;

bool load_bios = true;

uint load_test_at = 0xffffffff;

bool debugger = false;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-h") {
        Console.WriteLine("-t file   load 'file' in RAM");
        Console.WriteLine("-T addr   sets the load-address for -t");
        Console.WriteLine("-F        -t parameter is a floppy image");
        Console.WriteLine("-l file   log to file");
        Console.WriteLine("-i        intercept some of the BIOS calls");
        Console.WriteLine("-B        disable loading of the BIOS ROM images");
        Console.WriteLine("-d        enable debugger");
        Console.WriteLine("-o cs,ip  start address (in hexadecimal)");
        System.Environment.Exit(0);
    }
    else if (args[i] == "-t")
        test = args[++i];
    else if (args[i] == "-T")
        load_test_at = (uint)Convert.ToInt32(args[++i], 16);
    else if (args[i] == "-F")
        t_is_floppy = true;
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else if (args[i] == "-i")
        intercept_int = true;
    else if (args[i] == "-B")
        load_bios = false;
    else if (args[i] == "-d")
        debugger = true;
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

CGA cga = new();

MDA mda = new();

i8253 _i8253 = new();

List<Device> devices = new();
devices.Add(cga);
devices.Add(mda);
devices.Add(_i8253);

uint ram_size = 64 * 1024;  // if 64, then tweak i/o register 63
if (test != "")
    ram_size = 1024 * 1024;

// Bus gets the devices for memory mapped i/o
Bus b = new Bus(ram_size, load_bios, ref devices);

var p = new P8086(ref b, test, t_is_floppy, load_test_at, intercept_int, !debugger, ref devices);

if (set_initial_ip)
    p.set_ip(initial_cs, initial_ip);

if (debugger)
{
    bool echo_state = true;

    Log.EchoToConsole(echo_state);

    for(;;)
    {
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
    }
}
else
{
    for (;;)
        p.Tick();
}

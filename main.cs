using DotXT;

string test = "";
bool t_is_floppy = false;

bool intercept_int = false;

ushort initial_cs = 0;
ushort initial_ip = 0;
bool set_initial_ip = false;

bool load_bios = true;

uint load_test_at = 0xffffffff;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-t")
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

var p = new P8086(test, t_is_floppy, load_test_at, intercept_int, true, load_bios);

if (set_initial_ip)
    p.set_ip(initial_cs, initial_ip);

for (;;)
    p.Tick();

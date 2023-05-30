using DotXT;

string test = "";
bool t_is_floppy = false;

bool intercept_int = false;

ushort initial_ip = 0;
bool set_initial_ip = false;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-t")
        test = args[++i];
    else if (args[i] == "-F")
        t_is_floppy = true;
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else if (args[i] == "-i")
        intercept_int = true;
    else if (args[i] == "-o")
    {
        initial_ip = (ushort)Convert.ToInt32(args[++i], 16);
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

var p = new P8086(test, t_is_floppy, intercept_int, true);

if (set_initial_ip)
    p.set_ip(initial_ip);

for (;;)
    p.Tick();

using DotXT;

string test = "";
bool t_is_floppy = false;

for(int i=0; i<args.Length; i++)
{
    if (args[i] == "-t")
        test = args[++i];
    else if (args[i] == "-F")
        t_is_floppy = true;
    else if (args[i] == "-l")
        Log.SetLogFile(args[++i]);
    else
        Console.WriteLine($"{args[i]} is not understood");
}

if (test == "")
{
    Console.WriteLine("DotXT, (C) 2023 by Folkert van Heusden");
    Console.WriteLine("Released in the public domain");
}

#if DEBUG
Console.WriteLine("Debug mode");
#endif

var p = new P8086(test, t_is_floppy);

for (;;)
    p.Tick();

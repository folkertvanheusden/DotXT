using DotXT;

Console.WriteLine("DotXT, (C) 2023 by Folkert van Heusden");
Console.WriteLine("Released in the public domain");

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
}

var p = new P8086(test, t_is_floppy);

for (;;)
    p.Tick();

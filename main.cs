using DotXT;

Console.WriteLine("DotXT, (C) 2023 by Folkert van Heusden");
Console.WriteLine("Released in the public domain");

string test = "";

if (args.Length > 0)
    test = args[0];

var p = new P8086(test);

for (;;)
    p.Tick();

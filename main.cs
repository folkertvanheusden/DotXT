using DotXT;

Console.WriteLine("DotXT, (C) 2023 by Folkert van Heusden");
Console.WriteLine("Released in the public domain");

bool do_test = false;

foreach(var s in args)
{
    if (s == "test")
    {
        do_test = true;
        break;
    }
}

var p = new P8086(do_test);

for (;;)
    p.Tick();

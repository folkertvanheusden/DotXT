using System;
using XT;

namespace dotxt
{
    class DotXT
    {
        static void Main(string[] args)
        {
            Console.WriteLine("DotXT, (C) 2023 by Folkert van Heusden");
            Console.WriteLine("Released in the public domain");

            p8086 p = new p8086();

            for(;;)
                p.tick();
        }
    }
}

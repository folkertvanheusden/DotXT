internal struct Timer
{
    public ushort counter    { get; set; }
    public int    mode       { get; set; }
    public bool   in_setup   { get; set; }
    public int    latch_type { get; set; }
    public int    latch_n    { get; set; }
    public bool   is_running { get; set; }
}

internal class i8253
{
    Timer [] _timers = new Timer[3];

    // using a static seed to make it behave
    // the same every invocation (until threads
    // and timers are introduced)
    private Random _random = new Random(1);

    public i8253()
    {
        for(int i=0; i<_timers.Length; i++)
            _timers[i] = new Timer();
    }

    public void latch_counter(int nr, byte v)
    {
#if DEBUG
        Log.DoLog($"OUT 8253: latch_counter {nr} to {v}");
#endif

        if (_timers[nr].latch_n > 0)
        {
            if (_timers[nr].latch_n == 2)
            {
                _timers[nr].counter &= 0xff00;
                _timers[nr].counter |= v;
            }
            else if (_timers[nr].latch_n == 1 && _timers[nr].latch_type == 3)
            {
                _timers[nr].counter &= 0xff00;
                _timers[nr].counter |= v;
            }
            else if (_timers[nr].latch_type == 1)
            {
                _timers[nr].counter = v;
            }
            else if (_timers[nr].latch_type == 2)
            {
                _timers[nr].counter = (ushort)(v << 8);
            }

            _timers[nr].latch_n--;

            if (_timers[nr].latch_n == 0)
            {
                _timers[nr].is_running = true;
                _timers[nr].in_setup   = false;
            }
        }
    }

    public byte get_counter(int nr)
    {
#if DEBUG
        Log.DoLog($"OUT 8253: get_counter {nr}");
#endif

        return (byte)_timers[nr].counter;
    }

    public void command(byte v)
    {
        int counter = v >> 6;
        int latch   = (v >> 4) & 3;
        int mode    = (v >> 1) & 7;
        int type    = v & 1;

#if DEBUG
        Log.DoLog($"OUT 8253: command counter {counter}, latch {latch}, mode {mode}, type {type}");
#endif

        _timers[counter].mode       = mode;
        _timers[counter].in_setup   = true;
        _timers[counter].latch_type = latch;
        _timers[counter].is_running = false;

        _timers[counter].counter = 0;

        if (_timers[counter].latch_type == 1 || _timers[counter].latch_type == 2)
            _timers[counter].latch_n = 1;
        else if (_timers[counter].latch_type == 3)
            _timers[counter].latch_n = 2;
    }

    public bool Tick()
    {
        // this trickery is to (hopefully) trigger code that expects
        // some kind of cycle-count versus interrupt-count locking
        if (_random.Next(2) == 1)
            _timers[1].counter--;  // RAM refresh

        _timers[0].counter--;  // counter
       
        if (_timers[0].counter == 0 && _timers[0].mode == 0 && _timers[0].is_running == true)
        {
            _timers[0].is_running = false;

            // interrupt
            return true;
        }

        _timers[2].counter--;  // speaker

        return false;
    }
}

// programmable interrupt controller (PIC)
internal class pic8259
{
    bool _init = false;
    byte _init_data = 0;
    byte _ICW1, _ICW2, _ICW3, _ICW4;
    bool _is_ocw = false;
    int _ocw_nr = 0;
    byte _OCW1, _OCW2, _OCW3;

    int _int_offset = 8;
    byte _interrupt_mask = 0xff;

    byte [] register_cache = new byte[2];

    public pic8259()
    {
    }

    public byte In(Dictionary <int, int> scheduled_interrupts, ushort addr)
    {
        if (_is_ocw)
        {
            Log.DoLog($"8259 IN: is ocw {_ocw_nr}, read nr {_ocw_nr}");

            if (_ocw_nr == 0)
            {
                _ocw_nr++;
                return _OCW1;
            }
            else if (_ocw_nr == 1)
            {
                _ocw_nr++;
                return _OCW2;
            }
            else if (_ocw_nr == 2)
            {
                _ocw_nr++;
                return _OCW3;
            }
            else
            {
                Log.DoLog($"8259 IN: OCW nr is {_ocw_nr}");
                _is_ocw = false;
            }
        }

        return register_cache[addr];
    }

    public void Tick(Dictionary <int, int> scheduled_interrupts)
    {
    }

    public void Out(Dictionary <int, int> scheduled_interrupts, ushort addr, byte value)
    {
        Log.DoLog($"8259 OUT port {addr} value {value:X2}");

        register_cache[addr] = value;

        if (addr == 0)
        {
            if ((value & 128) == 0)
            {
                _init = (value & 16) == 16;

                Log.DoLog($"8259 OUT: is init: {_init}");
            }
            else
            {
                Log.DoLog($"8259 OUT: is OCW, value {value:X2}");

                _is_ocw = true;
                _OCW1 = value;
                _ocw_nr = 0;
            }
        }
        else if (addr == 1)
        {
            if (_init)
            {
                Log.DoLog($"8259 OUT: is ICW, value {value:X2}, {_init_data:X}");

                if ((_init_data & 16) == 16)  // waiting for ICW2
                {
                    _ICW2 = value;

                    _init_data = (byte)(_init_data & ~16);
                }
                else if ((_init_data & 2) == 2)  // waiting for ICW3
                {
                    _ICW3 = value;

                    _init_data = (byte)(_init_data & ~2);
                }
                else if ((_init_data & 1) == 1)  // waiting for ICW4
                {
                    _ICW4 = value;

                    _init_data = (byte)(_init_data & ~1);
                }
                else
                {
                    _init = false;
                }
            }
            else if (_is_ocw)
            {
                Log.DoLog($"8259 OUT: is OCW, value {value:X2}, {_ocw_nr}");

                if (_ocw_nr == 0)
                    _OCW2 = value;
                else if (_ocw_nr == 1)
                    _OCW3 = value;
                else
                {
                    Log.DoLog($"8259 OCW OUT nr is {_ocw_nr}");
                    _is_ocw = false;
                }

                _ocw_nr++;
            }
        }
        else
        {
            Log.DoLog($"8259 OUT has no port {addr:X2}");
        }
    }

    public int get_interrupt_offset()
    {
        return _int_offset;
    }
}

class Terminal
{
    private byte [,,] _chars = new byte[8, 25, 80];
    private byte [,,] _meta = new byte[8, 25, 80];
    private int _page = 0;
    private int _x = 0;
    private int _y = 0;

    public Terminal()
    {
    //    TerminalClear();
    }

    private void TerminalClear()
    {
        Console.Write((char)27);  // clear screen
        Console.Write($"[2J");
    }

    private void Redraw()
    {
        TerminalClear();

        for(int y=0; y<25; y++)
        {
            for(int x=0; x<80; x++)
                DrawChar(x, y);
        }
    }

    public void Clear()
    {
        for(int y=0; y<25; y++)
        {
            for(int x=0; x<80; x++)
            {
                _chars[_page, y, x] = 0;
                _meta[_page, y, x] = 0;
            }
        }

        Redraw();
    }

    public void SetPage(int page)
    {
        _page = page;

        Redraw();
    }

    private void DrawChar(int x, int y)
    {
        Console.Write((char)27);  // position cursor
        Console.Write($"[{y + 1};{x + 1}H");

        byte m = _meta[_page, y, x];
        int bg_col = m >> 4;
        int fg_col = m & 15;

        if (fg_col >= 8)
        {
            Console.Write((char)27);  // set to increased intensity
            Console.Write($"[1m");
        }
        else
        {
            Console.Write((char)27);  // set to normal
            Console.Write($"[22m");
        }

        Console.Write((char)27);  // set color
        Console.Write($"[{30 + (fg_col & 7)};{40 + (bg_col & 7)}m");

        char c = (char)_chars[_page, y, x];

        if (c == 0x00)
            c = ' ';

        Console.Write(c);  // emit character
    }

    public (int, int) GetXY()
    {
        return (_x, _y);
    }

    public int GetX()
    {
        return _x;
    }

    public int GetY()
    {
        return _y;
    }

    public void SetXY(int x, int y)
    {
        if (x < 80)
            _x = x;

        if (y < 25)
            _y = y;
    }

    public (int, int) GetText(int x, int y)
    {
        return (_meta[_page, y, x], _chars[_page, y, x]);
    }

    public void PutText(byte m, byte c)
    {
        if (c == 13)
            _x = 0;
        else if (c == 10)
            _y++;
        else
        {
            _chars[_page, _y, _x] = c;
            _meta[_page, _y, _x] = m;

            DrawChar(_x, _y);

            _x++;
        }

        if (_x == 80)
        {
            _x = 0;

            _y++;
        }

        if (_y == 25)
        {
            // move
            for(int y=1; y<25; y++)
            {
                for(int x=0; x<80; x++)
                {
                    _chars[_page, y - 1, x] = _chars[_page, y, x];
                    _meta[_page, y - 1, x] = _meta[_page, y, x];

                    DrawChar(x, y - 1);
                }
            }

            // clear last line
            for(int x=0; x<80; x++)
            {
                _chars[_page, 24, x] = 0;
                _meta[_page, 24, x] = 0;

                DrawChar(x, 24);
            }

            _y = 24;
        }
    }
}

class IO
{
    private i8253 _i8253 = new();
    private pic8259 _pic = new();

    private Terminal _t = new();

    private bool floppy_0_state = false;

    private Dictionary <ushort, byte> values = new Dictionary <ushort, byte>();

    public byte In(Dictionary <int, int> scheduled_interrupts, ushort addr)
    {
        Log.DoLog($"IN: {addr:X4}");

        if (addr == 0x0008)  // DMA status register
            return 0x0f;  // 'transfer complete'

        if (addr == 0x0020 || addr == 0x0021)  // PIC
            return _pic.In(scheduled_interrupts, (ushort)(addr - 0x0020));

        if (addr == 0x0040)
            return _i8253.get_counter(0);

        if (addr == 0x0041)
            return _i8253.get_counter(1);

        if (addr == 0x0042)
            return _i8253.get_counter(2);

        if (addr == 0x0062)  // PPI (XT only)
        {
            // note: the switch bits are inverted when read through the PPI
            byte mode = 0;

            if (values.ContainsKey(0x61))
                 mode = values[0x61];

            if ((mode & 8) == 0)
                return 3;  // ~(LOOP IN POST, COPROCESSOR INSTALLED)

            return 0b00100000 ^ 0xff;  // 1 floppy drive, 80x25 color, 64kB, reserved=00
        }

        if (addr == 0x0210)  // verify expansion bus data
            return 0xa5;

        if (addr == 0x03f4)
        {
            // diskette controller main status register
            floppy_0_state = !floppy_0_state;

            return (byte)(floppy_0_state ? 0x91 : 0x80);
        }

        if (addr == 0x03f5)  // diskette command/data register 0 (ST0)
            return 0b00100000;  // seek completed

#if DEBUG
        Log.DoLog($"IN: I/O port {addr:X4} not implemented");
#endif

        if (values.ContainsKey(addr))
            return values[addr];

        return 0;
    }

    public void Tick(Dictionary <int, int> scheduled_interrupts)
    {
        if (_i8253.Tick())
            scheduled_interrupts[_pic.get_interrupt_offset() + 0] = 10;
    }

    public void Out(Dictionary <int, int> scheduled_interrupts, ushort addr, byte value)
    {
        Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2})");

        // TODO

        if (addr == 0x0020 || addr == 0x0021)  // PIC
            _pic.Out(scheduled_interrupts, (ushort)(addr - 0x0020), value);

        else if (addr == 0x0040)
            _i8253.latch_counter(0, value);

        else if (addr == 0x0041)
            _i8253.latch_counter(1, value);

        else if (addr == 0x0042)
            _i8253.latch_counter(2, value);

        else if (addr == 0x0043)
            _i8253.command(value);

        else if (addr == 0x0080)
            Console.WriteLine($"Manufacturer systems checkpoint {value:X2}");

        else if (addr == 0x0322)
        {
#if DEBUG
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) generate controller select pulse");
#endif
        }
        else if (addr == 0x03f2)
        {
            int harddisk_interrupt_nr = _pic.get_interrupt_offset() + 14;

            if (scheduled_interrupts.ContainsKey(harddisk_interrupt_nr) == false)
                scheduled_interrupts[harddisk_interrupt_nr] = 31;  // generate (XT disk-)controller select pulse (IRQ 5)
        }
        else if (addr == 0x03f2)
        {
#if DEBUG
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) FDC enable");
#endif

            scheduled_interrupts[_pic.get_interrupt_offset() + 6] = 10;  // FDC enable (controller reset) (IRQ 6)
        }
        else
        {
#if DEBUG
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented");
#endif
        }

        values[addr] = value;
    }
}

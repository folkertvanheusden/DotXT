internal struct Timer
{
    public ushort counter_cur { get; set; }
    public ushort counter_ini { get; set; }
    public int    mode        { get; set; }
    public int    latch_type  { get; set; }
    public int    latch_n     { get; set; }
    public int    latch_n_cur { get; set; }
    public bool   is_running  { get; set; }
}

internal class i8253
{
    Timer [] _timers = new Timer[3];
    i8237 _i8237 = null;
    int clock;

    // using a static seed to make it behave
    // the same every invocation (until threads
    // and timers are introduced)
    private Random _random = new Random(1);

    public i8253()
    {
        for(int i=0; i<_timers.Length; i++)
            _timers[i] = new Timer();
    }

    public void SetDma(i8237 dma_instance)
    {
        _i8237 = dma_instance;
    }

    public void LatchCounter(int nr, byte v)
    {
#if DEBUG
        Log.DoLog($"OUT 8253: latch_counter {nr} to {v} (type {_timers[nr].latch_type}, {_timers[nr].latch_n_cur} out of {_timers[nr].latch_n})");
#endif

        if (_timers[nr].latch_n_cur > 0)
        {
            if (_timers[nr].latch_n_cur == 2)
            {
                _timers[nr].counter_ini &= 0xff00;
                _timers[nr].counter_ini |= v;
            }
            else if (_timers[nr].latch_n_cur == 1 && _timers[nr].latch_type == 3)
            {
                _timers[nr].counter_ini &= 0x00ff;
                _timers[nr].counter_ini |= (ushort)(v << 8);
            }
            else if (_timers[nr].latch_type == 1)
            {
                _timers[nr].counter_ini = v;
            }
            else if (_timers[nr].latch_type == 2)
            {
                _timers[nr].counter_ini = (ushort)(v << 8);
            }

            _timers[nr].latch_n_cur--;

            if (_timers[nr].latch_n_cur == 0)
            {
                Log.DoLog($"OUT 8253: counter {nr} started (count start: {_timers[nr].counter_ini})");

                _timers[nr].latch_n_cur = _timers[nr].latch_n;  // restart setup

                _timers[nr].counter_cur = _timers[nr].counter_ini;

                _timers[nr].is_running = true;
            }
        }
    }

    public byte GetCounter(int nr)
    {
#if DEBUG
        Log.DoLog($"OUT 8253: GetCounter {nr}: {(byte)_timers[nr].counter_cur}");
#endif

        return (byte)_timers[nr].counter_cur;
    }

    public void Command(byte v)
    {
        int nr    = v >> 6;
        int latch = (v >> 4) & 3;
        int mode  = (v >> 1) & 7;
        int type  = v & 1;

#if DEBUG
        Log.DoLog($"OUT 8253: command counter {nr}, latch {latch}, mode {mode}, type {type}");
#endif

        if (latch == 0)
        {
            _timers[nr].counter_cur = _timers[nr].counter_ini;
        }
        else
        {
            _timers[nr].mode       = mode;
            _timers[nr].latch_type = latch;
            _timers[nr].is_running = false;

            _timers[nr].counter_cur = 0;

            if (_timers[nr].latch_type == 1 || _timers[nr].latch_type == 2)
                _timers[nr].latch_n = 1;
            else if (_timers[nr].latch_type == 3)
                _timers[nr].latch_n = 2;

            _timers[nr].latch_n_cur = _timers[nr].latch_n;
        }
    }

    public bool Tick(int ticks)
    {
        clock += ticks;

        Log.DoLog($"{clock} cycles, {ticks} added");

        bool interrupt = false;

        while(clock >= 4)
        {
            for(int i=0; i<3; i++)
            {
                if (_timers[i].is_running == false)
                    continue;

                _timers[i].counter_cur--;

                if (_timers[i].counter_cur == 0)
                {
                    // timer 0 is RAM refresh counter
                    if (i == 0)
                        _i8237.TickChannel0();

                    _timers[i].counter_cur = _timers[i].counter_ini;

                    // mode 0 generates an interrupt
                    if (_timers[i].mode == 0)
                        interrupt = true;
                }   
            }

            clock -= 4;
        }

        return interrupt;
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

internal class FlipFlop
{
    bool state = false;

    public bool get_state()
    {
        bool rc = state;
        state = !state;
        return rc;
    }

    public void reset()
    {
        state = false;
    }
}

internal class b16buffer
{
    ushort _value;
    FlipFlop _f;

    public b16buffer(FlipFlop f)
    {
        _f = f;
    }

    public void Put(byte v)
    {
        bool low_high = _f.get_state();

        if (low_high)
        {
            _value &= 0xff;
            _value |= (ushort)(v << 8);
        }
        else
        {
            _value &= 0xff00;
            _value |= v;
        }
    }

    public ushort GetValue()
    {
        return _value;
    }

    public void SetValue(ushort v)
    {
        _value = v;
    }

    public byte Get()
    {
        bool low_high = _f.get_state();

        if (low_high)
            return (byte)(_value >> 8);

        return (byte)(_value & 0xff);
    }
}

internal class i8237
{
    byte [] _channel_page = new byte[4];
    b16buffer [] _channel_address_register = new b16buffer[4];
    b16buffer [] _channel_word_count = new b16buffer[4];
    byte _command;
    bool [] _channel_mask = new bool[4];
    bool [] _reached_tc = new bool[4];
    byte [] _channel_mode = new byte[4];
    FlipFlop _ff = new();
    bool _dma_enabled = true;
    Bus _b;

    public i8237(Bus b)
    {
        for(int i=0; i<4; i++) {
            _channel_address_register[i] = new b16buffer(_ff);
            _channel_word_count[i] = new b16buffer(_ff);
        }

        _b = b;
    }

    public void Tick(int ticks)
    {
    }

    public void TickChannel0()
    {
        // RAM refresh
        _channel_address_register[0].SetValue((ushort)(_channel_address_register[0].GetValue() + 1));

        _channel_word_count[0].SetValue((ushort)(_channel_word_count[0].GetValue() - 1));
    }

    public byte In(Dictionary <int, int> scheduled_interrupts, ushort addr)
    {
        string prefix = $"8237_IN: {addr:X4}";

        byte v = 0;

        if (addr == 0 || addr == 2 || addr == 4 || addr == 6)
        {
            v = _channel_address_register[addr / 2].Get();

            // This hack is to make sure the bios doesn't wait forever.
            // With proper cycle-count emulation this is not required.
            if (addr == 0)
                v = 0xfe;

            Log.DoLog($"{prefix} {v:X2}");
        }

        else if (addr == 1 || addr == 3 || addr == 5 || addr == 7)
        {
            v = _channel_word_count[addr / 2].Get();

            Log.DoLog($"{prefix} {v:X2}");
        }

        else if (addr == 8)  // status register
        {
            for(int i=0; i<4; i++)
            {
                if (_reached_tc[i])
                {
                    _reached_tc[i] = false;

                    v |= (byte)(1 << i);
                }
            }
        }

        else
        {
            Log.DoLog($"{prefix} ?");
        }

        return v;
    }

    void reset_masks(bool state)
    {
        for(int i=0; i<4; i++)
            _channel_mask[i] = state;
    }

    public void Out(Dictionary <int, int> scheduled_interrupts, ushort addr, byte value)
    {
        Log.DoLog($"8237_OUT: {addr:X4} {value:X2}");

        if (addr == 0 || addr == 2 || addr == 4 || addr == 6)
            _channel_address_register[addr / 2].Put(value);

        else if (addr == 1 || addr == 3 || addr == 5 || addr == 7)
            _channel_word_count[addr / 2].Put(value);

        else if (addr == 8)
        {
            _command = value;

            _dma_enabled = (_command & 4) == 0;
        }

        else if (addr == 0x0a)  // mask
            _channel_mask[value & 3] = (value & 4) == 4;  // dreq enable/disable

        else if (addr == 0x0b)  // mode register
            _channel_mode[value & 3] = value;

        else if (addr == 0x0c)  // reset flipflop
            _ff.reset();

        else if (addr == 0x0d)  // master reset
        {
            reset_masks(true);
            _ff.reset();
            // TODO: clear status
        }

        else if (addr == 0x0e)  // reset masks
        {
            reset_masks(false);
        }

        else if (addr == 0x0f)  // multiple mask
        {
            for(int i=0; i<4; i++)
                _channel_mask[i] = (value & (1 << i)) != 0;
        }
        else if (addr == 0x87)
        {
            _channel_page[0] = (byte)(value & 0x0f);
        }
        else if (addr == 0x83)
        {
            _channel_page[1] = (byte)(value & 0x0f);
        }
        else if (addr == 0x81)
        {
            _channel_page[2] = (byte)(value & 0x0f);
        }
        else if (addr == 0x82)
        {
            _channel_page[3] = (byte)(value & 0x0f);
        }
    }

    // used by devices, e.g. floppy
    public bool SendToChannel(int channel, byte value)
    {
        if (_dma_enabled == false)
            return false;

        if (_channel_mask[channel])
            return false;

        if (_reached_tc[channel])
            return false;

        ushort addr = _channel_address_register[channel].GetValue();

        uint full_addr = (uint)((_channel_page[channel] << 16) | addr);

        _b.WriteByte(full_addr, value);

        addr++;
         _channel_address_register[channel].SetValue(addr);

         ushort count = _channel_word_count[channel].GetValue();

         count--;

         if (count == 0xffff)
             _reached_tc[channel] = true;

         _channel_word_count[channel].SetValue(count);

        return true;
    }
}

class FloppyDisk
{
    i8237 _dma_controller;
    pic8259 _pic;

    public FloppyDisk(i8237 dma_controller, pic8259 pic)
    {
        _dma_controller = dma_controller;
        _pic = pic;
    }

    public byte In(Dictionary <int, int> scheduled_interrupts, ushort addr)
    {
        Log.DoLog($"Floppy-IN {addr:X4}");

        if (addr == 0x3f4)
            return 128;

        return 0x00;
    }

    public void Out(Dictionary <int, int> scheduled_interrupts, ushort addr, byte value)
    {
        Log.DoLog($"Floppy-OUT {addr:X4} {value:X2}");

        if (addr == 0x3f2)
            scheduled_interrupts[_pic.get_interrupt_offset() + 6] = 10;  // FDC enable (controller reset) (IRQ 6)
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
    private i8237 _i8237;

    private Terminal _t = new();

    private Bus _b;

    private FloppyDisk _fd;

    private Dictionary <ushort, byte> values = new Dictionary <ushort, byte>();

    public IO(Bus b)
    {
        _b = b;

        _i8237 = new(_b);
        _i8253.SetDma(_i8237);

        _fd = new(_i8237, _pic);
    }

    public byte In(Dictionary <int, int> scheduled_interrupts, ushort addr)
    {
        Log.DoLog($"IN: {addr:X4}");

        if (addr <= 0x000f || addr == 0x81 || addr == 0x82 || addr == 0x83 || addr == 0xc2)
            return _i8237.In(scheduled_interrupts, addr);

        if (addr == 0x0008)  // DMA status register
            return 0x0f;  // 'transfer complete'

        if (addr == 0x0020 || addr == 0x0021)  // PIC
            return _pic.In(scheduled_interrupts, (ushort)(addr - 0x0020));

        if (addr == 0x0040)
            return _i8253.GetCounter(0);

        if (addr == 0x0041)
            return _i8253.GetCounter(1);

        if (addr == 0x0042)
            return _i8253.GetCounter(2);

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

        if (addr >= 0x03f0 && addr <= 0x3f7)
            return _fd.In(scheduled_interrupts, addr);

#if DEBUG
        Log.DoLog($"IN: I/O port {addr:X4} not implemented");
#endif

        if (values.ContainsKey(addr))
            return values[addr];

        return 0;
    }

    public void Tick(Dictionary <int, int> scheduled_interrupts, int ticks)
    {
        if (_i8253.Tick(ticks))
            scheduled_interrupts[_pic.get_interrupt_offset() + 0] = 10;

        _i8237.Tick(ticks);
    }

    public void Out(Dictionary <int, int> scheduled_interrupts, ushort addr, byte value)
    {
        Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2})");

        if (addr <= 0x000f || addr == 0x81 || addr == 0x82 || addr == 0x83 || addr == 0xc2) // 8237
            _i8237.Out(scheduled_interrupts, addr, value);

        else if (addr == 0x0020 || addr == 0x0021)  // PIC
            _pic.Out(scheduled_interrupts, (ushort)(addr - 0x0020), value);

        else if (addr == 0x0040)
            _i8253.LatchCounter(0, value);

        else if (addr == 0x0041)
            _i8253.LatchCounter(1, value);

        else if (addr == 0x0042)
            _i8253.LatchCounter(2, value);

        else if (addr == 0x0043)
            _i8253.Command(value);

        else if (addr == 0x0080)
            Console.WriteLine($"Manufacturer systems checkpoint {value:X2}");

        else if (addr == 0x0322)
        {
            int harddisk_interrupt_nr = _pic.get_interrupt_offset() + 14;

            if (scheduled_interrupts.ContainsKey(harddisk_interrupt_nr) == false)
                scheduled_interrupts[harddisk_interrupt_nr] = 31;  // generate (XT disk-)controller select pulse (IRQ 5)

#if DEBUG
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) generate controller select pulse");
#endif
        }

        else if (addr >= 0x03f0 && addr <= 0x3f7)
            _fd.Out(scheduled_interrupts, addr, value);

        else
        {
#if DEBUG
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented");
#endif
        }

        values[addr] = value;
    }
}

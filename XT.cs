namespace DotXT;

internal class Log
{
    public static void DoLog(string what)
    {
        File.AppendAllText(@"logfile.txt", what + Environment.NewLine);
    }
}

internal class Memory
{
    private const uint size = 32 * 1024;

    private readonly byte[] _m = new byte[size]; // 1MB of RAM

    public byte ReadByte(uint address)
    {
        if (address >= size)
            return 0xee;

        return _m[address];
    }

    public void WriteByte(uint address, byte v)
    {
        if (address < size)
            _m[address] = v;
    }
}

internal class Rom
{
    private readonly byte[] _contents;

    public Rom(string filename)
    {
        _contents = File.ReadAllBytes(filename);
    }

    public byte ReadByte(uint address)
    {
        return _contents[address];
    }
}

internal class Bus
{
    private readonly Memory _m = new();

    private readonly Rom _bios = new("roms/BIOS_5160_16AUG82_U18_5000026.BIN");
    private readonly Rom _basic = new("roms/BIOS_5160_16AUG82_U19_5000027.BIN");

    public byte ReadByte(uint address)
    {
        if (address is >= 0x000f8000 and <= 0x000fffff)
            return _bios.ReadByte(address - 0x000f8000);

        if (address is >= 0x000f0000 and <= 0x000f1fff)
            return _basic.ReadByte(address - 0x000f0000);

        return _m.ReadByte(address);
    }

    public void WriteByte(uint address, byte v)
    {
//        if (address >= 0x0009fc00)
//            Console.WriteLine($"{address:X6} {v:X2}");

        _m.WriteByte(address, v);
    }
}

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

    private Random _random = new Random();

    public i8253()
    {
        for(int i=0; i<_timers.Length; i++)
            _timers[i] = new Timer();
    }

    public void latch_counter(int nr, byte v)
    {
        Log.DoLog($"OUT 8253: latch_counter {nr} to {v}");

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
        Log.DoLog($"OUT 8253: get_counter {nr}");

        return (byte)_timers[nr].counter;
    }

    public void command(byte v)
    {
        int counter = v >> 6;
        int latch   = (v >> 4) & 3;
        int mode    = (v >> 1) & 7;
        int type    = v & 1;

        Log.DoLog($"OUT 8253: command counter {counter}, latch {latch}, mode {mode}, type {type}");

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

internal class IO
{
    private i8253 _i8253 = new i8253();

    private Dictionary <ushort, byte> values = new Dictionary <ushort, byte>();

    public byte In(ushort addr)
    {
        if (addr == 0x0008)  // DMA status register
            return 0x0f;  // 'transfer complete'

        if (addr == 0x0040)
            return (byte)_i8253.get_counter(0);

        if (addr == 0x0041)
            return (byte)_i8253.get_counter(1);

        if (addr == 0x0042)
            return (byte)_i8253.get_counter(2);

        if (addr == 0x0062)  // PPI (XT only)
            return 0x01;  // LOOP IN POST

        if (addr == 0x0210)  // verify expansion bus data
            return 0xa5;

        Log.DoLog($"IN: I/O port {addr:X4} not implemented");

        if (values.ContainsKey(addr))
            return values[addr];

        return 0;
    }

    public int Tick()
    {
        if (_i8253.Tick())
            return 8;

        return -1;
    }

    public void Out(ushort addr, byte value)
    {
        // TODO

        if (addr == 0x0040)
            _i8253.latch_counter(0, value);

        else if (addr == 0x0041)
            _i8253.latch_counter(1, value);

        else if (addr == 0x0042)
            _i8253.latch_counter(2, value);

        else if (addr == 0x0043)
            _i8253.command(value);

        else
            Log.DoLog($"OUT: I/O port {addr:X4} ({value:X2}) not implemented");

        values[addr] = value;
    }
}

internal class P8086
{
    private byte _ah, _al;
    private byte _bh, _bl;
    private byte _ch, _cl;
    private byte _dh, _dl;

    private ushort _si;
    private ushort _di;
    private ushort _bp;
    private ushort _sp;

    private ushort _ip;

    private ushort _cs;
    private ushort _ds;
    private ushort _es;
    private ushort _ss;

    // replace by an Optional-type when available
    private ushort segment_override;
    private bool segment_override_set;

    private ushort _flags;

    private const uint MemMask = 0x00ffffff;

    private readonly Bus _b = new();

    private readonly IO _io = new();

    public P8086()
    {
        _cs = 0xf000;
        _ip = 0xfff0;
    }

    private bool intercept_int(int nr)
    {
        if (nr == 0x10 && _ah == 0x0e)
            Console.Write((char)_al);
        else
            Console.WriteLine($"INT NR {nr:X2}, AH: {_ah:X2}");

        return false;
    }

    private byte GetPcByte()
    {
        uint address = (uint)(_cs * 16 + _ip++) & MemMask;

        byte val = _b.ReadByte(address);

        // Log.DoLog($"{address:X} {val:X}");

        return val;
    }

    private ushort GetPcWord()
    {
        ushort v = 0;

        v |= GetPcByte();
        v |= (ushort)(GetPcByte() << 8);

        return v;
    }

    private ushort GetAX()
    {
        return (ushort)((_ah << 8) | _al);
    }

    private void SetAX(ushort v)
    {
        _ah = (byte)(v >> 8);
        _al = (byte)v;
    }

    private ushort GetBX()
    {
        return (ushort)((_bh << 8) | _bl);
    }

    private void SetBX(ushort v)
    {
        _bh = (byte)(v >> 8);
        _bl = (byte)v;
    }

    private ushort GetCX()
    {
        return (ushort)((_ch << 8) | _cl);
    }

    private void SetCX(ushort v)
    {
        _ch = (byte)(v >> 8);
        _cl = (byte)v;
    }

    private ushort GetDX()
    {
        return (ushort)((_dh << 8) | _dl);
    }

    private void SetDX(ushort v)
    {
        _dh = (byte)(v >> 8);
        _dl = (byte)v;
    }

    private void WriteMemByte(ushort segment, ushort offset, byte v)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        // Log.DoLog($"WriteMemByte {segment:X4}:{offset:X4}: a:{a:X6}, v:{v:X2}");

       _b.WriteByte(a, v);
    }

    private void WriteMemWord(ushort segment, ushort offset, ushort v)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

        // Log.DoLog($"WriteMemWord {segment:X4}:{offset:X4}: a1:{a1:X6}, v:{v:X2}");

       _b.WriteByte(a1, (byte)v);
       _b.WriteByte(a2, (byte)(v >> 8));
    }

    private byte ReadMemByte(ushort segment, ushort offset)
    {
        uint a = (uint)(((segment << 4) + offset) & MemMask);

        // Log.DoLog($"ReadMemByte {segment:X4}:{offset:X4}: {a:X6}");

        return _b.ReadByte(a);
    } 

    private ushort ReadMemWord(ushort segment, ushort offset)
    {
        uint a1 = (uint)(((segment << 4) + offset) & MemMask);
        uint a2 = (uint)(((segment << 4) + ((offset + 1) & 0xffff)) & MemMask);

        return (ushort)(_b.ReadByte(a1) | (_b.ReadByte(a2) << 8));
    } 

    private (ushort, string) GetRegister(int reg, bool w)
    {
        if (w)
        {
            if (reg == 0)
                return (GetAX(), "AX");
            if (reg == 1)
                return (GetCX(), "CX");
            if (reg == 2)
                return (GetDX(), "DX");
            if (reg == 3)
                return (GetBX(), "BX");
            if (reg == 4)
                return (_sp, "SP");
            if (reg == 5)
                return (_bp, "BP");
            if (reg == 6)
                return (_si, "SI");
            if (reg == 7)
                return (_di, "DI");
        }
        else
        {
            if (reg == 0)
                return (_al, "AL");
            if (reg == 1)
                return (_cl, "CL");
            if (reg == 2)
                return (_dl, "DL");
            if (reg == 3)
                return (_bl, "BL");
            if (reg == 4)
                return (_ah, "AH");
            if (reg == 5)
                return (_ch, "CH");
            if (reg == 6)
                return (_dh, "DH");
            if (reg == 7)
                return (_bh, "BH");
        }

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(GetRegister)}");

        return (0, "error");
    }

    private (ushort, string) GetSRegister(int reg)
    {
        if (reg == 0b000)
            return (_es, "ES");
        if (reg == 0b001)
            return (_cs, "CS");
        if (reg == 0b010)
            return (_ss, "SS");
        if (reg == 0b011)
            return (_ds, "DS");

        Log.DoLog($"reg {reg} not supported for {nameof(GetSRegister)}");

        return (0, "error");
    }

    private (ushort, string) GetDoubleRegisterMod00(int reg)
    {
        ushort a = 0;
        string name = "error";

        if (reg == 0)
        {
            a = (ushort)(GetBX() + _si);
            name = "[BX+SI]";
        }
        else if (reg == 1)
        {
            a = (ushort)(GetBX() + _di);
            name = "[BX+DI]";
        }
        else if (reg == 2)
        {
            a = (ushort)(_bp + _si);
            name = "[BP+SI]";
        }
        else if (reg == 3)
        {
            a = (ushort)(_bp + _di);
            name = "[BP+DI]";
        }
        else if (reg == 4)
        {
            a = _si;
            name = "[SI]";
        }
        else if (reg == 5)
        {
            a = _di;
            name = "[DI]";
        }
        else if (reg == 6)
        {
            a = GetPcWord();

            name = $"[${a:X4}]";
        }
        else if (reg == 7)
        {
            a = GetBX();
            name = "[BX]";
        }
        else
        {
            Log.DoLog($"{nameof(GetDoubleRegisterMod00)} {reg} not implemented");
        }

        return (a, name);
    }

    private (ushort, string) GetDoubleRegisterMod01(int reg)
    {
        ushort a = 0;
        string name = "error";

        if (reg == 6)
        {
            a = _bp;
            name = "[BP]";
        }
        else
        {
            (a, name) = GetDoubleRegisterMod00(reg);
        }

        ushort disp = GetPcWord();

        return ((ushort)(a + disp), name + $" disp {disp:X4}");
    }

    private (ushort, string) GetRegisterMem(int reg, int mod, bool w)
    {
        if (mod == 0)
        {
            (ushort a, string name) = GetDoubleRegisterMod00(reg);

            ushort segment = segment_override_set ? segment_override : _ds;

            name += $" (${segment * 16 + a:X6})";

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            return (v, name);
        }

        if (mod == 1)
        {
            (ushort a, string name) = GetDoubleRegisterMod01(reg);

            ushort segment = segment_override_set ? segment_override : _ds;

            name += $" (${segment * 16 + a:X6})";

            ushort v = w ? ReadMemWord(segment, a) : ReadMemByte(segment, a);

            return (v, name);
        }

        if (mod == 3)
            return GetRegister(reg, w);

        Log.DoLog($"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}");

        return (0, "error");
    }

    private string PutRegister(int reg, bool w, ushort val)
    {
        if (reg == 0)
        {
            if (w)
            {
                SetAX(val);

                return "AX";
            }

            _al = (byte)val;

            return "AL";
        }

        if (reg == 1)
        {
            if (w)
            {
                SetCX(val);

                return "CX";
            }

            _cl = (byte)val;

            return "CL";
        }

        if (reg == 2)
        {
            if (w)
            {
                SetDX(val);

                return "DX";
            }

            _dl = (byte)val;

            return "DL";
        }

        if (reg == 3)
        {
            if (w)
            {
                SetBX(val);

                return "BX";
            }

            _bl = (byte)val;

            return "BL";
        }

        if (reg == 4)
        {
            if (w)
            {
                _sp = val;

                return "SP";
            }

            _ah = (byte)val;

            return "AH";
        }

        if (reg == 5)
        {
            if (w)
            {
                _bp = val;

                return "BP";
            }

            _ch = (byte)val;

            return "CH";
        }

        if (reg == 6)
        {
            if (w)
            {
                _si = val;

                return "SI";
            }

            _dh = (byte)val;

            return "DH";
        }

        if (reg == 7)
        {
            if (w)
            {
                _di = val;

                return "DI";
            }

            _bh = (byte)val;

            return "BH";
        }

        Log.DoLog($"reg {reg} w {w} not supported for {nameof(PutRegister)} ({val:X})");

        return "error";
    }

    private string PutSRegister(int reg, ushort v)
    {
        if (reg == 0b000)
        {
            _es = v;
            return "ES";
        }

        if (reg == 0b001)
        {
            _cs = v;
            return "CS";
        }

        if (reg == 0b010)
        {
            _ss = v;
            return "SS";
        }

        if (reg == 0b011)
        {
            _ds = v;
            return "DS";
        }

        Log.DoLog($"reg {reg} not supported for {nameof(PutSRegister)}");

        return "error";
    }

    private string PutRegisterMem(int reg, int mod, bool w, ushort val)
    {
        if (mod == 0)
        {
            (ushort a, string name) = GetDoubleRegisterMod00(reg);

            ushort segment = segment_override_set ? segment_override : _ds;

            name += $" (${segment * 16 + a:X6})";

            WriteMemWord(segment, a, val);

            return name;
        }

        if (mod == 3)
            return PutRegister(reg, w, val);

        Log.DoLog($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(PutRegisterMem)}");

        return "error";
    }

    private void ClearFlagBit(int bit)
    {
        _flags &= (ushort)(ushort.MaxValue ^ (1 << bit));
    }

    private void SetFlagBit(int bit)
    {
        _flags |= (ushort)(1 << bit);
    }

    private void SetFlag(int bit, bool state)
    {
        if (state)
            SetFlagBit(bit);
        else
            ClearFlagBit(bit);
    }

    private bool GetFlag(int bit)
    {
        return (_flags & (1 << bit)) != 0;
    }

    private void SetFlagC(bool state)
    {
        SetFlag(0, state);
    }

    private bool GetFlagC()
    {
        return GetFlag(0);
    }

    private void SetFlagP(byte v)
    {
        int count = 0;

        while (v != 0)
        {
            count++;

            v &= (byte)(v - 1);
        }

        SetFlag(2, (count & 1) == 0);
    }

    private bool GetFlagP()
    {
        return GetFlag(2);
    }

    private void SetFlagA(bool state)
    {
        SetFlag(4, state);
    }

    private bool GetFlagA()
    {
        return GetFlag(4);
    }

    private void SetFlagZ(bool state)
    {
        SetFlag(6, state);
    }

    private bool GetFlagZ()
    {
        return GetFlag(6);
    }

    private void SetFlagS(bool state)
    {
        SetFlag(7, state);
    }

    private bool GetFlagS()
    {
        return GetFlag(7);
    }

    private void SetFlagD(bool state)
    {
        SetFlag(10, state);
    }

    private bool GetFlagD()
    {
        return GetFlag(10);
    }

    private void SetFlagO(bool state)
    {
        SetFlag(11, state);
    }

    private bool GetFlagO()
    {
        return GetFlag(11);
    }

    // TODO class/struct or enum Flags (with [Flags]) and ToString()
    private string GetFlagsAsString()
    {
        string @out = String.Empty;

        @out += GetFlagO() ? "o" : "-";
        @out += GetFlagD() ? "d" : "-";
        @out += GetFlagS() ? "s" : "-";
        @out += GetFlagZ() ? "z" : "-";
        @out += GetFlagC() ? "c" : "-";

        return @out;
    }

    public void push(ushort v)
    {
        _sp -= 2;

        WriteMemWord(_ss, _sp, v);
    }

    public ushort pop()
    {
        ushort v = ReadMemWord(_ss, _sp);

        _sp += 2;

        return v;
    }

    public void Tick()
    {
        string flagStr = GetFlagsAsString();

        // tick I/O, check for interrupt
        int interrupt_nr = _io.Tick();

        if (interrupt_nr != -1 && GetFlag(9) == true)
        {
            push(_flags);
            push(_cs);
            push(_ip);

            uint addr = (uint)(interrupt_nr * 4);

            _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
            _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));

            Log.DoLog($"{flagStr} ------ INT {interrupt_nr:X2}");
        }

        uint address = (uint)(_cs * 16 + _ip) & MemMask;
        byte opcode = GetPcByte();

        // handle prefixes
        if (opcode == 0x26 || opcode == 0x2e || opcode == 0x36 || opcode == 0x3e)
        {
            if (opcode == 0x26)
                segment_override = _es;
            else if (opcode == 0x2e)
                segment_override = _cs;
            else if (opcode == 0x36)
                segment_override = _ss;
            else if (opcode == 0x3e)
                segment_override = _ds;
	    else
                Log.DoLog($"------ {address:X6} segment override {opcode:X2} not implemented");

            segment_override_set = true;

            address = (uint)(_cs * 16 + _ip) & MemMask;
            opcode = GetPcByte();
        }

        string prefixStr =
            $"{flagStr} {address:X6} {opcode:X2} AX:{_ah:X2}{_al:X2} BX:{_bh:X2}{_bl:X2} CX:{_ch:X2}{_cl:X2} DX:{_dh:X2}{_dl:X2} SP:{_sp:X4} BP:{_bp:X4} SI:{_si:X4} DI:{_di:X4} ES:{_es:X4} | ";

        // main instruction handling
        if (opcode == 0x04 || opcode == 0x14)
        {
            // ADD AL,xx
            byte v = GetPcByte();

            string name = "ADD";

            _al += v;

            if (opcode == 0x14 && GetFlagC())
            {
                _al++;
                name = "ADC";
            }

            Log.DoLog($"{prefixStr} {name} AL,${v:X2}");
        }
        else if (opcode == 0x05)
        {
            // ADD AX,xxxx
            ushort v = GetPcWord();

            SetAX((ushort)(GetAX() + v));

            Log.DoLog($"{prefixStr} ADD AX,${v:X4}");
        }
        else if (opcode == 0x06)
        {
            // PUSH ES
            push(_es);

            Log.DoLog($"{prefixStr} PUSH ES");
        }
        else if (opcode == 0x07)
        {
            // POP ES
            _es = pop();

            Log.DoLog($"{prefixStr} POP ES");
        }
        else if (opcode == 0x0e)
        {
            // PUSH CS
            push(_cs);

            Log.DoLog($"{prefixStr} PUSH CS");
        }
        else if (opcode == 0x16)
        {
            // PUSH SS
            push(_ss);

            Log.DoLog($"{prefixStr} PUSH SS");
        }
        else if (opcode == 0x1e)
        {
            // PUSH DS
            push(_ds);

            Log.DoLog($"{prefixStr} PUSH DS");
        }
        else if (opcode == 0x1f)
        {
            // POP DS
            _ds = pop();

            Log.DoLog($"{prefixStr} POP DS");
        }
        else if (opcode == 0x27)
        {
            // DAA
            // from https://stackoverflow.com/questions/8119577/z80-daa-instruction/8119836
            int t = 0;

            t += GetFlagA() || (_al & 0x0f) > 9 ? 1 : 0;

            if (GetFlagC() || _al > 0x99)
            {
                t += 2;
                SetFlagC(true);
            }

            if (GetFlagS() && !GetFlagA())
                SetFlagA(false);
            else
            {
                if (GetFlagS() && GetFlagA())
                    SetFlagA((_al & 0x0F) < 6);
                else
                    SetFlagA((_al & 0x0F) >= 0x0A);
            }

            bool n = GetFlagS();
    
            if (t == 1)
                _al += (byte)(n ? 0xFA:0x06); // -6:6
            else if (t == 2)
                _al += (byte)(n ? 0xA0:0x60); // -0x60:0x60
            else if (t == 3)
                _al += (byte)(n ? 0x9A:0x66); // -0x66:0x66

            SetFlagS((_al & 0x80) == 0x80);
            SetFlagZ(_al != 0);
            SetFlagP(_al);

            Log.DoLog($"{prefixStr} DAA");
        }
        else if (opcode == 0x58)
        {
            // POP AX
            SetAX(pop());

            Log.DoLog($"{prefixStr} POP AX");
        }
        else if (opcode == 0x59)
        {
            // POP CX
            SetCX(pop());

            Log.DoLog($"{prefixStr} POP CX");
        }
        else if (opcode == 0x5a)
        {
            // POP DX
            SetDX(pop());

            Log.DoLog($"{prefixStr} POP DX");
        }
        else if (opcode == 0x5b)
        {
            // POP BX
            SetBX(pop());

            Log.DoLog($"{prefixStr} POP BX");
        }
        else if (opcode == 0x5e)
        {
            // POP SI
            _si = pop();

            Log.DoLog($"{prefixStr} POP SI");
        }
        else if (opcode == 0x5f)
        {
            // POP DI
            _di = pop();

            Log.DoLog($"{prefixStr} POP DI");
        }
        else if (opcode == 0xa5)
        {
            // MOVSW
            _b.WriteByte((uint)(_es * 16 + _di) & MemMask, _b.ReadByte((uint)(_ds * 16 + _si) & MemMask));
            _b.WriteByte((uint)(_es * 16 + _di + 1) & MemMask, _b.ReadByte((uint)(_ds * 16 + _si + 1) & MemMask));  // TODO: handle segment wrapping

            if (GetFlagD())
            {
                _si -= 2;
                _di -= 2;
            }
            else
            {
                _si += 2;
                _di += 2;
            }

            Log.DoLog($"{prefixStr} MOVSW");
        }
        else if (opcode == 0xe3)
	{
            // JCXZ np
            sbyte offset = (sbyte)GetPcByte();

	    ushort addr = (ushort)(_ip + offset);

	    if (GetCX() == 0)
		    _ip = addr;

            Log.DoLog($"{prefixStr} JCXZ {addr:X}");
	}
        else if (opcode == 0xe9)
        {
            // JMP np
            short offset = (short)GetPcWord();

            _ip = (ushort)(_ip + offset);

            Log.DoLog($"{prefixStr} JMP {_ip:X}");
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            push(GetAX());

            Log.DoLog($"{prefixStr} PUSH AX");
        }
        else if (opcode == 0x51)
        {
            // PUSH CX
            push(GetCX());

            Log.DoLog($"{prefixStr} PUSH CX");
        }
        else if (opcode == 0x52)
        {
            // PUSH DX
            push(GetDX());

            Log.DoLog($"{prefixStr} PUSH DX");
        }
        else if (opcode == 0x53)
        {
            // PUSH BX
            push(GetBX());

            Log.DoLog($"{prefixStr} PUSH BX");
        }
        else if (opcode == 0x55)
        {
            // PUSH BP
            push(_bp);

            Log.DoLog($"{prefixStr} PUSH BP");
        }
        else if (opcode == 0x56)
        {
            // PUSH SI
            push(_si);

            Log.DoLog($"{prefixStr} PUSH SI");
        }
        else if (opcode == 0x57)
        {
            // PUSH DI
            push(_di);

            Log.DoLog($"{prefixStr} PUSH DI");
        }
        else if (opcode == 0x80 || opcode == 0x81 || opcode == 0x83)
        {
            // CMP and others
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            int function = (o1 >> 3) & 7;

            ushort r1 = 0;
            string name1 = "error";

            ushort r2 = 0;

            bool word = false;

            int result = 0;

            if (opcode == 0x80)
            {
                (r1, name1) = GetRegisterMem(reg, mod, false);

                r2 = GetPcByte();
            }
            else if (opcode == 0x81)
            {
                (r1, name1) = GetRegisterMem(reg, mod, true);

                r2 = GetPcWord();

                word = true;
            }
            else if (opcode == 0x83)
            {
                (r1, name1) = GetRegisterMem(reg, mod, true);

                r2 = GetPcByte();

                word = true;
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented");
            }

            string iname = "error";
            bool apply = true;

            if (function == 0)
            {
                result = r1 + r2;
                iname = "ADD";
            }
            else if (function == 1)
            {
                result = r1 | r2;
                iname = "OR";
            }
            else if (function == 4)
            {
                result = r1 & r2;
                iname = "AND";
            }
            else if (function == 5)
            {
                result = r1 - r2;
                iname = "SUB";
            }
            else if (function == 7)
            {
                result = r1 - r2;
                apply = false;
                iname = "CMP";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            SetFlagO(false); // TODO
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(((r1 & 0x10) ^ (r2 & 0x10) ^ (result & 0x10)) == 0x10);
            SetFlagP((byte)result);

            if (apply)
                PutRegisterMem(reg, mod, word, (ushort)result);

            Log.DoLog($"{prefixStr} {iname} {name1},${r2:X2}");
        }
        else if (opcode == 0x86)
        {
            // XCHG
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            ushort temp = r1;
            r1 = r2;
            r2 = temp;

            PutRegisterMem(reg2, mod, word, r1);
            PutRegister(reg1, word, r2);

            Log.DoLog($"{prefixStr} XCHG {name1},{name2}");
        }
        else if (opcode == 0x90)
        {
            // NOP
            Log.DoLog($"{prefixStr} NOP");
        }
        else if (opcode == 0x9c)
        {
            // PUSHF
            push(_flags);

            Log.DoLog($"{prefixStr} PUSHF");
        }
        else if (opcode == 0x9d)
        {
            // POPF
            _flags = pop();

            Log.DoLog($"{prefixStr} POPF");
        }
        else if (opcode == 0xac)
        {
            // LODSB
            _al = ReadMemByte(_ds, _si);

            if (GetFlagD())
                _si--;
            else
                _si++;

            Log.DoLog($"{prefixStr} LODSB");
        }
        else if (opcode == 0xc3)
        {
            // RET
            _ip = pop();

            Log.DoLog($"{prefixStr} RET");
        }
        else if (opcode == 0xcd)
        {
            // INT 0x..
            byte @int = GetPcByte();

            uint addr = (uint)(@int * 4);

            if (intercept_int(@int) == false)
            {
                push(_flags);
                push(_cs);
                push(_ip);

                _ip = (ushort)(_b.ReadByte(addr + 0) + (_b.ReadByte(addr + 1) << 8));
                _cs = (ushort)(_b.ReadByte(addr + 2) + (_b.ReadByte(addr + 3) << 8));
            }

            Log.DoLog($"{prefixStr} INT {@int:X2} -> ${_cs * 16 + _ip:X6} (from {addr:X4})");
        }
        else if (opcode == 0xcf)
        {
            // IRET
            _ip = pop();
            _cs = pop();
            _flags = pop();

            Log.DoLog($"{prefixStr} IRET");
        }
        else if (opcode == 0x02 || opcode == 0x03 || opcode == 0x2a || opcode == 0x2b || opcode == 0x3a || opcode == 0x3b)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            string name = "error";
            int result = 0;
           
            if (opcode == 0x02 || opcode == 0x03)
            {
                result = r2 + r1;

                name = "ADD";
            }
            else
            {
                result = r2 - r1;

                if (opcode == 0x3a || opcode == 0x3b)
                    name = "CMP";
                else
                    name = "SUB";
            }

            // 0x3a/0x3b are CMP
            if (opcode != 0x3a && opcode != 0x3b)
                PutRegister(reg1, word, (ushort)result);

            SetFlagO(false); // TODO
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(((r1 & 0x10) ^ (r2 & 0x10) ^ (result & 0x10)) == 0x10);
            SetFlagP((byte)result);

            Log.DoLog($"{prefixStr} {name} {name2},{name1}");
        }
        else if (opcode == 0x3c || opcode == 0x3d)
        {
            // CMP
            bool word = true;

            int result = 0;

            ushort r1 = 0;
            ushort r2 = 0;

            if (opcode == 0x3d)
            {
                r1 = GetAX();
                r2 = GetPcWord();

                result = r1 - r2;

                Log.DoLog($"{prefixStr} CMP AX,{r2:X4}");
            }
            else if (opcode == 0x3c)
            {
                r1 = _al;
                r2 = GetPcByte();

                word = false;

                result = r1 - r2;

                Log.DoLog($"{prefixStr} CMP AL,{r2:X2}");
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} not implemented");
            }

            SetFlagO(false); // TODO
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(((r1 & 0x10) ^ (r2 & 0x10) ^ (result & 0x10)) == 0x10);
            SetFlagP((byte)result);
        }
        else if (opcode is >= 0x30 and <= 0x33 || opcode is >= 0x20 and <= 0x23 || opcode is >= 0x08 and <= 0x0b)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1) = GetRegisterMem(reg1, mod, word);
            (ushort r2, string name2) = GetRegister(reg2, word);

            string name = "error";

            ushort result = 0;

            int function = opcode >> 4;

            if (function == 0)
            {
                result = (ushort)(r2 | r1);
                name = "OR";
            }
            else if (function == 2)
            {
                result = (ushort)(r2 & r1);
                name = "AND";
            }
            else if (function == 3) // TODO always true here?
            {
                result = (ushort)(r2 ^ r1);
                name = "XOR";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

//            if (opcode == 0x22)
//                Log.DoLog($"{name}, r1 {r1:X} ({reg1} | {name1}), r2 {r2:X} ({reg2} | {name2}), result {result:X}");

            PutRegisterMem(reg1, mod, word, result);

            SetFlagO(false);
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(false);

            SetFlagP((byte)result); // TODO verify

            Log.DoLog($"{prefixStr} {name} {name1},{name2}");
        }
        else if ((opcode == 0x34 || opcode == 0x35) || (opcode == 0x24 || opcode == 0x25) ||
                 (opcode == 0x0c || opcode == 0x0d))
        {
            bool word = (opcode & 1) == 1;

            byte bLow = GetPcByte();
            byte bHigh = word ? GetPcByte() : (byte)0;

            string tgt_name = word ? "AX" : "AL";
            string name = "error";

            int function = opcode >> 4;

            if (function == 0)
            {
                _al |= bLow;

                if (word)
                    _ah |= bHigh;

                name = "OR";
            }
            else if (function == 2)
            {
                _al &= bLow;

                if (word)
                    _ah &= bHigh;

                name = "AND";
            }
            else if (function == 3)
            {
                // TODO always true here
                _al ^= bLow;

                if (word)
                    _ah ^= bHigh;

                name = "XOR";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            SetFlagO(false);
            SetFlagS((word ? _ah & 0x8000 : _al & 0x80) != 0);
            SetFlagZ(word ? _ah == 0 && _al == 0 : _al == 0);
            SetFlagA(false);

            SetFlagP(word ? _ah : _al);

            Log.DoLog($"{prefixStr} {name} {tgt_name},${bHigh:X2}{bLow:X2}");
        }
        else if (opcode == 0xe8)
        {
            // CALL
            short a = (short)GetPcWord();

            push(_ip);

            _ip = (ushort)(a + _ip);

            Log.DoLog($"{prefixStr} CALL {a:X4} (${_ip:X4} -> ${_cs * 16 + _ip:X6})");
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            ushort temp_ip = GetPcWord();
            ushort temp_cs = GetPcWord();

            _ip = temp_ip;
            _cs = temp_cs;

            Log.DoLog($"{prefixStr} JMP ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
        }
        else if (opcode == 0xf6)
        {
            // TEST and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort r1, string name1) = GetRegisterMem(reg1, mod, word);

            byte r2 = GetPcByte();

            string name2 = $"{r2:X2}";

            string cmd_name = "error";
            ushort result = 0;

            int function = (o1 >> 3) & 7;

            if (function == 0)
            {
                // TEST
                result = (ushort)(r1 & r2);
                cmd_name = "TEST";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} o1 {o1:X2} function {function} not implemented");
            }

            SetFlagO(false);
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(false);

            Log.DoLog($"{prefixStr} {cmd_name} {name1},{name2}");
        }
        else if (opcode == 0xf7)
        {
            // MUL and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;
            int reg2 = (o1 >> 3) & 7;

            (ushort r1, string name1) = GetRegisterMem(reg1, mod, word);
            (ushort r2, string name2) = GetRegister(reg2, word);

            string cmd_name = "error";
            ushort result = 0;
            bool put = false;

            int function = (o1 >> 3) & 7;

            if (function == 2)
            {
                // TEST
                result = (ushort)~result;

                put = true;

                cmd_name = "NOT";
            }
            else if (function == 6)
            {
                // DIV
                uint dx_ax = (uint)((GetDX() << 16) | GetAX());

                SetAX((ushort)(dx_ax / r1));
                SetDX((ushort)(dx_ax % r1));

                cmd_name = "DIV";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} o1 {o1:X2} function {function} not implemented");
            }

            if (put)
                PutRegisterMem(reg1, mod, word, result);

            SetFlagO(false);
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(false);

            Log.DoLog($"{prefixStr} {cmd_name} {name1},{name2}");
        }
        else if (opcode == 0xfa)
        {
            // CLI
            ClearFlagBit(9); // IF

            Log.DoLog($"{prefixStr} CLI");
        }
        else if ((opcode & 0xf0) == 0xb0)
        {
            // MOV reg,ib
            int reg = opcode & 0x07;

            bool word = (opcode & 0x08) == 0x08;

            ushort v = GetPcByte();

            if (word)
                v |= (ushort)(GetPcByte() << 8);

            string name = PutRegister(reg, word, v);

            Log.DoLog($"{prefixStr} MOV {name},${v:X}");
        }
        else if (opcode == 0xa0)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _al = _b.ReadByte((uint)(a + (_ds << 4)));

            Log.DoLog($"{prefixStr} MOV AL,{a:X4}");
        }
        else if (opcode == 0xa1)
        {
            // MOV AX,[...]
            ushort a = GetPcWord();

            SetAX(ReadMemWord(_ds, a));

            Log.DoLog($"{prefixStr} MOV AL,{a:X4}");
        }
        else if (opcode == 0xa2)
        {
            // MOV AL,[...]
            ushort a = GetPcWord();

            _b.WriteByte((uint)(a + (_ds << 4)), _al);

            Log.DoLog($"{prefixStr} MOV [${a:X4}],AL");
        }
        else if (opcode == 0xa3)
        {
            // MOV [...],AX
            ushort a = GetPcWord();

            WriteMemWord(_ds, a, GetAX());

            Log.DoLog($"{prefixStr} MOV [${a:X4}],AX");
        }
        else if (opcode == 0xa8)
        {
            // TEST AL,..
            byte v = GetPcByte();

	    byte result = (byte)(_al & v);

            SetFlagO(false);
            SetFlagS((result & 0x80) != 0);
            SetFlagZ(result == 0);
            SetFlagA(false);

            Log.DoLog($"{prefixStr} TEST AL,${v:X2}");
        }
        else if (((opcode & 0b11111100) == 0b10001000 /* 0x88 */) || opcode == 0b10001110 /* 0x8e */|| opcode == 0x8c)
        {
            bool dir = (opcode & 2) == 2; // direction
            bool word = (opcode & 1) == 1; // b/w

            byte o1 = GetPcByte();
            int mode = o1 >> 6;
            int reg = (o1 >> 3) & 7;
            int rm = o1 & 7;

            bool sreg = opcode == 0x8e || opcode == 0x8c;

            if (sreg)
                word = true;

            // Log.DoLog($"{opcode:X}|{o1:X} mode {mode}, reg {reg}, rm {rm}, dir {dir}, word {word}, sreg {sreg}");

            if (dir)
            {
                // to 'REG' from 'rm'
                (ushort v, string fromName) = GetRegisterMem(rm, mode, word);

                string toName;

                if (sreg)
                    toName = PutSRegister(reg, v);
                else
                    toName = PutRegister(reg, word, v);

                Log.DoLog($"{prefixStr} MOV {toName},{fromName}");
            }
            else
            {
                // from 'REG' to 'rm'
                ushort v;
                string fromName;

                if (sreg)
                    (v, fromName) = GetSRegister(reg);
                else
                    (v, fromName) = GetRegister(reg, word);

                string toName = PutRegisterMem(rm, mode, word, v);

                Log.DoLog($"{prefixStr} MOV {toName},{fromName}");
            }
        }
        else if ((opcode & 0xf8) == 0xb8)
        {
            // MOV immed to reg
            bool word = (opcode & 8) == 8; // b/w
            int reg = opcode & 7;

            ushort val = GetPcByte();

            if (word)
                val |= (ushort)(GetPcByte() << 8);

            string toName = PutRegister(reg, word, val);

            Log.DoLog($"{prefixStr} MOV {toName},${val:X}");
        }
        else if (opcode == 0x9e)
        {
            // SAHF
            ushort keep = (ushort)(_flags & 0b1111111100101010);
            ushort add = (ushort)(_ah & 0b11010101);

            _flags = (ushort)(keep | add);

            Log.DoLog($"{prefixStr} SAHF (set to {GetFlagsAsString()})");
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _ah = (byte)_flags;

            Log.DoLog($"{prefixStr} LAHF");
        }
        else if (opcode is >= 0x40 and <= 0x4f)
        {
            // INC/DECw
            int reg = (opcode - 0x40) & 7;

            (ushort v, string name) = GetRegister(reg, true);

            bool isDec = opcode >= 0x48;

            if (isDec)
                v--;
            else
                v++;

            if (isDec)
                SetFlagO(v == 0x7fff);
            else
                SetFlagO(v == 0x8000);
            SetFlagS((v & 0x8000) == 0x8000);
            SetFlagZ(v == 0);
            SetFlagA((v & 15) == 0);
            SetFlagP((byte)v);

            PutRegister(reg, true, v);

            if (isDec)
                Log.DoLog($"{prefixStr} DEC {name}");
            else
                Log.DoLog($"{prefixStr} INC {name}");
        }
        else if (opcode == 0xaa)
        {
            // STOSB
            WriteMemByte(_es, _di, _al);

            if (GetFlagD())
                _di--;
            else
                _di++;

            Log.DoLog($"{prefixStr} STOSB");
        }
        else if (opcode == 0xab)
        {
            // STOSW
            WriteMemWord(_es, _di, GetAX());

            _di += (ushort)(GetFlagD() ? -2 : 2);

            Log.DoLog($"{prefixStr} STOSW");
        }
        else if (opcode == 0xc6 || opcode == 0xc7)
        {
            // MOV
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;

            int mreg = o1 & 7;

            if (mod == 0)
            {
                // TODO: PutDoubleRegisterMod00

                if (mreg == 5)
                {
                    if (word)
                    {
                        ushort v = GetPcWord();

                        WriteMemWord(_ds, _di, v);

                        Log.DoLog($"{prefixStr} MOV word [DI],${v:X4}");
                    }
                    else
                    {
                        byte v = GetPcByte();

                        WriteMemByte(_ds, _di, v);

                        Log.DoLog($"{prefixStr} MOV byte [DI],${v:X2}");
                    }
                }
                else if (mreg == 6)
                {
                    ushort a = GetPcWord();

                    if (word)
                    {
                        ushort v = GetPcWord();

                        WriteMemWord(_ds, a, v);

                        Log.DoLog($"{prefixStr} MOV word [${a:X4}],${v:X4}");
                    }
                    else
                    {
                        byte v = GetPcByte();

                        WriteMemByte(_ds, a, v);

                        Log.DoLog($"{prefixStr} MOV byte [${a:X4}],${v:X2}");
                    }
                }
                else
                {
                    Log.DoLog($"{prefixStr} MOV opcode {opcode:X2} o1 {o1:X2} not implemented");
                }
            }
            else
            {
                Log.DoLog($"{prefixStr} MOV opcode {opcode:X2} o1 {o1:X2} not implemented");
            }
        }
        else if (opcode == 0xca)
        {
            // RETF
            ushort nToRelease = GetPcWord();

            _ip = pop();
            _cs = pop();

            _ss += nToRelease;

            Log.DoLog($"{prefixStr} RETF ${nToRelease:X4}");
        }
        else if ((opcode & 0xf8) == 0xd0)
        {
            // RCR
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = o1 & 7;

            (ushort v1, string vName) = GetRegisterMem(reg1, mod, word);

            int countSpec = opcode & 3;
            int count = -1;

            string countName = "error";

            if (countSpec == 0 || countSpec == 1)
            {
                count = 1;
                countName = "1";
            }
            else if (countSpec == 2 || countSpec == 3)
            {
                count = _cl;
                countName = "CL";
            }

            bool oldSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

            int mode = (o1 >> 3) & 7;

            if (mode == 0)
            {
                // ROL
                for (int i = 0; i < count; i++)
                {
                    SetFlagC((v1 & 128) == 128);
                    v1 <<= 1;
                }

                Log.DoLog($"{prefixStr} ROL {vName},{countName}");
            }
            else if (mode == 3)
            {
                // RCR
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 1) == 1;
                    v1 >>= 1;

                    bool oldCarry = GetFlagC();

                    if (oldCarry)
                        v1 |= (ushort)(word ? 0x8000 : 0x80);

                    SetFlagC(newCarry);
                }

                Log.DoLog($"{prefixStr} RCR {vName},{countName}");
            }
            else if (mode == 4)
            {
                // SHL
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 0x80) == 0x80;

                    v1 <<= 1;

                    SetFlagC(newCarry);
                }

                Log.DoLog($"{prefixStr} SHL {vName},{countName}");
            }
            else if (mode == 5)
            {
                // SHR
                for (int i = 0; i < count; i++)
                {
                    bool newCarry = (v1 & 1) == 1;

                    v1 >>= 1;

                    SetFlagC(newCarry);
                }

                Log.DoLog($"{prefixStr} SHR {vName},{countName}");
            }
            else
            {
                Log.DoLog($"{prefixStr} RCR/SHR mode {mode} not implemented");
            }

            bool newSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

            SetFlagO(oldSign != newSign);

            if (!word)
                v1 &= 0xff;

            PutRegisterMem(reg1, mod, word, v1);
        }
        else if ((opcode & 0xf0) == 0b01110000)
        {
            // J..., 0x70
            byte to = GetPcByte();

            bool state = false;
            string name = String.Empty;

            if (opcode == 0x70)
            {
                state = GetFlagO();
                name = "JO";
            }
            else if (opcode == 0x71)
            {
                state = GetFlagO() == false;
                name = "JNO";
            }
            else if (opcode == 0x72)
            {
                state = GetFlagC();
                name = "JC";
            }
            else if (opcode == 0x73)
            {
                state = GetFlagC() == false;
                name = "JNC";
            }
            else if (opcode == 0x74)
            {
                state = GetFlagZ();
                name = "JE/JZ";
            }
            else if (opcode == 0x75)
            {
                state = GetFlagZ() == false;
                name = "JNE/JNZ";
            }
            else if (opcode == 0x76)
            {
                state = GetFlagC() || GetFlagZ();
                name = "JBE/JNA";
            }
            else if (opcode == 0x77)
            {
                state = GetFlagC() == false && GetFlagZ() == false;
                name = "JA/JNBE";
            }
            else if (opcode == 0x78)
            {
                state = GetFlagS();
                name = "JS";
            }
            else if (opcode == 0x79)
            {
                state = GetFlagS() == false;
                name = "JNS";
            }
            else if (opcode == 0x7a)
            {
                state = GetFlagP();
                name = "JNP/JPO";
            }
            else if (opcode == 0x7b)
            {
                state = GetFlagP() == false;
                name = "JNP/JPO";
            }
            else if (opcode == 0x7c)
            {
                state = GetFlagS() != GetFlagO();
                name = "JNGE";
            }
            else if (opcode == 0x7d)
            {
                state = GetFlagS() == GetFlagO();
                name = "JNL";
            }
            else if (opcode == 0x7e)
            {
                state = GetFlagZ() || GetFlagS() != GetFlagO();
                name = "JLE";
            }
            else if (opcode == 0x7f)
            {
                state = GetFlagZ() && GetFlagS() == GetFlagO();
                name = "JNLE";
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:x2} not implemented");
            }

            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            if (state)
                _ip = newAddresses;

            Log.DoLog($"{prefixStr} {name} {to} ({newAddresses:X4})");
        }
        else if (opcode == 0xe2)
        {
            // LOOP
            byte to = GetPcByte();

            (ushort cx, string dummy) = GetRegister(1, true);

            cx--;

            PutRegister(1, true, cx);

            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            if (cx > 0)
                _ip = newAddresses;

            Log.DoLog($"{prefixStr} LOOP {to} ({newAddresses:X4})");
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            _al = _io.In(@from);

            Log.DoLog($"{prefixStr} IN AL,${from:X2}");
        }
        else if (opcode == 0xec)
        {
            // IN AL,DX
            _al = _io.In(GetDX());

            Log.DoLog($"{prefixStr} IN AL,DX");
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();

            _io.Out(@to, _al);

            Log.DoLog($"{prefixStr} OUT ${to:X2},AL");
        }
        else if (opcode == 0xee)
        {
            // OUT
            _io.Out(GetDX(), _al);

            Log.DoLog($"{prefixStr} OUT DX,AL");
        }
        else if (opcode == 0xeb)
        {
            // JMP
            byte to = GetPcByte();

            _ip = (ushort)(_ip + (sbyte)to);

            Log.DoLog($"{prefixStr} JP ${_ip:X4}");
        }
        else if (opcode == 0xf3)
        {
            // REP

            // it looks like f3 can only used with a specific set of
            // instructions according to
            // https://www.felixcloutier.com/x86/rep:repe:repz:repne:repnz

            byte next_opcode = GetPcByte();

            if (next_opcode == 0xab)
            {
                Log.DoLog($"{prefixStr} REP STOSW");

                ushort ax = GetAX();
                ushort cx = GetCX();

                while(cx > 0)
                {
                    WriteMemWord(_es, _di, ax);

                    if (GetFlagD())
                        _di -= 2;
                    else
                        _di += 2;

                    cx--;
                }

                SetCX(0);
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} next_opcode {next_opcode:X2} not implemented");
            }
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _ip--;

            Log.DoLog($"{prefixStr} HLT");

            Console.WriteLine($"{address:X6} HLT");

            System.Environment.Exit(1);
        }
        else if (opcode == 0xf8)
        {
            // CLC
            SetFlagC(false);

            Log.DoLog($"{prefixStr} CLC");
        }
        else if (opcode == 0xf9)
        {
            // STC
            SetFlagC(true);

            Log.DoLog($"{prefixStr} STC");
        }
        else if (opcode == 0xfb)
        {
            // STI
            SetFlagBit(9); // IF

            Log.DoLog($"{prefixStr} STI");
        }
        else if (opcode == 0xfc)
        {
            // CLD
            SetFlagD(false);

            Log.DoLog($"{prefixStr} CLD");
        }
        else if (opcode == 0xfd)
        {
            // STD
            SetFlagD(true);

            Log.DoLog($"{prefixStr} STD");
        }
        else if (opcode == 0xfe || opcode == 0xff)
        {
            // DEC and others
            bool word = (opcode & 1) == 1;

            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            (ushort v, string name) = GetRegisterMem(reg, mod, word);

            int function = (o1 >> 3) & 7;

            if (function == 0)
            {
                v++;

                SetFlagO(word ? v == 0x8000 : v == 0x80);

                Log.DoLog($"{prefixStr} INC {name}");
            }
            else if (function == 1)
            {
                v--;

                SetFlagO(word ? v == 0x7fff : v == 0x7f);

                Log.DoLog($"{prefixStr} DEC {name}");
            }
            else
            {
                Log.DoLog($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            if (!word)
                v &= 0xff;

            SetFlagS((v & 0x8000) == 0x8000);
            SetFlagZ(v == 0);
            SetFlagA((v & 15) == 0);
            SetFlagP((byte)v);

            PutRegisterMem(reg, mod, word, v);
        }
        else
        {
            Log.DoLog($"{prefixStr} opcode {opcode:x} not implemented");
        }

        segment_override_set = false;
    }
}

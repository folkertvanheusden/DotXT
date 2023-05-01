namespace DotXT;

internal class Memory
{
    private readonly byte[] _m = new byte[1024 * 1024]; // 1MB of RAM

    public byte ReadByte(uint address)
    {
        return _m[address];
    }

    public void WriteByte(uint address, byte v)
    {
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

    public byte read_byte(uint address)
    {
        if (address is >= 0x000f8000 and <= 0x000fffff)
            return _bios.ReadByte(address - 0x000f8000);

        if (address is >= 0x000f0000 and <= 0x000f7fff)
            return _basic.ReadByte(address - 0x000f0000);

        return _m.ReadByte(address);
    }

    public void write_byte(uint address, byte v)
    {
        _m.WriteByte(address, v);
    }
}

internal class IO
{
    private byte _RAM_refresh_counter;

    private Dictionary <ushort, byte> values = new Dictionary <ushort, byte>();

    public byte In(ushort addr)
    {
        if (addr == 0x0041)
            return _RAM_refresh_counter++;

        Console.WriteLine($"IN: I/O port {addr:X4} not implemented");

        if (values.ContainsKey(addr))
            return values[addr];

        return 0;
    }

    public void Out(ushort addr, byte value)
    {
        // TODO

        Console.WriteLine($"OUT: I/O port {addr:X4} ({value:X2}) not implemented");

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

    private ushort _flags;

    private const uint MemMask = 0x00ffffff;

    private readonly Bus _b = new();

    private readonly IO _io = new();

    public P8086()
    {
        _cs = 0xf000;
        _ip = 0xfff0;
    }

    private byte GetPcByte()
    {
        uint address = (uint)(_cs * 16 + _ip++) & MemMask;

        byte val = _b.read_byte(address);

        // Console.WriteLine($"{address:X} {val:X}");

        return val;
    }

    private (ushort, string) GetRegister(int reg, bool w)
    {
        if (w)
        {
            if (reg == 0)
                return ((ushort)((_ah << 8) | _al), "AX");
            if (reg == 1)
                return ((ushort)((_ch << 8) | _cl), "CX");
            if (reg == 2)
                return ((ushort)((_dh << 8) | _dl), "DX");
            if (reg == 3)
                return ((ushort)((_bh << 8) | _bl), "BX");
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

        Console.WriteLine($"reg {reg} w {w} not supported for {nameof(GetRegister)}");

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

        Console.WriteLine($"reg {reg} not supported for {nameof(GetSRegister)}");

        return (0, "error");
    }

    private (ushort, string) GetDoubleRegister(int reg)
    {
        ushort a = 0;
        string name = "error";

        if (reg == 0)
        {
            a = (ushort)((_bh << 8) + _bl + _si);
            name = "[BX+SI]";
        }
        else if (reg == 1)
        {
            a = (ushort)((_bh << 8) + _bl + _di);
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
        //else if (reg == 6)  TODO
        else if (reg == 7)
        {
            a = (ushort)((_bh << 8) + _bl);
            name = "[BX]";
        }
        else
        {
            Console.WriteLine($"{nameof(GetDoubleRegister)} {reg} not implemented");
        }

        return (a, name);
    }

    private (ushort, string) GetRegisterMem(int reg, int mod, bool w)
    {
        if (mod == 0)
        {
            (ushort a, string name) = GetDoubleRegister(reg);

            ushort v = _b.read_byte(a);

            if (w)
                v |= (ushort)(_b.read_byte((ushort)(a + 1)) << 8);

            return (v, name);
        }

        if (mod == 3)
            return GetRegister(reg, w);

        Console.WriteLine($"reg {reg} mod {mod} w {w} not supported for {nameof(GetRegisterMem)}");

        return (0, "error");
    }

    private string PutRegister(int reg, bool w, ushort val)
    {
        if (reg == 0)
        {
            if (w)
            {
                _ah = (byte)(val >> 8);
                _al = (byte)val;

                return "AX";
            }

            _al = (byte)val;

            return "AL";
        }

        if (reg == 1)
        {
            if (w)
            {
                _ch = (byte)(val >> 8);
                _cl = (byte)val;

                return "CX";
            }

            _cl = (byte)val;

            return "CL";
        }

        if (reg == 2)
        {
            if (w)
            {
                _dh = (byte)(val >> 8);
                _dl = (byte)val;

                return "DX";
            }

            _dl = (byte)val;

            return "DL";
        }

        if (reg == 3)
        {
            if (w)
            {
                _bh = (byte)(val >> 8);
                _bl = (byte)val;

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

        Console.WriteLine($"reg {reg} w {w} not supported for {nameof(PutRegister)} ({val:X})");

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

        Console.WriteLine($"reg {reg} not supported for {nameof(PutSRegister)}");

        return "error";
    }

    private string put_register_mem(int reg, int mod, bool w, ushort val)
    {
        if (mod == 0)
        {
            (ushort a, string name) = GetDoubleRegister(reg);

            _b.write_byte(a, (byte)val);

            if (w)
                _b.write_byte((ushort)(a + 1), (byte)(val >> 8));

            return name;
        }

        if (mod == 3)
            return PutRegister(reg, w, val);

        Console.WriteLine($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(put_register_mem)}");

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

    public void Tick()
    {
        uint address = (uint)(_cs * 16 + _ip) & MemMask;
        byte opcode = GetPcByte();

        string flagStr = GetFlagsAsString();

        string prefixStr =
            $"{flagStr} {address:X4} {opcode:X2} AX:{_ah:X2}{_al:X2} BX:{_bh:X2}{_bl:X2} CX:{_ch:X2}{_cl:X2} DX:{_dh:X2}{_dl:X2} SP:{_sp:X4} BP:{_bp:X4} SI:{_si:X4} DI:{_di:X4}";

        if (opcode == 0xe9)
        {
            // JMP np
            byte o0 = GetPcByte();
            byte o1 = GetPcByte();

            short offset = (short)((o1 << 8) | o0);

            _ip = (ushort)(_ip + offset);

            Console.WriteLine($"{prefixStr} JMP {_ip:X}");
        }
        else if (opcode == 0x50)
        {
            // PUSH AX
            _b.write_byte((uint)(_ss * 16 + _sp++) & MemMask, _ah);
            _b.write_byte((uint)(_ss * 16 + _sp++) & MemMask, _al);

            Console.WriteLine($"{prefixStr} PUSH AX");
        }
        else if (opcode == 0x90)
        {
            // NOP
            Console.WriteLine($"{prefixStr} NOP");
        }
        else if (opcode == 0x80 || opcode == 0x81 || opcode == 0x83)
        {
            // CMP
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg = o1 & 7;

            int function = (o1 >> 3) & 7;

            if (function == 7)
            {
                // CMP
                ushort r1 = 0;
                string name1 = "error";

                ushort r2 = 0;

                bool word = false;

                if (opcode == 0x80)
                {
                    (r1, name1) = GetRegisterMem(reg, mod, false);

                    r2 = GetPcByte();
                }
                else if (opcode == 0x81)
                {
                    (r1, name1) = GetRegisterMem(reg, mod, true);

                    r2 = GetPcByte();
                    r2 |= (ushort)(GetPcByte() << 8);

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
                    Console.WriteLine($"{prefixStr} opcode {opcode:X2} not implemented");
                }

                int result = r1 - r2;

                SetFlagO(false); // TODO
                SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
                SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
                SetFlagA(((r1 & 0x10) ^ (r2 & 0x10) ^ (result & 0x10)) == 0x10);
                SetFlagP((byte)result);

                Console.WriteLine($"{prefixStr} CMP {name1},${r2:X2}");
            }
            else
            {
                    Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }
        }
        else if (opcode == 0xac)
        {
            // LODSB
            _al = _b.read_byte((uint)(_ds * 16 + _si++) & MemMask);

            if (GetFlagD())
                _si--;
            else
                _si++;

            Console.WriteLine($"{prefixStr} LODSB");
        }
        else if (opcode == 0xc3)
        {
            // RET
            byte low = _b.read_byte((uint)(_ss * 16 + _sp++) & MemMask);
            byte high = _b.read_byte((uint)(_ss * 16 + _sp++) & MemMask);

            _ip = (ushort)((high << 8) + low);

            Console.WriteLine($"{prefixStr} RET");
        }
        else if (opcode == 0x02 || opcode == 0x03 || opcode == 0x2a || opcode == 0x2b || opcode == 0x3b)
        {
            bool word = (opcode & 1) == 1;
            byte o1 = GetPcByte();

            int mod = o1 >> 6;
            int reg1 = (o1 >> 3) & 7;
            int reg2 = o1 & 7;

            (ushort r1, string name1) = GetRegisterMem(reg2, mod, word);
            (ushort r2, string name2) = GetRegister(reg1, word);

            int result = 0;
           
            if (opcode == 0x02 || opcode == 0x03)
            {
                result = r2 + r1;

                Console.WriteLine($"{prefixStr} ADD {name2},{name1}");
            }
            else
            {
                result = r2 - r1;

                Console.WriteLine($"{prefixStr} SUB {name2},{name1}");
            }

            // 0x3b is CMP
            if (opcode != 0x3b)
                PutRegister(reg1, word, (ushort)result);

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

            ushort result = 0;

            int function = opcode >> 4;

            if (function == 0)
                result = (ushort)(r2 | r1);
            else if (function == 2)
                result = (ushort)(r2 & r1);
            else if (function == 3) // TODO always true here?
                result = (ushort)(r2 ^ r1);
            else
                Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");

            // if (opcode == 0x0b || opcode == 0x33)
            //     Console.WriteLine($"r1 {r1:X} ({reg1} | {name1}), r2 {r2:X} ({reg2} | {name2}), result {result:X}");

            put_register_mem(reg1, mod, word, result);

            SetFlagO(false);
            SetFlagS((word ? result & 0x8000 : result & 0x80) != 0);
            SetFlagZ(word ? result == 0 : (result & 0xff) == 0);
            SetFlagA(false);

            SetFlagP((byte)result); // TODO verify

            Console.WriteLine($"{prefixStr} XOR {name1},{name2}");
        }
        else if ((opcode == 0x34 || opcode == 0x35) || (opcode == 0x24 || opcode == 0x25) ||
                 (opcode == 0x0c || opcode == 0x0d))
        {
            bool word = (opcode & 1) == 1;

            byte bLow = GetPcByte();
            byte bHigh = word ? GetPcByte() : (byte)0;

            int function = opcode >> 4;

            if (function == 0)
            {
                _al |= bLow;

                if (word)
                    _ah |= bHigh;
            }
            else if (function == 2)
            {
                _al &= bLow;

                if (word)
                    _ah &= bHigh;
            }
            else if (function == 3)
            {
                // TODO always true here
                _al ^= bLow;

                if (word)
                    _ah ^= bHigh;
            }
            else
            {
                Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            SetFlagO(false);
            SetFlagS((word ? _ah & 0x8000 : _al & 0x80) != 0);
            SetFlagZ(word ? _ah == 0 && _al == 0 : _al == 0);
            SetFlagA(false);

            SetFlagP(word ? _ah : _al);
        }
        else if (opcode == 0xea)
        {
            // JMP far ptr
            byte o0 = GetPcByte();
            byte o1 = GetPcByte();
            byte s0 = GetPcByte();
            byte s1 = GetPcByte();

            _cs = (ushort)((s1 << 8) | s0);
            _ip = (ushort)((o1 << 8) | o0);

            Console.WriteLine($"{prefixStr} JMP ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
        }
        else if (opcode == 0xfa)
        {
            // CLI
            ClearFlagBit(9); // IF

            Console.WriteLine($"{prefixStr} CLI");
        }
        else if ((opcode & 0xf8) == 0xb0)
        {
            // MOV reg,ib
            int reg = opcode & 0x07;

            ushort v = GetPcByte();

            string name = PutRegister(reg, false, v);

            Console.WriteLine($"{prefixStr} MOV {name},${v:X}");
        }
        else if (((opcode & 0b11111100) == 0b10001000) || opcode == 0b10001110 ||
                 ((opcode & 0b11111110) == 0b11000110) || ((opcode & 0b11111100) == 0b10100000) || opcode == 0x8c)
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

            // Console.WriteLine($"{opcode:X}|{o1:X} mode {mode}, reg {reg}, rm {rm}, dir {dir}, word {word}");

            if (dir)
            {
                // to 'REG' from 'rm'
                (ushort v, string fromName) = GetRegisterMem(rm, mode, word);

                string toName;

                if (sreg)
                    toName = PutSRegister(reg, v);
                else
                    toName = PutRegister(reg, word, v);

                Console.WriteLine($"{prefixStr} MOV {toName},{fromName}");
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

                string toName = put_register_mem(rm, mode, word, v);

                Console.WriteLine($"{prefixStr} MOV {toName},{fromName}");
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

            Console.WriteLine($"{prefixStr} MOV {toName},${val:X}");
        }
        else if (opcode == 0x9e)
        {
            // SAHF
            ushort keep = (ushort)(_flags & 0b1111111100101010);
            ushort add = (ushort)(_ah & 0b11010101);

            _flags = (ushort)(keep | add);

            Console.WriteLine($"{prefixStr} SAHF (set to {GetFlagsAsString()})");
        }
        else if (opcode == 0x9f)
        {
            // LAHF
            _ah = (byte)_flags;

            Console.WriteLine($"{prefixStr} LAHF");
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
                Console.WriteLine($"{prefixStr} DEC {name}");
            else
                Console.WriteLine($"{prefixStr} INC {name}");
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

            if (mode == 3)
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

                Console.WriteLine($"{prefixStr} RCR {vName},{countName}");
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

                Console.WriteLine($"{prefixStr} SHL {vName},{countName}");
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

                Console.WriteLine($"{prefixStr} SHR {vName},{countName}");
            }
            else
            {
                Console.WriteLine($"{prefixStr} RCR/SHR mode {mode} not implemented");
            }

            bool newSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

            SetFlagO(oldSign != newSign);

            if (!word)
                v1 &= 0xff;

            put_register_mem(reg1, mod, word, v1);
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
                Console.WriteLine($"{prefixStr} Opcode {opcode:x2} not implemented");
            }

            ushort newAddresses = (ushort)(_ip + (sbyte)to);

            if (state)
                _ip = newAddresses;

            Console.WriteLine($"{prefixStr} {name} {to} ({newAddresses:X4})");
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

            Console.WriteLine($"{prefixStr} LOOP {to} ({newAddresses:X4})");
        }
        else if (opcode == 0xe4)
        {
            // IN AL,ib
            byte @from = GetPcByte();

            _al = _io.In(@from);

            Console.WriteLine($"{prefixStr} IN AL,${from:X2}");
        }
        else if (opcode == 0xec)
        {
            // IN AL,DX
            _al = _io.In((ushort)((_dh << 8) | _dl));

            Console.WriteLine($"{prefixStr} IN AL,DX");
        }
        else if (opcode == 0xe6)
        {
            // OUT
            byte to = GetPcByte();

            _io.Out(@to, _al);

            Console.WriteLine($"{prefixStr} OUT ${to:X2},AL");
        }
        else if (opcode == 0xee)
        {
            // OUT
            _io.Out((ushort)((_dh << 8) | _dl), _al);

            Console.WriteLine($"{prefixStr} OUT ${_dh:X2}{_dl:X2},AL");
        }
        else if (opcode == 0xeb)
        {
            // JMP
            byte to = GetPcByte();

            _ip = (ushort)(_ip + (sbyte)to);

            Console.WriteLine($"{prefixStr} JP ${_ip:X4}");
        }
        else if (opcode == 0xf4)
        {
            // HLT
            _ip--;

            Console.WriteLine($"{prefixStr} HLT");
        }
        else if (opcode == 0xf8)
        {
            // CLC
            SetFlagC(false);

            Console.WriteLine($"{prefixStr} CLC");
        }
        else if (opcode == 0xf9)
        {
            // STC
            SetFlagC(true);

            Console.WriteLine($"{prefixStr} STC");
        }
        else if (opcode == 0xfc)
        {
            // CLD
            SetFlagD(false);

            Console.WriteLine($"{prefixStr} CLD");
        }
        else if (opcode == 0xfd)
        {
            // STD
            SetFlagD(true);

            Console.WriteLine($"{prefixStr} STD");
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

                Console.WriteLine($"{prefixStr} INC {name}");
            }
            else if (function == 1)
            {
                v--;

                SetFlagO(word ? v == 0x7fff : v == 0x7f);

                Console.WriteLine($"{prefixStr} DEC {name}");
            }
            else
            {
                Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
            }

            if (!word)
                v &= 0xff;

            SetFlagS((v & 0x8000) == 0x8000);
            SetFlagZ(v == 0);
            SetFlagA((v & 15) == 0);
            SetFlagP((byte)v);

            put_register_mem(reg, mod, word, v);
        }
        else
        {
            Console.WriteLine($"{prefixStr} Opcode {opcode:x} not implemented");
        }
    }
}

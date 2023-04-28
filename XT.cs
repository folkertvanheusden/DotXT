namespace DotXT
{
    internal class Memory
    {
        private readonly byte[] _m = new byte[1024 * 1024];  // 1MB of RAM

        public byte read_byte(uint address)
        {
            return _m[address];
        }

        public void write_byte(uint address, byte v)
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

        public byte read_byte(uint address)
        {
            return _contents[address];
        }
    }

    internal class Bus
    {
        private readonly Memory _m = new();

        private readonly Rom _bios  = new("roms/BIOS_5160_16AUG82_U18_5000026.BIN");
        private readonly Rom _basic = new("roms/BIOS_5160_08NOV82_U19_5000027_27256.BIN");

        public byte read_byte(uint address)
        {
            if (address is >= 0x000f8000 and <= 0x000fffff)
                return _bios.read_byte(address - 0x000f8000);

            if (address is >= 0x000f0000 and <= 0x000f7fff)
                return _basic.read_byte(address - 0x000f0000);

            return _m.read_byte(address);
        }

        public void write_byte(uint address, byte v)
        {
            _m.write_byte(address, v);
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

        public P8086()
        {
            _cs = 0xf000;
            _ip = 0xfff0;
        }

        public byte get_pc_byte()
        {
            uint address = (uint)(_cs * 16 + _ip++) & MemMask;

            byte val  = _b.read_byte(address);

            // Console.WriteLine($"{address:X} {val:X}");

            return val;
        }

        private (ushort, string) get_register(int reg, bool w)
        {
            if (w) {
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
            else {
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

            Console.WriteLine($"reg {reg} w {w} not supported for {nameof(get_register)}");

            return (0, "error");
        }

        private (ushort, string) get_sregister(int reg)
        {
            if (reg == 0b000)
                return (_es, "ES");
            if (reg == 0b001)
                return (_cs, "CS");
            if (reg == 0b010)
                return (_ss, "SS");
            if (reg == 0b011)
                return (_ds, "DS");

            Console.WriteLine($"reg {reg} not supported for {nameof(get_sregister)}");

            return (0, "error");
        }

        private (ushort, string) get_double_reg(int reg)
        {
            ushort a    = 0;
            string name = "error";

            if (reg == 0) {
                a = (ushort)((_bh << 8) + _bl + _si);
                name = "[BX+SI]";
            }
            else if (reg == 1) {
                a = (ushort)((_bh << 8) + _bl + _di);
                name = "[BX+DI]";
            }
            else if (reg == 2) {
                a = (ushort)(_bp + _si);
                name = "[BP+SI]";
            }
            else if (reg == 3) {
                a = (ushort)(_bp + _di);
                name = "[BP+DI]";
            }
            else if (reg == 4) {
                a = _si;
                name = "[SI]";
            }
            else if (reg == 5) {
                a = _di;
                name = "[DI]";
            }
            //else if (reg == 6)  TODO
            else if (reg == 7) {
                a = (ushort)((_bh << 8) + _bl);
                name = "[BX]";
            }
            else {
                Console.WriteLine($"get_double_reg {reg} not implemented");
            }

            return (a, name);
        }

        private (ushort, string) get_register_mem(int reg, int mod, bool w)
        {
            if (mod == 0) {
                (ushort a, string name) = get_double_reg(reg);

                ushort v = _b.read_byte(a);

                if (w)
                    v |= (ushort)(_b.read_byte((ushort)(a + 1)) << 8);

                return (v, name);
            }

            if (mod == 3)
                return get_register(reg, w);

            Console.WriteLine($"reg {reg} mod {mod} w {w} not supported for {nameof(get_register_mem)}");

            return (0, "error");
        }

        private string put_register(int reg, bool w, ushort val)
        {
            if (reg == 0) {
                if (w) {
                    _ah = (byte)(val >> 8);
                    _al = (byte)val;

                    return "AX";
                }

                _al = (byte)val;

                return "AL";
            }

            if (reg == 1) {
                if (w) {
                    _ch = (byte)(val >> 8);
                    _cl = (byte)val;

                    return "CX";
                }

                _cl = (byte)val;

                return "CL";
            }

            if (reg == 2) {
                if (w) {
                    _dh = (byte)(val >> 8);
                    _dl = (byte)val;

                    return "DX";
                }

                _dl = (byte)val;

                return "DL";
            }

            if (reg == 3) {
                if (w) {
                    _bh = (byte)(val >> 8);
                    _bl = (byte)val;

                    return "BX";
                }

                _bl = (byte)val;

                return "BL";
            }

            if (reg == 4) {
                if (w) {
                    _sp = val;

                    return "SP";
                }

                _ah = (byte)val;

                return "AH";
            }

            if (reg == 5) {
                if (w) {
                    _bp = val;

                    return "BP";
                }

                _ch = (byte)val;

                return "CH";
            }

            if (reg == 6) {
                if (w) {
                    _si = val;

                    return "SI";
                }

                _dh = (byte)val;

                return "DH";
            }

            if (reg == 7) {
                if (w) {
                    _di = val;

                    return "DI";
                }

                _bh = (byte)val;

                return "BH";
            }

            Console.WriteLine($"reg {reg} w {w} not supported for {nameof(put_register)} ({val:X})");

            return "error";
        }

        private string put_sregister(int reg, ushort v)
        {
            if (reg == 0b000) {
                _es = v;
                return "ES";
            }
            if (reg == 0b001) {
                _cs = v;
                return "CS";
            }
            if (reg == 0b010) {
                _ss = v;
                return "SS";
            }
            if (reg == 0b011) {
                _ds = v;
                return "DS";
            }

            Console.WriteLine($"reg {reg} not supported for {nameof(put_sregister)}");

            return "error";
        }

        private string put_register_mem(int reg, int mod, bool w, ushort val)
        {
            if (mod == 0) {
                (ushort a, string name) = get_double_reg(reg);

                _b.write_byte(a, (byte)val);

                if (w)
                    _b.write_byte((ushort)(a + 1), (byte)(val >> 8));

                return name;
            }

            if (mod == 3)
                return put_register(reg, w, val);

            Console.WriteLine($"reg {reg} mod {mod} w {w} value {val} not supported for {nameof(put_register_mem)}");

            return "error";
        }

        private void clear_flag_bit(int bit)
        {
            _flags &= (ushort)(ushort.MaxValue ^ (1 << bit));
        }

        private void set_flag_bit(int bit)
        {
            _flags |= (ushort)(1 << bit);
        }

        private void set_flag(int bit, bool state)
        {
            if (state)
                set_flag_bit(bit);
            else
                clear_flag_bit(bit);
        }

        private bool get_flag(int bit)
        {
            return (_flags & (1 << bit)) != 0;
        }

        private void set_flag_c(bool state)
        {
            set_flag(0, state);
        }

        private bool get_flag_c()
        {
            return get_flag(0);
        }

        private void set_flag_p(byte v)
        {
            int count = 0;

            while(v != 0) {
                count++;

                v &= (byte)(v - 1);
            }

            set_flag(2, (count & 1) == 0);
        }

        private bool get_flag_p()
        {
            return get_flag(2);
        }

        private void set_flag_a(bool state)
        {
            set_flag(4, state);
        }

        private void set_flag_z(bool state)
        {
            set_flag(6, state);
        }

        private bool get_flag_z()
        {
            return get_flag(6);
        }

        private void set_flag_s(bool state)
        {
            set_flag(7, state);
        }

        private bool get_flag_s()
        {
            return get_flag(7);
        }

        private void set_flag_d(bool state)
        {
            set_flag(10, state);
        }

        private bool get_flag_d()
        {
            return get_flag(10);
        }

        private void set_flag_o(bool state)
        {
            set_flag(11, state);
        }

        private bool get_flag_o()
        {
            return get_flag(11);
        }

        private string get_flags_as_str()
        {
            string @out = String.Empty;

            @out += get_flag_o() ? "o" : "-";
            @out += get_flag_d() ? "d" : "-";
            @out += get_flag_s() ? "s" : "-";
            @out += get_flag_z() ? "z" : "-";
            @out += get_flag_c() ? "c" : "-";

            return @out;
        }

        public void Tick()
        {
            uint address   = (uint)(_cs * 16 + _ip) & MemMask;
            byte opcode = get_pc_byte();

            string flagStr = get_flags_as_str();

            string prefixStr = $"{flagStr} {address:X4} {opcode:X2} AX:{_ah:X2}{_al:X2} BX:{_bh:X2}{_bl:X2} CX:{_ch:X2}{_cl:X2} DX:{_dh:X2}{_dl:X2} SP:{_sp:X4} BP:{_bp:X4} SI:{_si:X4} DI:{_di:X4}";

            if (opcode == 0xe9) {  // JMP np
                byte o0 = get_pc_byte();
                byte o1 = get_pc_byte();

                short offset = (short)((o1 << 8) | o0);

                _ip = (ushort)(_ip + offset);

                Console.WriteLine($"{prefixStr} JMP {_ip:X}");
            }
            else if (opcode == 0xc3) {  // RET
                byte low  = _b.read_byte((uint)(_ss * 16 + _sp++) & MemMask);
                byte high = _b.read_byte((uint)(_ss * 16 + _sp++) & MemMask);

                _ip = (ushort)((high << 8) + low);

                Console.WriteLine($"{prefixStr} RET");
            }
            else if (opcode == 0x02 || opcode == 0x03) {
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = (o1 >> 3) & 7;
                int  reg2 = o1 & 7;

                (ushort r1, string name1) = get_register_mem(reg2, mod, word);
                (ushort r2, string name2) = get_register(reg1, word);

                int result = r2 - r1;

                put_register(reg1, word, (ushort)result);

                set_flag_o(false);  // TODO
                set_flag_s((word ? result & 0x8000 : result & 0x80) != 0);
                set_flag_z(word ? result == 0 : (result & 0xff) == 0);
                set_flag_a(((r1 & 0x10) ^ (r2 & 0x10) ^ (result & 0x10)) == 0x10);
                set_flag_p((byte)result);

                Console.WriteLine($"{prefixStr} ADD {name2},{name1}");
            }
            else if (opcode is >= 0x30 and <= 0x33 || opcode is >= 0x20 and <= 0x23 || opcode is >= 0x08 and <= 0x0b) {
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = (o1 >> 3) & 7;
                int  reg2 = o1 & 7;

                (ushort r1, string name1) = get_register_mem(reg1, mod, word);
                (ushort r2, string name2) = get_register(reg2, word);

                ushort result = 0;

                int function = opcode >> 4;

                if (function == 0)
                    result = (ushort)(r2 | r1);
                else if (function == 2)
                    result = (ushort)(r2 & r1);
                else if (function == 3)
                    result = (ushort)(r2 ^ r1);
                else
                    Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");

                // if (opcode == 0x0b || opcode == 0x33)
                //     Console.WriteLine($"r1 {r1:X} ({reg1} | {name1}), r2 {r2:X} ({reg2} | {name2}), result {result:X}");

                put_register_mem(reg1, mod, word, result);

                set_flag_o(false);
                set_flag_s((word ? result & 0x8000 : result & 0x80) != 0);
                set_flag_z(word ? result == 0 : (result & 0xff) == 0);
                set_flag_a(false);

                set_flag_p((byte)result);  // TODO verify

                Console.WriteLine($"{prefixStr} XOR {name1},{name2}");
            }
            else if ((opcode == 0x34 || opcode == 0x35) || (opcode == 0x24 || opcode == 0x25) || (opcode == 0x0c || opcode == 0x0d)) {
                bool word = (opcode & 1) == 1;

                byte bLow  = get_pc_byte();
                byte bHigh = word ? get_pc_byte() : (byte)0;

                int function = opcode >> 4;

                if (function == 0) {
                    _al |= bLow;

                    if (word)
                        _ah |= bHigh;
                }
                else if (function == 2) {
                    _al &= bLow;

                    if (word)
                        _ah &= bHigh;
                }
                else if (function == 3) {
                    _al ^= bLow;

                    if (word)
                        _ah ^= bHigh;
                }
                else {
                    Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
                }

                set_flag_o(false);
                set_flag_s((word ? _ah & 0x8000 : _al & 0x80) != 0);
                set_flag_z(word ? _ah == 0 && _al == 0 : _al == 0);
                set_flag_a(false);

                if (word)
                    set_flag_p(_ah);  // TODO verify
                else
                    set_flag_p(_al);
            }
            else if (opcode == 0xea) {  // JMP far ptr
                byte o0 = get_pc_byte();
                byte o1 = get_pc_byte();
                byte s0 = get_pc_byte();
                byte s1 = get_pc_byte();

                _cs = (ushort)((s1 << 8) | s0);
                _ip = (ushort)((o1 << 8) | o0);

                Console.WriteLine($"{prefixStr} JMP ${_cs:X} ${_ip:X}: ${_cs * 16 + _ip:X}");
            }
            else if (opcode == 0xfa) {  // CLI
                clear_flag_bit(9);  // IF

                Console.WriteLine($"{prefixStr} CLI");
            }
            else if ((opcode & 0xf8) == 0xb0) {  // MOV reg,ib
                int  reg  = opcode & 0x07;

                ushort v  = get_pc_byte();

                string name = put_register(reg, false, v);

                Console.WriteLine($"{prefixStr} MOV {name},${v:X}");
            }
            else if (((opcode & 0b11111100) == 0b10001000) || opcode == 0b10001110 || ((opcode & 0b11111110) == 0b11000110) || ((opcode & 0b11111100) == 0b10100000) || opcode == 0x8c) {
                bool dir  = (opcode & 2) == 2;  // direction
                bool word = (opcode & 1) == 1;  // b/w

                byte o1   = get_pc_byte();
                int  mode = o1 >> 6;
                int  reg  = (o1 >> 3) & 7;
                int  rm   = o1 & 7;

                bool sreg = opcode == 0x8e || opcode == 0x8c;

                if (sreg)
                    word = true;

                // Console.WriteLine($"{opcode:X}|{o1:X} mode {mode}, reg {reg}, rm {rm}, dir {dir}, word {word}");

                if (dir) {  // to 'REG' from 'rm'
                    (ushort v, string fromName) = get_register_mem(rm, mode, word);

                    string toName = "error";

                    if (sreg)
                        toName = put_sregister(reg, v);
                    else
                        toName = put_register(reg, word, v);

                    Console.WriteLine($"{prefixStr} MOV {toName},{fromName}");
                }
                else {  // from 'REG' to 'rm'
                    ushort v = 0;
                    string fromName = "error";

                    if (sreg)
                        (v, fromName) = get_sregister(reg);
                    else
                        (v, fromName) = get_register(reg, word);

                    string toName = put_register_mem(rm, mode, word, v);

                    Console.WriteLine($"{prefixStr} MOV {toName},{fromName}");
                }
            }
            else if ((opcode & 0xf8) == 0xb8) {  // MOV immed to reg
                bool word = (opcode & 8) == 8;  // b/w
                int  reg  = opcode & 7;

                ushort val = get_pc_byte();

                if (word)
                    val |= (ushort)(get_pc_byte() << 8);

                string toName = put_register(reg, word, val);

                Console.WriteLine($"{prefixStr} MOV {toName},${val:X}");
            }
            else if (opcode == 0x9e) {  // SAHF
                ushort keep = (ushort)(_flags & 0b1111111100101010);
                ushort add = (ushort)(_ah & 0b11010101);

                _flags = (ushort)(keep | add);

                Console.WriteLine($"{prefixStr} SAHF (set to {get_flags_as_str()})");
            }
            else if (opcode == 0x9f) {  // LAHF
                _ah = (byte)_flags;

                Console.WriteLine($"{prefixStr} LAHF");
            }
            else if (opcode is >= 0x40 and <= 0x4f) {  // INC/DECw
                int reg = (opcode - 0x40) & 7;

                (ushort v, string name) = get_register(reg, true);

                bool isDec = opcode >= 0x48;

                if (isDec)
                    v--;
                else
                    v++;

                if (isDec)
                    set_flag_o(v == 0x7fff);
                else
                    set_flag_o(v == 0x8000);
                set_flag_s((v & 0x8000) == 0x8000);
                set_flag_z(v == 0);
                set_flag_a((v & 15) == 0);
                set_flag_p((byte)v);

                put_register(reg, true, v);

                if (isDec)
                    Console.WriteLine($"{prefixStr} DEC {name}");
                else
                    Console.WriteLine($"{prefixStr} INC {name}");
            }
            else if ((opcode & 0xf8) == 0xd0) {  // RCR
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = o1 & 7;

                (ushort v1, string vName) = get_register_mem(reg1, mod, word);

                int  countSpec = opcode & 3;
                int  count = -1;

                string countName = "error";

                if (countSpec == 0 || countSpec == 1) {
                    count = 1;
                    countName = "1";
                }
                else if (countSpec == 2 || countSpec == 3) {
                    count = _cl;
                    countName = "CL";
                }

                bool oldSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

                int mode = (o1 >> 3) & 7;

                if (mode == 3) {  // RCR
                    for(int i=0; i<count; i++) {
                        bool newCarry = (v1 & 1) == 1;
                        v1 >>= 1;

                        bool oldCarry = get_flag_c();

                        if (oldCarry)
                            v1 |= (ushort)(word ? 0x8000 : 0x80);

                        set_flag_c(newCarry);
                    }

                    Console.WriteLine($"{prefixStr} RCR {vName},{countName}");
                }
                else if (mode == 4) {  // SHL
                    for(int i=0; i<count; i++) {
                        bool newCarry = (v1 & 0x80) == 0x80;

                        v1 <<= 1;

                        set_flag_c(newCarry);
                    }

                    Console.WriteLine($"{prefixStr} SHL {vName},{countName}");
                }
                else if (mode == 5) {  // SHR
                    for(int i=0; i<count; i++) {
                        bool newCarry = (v1 & 1) == 1;

                        v1 >>= 1;

                        set_flag_c(newCarry);
                    }

                    Console.WriteLine($"{prefixStr} SHR {vName},{countName}");
                }
                else {
                    Console.WriteLine($"{prefixStr} RCR/SHR mode {mode} not implemented");
                }

                bool newSign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

                set_flag_o(oldSign != newSign);

                if (!word)
                    v1 &= 0xff;

                put_register_mem(reg1, mod, word, v1);
            }
            else if ((opcode & 0xf0) == 0b01110000) {  // J..., 0x70
                byte   to    = get_pc_byte();

                bool   state = false;
                string name  = String.Empty;

                if (opcode == 0x70) {
                    state = get_flag_o();
                    name  = "JO";
                }
                else if (opcode == 0x71) {
                    state = get_flag_o() == false;
                    name  = "JNO";
                }
                else if (opcode == 0x72) {
                    state = get_flag_c();
                    name  = "JC";
                }
                else if (opcode == 0x73) {
                    state = get_flag_c() == false;
                    name  = "JNC";
                }
                else if (opcode == 0x74) {
                    state = get_flag_z();
                    name  = "JE/JZ";
                }
                else if (opcode == 0x75) {
                    state = get_flag_z() == false;
                    name  = "JNE/JNZ";
                }
                else if (opcode == 0x76) {
                    state = get_flag_c() || get_flag_z();
                    name  = "JBE/JNA";
                }
                else if (opcode == 0x77) {
                    state = get_flag_c() == false && get_flag_z() == false;
                    name  = "JA/JNBE";
                }
                else if (opcode == 0x78) {
                    state = get_flag_s();
                    name  = "JS";
                }
                else if (opcode == 0x79) {
                    state = get_flag_s() == false;
                    name  = "JNS";
                }
                else if (opcode == 0x7a) {
                    state = get_flag_p();
                    name  = "JNP/JPO";
                }
                else if (opcode == 0x7b) {
                    state = get_flag_p() == false;
                    name  = "JNP/JPO";
                }
                else if (opcode == 0x7c) {
                    state = get_flag_s() != get_flag_o();
                    name  = "JNGE";
                }
                else if (opcode == 0x7d) {
                    state = get_flag_s() == get_flag_o();
                    name  = "JNL";
                }
                else if (opcode == 0x7e) {
                    state = get_flag_z() || get_flag_s() != get_flag_o();
                    name  = "JLE";
                }
                else if (opcode == 0x7f) {
                    state = get_flag_z() && get_flag_s() == get_flag_o();
                    name  = "JNLE";
                }
                else {
                    Console.WriteLine($"{prefixStr} Opcode {opcode:x2} not implemented");
                }

                ushort newaddressess = (ushort)(_ip + (sbyte)to);

                if (state)
                    _ip = newaddressess;

                Console.WriteLine($"{prefixStr} {name} {to} ({newaddressess:X4})");
            }
            else if (opcode == 0xe2) {  // LOOP
                byte   to = get_pc_byte();

                (ushort cx, string dummy) = get_register(1, true);

                cx--;

                put_register(1, true, cx);

                ushort newaddressess = (ushort)(_ip + (sbyte)to);

                if (cx > 0)
                    _ip = newaddressess;

                Console.WriteLine($"{prefixStr} LOOP {to} ({newaddressess:X4})");
            }
            else if (opcode == 0xe6) {  // OUT
                byte to = get_pc_byte();

                // TODO

                Console.WriteLine($"{prefixStr} OUT ${to:X2},AL");
            }
            else if (opcode == 0xee) {  // OUT
                // TODO

                Console.WriteLine($"{prefixStr} OUT ${_dh:X2}{_dl:X2},AL");
            }
            else if (opcode == 0xeb) {  // JMP
                byte to = get_pc_byte();

                _ip = (ushort)(_ip + (sbyte)to);

                Console.WriteLine($"{prefixStr} JP ${_ip:X4}");
            }
            else if (opcode == 0xf4) {  // HLT
                _ip--;

                Console.WriteLine($"{prefixStr} HLT");
            }
            else if (opcode == 0xf8) {  // CLC
                set_flag_c(false);

                Console.WriteLine($"{prefixStr} CLC");
            }
            else if (opcode == 0xf9) {  // STC
                set_flag_c(true);

                Console.WriteLine($"{prefixStr} STC");
            }
            else if (opcode == 0xfc) {  // CLD
                set_flag_d(false);

                Console.WriteLine($"{prefixStr} CLD");
            }
            else if (opcode == 0xfe || opcode == 0xff) {  // DEC and others
                bool word = (opcode & 1) == 1;

                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg  = o1 & 7;

                (ushort v, string name) = get_register_mem(reg, mod, word);

                int function = (o1 >> 3) & 7;

                if (function == 0) {
                    v++;

                    set_flag_o(v == 0x8000);

                    Console.WriteLine($"{prefixStr} INC {name}");
                }
                else if (function == 1) {
                    v--;

                    set_flag_o(v == 0x7fff);

                    Console.WriteLine($"{prefixStr} DEC {name}");
                }
                else {
                    Console.WriteLine($"{prefixStr} opcode {opcode:X2} function {function} not implemented");
                }

                set_flag_s((v & 0x8000) == 0x8000);
                set_flag_z(v == 0);
                set_flag_a((v & 15) == 0);
                set_flag_p((byte)v);

                put_register_mem(reg, mod, word, v);
            }
            else {
                Console.WriteLine($"{prefixStr} Opcode {opcode:x} not implemented");
            }
        }
    }
}

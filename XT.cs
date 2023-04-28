namespace XT
{
    class memory
    {
        byte[] m = new byte[1024 * 1024];  // 1MB of RAM

        public memory()
        {
        }

        public byte read_byte(uint addr)
        {
            return m[addr];
        }

        public void write_byte(uint addr, byte v)
        {
            m[addr] = v;
        }
    }

    class rom
    {
        byte[] contents;

        public rom(string filename)
        {
            contents = File.ReadAllBytes(filename);
        }

        public byte read_byte(uint addr)
        {
            return contents[addr];
        }
    }

    class bus
    {
        memory m = new memory();

        rom bios  = new rom("roms/BIOS_5160_09MAY86_U19_62X0819_68X4370_27256_F000.BIN");
        rom basic = new rom("roms/BIOS_5160_09MAY86_U18_59X7268_62X0890_27256_F800.BIN");

        public bus()
        {
        }

        public byte read_byte(uint addr)
        {
            if (addr >= 0x000f0000 && addr <= 0x000f7fff)
                return bios.read_byte(addr - 0x000f0000);

            if (addr >= 0x000f8000 && addr <= 0x000fffff)
                return basic.read_byte(addr - 0x000f8000);

            return m.read_byte(addr);
        }

        public void write_byte(uint addr, byte v)
        {
            m.write_byte(addr, v);
        }
    }

    class p8086
    {
        byte ah = 0, al = 0;
        byte bh = 0, bl = 0;
        byte ch = 0, cl = 0;
        byte dh = 0, dl = 0;

        ushort si = 0;
        ushort di = 0;
        ushort bp = 0;
        ushort sp = 0;

        ushort ip = 0;

        ushort cs = 0;
        ushort ds = 0;
        ushort es = 0;
        ushort ss = 0;

        ushort flags = 0;

        const uint mem_mask = (uint)0x00ffffff;

        bus b = new bus();

        public p8086()
        {
            cs = 0xf000;
            ip = 0xfff0;
        }

        public byte get_pc_byte()
        {
            uint addr = (uint)(cs * 16 + ip++) & mem_mask;

            byte val  = b.read_byte(addr);

            // Console.WriteLine($"{addr:X} {val:X}");

            return val;
        }
  
        (ushort, string) get_register(int reg, bool w)
        {
            if (w) {
                if (reg == 0)
                    return ((ushort)((ah << 8) | al), "AX");
                if (reg == 1)
                    return ((ushort)((ch << 8) | cl), "CX");
                if (reg == 2)
                    return ((ushort)((dh << 8) | dl), "DX");
                if (reg == 3)
                    return ((ushort)((bh << 8) | bl), "BX");
                if (reg == 4)
                    return (sp, "SP");
                if (reg == 5)
                    return (bp, "BP");
                if (reg == 6)
                    return (si, "SI");
                if (reg == 7)
                    return (di, "DI");
            }
            else {
                if (reg == 0)
                    return (al, "AL");
                if (reg == 1)
                    return (cl, "CL");
                if (reg == 2)
                    return (dl, "DL");
                if (reg == 3)
                    return (bl, "BL");
                if (reg == 4)
                    return (ah, "AH");
                if (reg == 5)
                    return (ch, "CH");
                if (reg == 6)
                    return (dh, "DH");
                if (reg == 7)
                    return (bh, "BH");
            }

            Console.WriteLine($"reg {reg} w {w} not supported for get_register");

            return (0, "error");
        }
  
        (ushort, string) get_sregister(int reg)
        {
            if (reg == 0b000)
                return (es, "ES");
            if (reg == 0b001)
                return (cs, "CS");
            if (reg == 0b010)
                return (ss, "SS");
            if (reg == 0b011)
                return (ds, "DS");

            Console.WriteLine($"reg {reg} not supported for get_sregister");

            return (0, "error");
        }

        (ushort, string) get_double_reg(int reg)
        {
            ushort a    = 0;
            string name = "error";

            if (reg == 0) {
                a = (ushort)((bh << 8) + bl + si);
                name = "[BX+SI]";
            }
            else if (reg == 1) {
                a = (ushort)((bh << 8) + bl + di);
                name = "[BX+DI]";
            }
            else if (reg == 2) {
                a = (ushort)(bp + si);
                name = "[BP+SI]";
            }
            else if (reg == 3) {
                a = (ushort)(bp + di);
                name = "[BP+DI]";
            }
            else if (reg == 4) {
                a = si;
                name = "[SI]";
            }
            else if (reg == 5) {
                a = di;
                name = "[DI]";
            }
            //else if (reg == 6)  TODO
            else if (reg == 7) {
                a = (ushort)((bh << 8) + bl);
                name = "[BX]";
            }
            else {
                Console.WriteLine($"get_double_reg {reg} not implemented");
            }

            return (a, name);
        }

        (ushort, string) get_register_mem(int reg, int mod, bool w)
        {
            if (mod == 0) {
                (ushort a, string name) = get_double_reg(reg);

                ushort v = b.read_byte(a);

                if (w)
                    v |= (ushort)(b.read_byte((ushort)(a + 1)) << 8);

                return (v, name);
            }

            if (mod == 3)
                return get_register(reg, w);

            Console.WriteLine($"reg {reg} mod {mod} w {w} not supported for get");

            return (0, "error");
        }

        string put_register(int reg, bool w, ushort val)
        {
            if (reg == 0) {
                if (w) {
                    ah = (byte)(val >> 8);
                    al = (byte)val;

                    return "AX";
                }

                al = (byte)val;

                return "AL";
            }
            else if (reg == 1) {
                if (w) {
                    ch = (byte)(val >> 8);
                    cl = (byte)val;

                    return "CX";
                }

                cl = (byte)val;

                return "CL";
            }
            else if (reg == 2) {
                if (w) {
                    dh = (byte)(val >> 8);
                    dl = (byte)val;

                    return "DX";
                }

                dl = (byte)val;

                return "DL";
            }
            else if (reg == 3) {
                if (w) {
                    bh = (byte)(val >> 8);
                    bl = (byte)val;

                    return "BX";
                }

                bl = (byte)val;

                return "BL";
            }
            else if (reg == 4) {
                if (w) {
                    sp = val;

                    return "SP";
                }

                ah = (byte)val;

                return "AH";
            }
            else if (reg == 5) {
                if (w) {
                    bp = val;

                    return "BP";
                }

                ch = (byte)val;

                return "CH";
            }
            else if (reg == 6) {
                if (w) {
                    si = val;

                    return "SI";
                }

                dh = (byte)val;

                return "DH";
            }
            else if (reg == 7) {
                if (w) {
                    di = val;

                    return "DI";
                }

                bh = (byte)val;

                return "BH";
            }

            Console.WriteLine($"reg {reg} w {w} not supported for put_register ({val:X})");

            return "error";
        }
  
        string put_sregister(int reg, ushort v)
        {
            if (reg == 0b000) {
                es = v;
                return "ES";
            }
            if (reg == 0b001) {
                cs = v;
                return "CS";
            }
            if (reg == 0b010) {
                ss = v;
                return "SS";
            }
            if (reg == 0b011) {
                ds = v;
                return "DS";
            }

            Console.WriteLine($"reg {reg} not supported for get_sregister");

            return "error";
        }

        string put_register_mem(int reg, int mod, bool w, ushort val)
        {
            if (mod == 0) {
                (ushort a, string name) = get_double_reg(reg);

                b.write_byte(a, (byte)val);

                if (w)
                    b.write_byte((ushort)(a + 1), (byte)(val >> 8));

                return name;
            }

            if (mod == 3)
                return put_register(reg, w, val);

            Console.WriteLine($"reg {reg} mod {mod} w {w} value {val} not supported for put");

            return "error";
        }

        void clear_flag_bit(int bit)
        {
            flags &= (ushort)(ushort.MaxValue ^ (1 << bit));
        }

        void set_flag_bit(int bit)
        {
            flags |= (ushort)(1 << bit);
        }

        void set_flag(int bit, bool state)
        {
            if (state)
                set_flag_bit(bit);
            else
                clear_flag_bit(bit);
        }

        bool get_flag(int bit)
        {
            return (flags & (1 << bit)) != 0;
        }

        void set_flag_c(bool state)
        {
            set_flag(0, state);
        }

        bool get_flag_c()
        {
            return get_flag(0);
        }

        void set_flag_p(byte v)
        {
            int count = 0;

            while(v != 0) {
                count++;

                v &= (byte)(v - 1);
            }

            set_flag(2, (count & 1) == 0);
        }

        bool get_flag_p()
        {
            return get_flag(2);
        }

        void set_flag_a(bool state)
        {
            set_flag(4, state);
        }

        void set_flag_z(bool state)
        {
            set_flag(6, state);
        }

        bool get_flag_z()
        {
            return get_flag(6);
        }

        void set_flag_s(bool state)
        {
            set_flag(7, state);
        }

        bool get_flag_s()
        {
            return get_flag(7);
        }

        void set_flag_d(bool state)
        {
            set_flag(10, state);
        }

        bool get_flag_d()
        {
            return get_flag(10);
        }

        void set_flag_o(bool state)
        {
            set_flag(11, state);
        }

        bool get_flag_o()
        {
            return get_flag(11);
        }

        string get_flags_as_str()
        {
            string out_ = System.String.Empty;

            out_ += get_flag_o() ? "o" : "-";
            out_ += get_flag_d() ? "d" : "-";
            out_ += get_flag_s() ? "s" : "-";
            out_ += get_flag_z() ? "z" : "-";
            out_ += get_flag_c() ? "c" : "-";

            return out_;
        }

        public void tick()
        {
            uint addr   = (uint)(cs * 16 + ip) & mem_mask;
            byte opcode = get_pc_byte();

            string flag_str = get_flags_as_str();

            string prefix_str = $"{flag_str} {addr:X4} {opcode:X2} AX:{ah:X2}{al:X2} BX:{bh:X2}{bl:X2} CX:{ch:X2}{cl:X2} DX:{dh:X2}{dl:X2} SP:{sp:X4} BP:{bp:X4} SI:{si:X4} DI:{di:X4}";

            if (opcode == 0xe9) {  // JMP np
                byte o0 = get_pc_byte();
                byte o1 = get_pc_byte();

                short offset = (short)((o1 << 8) | o0);

                ip = (ushort)(ip + offset);

                Console.WriteLine($"{prefix_str} JMP {ip:X}");
            }
            else if (opcode == 0xc3) {  // RET
                byte low  = b.read_byte(sp++);
                byte high = b.read_byte(sp++);

                ip = (ushort)((high << 8) + low);

                Console.WriteLine($"{prefix_str} RET");
            }
            else if (opcode == 0x02 || opcode == 0x03) {
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = (o1 >> 3) & 7;
                int  reg2 = o1 & 7;

                (ushort r1, string name1) = get_register_mem(reg1, mod, word);
                (ushort r2, string name2) = get_register(reg2, word);

                int result = r2 - r1;

                put_register(reg2, word, (ushort)result);

                set_flag_o(false);  // TODO
                set_flag_s((word ? result & 0x8000 : result & 0x80) != 0);
                set_flag_z(word ? result == 0 : (result & 0xff) == 0);
                set_flag_a(((r1 & 0x10) ^ (r2 & 0x10) ^ (result & 0x10)) == 0x10);
                set_flag_p((byte)result);

                Console.WriteLine($"{prefix_str} ADD {name2},{name1}");
            }
            else if ((opcode >= 0x30 && opcode <= 0x33) || (opcode >= 0x20 && opcode <= 0x23) || (opcode >= 0x08 && opcode <= 0x0b)) {
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
                    Console.WriteLine($"{prefix_str} opcode {opcode:X2} function {function} not implemented");

                // if (opcode == 0x0b || opcode == 0x33)
                //     Console.WriteLine($"r1 {r1:X} ({reg1} | {name1}), r2 {r2:X} ({reg2} | {name2}), result {result:X}");

                put_register_mem(reg1, mod, word, result);

                set_flag_o(false);
                set_flag_s((word ? result & 0x8000 : result & 0x80) != 0);
                set_flag_z(word ? result == 0 : (result & 0xff) == 0);
                set_flag_a(false);

                set_flag_p((byte)result);  // TODO verify

                Console.WriteLine($"{prefix_str} XOR {name1},{name2}");
            }
            else if ((opcode == 0x34 || opcode == 0x35) || (opcode == 0x24 || opcode == 0x25) || (opcode == 0x0c || opcode == 0x0d)) {
                bool word = (opcode & 1) == 1;

                byte b_low  = get_pc_byte();
                byte b_high = word ? get_pc_byte() : (byte)0;

                int function = opcode >> 4;

                if (function == 0) {
                    al |= b_low;

                    if (word)
                        ah |= b_high;
                }
                else if (function == 2) {
                    al &= b_low;

                    if (word)
                        ah &= b_high;
                }
                else if (function == 3) {
                    al ^= b_low;

                    if (word)
                        ah ^= b_high;
                }
                else {
                    Console.WriteLine($"{prefix_str} opcode {opcode:X2} function {function} not implemented");
                }

                set_flag_o(false);
                set_flag_s((word ? ah & 0x8000 : al & 0x80) != 0);
                set_flag_z(word ? ah == 0 && al == 0 : al == 0);
                set_flag_a(false);

                if (word)
                    set_flag_p(ah);  // TODO verify
                else
                    set_flag_p(al);
            }
            else if (opcode == 0xea) {  // JMP far ptr
                byte o0 = get_pc_byte();
                byte o1 = get_pc_byte();
                byte s0 = get_pc_byte();
                byte s1 = get_pc_byte();

                cs = (ushort)((s1 << 8) | s0);
                ip = (ushort)((o1 << 8) | o0);

                Console.WriteLine($"{prefix_str} JMP ${cs:X} ${ip:X}: ${cs * 16 + ip:X}");
            }
            else if (opcode == 0xfa) {  // CLI
                clear_flag_bit(9);  // IF

                Console.WriteLine($"{prefix_str} CLI");
            }
            else if ((opcode & 0xf8) == 0xb0) {  // MOV reg,ib
                int  reg  = opcode & 0x07;

                ushort v  = get_pc_byte();

                string name = put_register(reg, false, v);

                Console.WriteLine($"{prefix_str} MOV {name},${v:X}");
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

                if (dir == true) {  // to 'REG' from 'rm'
                    (ushort v, string from_name) = get_register_mem(rm, mode, word);

                    string to_name = "error";

                    if (sreg)
                        to_name = put_sregister(reg, v);
                    else
                        to_name = put_register(reg, word, v);

                    Console.WriteLine($"{prefix_str} MOV {to_name},{from_name}");
                }
                else {  // from 'REG' to 'rm'
                    ushort v = 0;
                    string from_name = "error";

                    if (sreg)
                        (v, from_name) = get_sregister(reg);
                    else
                        (v, from_name) = get_register(reg, word);

                    string to_name = put_register_mem(rm, mode, word, v);

                    Console.WriteLine($"{prefix_str} MOV {to_name},{from_name}");
                }
            }
            else if ((opcode & 0xf8) == 0xb8) {  // MOV immed to reg
                bool word = (opcode & 8) == 8;  // b/w
                int  reg  = opcode & 7;

                ushort val = get_pc_byte();

                if (word)
                    val |= (ushort)(get_pc_byte() << 8);

                string to_name = put_register(reg, word, val);

                Console.WriteLine($"{prefix_str} MOV {to_name},${val:X}");
            }
            else if (opcode == 0x9e) {  // SAHF
                ushort keep = (ushort)(flags & 0b1111111100101010);
                ushort add_ = (ushort)(ah & 0b11010101);

                flags = (ushort)(keep | add_);

                Console.WriteLine($"{prefix_str} SAHF (set to {get_flags_as_str()})");
            }
            else if (opcode == 0x9f) {  // LAHF
                ah = (byte)flags;

                Console.WriteLine($"{prefix_str} LAHF");
            }
            else if (opcode >= 0x40 && opcode <= 0x4f) {  // INC/DECw
                int reg = (opcode - 0x40) & 7;

                (ushort v, string name) = get_register(reg, true);

                bool is_dec = opcode >= 0x48;

                if (is_dec)
                    v--;
                else
                    v++;

                if (is_dec)
                    set_flag_o(v == 0x7fff);
                else
                    set_flag_o(v == 0x8000);
                set_flag_s((v & 0x8000) == 0x8000);
                set_flag_z(v == 0);
                set_flag_a((v & 15) == 0);
                set_flag_p((byte)v);

                put_register(reg, true, v);

                if (is_dec)
                    Console.WriteLine($"{prefix_str} DEC {name}");
                else
                    Console.WriteLine($"{prefix_str} INC {name}");
            }
            else if ((opcode & 0xf8) == 0xd0) {  // RCR
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = o1 & 7;

                (ushort v1, string v_name) = get_register_mem(reg1, mod, word);

                int  count_spec = opcode & 3;
                int  count = -1;

                string count_name = "error";

                if (count_spec == 0 || count_spec == 1) {
                    count = 1;
                    count_name = "1";
                }
                else if (count_spec == 2 || count_spec == 3) {
                    count = cl;
                    count_name = "CL";
                }

                bool old_sign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

                int mode = (o1 >> 3) & 7;

                if (mode == 3) {  // RCR
                    for(int i=0; i<count; i++) {
                        bool new_carry = (v1 & 1) == 1;
                        v1 >>= 1;

                        bool old_carry = get_flag_c();

                        if (old_carry)
                            v1 |= (ushort)(word ? 0x8000 : 0x80);

                        set_flag_c(new_carry);
                    }

                    Console.WriteLine($"{prefix_str} RCR {v_name},{count_name}");
                }
                else if (mode == 4) {  // SHL
                    for(int i=0; i<count; i++) {
                        bool new_carry = (v1 & 0x80) == 0x80;

                        v1 <<= 1;

                        set_flag_c(new_carry);
                    }

                    Console.WriteLine($"{prefix_str} SHL {v_name},{count_name}");
                }
                else if (mode == 5) {  // SHR
                    for(int i=0; i<count; i++) {
                        bool new_carry = (v1 & 1) == 1;

                        v1 >>= 1;

                        set_flag_c(new_carry);
                    }

                    Console.WriteLine($"{prefix_str} SHR {v_name},{count_name}");
                }
                else {
                    Console.WriteLine($"{prefix_str} RCR/SHR mode {mode} not implemented");
                }

                bool new_sign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

                set_flag_o(old_sign != new_sign);

                if (!word)
                    v1 &= 0xff;

                put_register_mem(reg1, mod, word, v1);
            }
            else if ((opcode & 0xf0) == 0b01110000) {  // J..., 0x70
                byte   to    = get_pc_byte();

                bool   state = false;
                string name  = System.String.Empty;

                if (opcode == 0x70) {
                    state = get_flag_o() == true;
                    name  = "JO";
                }
                else if (opcode == 0x71) {
                    state = get_flag_o() == false;
                    name  = "JNO";
                }
                else if (opcode == 0x72) {
                    state = get_flag_c() == true;
                    name  = "JC";
                }
                else if (opcode == 0x73) {
                    state = get_flag_c() == false;
                    name  = "JNC";
                }
                else if (opcode == 0x74) {
                    state = get_flag_z() == true;
                    name  = "JE/JZ";
                }
                else if (opcode == 0x75) {
                    state = get_flag_z() == false;
                    name  = "JNE/JNZ";
                }
                else if (opcode == 0x76) {
                    state = get_flag_c() == true || get_flag_z() == true;
                    name  = "JBE/JNA";
                }
                else if (opcode == 0x77) {
                    state = get_flag_c() == false && get_flag_z() == false;
                    name  = "JA/JNBE";
                }
                else if (opcode == 0x78) {
                    state = get_flag_s() == true;
                    name  = "JS";
                }
                else if (opcode == 0x79) {
                    state = get_flag_s() == false;
                    name  = "JNS";
                }
                else if (opcode == 0x7a) {
                    state = get_flag_p() == true;
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
                    Console.WriteLine($"{prefix_str} Opcode {opcode:x2} not implemented");
                }

                ushort new_addr = (ushort)(ip + (sbyte)to);

                if (state)
                    ip = new_addr;

                Console.WriteLine($"{prefix_str} {name} {to} ({new_addr:X4})");
            }
            else if (opcode == 0xe2) {  // LOOP
                byte   to = get_pc_byte();

                (ushort CX, string dummy) = get_register(1, true);

                CX--;

                put_register(1, true, CX);

                ushort new_addr = (ushort)(ip + (sbyte)to);

                if (CX > 0)
                    ip = new_addr;

                Console.WriteLine($"{prefix_str} LOOP {to} ({new_addr:X4})");
            }
            else if (opcode == 0xe6) {  // OUT
                byte to = get_pc_byte();

                // TODO

                Console.WriteLine($"{prefix_str} OUT ${to:X2},AL");
            }
            else if (opcode == 0xee) {  // OUT
                // TODO

                Console.WriteLine($"{prefix_str} OUT ${dh:X2}{dl:X2},AL");
            }
            else if (opcode == 0xeb) {  // JMP
                byte to = get_pc_byte();

                ip = (ushort)(ip + (sbyte)to);

                Console.WriteLine($"{prefix_str} JP ${ip:X4}");
            }
            else if (opcode == 0xf4) {  // HLT
                ip--;

                Console.WriteLine($"{prefix_str} HLT");
            }
            else if (opcode == 0xf8) {  // CLC
                set_flag_c(false);

                Console.WriteLine($"{prefix_str} CLC");
            }
            else if (opcode == 0xf9) {  // STC
                set_flag_c(true);

                Console.WriteLine($"{prefix_str} STC");
            }
            else if (opcode == 0xfc) {  // CLD
                set_flag_d(false);

                Console.WriteLine($"{prefix_str} CLD");
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

                    Console.WriteLine($"{prefix_str} INC {name}");
                }
                else if (function == 1) {
                    v--;

                    set_flag_o(v == 0x7fff);

                    Console.WriteLine($"{prefix_str} DEC {name}");
                }
                else {
                    Console.WriteLine($"{prefix_str} opcode {opcode:X2} function {function} not implemented");
                }

                set_flag_s((v & 0x8000) == 0x8000);
                set_flag_z(v == 0);
                set_flag_a((v & 15) == 0);
                set_flag_p((byte)v);

                put_register_mem(reg, mod, word, v);
            }
            else {
                Console.WriteLine($"{prefix_str} Opcode {opcode:x} not implemented");
            }
        }
    }
}

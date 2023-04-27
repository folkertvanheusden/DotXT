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

        ushort get_register_mem(int reg, int mod, bool w)
        {
            if (mod == 3) {
                if (reg == 0)
                    return (ushort)(w ? (ah << 8) | al : al);
                if (reg == 1)
                    return (ushort)(w ? (ch << 8) | cl : cl);
                if (reg == 2)
                    return (ushort)(w ? (dh << 8) | dl : dl);
                if (reg == 3)
                    return (ushort)(w ? (bh << 8) | bl : bl);
                if (reg == 4)
                    return (ushort)(w ? sp : ah);
                if (reg == 5)
                    return (ushort)(w ? bp : ch);
                if (reg == 6)
                    return (ushort)(w ? si : dh);
                if (reg == 7)
                    return (ushort)(w ? di : bh);
            }
            else {
            }

            Console.WriteLine($"reg {reg} mod {mod} w {w} not supported for get");
            return 0;
        }

        void put_register_mem(int reg, int mod, bool w, ushort val)
        {
            if (mod == 3) {
                if (reg == 0) {
                    if (w) {
                        ah = (byte)(val >> 8);
                        al = (byte)val;
                    }
                    else {
                        al = (byte)val;
                    }
                    return;
                }
                if (reg == 1) {
                    if (w) {
                        ch = (byte)(val >> 8);
                        cl = (byte)val;
                    }
                    else {
                        cl = (byte)val;
                    }
                    return;
                }
                if (reg == 2) {
                    if (w) {
                        dh = (byte)(val >> 8);
                        dl = (byte)val;
                    }
                    else {
                        dl = (byte)val;
                    }
                    return;
                }
                if (reg == 3) {
                    if (w) {
                        bh = (byte)(val >> 8);
                        bl = (byte)val;
                    }
                    else {
                        bl = (byte)val;
                    }
                    return;
                }
                if (reg == 4) {
                    if (w)
                        sp = val;
                    else
                        ah = (byte)val;
                    return;
                }
                if (reg == 5) {
                    if (w)
                        bp = val;
                    else
                        ch = (byte)val;
                    return;
                }
                if (reg == 6) {
                    if (w)
                        si = val;
                    else
                        dh = (byte)val;
                    return;
                }
                if (reg == 7) {
                    if (w)
                        di = val;
                    else
                        bh = (byte)val;
                    return;
                }
            }
            else {
            }

            Console.WriteLine($"reg {reg} mod {mod} w {w} value {val} not supported for put");
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
            out_ += get_flag_s() ? "s" : "-";
            out_ += get_flag_z() ? "z" : "-";

            return out_;
        }

        public void tick()
        {
            uint addr   = (uint)(cs * 16 + ip) & mem_mask;
            byte opcode = get_pc_byte();

            string flag_str = get_flags_as_str();

            Console.Write($"{flag_str} ");

            if (opcode == 0xe9) {  // JMP np
                byte o0 = get_pc_byte();
                byte o1 = get_pc_byte();

                short offset = (short)((o1 << 8) | o0);

                ip = (ushort)(ip + offset);

                Console.WriteLine($"{addr:X} JMP {ip:X}");
            }
            else if ((opcode & 254) == 0b00110010) {  // XOR, 0x62/0x63
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = (o1 >> 3) & 7;
                int  reg2 = o1 & 7;

                ushort r1 = get_register_mem(reg1, mod, word);
                ushort r2 = get_register_mem(reg2, mod, word);

                ushort result = (ushort)(r1 ^ r2);

                put_register_mem(reg2, mod, word, result);

                Console.WriteLine($"{addr:X} XOR");
            }
            else if (opcode == 0xea) {  // JMP far ptr
                byte o0 = get_pc_byte();
                byte o1 = get_pc_byte();
                byte s0 = get_pc_byte();
                byte s1 = get_pc_byte();

                cs = (ushort)((s1 << 8) | s0);
                ip = (ushort)((o1 << 8) | o0);

                Console.WriteLine($"{addr:X} JMP ${cs:X} ${ip:X}: ${cs * 16 + ip:X}");
            }
            else if (opcode == 0xfa) {  // CLI
                clear_flag_bit(9);  // IF

                Console.WriteLine($"{addr:X} CLI");
            }
            else if ((opcode & 0xf8) == 0xb0) {  // MOV reg,ib
                int  reg  = opcode & 0x07;

                ushort v  = get_pc_byte();

                string name = "error";

                if (reg == 0x00) {
                    al = (byte)v;
                    name = "AL";
                }
                else if (reg == 0x01) {
                    cl = (byte)v;
                    name = "CL";
                }
                else if (reg == 0x02) {
                    dl = (byte)v;
                    name = "DL";
                }
                else if (reg == 0x03) {
                    bl = (byte)v;
                    name = "BL";
                }
                else if (reg == 0x04) {
                    ah = (byte)v;
                    name = "AH";
                }
                else if (reg == 0x05) {
                    ch = (byte)v;
                    name = "CH";
                }
                else if (reg == 0x06) {
                    dh = (byte)v;
                    name = "DH";
                }
                else if (reg == 0x07) {
                    bh = (byte)v;
                    name = "BH";
                }
                else {
                    Console.WriteLine("MOVB: unexpected register {reg}");
                }

                Console.WriteLine($"{addr:X} MOV {name},${al:X}");
            }
            else if ((opcode & 0xf8) == 0xb8) {  // MOV reg,iw
                int  reg  = opcode & 0x0f;

                byte v1 = get_pc_byte();
                byte v2 = get_pc_byte();

                string name = "error";

                if (reg == 0x08) {
                    al = v1;
                    ah = v2;
                    name = "AX";
                }
                else if (reg == 0x09) {
                    cl = v1;
                    ch = v2;
                    name = "CX";
                }
                else if (reg == 0x0a) {
                    dl = v1;
                    dh = v2;
                    name = "DX";
                }
                else if (reg == 0x0b) {
                    bl = v1;
                    bh = v2;
                    name = "BX";
                }
                else if (reg == 0x0c) {
                    sp = (ushort)(v1 | (v2 << 8));
                    name = "SP";
                }
                else if (reg == 0x0d) {
                    bp = (ushort)(v1 | (v2 << 8));
                    name = "BH";
                }
                else if (reg == 0x0e) {
                    si = (ushort)(v1 | (v2 << 8));
                    name = "SI";
                }
                else if (reg == 0x0f) {
                    di = (ushort)(v1 | (v2 << 8));
                    name = "DI";
                }
                else {
                    Console.WriteLine("MOVW: unexpected register {reg}");
                }

                Console.WriteLine($"{addr:X} MOV {name},${al:X}");
            }
            else if (opcode == 0x8c) {  // MOV rmw,sr
                bool word = true;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  sreg = (o1 >> 3) & 7;
                int  reg  = o1 & 7;

                string name = "error";

                ushort v = 0;

                if (sreg == 0) {
                    v = es;
                    name = "ES";
                }
                else if (sreg == 1) {
                    v = cs;
                    name = "CS";
                }
                else if (sreg == 2) {
                    v = ss;
                    name = "SS";
                }
                else if (sreg == 3) {
                    v = ds;
                    name = "DS";
                }

                put_register_mem(reg, mod, word, v);

                Console.WriteLine($"{addr:X} MOV {name},");
            }
            else if (opcode == 0x8e) {  // MOV sr,rmw
                bool word = true;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  sreg = (o1 >> 3) & 7;
                int  reg  = o1 & 7;

                ushort v  = get_register_mem(reg, mod, word);

                string name = "error";

                if (sreg == 0) {
                    es = v;
                    name = "ES";
                }
                else if (sreg == 1) {
                    cs = v;
                    name = "CS";
                }
                else if (sreg == 2) {
                    ss = v;
                    name = "SS";
                }
                else if (sreg == 3) {
                    ds = v;
                    name = "DS";
                }

                Console.WriteLine($"{addr:X} MOV {name},");
            }
            else if (opcode == 0x9e) {  // SAHF
                ushort keep = (ushort)(flags & 0b1111111100101010);
                ushort add_ = (ushort)(ah & 0b11010101);

                flags = (ushort)(keep | add_);

                Console.WriteLine($"{addr:X} SAHF (set to {get_flags_as_str()})");
            }
            else if (opcode == 0x9f) {  // LAHF
                ah = (byte)flags;

                Console.WriteLine($"{addr:X} LAHF");
            }
            else if (opcode == 0x4a) {  // DEC BX
                // overflow, sign, zero, auxiliary, parity flags

                ushort bx = (ushort)((bh << 8) | bl);

                bx--;

                set_flag_o(bx == 0x7fff);
                set_flag_s((bx & 0x8000) == 0x8000);
                set_flag_z(bx == 0);
                set_flag_a((bx & 15) == 0);
                set_flag_p((byte)bx);

                bh = (byte)(bx >> 8);
                bl = (byte)bx;

                Console.WriteLine($"{addr:X} DEC BX");
            }
            else if ((opcode & 0xf8) == 0xd0) {  // RCR
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = o1 & 7;

                ushort v1 = get_register_mem(reg1, mod, word);

                int  count_spec = opcode & 3;
                int  count = -1;

                if (count_spec == 0 || count_spec == 1)
                    count = 1;
                else if (count_spec == 2 || count_spec == 3)
                    count = cl;

                bool old_sign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

                for(int i=0; i<count; i++) {
                    bool new_carry = (v1 & 1) == 1;
                    v1 >>= 1;

                    bool old_carry = get_flag_c();

                    if (old_carry)
                        v1 |= (ushort)(word ? 0x8000 : 0x80);

                    set_flag_c(new_carry);
                }

                bool new_sign = (word ? v1 & 0x8000 : v1 & 0x80) != 0;

                set_flag_o(old_sign != new_sign);

                if (!word)
                    v1 &= 0xff;

                put_register_mem(reg1, mod, word, v1);

                Console.WriteLine($"{addr:X} RCR");
            }
            else if ((opcode & 0xf0) == 0b01110000) {  // J..., 0x70
                byte to = get_pc_byte();

                bool   state = false;
                string name  = System.String.Empty;

                if (opcode == 0x70) {
                    state = get_flag_o() == true;
                    name  = "JO";
                }
                else if (opcode == 0x71) {
                    state = get_flag_o() == false;
                    name  = "JO";
                }
                else if (opcode == 0x72) {
                    state = get_flag_c() == true;
                    name  = "JC";
                }
                else if (opcode == 0x73) {
                    state = get_flag_c() == false;
                    name  = "JAE/JNB";
                }
                else if (opcode == 0x74) {
                    state = get_flag_z() == true;
                    name  = "JNE";
                }
                else if (opcode == 0x75) {
                    state = get_flag_z() == false;
                    name  = "JNE";
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
                    Console.WriteLine($"{addr:X} Opcode {opcode:x} not implemented");
                }

                if (state) {
                    ip = (ushort)(ip + (sbyte)to);

                    Console.WriteLine($"{addr:X} {name} {to} - TAKEN");
                }
                else {
                    Console.WriteLine($"{addr:X} {name} {to}");
                }
            }
            else if (opcode == 0xf4) {  // HLT
                ip--;

                Console.WriteLine($"{addr:X} HLT");
            }
            else if (opcode == 0xf9) {  // STC
                Console.WriteLine($"{addr:X} STC");
            }
            else {
                Console.WriteLine($"{addr:X} Opcode {opcode:x} not implemented");
            }
        }
    }
}

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
                if (reg == 0) {
                    return (ushort)(w ? (ah << 8) | al : al);
                }
                else {
                }
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
                        return;
                    }
                    else {
                        al = (byte)val;
                        return;
                    }
                }
                else {
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
            else if ((opcode & 254) == 0b00110010) {  // XOR
                bool word = (opcode & 1) == 1;
                byte o1   = get_pc_byte();

                int  mod  = o1 >> 6;
                int  reg1 = (o1 >> 3) & 7;
                int  reg2 = o1 & 7;

                ushort r1 = get_register_mem(reg1, mod, word);
                ushort r2 = get_register_mem(reg2, mod, word);

                ushort result = (ushort)(r1 ^ r2);

                put_register_mem(reg2, mod, word, result);
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
            else if (opcode == 0xb0) {  // MOV AL,ib
                al = get_pc_byte();

                Console.WriteLine($"{addr:X} MOV AL,${al:X}");
            }
            else if (opcode == 0xb4) {  // MOV AH,ib
                ah = get_pc_byte();

                Console.WriteLine($"{addr:X} MOV AH,${ah:X}");
            }
            else if (opcode == 0xb8) {  // MOV AX,iw
                al = get_pc_byte();
                ah = get_pc_byte();

                Console.WriteLine($"{addr:X} MOV AX,${ah:X} {al:X}");
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
            else if (opcode == 0x72) {  // JC
                byte to = get_pc_byte();

                if (get_flag_c() == true) {
                    ip = (ushort)(ip + (sbyte)to);

                    Console.WriteLine($"{addr:X} JC {to} - TAKEN");
                }
                else {
                    Console.WriteLine($"{addr:X} JC {to}");
                }
            }
            else if (opcode == 0x73) {  // JAE/JNB
                byte to = get_pc_byte();

                if (get_flag_c() == false) {
                    ip = (ushort)(ip + (sbyte)to);

                    Console.WriteLine($"{addr:X} JAE/JNB {to} - TAKEN");
                }
                else {
                    Console.WriteLine($"{addr:X} JAE/JNB {to}");
                }
            }
            else if (opcode == 0x75) {  // JNE
                byte to = get_pc_byte();

                if (get_flag_z() == false) {
                    ip = (ushort)(ip + (sbyte)to);

                    Console.WriteLine($"{addr:X} JNE {to} - TAKEN");
                }
                else {
                    Console.WriteLine($"{addr:X} JNE {to}");
                }
            }
            else if (opcode == 0x79) {  // JNS
                byte to = get_pc_byte();

                if (get_flag_s() == false) {
                    ip = (ushort)(ip + (sbyte)to);

                    Console.WriteLine($"{addr:X} JNS {to} - TAKEN");
                }
                else {
                    Console.WriteLine($"{addr:X} JNS {to}");
                }
            }
            else if (opcode == 0x7b) {  // JNP/JPO
                byte to = get_pc_byte();

                if (get_flag_p() == false) {
                    ip = (ushort)(ip + (sbyte)to);

                    Console.WriteLine($"{addr:X} JNP/JPO {to} - TAKEN");
                }
                else {
                    Console.WriteLine($"{addr:X} JNP/JPO {to}");
                }
            }
            else if (opcode == 0xf4) {  // HLT
                ip--;

                Console.WriteLine($"{addr:X} HLT");
            }
            else {
                Console.WriteLine($"{addr:X} Opcode {opcode:x} not implemented");
            }
        }
    }
}

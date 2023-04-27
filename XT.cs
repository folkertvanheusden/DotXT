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

        rom bios  = new rom("roms/BIOS_5160_09MAY86_U18_59X7268_62X0890_27256_F800.BIN");
        rom basic = new rom("roms/BIOS_5160_09MAY86_U19_62X0819_68X4370_27256_F000.BIN");

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
        byte ah, al;
        byte bh, bl;
        byte ch, cl;
        byte dh, dl;

        ushort si;
        ushort di;
        ushort bp;
        ushort sp;

        ushort ip;

        ushort cs;
        ushort ds;
        ushort es;
        ushort ss;

        ushort flags;

        const uint mem_mask = (uint)0x00ffffff;

        bus b = new bus();

        public p8086()
        {
        }

        public void tick()
        {
            uint addr   = (uint)(cs * 16 + ip++) & mem_mask;
            byte opcode = b.read_byte(addr);


        }
    }
}

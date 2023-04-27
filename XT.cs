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

    class bus
    {
        memory m = new memory();

        public bus()
        {
        }

        public byte read_byte(uint addr)
        {
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

        uint mem_mask = (uint)0x00ffffff;

        bus b = new bus();

        public p8086()
        {
        }

        public void tick()
        {
            uint addr   = (uint)(cs + ip) & mem_mask;
            byte opcode = b.read_byte(addr);
        }
    }
}

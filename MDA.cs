class MDA : Device
{
    private byte [] _ram = new byte[16384];
    private bool _hsync;
    private int _clock;
    private int _last_hsync;

    public MDA()
    {
    }

    public override String GetName()
    {
        return "MDA";
    }

    public override void SyncClock(int clock)
    {
        _clock = clock;
    }

    public override List<PendingInterrupt> GetPendingInterrupts()
    {
        return null;
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("MDA::RegisterDevice");

        for(ushort port=0x3b0; port<0x3c0; port++)
            mappings[port] = this;
    }

    public override bool HasAddress(uint addr)
    {
        return addr >= 0xb0000 && addr < 0xb8000;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"MDA::IO_Write {port:X4} {value:X2}");

        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"MDA::IO_Read {port:X4}");

        if (port == 0x03ba)
        {
            // 14318180Hz system clock
            // 18432Hz mda clock
            // 50Hz refreshes per second
            bool hsync = Math.Abs(_clock - _last_hsync) >= (14318180 / 18432 / 50);
            _last_hsync = _clock;

            return ((byte)(hsync ? 1 : 0), false);
        }

        return (0, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        Log.DoLog($"MDA::WriteByte({offset:X6}, {value:X2})");

        uint use_offset = (offset - 0xb0000) & 0x3fff;

        _ram[use_offset] = value;

        if (use_offset < 80 * 25 * 2)
        {
            if ((use_offset & 1) == 0)
            {
                uint y = use_offset / (80 * 2);
                uint x = (use_offset % (80 * 2)) / 2;

                // attribute, character
                Log.DoLog($"MDA::WriteByte {x},{y} = {(char)value}");

                Console.Write((char)27);  // position cursor
                Console.Write($"[{y + 1};{x + 1}H");

                Console.Write((char)value);
            }
        }
    }

    public override byte ReadByte(uint offset)
    {
        Log.DoLog($"MDA::ReadByte({offset:X6}");

        return _ram[(offset - 0xb0000) & 0x3fff];
    }

    public override bool Tick(int cycles)
    {
        return false;
    }
}

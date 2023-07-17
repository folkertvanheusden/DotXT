class MDA : Device
{
    private byte [] _ram = new byte[16384];
    private bool _hsync;

    private DateTime _prev_ts = DateTime.UtcNow;
    private int _prev_clock;
    private int _clock;

    private bool hsync;

    public MDA()
    {
    }

    public override String GetName()
    {
        return "MDA";
    }

    public override void SyncClock(int clock)
    {
        DateTime now_ts = DateTime.UtcNow;
        TimeSpan elapsed_time = now_ts.Subtract(_prev_ts);
        _prev_ts = now_ts;

        double target_cycles = 14318180 * elapsed_time.TotalMilliseconds / 3000;
        int done_cycles = clock - _prev_clock;

        int speed_percentage = (int)(done_cycles * 100.0 / target_cycles);

        Console.Write((char)27);
        Console.Write($"[1;82H{speed_percentage}%  ");

        _prev_clock = _clock;

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
        byte rc = 0;

        if (port == 0x03ba)
        {
            rc = (byte)(_hsync ? 9 : 0);

            _hsync = !_hsync;
        }

        Log.DoLog($"MDA::IO_Read {port:X4}: {rc:X2}");

        return (rc, false);
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

                if (value < 32 || value == 127)
                    Console.Write((char)32);
                else
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

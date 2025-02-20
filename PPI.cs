internal class PPI : Device
{
    private byte _control = 0;
    private bool _dipswitches_high = false;
    private byte [] _cache = new byte[4];
    private Keyboard _kb = null;

    public PPI(Keyboard kb)
    {
        _kb = kb;
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public override String GetName()
    {
        return "PPI";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        mappings[0x0060] = this;
        mappings[0x0061] = this;
        mappings[0x0062] = this;
        mappings[0x0063] = this;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"PPI::IO_Read: {port:X4}", true);

        if (port == 0x0062)
        {
            byte switches = 0b11100000;  // 1 floppy, CGA80, 256kB, reserved

            if (_dipswitches_high == true)
                return ((byte)(switches & 0x0f), false);

            return ((byte)(switches >> 4), false);
        }

        return _kb.IO_Read(port);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"PPI::IO_Write: {port:X4} {value:X2}", true);

        _cache[port - 0x0060] = value;

        if (port == 0x0061)
        {
            if ((_control & 4) == 4)  // dipswitches selection
                _dipswitches_high = (value & 8) == 8;

            if ((_control & 2) == 2)  // speaker
                return false;
            // fall through for keyboard
        }
        else if (port == 0x0063)
        {
            _control = value;
            return false;
        }

        return _kb.IO_Write(port, value);
    }

    public override void SyncClock(int clock)
    {
    }

    public override bool HasAddress(uint addr)
    {
        return false;
    }

    public override void WriteByte(uint offset, byte value)
    {
    }

    public override byte ReadByte(uint offset)
    {
        return 0xee;
    }

    public override bool Tick(int ticks)
    {
        return false;
    }
}

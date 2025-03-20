internal class PPI : Device
{
    private byte _control = 0;
    private bool _dipswitches_high = false;
    private Keyboard _kb = null;
    private SystemType _system_type;
    private bool _use_SW1 = false;
    private byte _SW1 = 0;
    private byte _SW2 = 0;

    public PPI(Keyboard kb, SystemType system_type, int n_floppies)
    {
        _kb = kb;
        _system_type = system_type;

        if (_system_type == SystemType.XT)
            _SW1 = 0b00100000;  // 2 floppy-drives, CGA80, 256kB, IPL bit
        else
        {
            _SW1 = (2 << 4) /*(cga80)*/ | (3 << 2 /* memory banks*/);
            _SW2 = 0b01101101;
        }

        if (n_floppies > 0)
            _SW1 |= (byte)(1 | ((n_floppies - 1) << 6));
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

    public override byte IO_Read(ushort port)
    {
        Log.DoLog($"PPI::IO_Read: {port:X4}", LogLevel.TRACE);

        if (_use_SW1 && port == 0x0060)  // PA0
            return _SW1;

        if (port == 0x0062)  // PC0
        {
            byte switches = _system_type == SystemType.XT ? _SW1 : _SW2;

            if (_dipswitches_high == true)
                return (byte)(switches >> 4);

            return (byte)(switches & 0x0f);
        }

        if (port == 0x0063)  // mode
            return 0x99;

        return _kb.IO_Read(port);
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"PPI::IO_Write: {port:X4} {value:X2}", LogLevel.TRACE);

        if (port == 0x0061)  // PB0
        {
            // dipswitches selection
            _use_SW1 = (value & 0x80) != 0 && _system_type == SystemType.PC;

            if (_system_type == SystemType.XT)
                _dipswitches_high = (value & 8) != 0;
            else
                _dipswitches_high = (value & 4) != 0;

//            if ((_control & 2) == 2)  // speaker
//                // return false;

            // fall through for keyboard
        }
        else if (port == 0x0063)  // control
        {
            return false;
        }

        return _kb.IO_Write(port, value);
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { };
    }

    public override void WriteByte(uint offset, byte value)
    {
    }

    public override byte ReadByte(uint offset)
    {
        return 0xee;
    }

    public override bool Ticks()
    {
        return false;
    }
}

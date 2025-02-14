class M6845
{
    private byte [] _registers = new byte[18];

    public void Write(int reg, byte value)
    {
        if (reg < 18)
            _registers[reg] = value;
    }

    public byte Read(int reg)
    {
        if (reg < 18)
           return _registers[reg];

        return 0xee;
    }
}

class CGA : Display
{
    private byte [] _ram = new byte[16384];

    private M6845 _m6845 = new();
    private byte _m6845_reg;
    private uint _display_address = 0;
    private int _clock;

    public CGA()
    {
    }

    public override String GetName()
    {
        return "CGA";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("CGA::RegisterDevice", true);

        mappings[0x3d4] = this;
        mappings[0x3d5] = this;
        mappings[0x3d6] = this;
        mappings[0x3d7] = this;
    }

    public override bool HasAddress(uint addr)
    {
        if (addr >= 0xb8000 && addr < 0xc0000)
            return true;

        return false;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"CGA::IO_Write {port:X4} {value:X2}", true);

        if (port == 0x3d4 || port == 0x3d6)
            _m6845_reg = value;
        else if (port == 0x3d5 || port == 0x3d7)
        {
            _m6845.Write(_m6845_reg, value);

            _display_address = (uint)(_m6845.Read(12) << 8) | _m6845.Read(13);
        }

        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog("CGA::IO_Read", true);

        if ((port == 0x3d5 || port == 0x3d7) && _m6845_reg >= 0x0c)
            return (_m6845.Read(_m6845_reg), false);

        return (0xee, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        // Log.DoLog($"CGA::WriteByte({offset:X6}, {value:X2})", true);

        uint use_offset = (offset - 0xb8000) & 0x3fff;

        _ram[use_offset] = value;

        if (use_offset >= _display_address && use_offset < _display_address + 80 * 25 * 2)
        {
            uint y = use_offset / (80 * 2);
            uint x = (use_offset % (80 * 2)) / 2;

            uint mask = uint.MaxValue - 1;
            uint char_base_offset = use_offset & mask;

            if ((use_offset & 1) == 0)
                EmulateTextDisplay(x, y, _ram[char_base_offset + 0], _ram[char_base_offset + 1]);
        }
    }

    public override byte ReadByte(uint offset)
    {
        // Log.DoLog($"CGA::ReadByte({offset:X6}", true);

        return _ram[(offset - 0xb8000) & 0x3fff];
    }

    public override bool Tick(int cycles)
    {
        return false;
    }
}

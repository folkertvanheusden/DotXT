class MDA : Display
{
    private byte [] _ram = new byte[16384];
    private bool _hsync = false;

    public MDA()
    {
    }

    public override String GetName()
    {
        return "MDA";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("MDA::RegisterDevice", true);

        for(ushort port=0x3b0; port<0x3c0; port++)
            mappings[port] = this;
    }

    public override bool HasAddress(uint addr)
    {
        return addr >= 0xb0000 && addr < 0xb8000;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"MDA::IO_Write {port:X4} {value:X2}", true);

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

        Log.DoLog($"MDA::IO_Read {port:X4}: {rc:X2}", true);

        return (rc, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        // Log.DoLog($"MDA::WriteByte({offset:X6}, {value:X2})", true);

        uint use_offset = (offset - 0xb0000) & 0x3fff;

        _ram[use_offset] = value;

        if (use_offset < 80 * 25 * 2)
        {
            uint y = use_offset / (80 * 2);
            uint x = (use_offset % (80 * 2)) / 2;

            uint mask = uint.MaxValue - 1;
            uint char_base_offset = use_offset & mask;

            EmulateTextDisplay(x, y, _ram[char_base_offset + 0], _ram[char_base_offset + 1]);
        }
    }

    public override byte ReadByte(uint offset)
    {
        Log.DoLog($"MDA::ReadByte({offset:X6})", true);

        return _ram[(offset - 0xb0000) & 0x3fff];
    }

    public override bool Tick(int cycles)
    {
        return false;
    }
}

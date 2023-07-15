class MDA : Device
{
    private byte [] _ram = new byte[16384];

    public MDA()
    {
    }

    public new void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("MDA::RegisterDevice");

        for(ushort port=0x3b0; port<0x3c0; port++)
            mappings[port] = this;
    }

    public new bool HasAddress(uint addr)
    {
        return addr >= 0xb0000 && addr < 0xb8000;
    }

    public new void IO_Write(ushort port, byte value)
    {
        Log.DoLog("MDA::IO_Write {port:X4} {value:X2}");
    }

    public new byte IO_Read(ushort port)
    {
        Log.DoLog("MDA::IO_Read {port:X4}");

        return 0;
    }

    public new void WriteByte(uint offset, byte value)
    {
        Log.DoLog($"MDA::WriteByte({offset:X6}, {value:X2}");

        uint use_offset = (offset - 0xb0000) & 0x3fff;

        _ram[use_offset] = value;

        Console.Write((char)value);
    }

    public new byte ReadByte(uint offset)
    {
        Log.DoLog($"MDA::ReadByte({offset:X6}");

        return _ram[(offset - 0xb0000) & 0x3fff];
    }
}

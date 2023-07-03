class Device
{
    public void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
    }

    public bool HasAddress(uint addr)
    {
        return false;
    }

    public void IO_Write(ushort port, byte value)
    {
    }

    public byte IO_Read(ushort port)
    {
        return 0xff;
    }

    public void WriteByte(uint offset, byte value)
    {
    }

    public byte ReadByte(uint offset)
    {
        return 0xff;
    }
}

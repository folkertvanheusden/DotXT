abstract class Device
{
    public abstract void RegisterDevice(Dictionary <ushort, Device> mappings);
    public abstract void IO_Write(ushort port, byte value);
    public abstract byte IO_Read(ushort port);

    public abstract bool HasAddress(uint addr);
    public abstract void WriteByte(uint offset, byte value);
    public abstract byte ReadByte(uint offset);
}

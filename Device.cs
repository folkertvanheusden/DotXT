abstract class Device
{
    protected pic8259 _pic = null;

    public abstract String GetName();

    public abstract void RegisterDevice(Dictionary <ushort, Device> mappings);
    public abstract bool IO_Write(ushort port, byte value);
    public abstract (byte, bool) IO_Read(ushort port);

    public abstract bool HasAddress(uint addr);
    public abstract void WriteByte(uint offset, byte value);
    public abstract byte ReadByte(uint offset);

    public abstract void SyncClock(int clock);

    public abstract bool Tick(int cycles);

    public abstract List<PendingInterrupt> GetPendingInterrupts();

    public virtual void SetPic(pic8259 pic_instance)
    {
        _pic = pic_instance;
    }
}

abstract class Device
{
    protected pic8259 _pic = null;
    private int next_interrupt = -1;

    public abstract String GetName();

    public abstract void RegisterDevice(Dictionary <ushort, Device> mappings);
    public abstract bool IO_Write(ushort port, byte value);
    public abstract (byte, bool) IO_Read(ushort port);

    public abstract bool HasAddress(uint addr);
    public abstract void WriteByte(uint offset, byte value);
    public abstract byte ReadByte(uint offset);

    public abstract void SyncClock(int clock);

    public abstract bool Tick(int cycles);

    public abstract int GetIRQNumber();

    protected void ScheduleInterrupt(int cycles_delay)
    {
        next_interrupt = cycles_delay;
    }

    protected bool CheckScheduledInterrupt(int cycles)
    {
        if (next_interrupt >= 0)
        {
            Log.DoLog($"CheckScheduledInterrupt {next_interrupt}, {cycles}");
            next_interrupt -= cycles;

            if (next_interrupt <= 0)
            {
                next_interrupt = -1;
                Log.DoLog($"CheckScheduledInterrupt triggered");
                return true;
            }
        }

        return false;
    }

    public virtual void SetPic(pic8259 pic_instance)
    {
        _pic = pic_instance;
    }
}

abstract class Device
{
    protected pic8259 _pic = null;
    private List<int> next_interrupt = new();
    protected long _clock = 0;

    public abstract String GetName();

    public abstract void RegisterDevice(Dictionary <ushort, Device> mappings);
    public abstract bool IO_Write(ushort port, ushort value);
    public abstract (ushort, bool) IO_Read(ushort port);

    public int GetWaitStateCycles()
    {
        return 0;
    }

    public abstract bool HasAddress(uint addr);
    public abstract void WriteByte(uint offset, byte value);
    public abstract byte ReadByte(uint offset);

    public virtual bool Tick(int cycles, long clock)
    {
        _clock = clock;
        return false;
    }

    public abstract int GetIRQNumber();

    protected void ScheduleInterrupt(int cycles_delay)
    {
        next_interrupt.Add(cycles_delay);
    }

    protected bool CheckScheduledInterrupt(int cycles)
    {
        if (next_interrupt.Count() > 0)
        {
            next_interrupt[0] -= cycles;
            if (next_interrupt[0] <= 0)
            {
                next_interrupt.RemoveAt(0);
                Log.DoLog($"CheckScheduledInterrupt triggered", true);
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

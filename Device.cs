using DotXT;

abstract class Device
{
    protected i8259 _pic = null;
    protected Bus _b = null;
    private List<int> next_interrupt = new();
    protected long _clock = 0;

    public abstract String GetName();

    public virtual List<string> GetState()
    {
        return new List<string>();
    }

    public abstract void RegisterDevice(Dictionary <ushort, Device> mappings);
    public abstract bool IO_Write(ushort port, byte value);
    public abstract byte IO_Read(ushort port);

    public virtual int GetWaitStateCycles()
    {
        return 0;
    }

    public abstract List<Tuple<uint, int> > GetAddressList();
    public abstract void WriteByte(uint offset, byte value);
    public abstract byte ReadByte(uint offset);

    public abstract bool Ticks();

    public virtual bool Tick(int cycles, long clock)
    {
        _clock = clock;
        return false;
    }

    public abstract int GetIRQNumber();

    public virtual void SetDma(i8237 dma_instance)
    {
    }

    protected void ScheduleInterrupt(int cycles_delay)
    {
        next_interrupt.Add(cycles_delay);
    }

    protected bool CheckScheduledInterrupt(int cycles)
    {
        if (next_interrupt.Count > 0)
        {
            next_interrupt[0] -= cycles;
            if (next_interrupt[0] <= 0)
            {
                next_interrupt.RemoveAt(0);
                Log.DoLog($"CheckScheduledInterrupt triggered", LogLevel.DEBUG);
                return true;
            }
        }

        return false;
    }

    public virtual void SetPic(i8259 pic_instance)
    {
        _pic = pic_instance;
    }

    public virtual void SetBus(Bus bus_instance)
    {
        _b = bus_instance;
    }
}

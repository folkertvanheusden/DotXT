abstract class Display : Device
{
    private DateTime _prev_ts = DateTime.UtcNow;
    private int _prev_clock;
    private int _clock;

    private int _last_hsync;

    public Display()
    {
        TerminalClear();
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    protected void TerminalClear()
    {
        Console.Write((char)27);  // clear screen
        Console.Write($"[2J");
    }

    public abstract override String GetName();

    public override void SyncClock(int clock)
    {
        DateTime now_ts = DateTime.UtcNow;
        TimeSpan elapsed_time = now_ts.Subtract(_prev_ts);
        _prev_ts = now_ts;

        double target_cycles = 14318180 * elapsed_time.TotalMilliseconds / 3000;
        int done_cycles = clock - _prev_clock;

        int speed_percentage = (int)(done_cycles * 100.0 / target_cycles);

        Console.Write((char)27);
        Console.Write($"[1;82H{speed_percentage}%  ");

        _prev_clock = _clock;

        _clock = clock;
    }

    public abstract override void RegisterDevice(Dictionary <ushort, Device> mappings);

    public abstract override bool HasAddress(uint addr);

    public abstract override bool IO_Write(ushort port, byte value);

    protected bool IsHsync()
    {
        // 14318180Hz system clock
        // 18432Hz mda clock
        // 50Hz refreshes per second
        bool hsync = Math.Abs(_clock - _last_hsync) >= (14318180 / 18432 / 50);
        _last_hsync = _clock;

        return hsync;
    }

    public abstract override (byte, bool) IO_Read(ushort port);

    protected void EmulateTextDisplay(uint x, uint y, byte character, byte attributes)
    {
        // attribute, character
        // Log.DoLog($"Display::WriteByte {x},{y} = {(char)character}", true);

        Console.Write((char)27);  // position cursor
        Console.Write($"[{y + 1};{x + 1}H");

        Console.Write((char)character);
    }

    public abstract override void WriteByte(uint offset, byte value);

    public abstract override byte ReadByte(uint offset);

    public abstract override bool Tick(int cycles);
}

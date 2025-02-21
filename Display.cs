using System.Text;

abstract class Display : Device
{
    private DateTime _prev_ts = DateTime.UtcNow;
    private int _prev_clock = 0;
    private int _last_hsync = 0;
    private List<EmulatorConsole> _consoles = null;
    protected GraphicalFrame _gf = new();

    public Display(List<EmulatorConsole> consoles)
    {
        _consoles = consoles;

        foreach(var c in _consoles)
            c.RegisterDisplay(this);  // for Redraw()

        TerminalClear();
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public GraphicalFrame GetFrame()
    {
        return _gf;
    }

    private void WriteTextConsole(char what)
    {
        foreach(var c in _consoles)
        {
            if (c is TextConsole)
                c.Write($"{what}");
        }
    }

    private void WriteTextConsole(string what)
    {
        foreach(var c in _consoles)
        {
            if (c is TextConsole)
                c.Write($"{what}");
        }
    }

    protected void TerminalClear()
    {
        WriteTextConsole((char)27);  // clear screen
        WriteTextConsole("[2J");
    }

    public abstract override String GetName();

    public abstract void Redraw();

/*
    public void SyncClock(int clock)
    {
        DateTime now_ts = DateTime.UtcNow;
        TimeSpan elapsed_time = now_ts.Subtract(_prev_ts);
        _prev_ts = now_ts;

        double target_cycles = 14318180 * elapsed_time.TotalMilliseconds / 3000;
        int done_cycles = clock - _prev_clock;

        int speed_percentage = (int)(done_cycles * 100.0 / target_cycles);

//        Console.Write((char)27);
//        Console.Write($"[1;82H{speed_percentage}%  ");

        _prev_clock = _clock;

        _clock = clock;
    }
*/
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
        Log.DoLog($"Display::WriteByte {x},{y} = {(char)character}", true);

        WriteTextConsole((char)27); // position cursor
        WriteTextConsole($"[{y + 1};{x + 1}H");

        int [] colormap = { 0, 4, 2, 6, 1, 5, 3, 7 };

        int fg = colormap[(attributes >> 4) & 7];
        int bg = colormap[attributes & 7];

        //	if (fg == bg) { fg = 7; bg = 0; }  // TODO temporary workaround

        WriteTextConsole((char)27);  // set attributes
        WriteTextConsole($"[0;{40 + fg};{30 + bg}m");  // BG & FG color
        if ((attributes & 8) == 8)
        {
            WriteTextConsole((char)27);  // set attributes
            WriteTextConsole($"[1m");  // bright
        }

        WriteTextConsole((char)character);
    }

    public abstract override void WriteByte(uint offset, byte value);

    public abstract override byte ReadByte(uint offset);
}

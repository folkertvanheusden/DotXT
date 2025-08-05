using System.Text;

abstract class Display : Device
{
    private DateTime _prev_ts = DateTime.UtcNow;
    private long _last_hsync = 0;
    private List<EmulatorConsole> _consoles = null;
    protected GraphicalFrame _gf = new();
    protected int _gf_version = 1;
    private Mutex _vsync_lock = new();
    protected bool _palette_per_scanline = false;

    public Display(List<EmulatorConsole> consoles)
    {
        _consoles = consoles;

        foreach(var c in _consoles)
            c.RegisterDisplay(this);  // for Redraw()

        TerminalClear();
    }

    public virtual int GetWidth()
    {
        return 640;
    }

    public virtual int GetHeight()
    {
        return 400;
    }

    public override int GetIRQNumber()
    {
        return -1;
    }

    public int GetFrameVersion()
    {
        return _gf_version;
    }

    public void RegisterPalettePerScanline(bool state)
    {
        _palette_per_scanline = state;
    }

    public virtual GraphicalFrame GetFrame(bool force)
    {
        if (force == false)
            WaitVSync();
        // TODO locking
        GraphicalFrame gf = new();
        gf.width = _gf.width;
        gf.height = _gf.height;
        int n_bytes = _gf.width * _gf.height * 3;
        if (_gf.rgb_pixels != null)
            gf.rgb_pixels = (byte [])_gf.rgb_pixels.Clone();
        else
            gf.rgb_pixels = new byte[n_bytes];
        return gf;
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

    public abstract override void RegisterDevice(Dictionary <ushort, Device> mappings);

    public abstract override bool IO_Write(ushort port, byte value);

    public abstract int GetCurrentScanLine();
    public abstract bool IsInHSync();
    public abstract bool IsInVSync();

    public void WaitVSync()
    {
        lock(_vsync_lock)
        {
            Monitor.Wait(_vsync_lock);
        }
    }

    protected void PublishVSync()
    {
        lock(_vsync_lock)
        {
            Monitor.Pulse(_vsync_lock);
        }
    }

    public abstract override byte IO_Read(ushort port);

    protected void EmulateTextDisplay(uint x, uint y, byte character, byte attributes)
    {
        // attribute, character
#if DEBUG
        if (character >= 32 && character < 127)
            Log.DoLog($"Display::WriteByte {x},{y} = {(char)character}", LogLevel.TRACE);
#endif

        WriteTextConsole((char)27); // position cursor
        WriteTextConsole($"[{y + 1};{x + 1}H");

        int [] colormap = { 0, 4, 2, 6, 1, 5, 3, 7 };

        int fg = colormap[(attributes >> 4) & 7];
        int bg = colormap[attributes & 7];

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

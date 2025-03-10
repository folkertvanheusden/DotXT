using System.Text;

abstract class Display : Device
{
    private DateTime _prev_ts = DateTime.UtcNow;
    private long _last_hsync = 0;
    private List<EmulatorConsole> _consoles = null;
    protected GraphicalFrame _gf = new();
    protected ulong _gf_version = 1;

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

    public ulong GetFrameVersion()
    {
        return _gf_version;
    }

    public GraphicalFrame GetFrame()
    {
        // TODO locking
        GraphicalFrame gf = new();
        gf.width = _gf.width;
        gf.height = _gf.height;
        int n_bytes = _gf.width * _gf.height * 3;
        gf.rgb_pixels = new byte[n_bytes];
        if (_gf.rgb_pixels != null)
            Array.Copy(_gf.rgb_pixels, 0, gf.rgb_pixels, 0, n_bytes);
        return gf;
    }

    public byte[] GraphicalFrameToBmp(GraphicalFrame g)
    {
        int out_len = g.width * g.height * 3 + 2 + 12 + 40;
        byte [] out_ = new byte[out_len];

        int offset = 0;
        out_[offset++] = (byte)'B';
        out_[offset++] = (byte)'M';
        out_[offset++] = (byte)out_len;  // file size in bytes
        out_[offset++] = (byte)(out_len >> 8);
        out_[offset++] = (byte)(out_len >> 16);
        out_[offset++] = (byte)(out_len >> 24);
        out_[offset++] = 0x00;  // reserved
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 54;  // offset of start (2 + 12 + 40)
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        //assert(offset == 0x0e);
        out_[offset++] = 40;  // header size
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.width;
        out_[offset++] = (byte)(g.width >> 8);
        out_[offset++] = (byte)(g.width >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.height;
        out_[offset++] = (byte)(g.height >> 8);
        out_[offset++] = (byte)(g.height >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = 0x01;  // color planes
        out_[offset++] = 0x00;
        out_[offset++] = 24;  // bits per pixel
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // compression method
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // image size
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.width;
        out_[offset++] = (byte)(g.width >> 8);
        out_[offset++] = (byte)(g.width >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = (byte)g.height;
        out_[offset++] = (byte)(g.height >> 8);
        out_[offset++] = (byte)(g.height >> 16);
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // color count
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;  // important colors
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;
        out_[offset++] = 0x00;

        for(int y=g.height - 1; y >= 0; y--) {
            int in_o = y * g.width * 3;
            for(int x=0; x<g.width; x++) {
                int in_o2 = in_o + x * 3;
                out_[offset++] = g.rgb_pixels[in_o2 + 2];
                out_[offset++] = g.rgb_pixels[in_o2 + 1];
                out_[offset++] = g.rgb_pixels[in_o2 + 0];
            }
        }

        return out_;
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

    public abstract override void RegisterDevice(Dictionary <ushort, Device> mappings);

    public abstract override bool HasAddress(uint addr);

    public abstract override bool IO_Write(ushort port, ushort value);

    protected bool IsHsync()
    {
        // 14318180Hz system clock
        // 18432Hz mda clock
        // 50Hz refreshes per second
        bool hsync = Math.Abs(_clock - _last_hsync) >= (14318180 / 18432 / 50);
        _last_hsync = _clock;

        return hsync;
    }

    public abstract override (ushort, bool) IO_Read(ushort port);

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

class MDA : Display
{
    protected byte [] _ram = null;
    protected bool _hsync = false;
    protected Fonts fonts = new();
    protected FontDescriptor font_descr;
    protected M6845 _m6845 = new();
    protected byte _m6845_reg;
    protected uint _display_address = 0;
    protected bool _is_hercules = false;
    protected List<byte []> palette = new() {
            new byte[] {   0,   0,   0 },
            new byte[] {   0,   0, 127 },
            new byte[] {   0, 127,   0 },
            new byte[] {   0, 127, 127 },
            new byte[] { 127,   0,   0 },
            new byte[] { 127,   0, 127 },
            new byte[] { 127, 127,   0 },
            new byte[] { 127, 127, 127 },
            new byte[] { 127, 127, 127 },
            new byte[] {   0,   0, 255 },
            new byte[] {   0, 255,   0 },
            new byte[] {   0, 255, 255 },
            new byte[] { 255,   0,   0 },
            new byte[] { 255,   0, 255 },
            new byte[] { 255, 255,   0 },
            new byte[] { 255, 255, 255 }
    };

    public MDA(List<EmulatorConsole> consoles, bool is_hercules) : base(consoles)
    {
        _ram = new byte[16384];
        font_descr = fonts.get_font(FontName.VGA);
        _is_hercules = is_hercules;
        _gf.rgb_pixels = null;
        _gf.width = 640;
        _gf.height = font_descr.height * 25;
        _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
    }

    public override String GetName()
    {
        return "MDA";
    }

    // taken from CGA thus not correct
    public override bool IsInHSync()
    {
        int pixel = (int)(_clock % 304);
        return pixel < 2 || pixel > 302;  // TODO
    }

    // taken from CGA thus not correct
    public override bool IsInVSync()
    {
        int scan_line = GetCurrentScanLine();
        return scan_line < 16 || scan_line >= 216;
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("MDA::RegisterDevice", LogLevel.DEBUG);

        for(ushort port=0x3b0; port<0x3c0; port++)
            mappings[port] = this;
    }

    public override List<Tuple<uint, int> > GetAddressList()
    {
        return new() { new(0xb0000, 0x8000) };
    }

    public override bool IO_Write(ushort port, byte value)
    {
        if (port == 0x3b4)
            _m6845_reg = value;
        else if (port == 0x3b5)
        {
            if (_m6845_reg != 15)
                _m6845.Write(_m6845_reg, value);

            if (_m6845_reg == 12 || _m6845_reg == 13)
            {
                _display_address = (uint)(_m6845.Read(12) << 8) | _m6845.Read(13);
                Log.DoLog($"Set base address to {_display_address:X04}", LogLevel.DEBUG);
            }
        }

        return false;
    }

    public override byte IO_Read(ushort port)
    {
        byte rc = 0;

        if (port == 0x03ba)
        {
            rc &= 9 ^ 0xff;
            rc |= (byte)(_hsync ? 9 : 0);
            _hsync = !_hsync;
        }
        else if (port == 0x03b5)
        {
            if (_m6845_reg == 15 && _is_hercules)
                rc = 0x5a;
            else
                rc = _m6845.Read(_m6845_reg);
        }

        Log.DoLog($"MDA::IO_Read {port:X4}: {rc:X2}", LogLevel.TRACE);

        return rc;
    }

    public override void WriteByte(uint offset, byte value)
    {
        uint use_offset = (offset - 0xb0000) & 0x3fff;
        _ram[use_offset] = value;
        DrawOnConsole(use_offset);
        _gf_version++;
    }

    public virtual void DrawOnConsole(uint offset)
    {
        if (offset < 80 * 25 * 2)
        {
            uint y = offset / (80 * 2);
            uint x = (offset % (80 * 2)) / 2;

            uint mask = uint.MaxValue - 1;
            uint char_base_offset = offset & mask;

            byte character = _ram[char_base_offset + 0];
            byte attributes = _ram[char_base_offset + 1];

            EmulateTextDisplay(x, y, character, attributes);

            if (_gf.rgb_pixels != null)
            {
                int char_offset = character * font_descr.height;
                int fg = attributes & 15;
                int bg = (attributes >> 4) & 7;
                for(int yo=0; yo<font_descr.height; yo++)
                {
                    int y_pixel_offset = ((int)y * font_descr.height + yo) * _gf.width * 3;
                    byte line = font_descr.pixels[char_offset + yo];
                    byte bit_mask = 128;
                    for(int xo=0; xo<8; xo++)
                    {
                        int x_pixel_offset = y_pixel_offset + ((int)x * 8 + xo) * 3;
                        bool is_fg = (line & bit_mask) != 0;
                        bit_mask >>= 1;
                        if (is_fg)
                        {
                            _gf.rgb_pixels[x_pixel_offset + 0] = palette[fg][0];
                            _gf.rgb_pixels[x_pixel_offset + 1] = palette[fg][1];
                            _gf.rgb_pixels[x_pixel_offset + 2] = palette[fg][2];
                        }
                        else
                        {
                            _gf.rgb_pixels[x_pixel_offset + 0] = palette[bg][0];
                            _gf.rgb_pixels[x_pixel_offset + 1] = palette[bg][1];
                            _gf.rgb_pixels[x_pixel_offset + 2] = palette[bg][2];
                        }
                    }
                }
            }
        }
    }

    public virtual void Redraw()
    {
        for(uint i=0; i<80 * 25 * 2; i += 2)
        {
            DrawOnConsole(i);
        }
    }

    public override byte ReadByte(uint offset)
    {
        return _ram[(offset - 0xb0000) & 0x3fff];
    }

    public override int GetCurrentScanLine()
    {
        // 304 cpu cycles per scan line  <-- from CGA, TODO
        // 262 scan lines
        return (int)((_clock / 304) % 262);
    }

    public override bool Ticks()
    {
        return true;
    }

    public override bool Tick(int cycles, long clock)
    {
        _clock = clock;

        int line = GetCurrentScanLine();
        if (line == 200)
            PublishVSync();

        return base.Tick(cycles, clock);
    }
}

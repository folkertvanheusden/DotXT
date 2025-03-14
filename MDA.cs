class MDA : Display
{
    private byte [] _ram = new byte[16384];
    private bool _hsync = false;
    private Fonts fonts = new();
    private FontDescriptor font_descr;
    private List<byte []> palette = new() {
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

    public MDA(List<EmulatorConsole> consoles) : base(consoles)
    {
        Log.Cnsl("MDA instantiated");
        font_descr = fonts.get_font(FontName.VGA);
        _gf.rgb_pixels = null;
        _gf.width = 640;
        _gf.height = font_descr.height * 25;
        _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
    }

    public override String GetName()
    {
        return "MDA";
    }

    public override int GetCurrentScanLine()
    {
        // 14318180 Hz system clock
        // 18432 Hz mda clock
        // 50 Hz refresh rate
        // 200 (?) lines
        return (int)((_clock / (14318180 / 18432 / 50 / 200)) % 200);
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

    public override bool HasAddress(uint addr)
    {
        return addr >= 0xb0000 && addr < 0xb8000;
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        Log.DoLog($"MDA::IO_Write {port:X4} {value:X4}", LogLevel.TRACE);

        return false;
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        byte rc = 0;

        if (port == 0x03ba)
        {
            rc = (byte)(_hsync ? 9 : 0);
            _hsync = !_hsync;
        }

        Log.DoLog($"MDA::IO_Read {port:X4}: {rc:X2}", LogLevel.TRACE);

        return (rc, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        uint use_offset = (offset - 0xb0000) & 0x3fff;
        _ram[use_offset] = value;
        DrawOnConsole(use_offset);
    }

    public void DrawOnConsole(uint offset)
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

    public void Redraw()
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
}

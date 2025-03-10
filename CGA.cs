internal enum CGAMode
{
    Text40,
    Text80,
    G320,
    G640
}

class M6845
{
    private byte [] _registers = new byte[18];

    public void Write(int reg, byte value)
    {
        if (reg < 18)
            _registers[reg] = value;
    }

    public byte Read(int reg)
    {
        if (reg < 18)
           return _registers[reg];

        return 0xee;
    }
}

class CGA : Display
{
    private byte [] _ram = new byte[16384];
    private Fonts fonts = new();
    private FontDescriptor font_descr;
    private M6845 _m6845 = new();
    private byte _m6845_reg;
    private uint _display_address = 0;
    private byte _graphics_mode = 255;
    private CGAMode _cga_mode = CGAMode.Text80;
    private byte _color_configuration = 32;
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

    public CGA(List<EmulatorConsole> consoles): base(consoles)
    {
        Log.Cnsl("CGA instantiated");
        font_descr = fonts.get_font(FontName.VGA);
        _gf.rgb_pixels = null;
    }

    public override String GetName()
    {
        return "CGA";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("CGA::RegisterDevice", LogLevel.INFO);

        mappings[0x3d0] = this;
        mappings[0x3d1] = this;
        mappings[0x3d2] = this;
        mappings[0x3d3] = this;
        mappings[0x3d4] = this;
        mappings[0x3d5] = this;
        mappings[0x3d6] = this;
        mappings[0x3d7] = this;
        mappings[0x3d8] = this;
        mappings[0x3d9] = this;
        mappings[0x3da] = this;
        mappings[0x3db] = this;
        mappings[0x3dc] = this;
    }

    public int GetWaitStateCycles()
    {
        return 4;
    }

    public override bool HasAddress(uint addr)
    {
        return addr >= 0xb8000 && addr < 0xc0000;
    }

    public override bool IO_Write(ushort port, ushort value)
    {
        Log.DoLog($"CGA::IO_Write {port:X4} {value:X4}", LogLevel.TRACE);

        if (port == 0x3d4 || port == 0x3d6 || port == 0x3d0 || port == 0x3d2)
            _m6845_reg = (byte)value;
        else if (port == 0x3d5 || port == 0x3d7 || port == 0x3d1 || port == 0x3d3)
        {
            _m6845.Write(_m6845_reg, (byte)value);
            _display_address = (uint)(_m6845.Read(12) << 8) | _m6845.Read(13);
            Log.DoLog($"Set base address to {_display_address:X04}", LogLevel.DEBUG);
            Redraw();
        }
        else if (port == 0x3d8)
        {
            if (_graphics_mode != (byte)value)
            {
                if ((value & 2) == 2)  // graphics 320x200
                {
                    if ((value & 16) == 16)  // graphics 640x200
                    {
                        _gf.width = 640;
                        _gf.height = 400;  // pixeldoubler, else 200
                        _cga_mode = CGAMode.G640;
                    }
                    else
                    {
                        _gf.width = 640;  // pixeldoubler, else 320
                        _gf.height = 400;  // pixeldoubler, else 200
                        _cga_mode = CGAMode.G320;
                    }
                }
                else
                {
                    if ((value & 1) == 1)
                    {
                        _cga_mode = CGAMode.Text80;
                        _gf.width = 640;
                    }
                    else
                    {
                        _cga_mode = CGAMode.Text40;
                        _gf.width = 320;
                    }
                    _gf.height = font_descr.height * 25;
                }
                _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
                _graphics_mode = (byte)value;
                Log.DoLog($"CGA mode is now {value:X04} ({_cga_mode}), {_gf.width}x{_gf.height}", LogLevel.DEBUG);
                Redraw();
            }
        }
        else if (port == 0x3d9)
        {
            _color_configuration = (byte)value;
            Log.DoLog($"CGA color configuration: {_color_configuration:X02}", LogLevel.DEBUG);
        }
        else
        {
            Log.DoLog($"CGA output to this ({port:X04}) port not implemented", LogLevel.DEBUG);
        }

        return false;
    }

    public override (ushort, bool) IO_Read(ushort port)
    {
        Log.DoLog("CGA::IO_Read", LogLevel.TRACE);

        if ((port == 0x3d5 || port == 0x3d7) && _m6845_reg >= 0x0c)
            return (_m6845.Read(_m6845_reg), false);

        if (port == 0x3da)
        {
            int scanline = (int)((_clock / 304) % 262);  // 262 scanlines, 304 cpu cycles per scanline
            Log.DoLog($"Scanline: {scanline}, clock: {_clock}", LogLevel.TRACE);

            if (scanline >= 200)  // 200 scanlines visible
                return (1 /* regen buffer */ | 8 /* in vertical retrace */, false);
            return ((byte)(scanline & 1), false);
        }

        if (port == 0x3d8)
            return (_graphics_mode, false);

        return (0xee, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        uint use_offset = (offset - 0xb8000) & 0x3fff;
        _ram[use_offset] = value;
        DrawOnConsole(use_offset);
        _gf_version++;
    }

    private byte [] GetPixelColor(int color_index)
    {
        byte [] rgb = new byte[3];
        if ((_color_configuration & 32) != 0)
        {
            if (color_index == 1)
                rgb[1] = rgb[2] = 255;
            else if (color_index == 2)
                rgb[0] = rgb[2] = 255;
            else if (color_index == 3)
                rgb[0] = rgb[1] = rgb[2] = 255;
        }
        else
        {
            if (color_index == 1)  // green
                rgb[1] = 255;
            else if (color_index == 2)  // red
                rgb[0] = 255;
            else if (color_index == 3)  // blue
                rgb[2] = 255;
        }
        return rgb;
    }

    public void DrawOnConsole(uint use_offset)
    {
        try
        {
            if (_cga_mode == CGAMode.G320)
            {
                int x = 0;
                int y = 0;

                if (use_offset - _display_address >= 8192)
                {
                    uint addr_without_base = use_offset - 8192 - _display_address;
                    y = (int)addr_without_base / 80 * 2 + 1;
                    x = (int)(addr_without_base % 80) * 4;
                }
                else
                {
                    uint addr_without_base = use_offset - _display_address;
                    y = (int)addr_without_base / 80 * 2;
                    x = (int)(addr_without_base % 80) * 4;
                }

                if (y < 200 && x < 320)
                {
                    byte b = _ram[use_offset];

                    int y_offset = y * 320 * 3 * 4;
                    for(int x_i = 0; x_i < 4; x_i++)
                    {
                        int color_index = (b >> (x_i * 2)) & 3;
                        int x_offset = (x + 3 - x_i) * 3 * 2;
                        int offset = y_offset + x_offset;

                        byte [] color = GetPixelColor(color_index);
                        _gf.rgb_pixels[offset + 0] = _gf.rgb_pixels[offset + 3] = color[0];
                        _gf.rgb_pixels[offset + 1] = _gf.rgb_pixels[offset + 4] = color[1];
                        _gf.rgb_pixels[offset + 2] = _gf.rgb_pixels[offset + 5] = color[2];
                        offset += 320 * 3 * 2;
                        _gf.rgb_pixels[offset + 0] = _gf.rgb_pixels[offset + 3] = color[0];
                        _gf.rgb_pixels[offset + 1] = _gf.rgb_pixels[offset + 4] = color[1];
                        _gf.rgb_pixels[offset + 2] = _gf.rgb_pixels[offset + 5] = color[2];
                    }
                }
            }
            else if (_cga_mode == CGAMode.G640)
            {
                if (use_offset >= _display_address && use_offset < _display_address + 16000)
                {
                    int x = 0;
                    int y = 0;

                    if (use_offset - _display_address >= 8192)
                    {
                        uint addr_without_base = use_offset - 8192 - _display_address;
                        y = (int)addr_without_base / 80 * 2 + 1;
                        x = (int)(addr_without_base % 80) * 8;
                    }
                    else
                    {
                        uint addr_without_base = use_offset - _display_address;
                        y = (int)addr_without_base / 80 * 2;
                        x = (int)(addr_without_base % 80) * 8;
                    }

                    if (y < 200 && x < 640)
                    {
                        byte b = _ram[use_offset];
                        for(int x_i = 0; x_i < 8; x_i++)
                        {
                            byte value = (byte)((b & 1) != 0 ? 255 : 0);
                            int offset1 = ((y + 0) * 640 * 2 + x + 7 - x_i) * 3;
                            _gf.rgb_pixels[offset1 + 0] = _gf.rgb_pixels[offset1 + 1] = _gf.rgb_pixels[offset1 + 2] = value;
                            int offset2 = ((y + 0) * 640 * 2 + x + 7 - x_i) * 3;
                            _gf.rgb_pixels[offset2 + 0] = _gf.rgb_pixels[offset2 + 1] = _gf.rgb_pixels[offset2 + 2] = value;
                            b >>= 1;
                        }
                    }
                }
            }
            else  // text
            {
                int width = _cga_mode == CGAMode.Text40 ? 40 : 80;

                if (use_offset >= _display_address && use_offset < Math.Min(_display_address + width * 25 * 2, 16384))
                {
                    uint addr_without_base = use_offset - _display_address;
                    uint y = (uint)(addr_without_base / (width * 2));
                    uint x = (uint)(addr_without_base % (width * 2)) / 2;

                    uint mask = uint.MaxValue - 1;
                    uint char_base_offset = addr_without_base & mask;

                    byte character = _ram[char_base_offset + 0];
                    byte attributes = _ram[char_base_offset + 1];

                    EmulateTextDisplay(x, y, character, attributes);

                    if (_gf.rgb_pixels != null && y + font_descr.height <= _gf.height && x + 8 <= _gf.width)
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
        }
        // it is not understood why this exception occasionally occurs
        // checking the offset against overflows did not show any problems
        // hence this workaround
        // TODO
        catch(System.IndexOutOfRangeException e)
        {
            Log.DoLog($"System.IndexOutOfRangeException {e}", LogLevel.TRACE);
        }
    }

    public override void Redraw()
    {
        int interval = 0;
        int byte_count = 0;
        if (_cga_mode == CGAMode.Text40)
        {
            byte_count = 40 * 25 * 2;
            interval = 2;
        }
        else if (_cga_mode == CGAMode.Text80)
        {
            byte_count = 80 * 25 * 2;
            interval = 2;
        }
        else if (_cga_mode == CGAMode.G320)
        {
            byte_count = 80 * 25 * 2;
            interval = 1;
        }
        else if (_cga_mode == CGAMode.G640)
        {
            byte_count = 80 * 25 * 2;
            interval = 1;
        }
        else
        {
            Log.DoLog($"Unexpected mode {_cga_mode}", LogLevel.DEBUG);
            return;
        }
        for(int i=(int)_display_address; i<_display_address + byte_count; i += interval)
        {
            DrawOnConsole((uint)i);
        }
        _gf_version++;
    }

    public override byte ReadByte(uint offset)
    {
        return _ram[(offset - 0xb8000) & 0x3fff];
    }
}

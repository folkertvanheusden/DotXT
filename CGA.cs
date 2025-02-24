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
        font_descr = fonts.get_font(FontName.VGA);
        _gf.rgb_pixels = null;
    }

    public override String GetName()
    {
        return "CGA";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("CGA::RegisterDevice", true);

        mappings[0x3d0] = this;
        mappings[0x3d1] = this;
        mappings[0x3d2] = this;
        mappings[0x3d3] = this;
        mappings[0x3d4] = this;
        mappings[0x3d5] = this;
        mappings[0x3d6] = this;
        mappings[0x3d7] = this;
        mappings[0x3d8] = this;
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
        if (addr >= 0xb8000 && addr < 0xc0000)
            return true;

        return false;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"CGA::IO_Write {port:X4} {value:X2}", true);

        if (port == 0x3d4 || port == 0x3d6 || port == 0x3d0 || port == 0x3d2)
            _m6845_reg = value;
        else if (port == 0x3d5 || port == 0x3d7 || port == 0x3d1 || port == 0x3d3)
        {
            _m6845.Write(_m6845_reg, value);
            _display_address = (uint)(_m6845.Read(12) << 8) | _m6845.Read(13);
            Console.WriteLine($"Set base address to {_display_address:X04}");
            Redraw();
        }
        else if (port == 0x3d8)
        {
            if (_graphics_mode != value)
            {
                if ((value & 2) == 2)  // graphics 320x200
                {
                    if ((value & 16) == 16)  // graphics 640x00
                    {
                        _gf.width = 640;
                        _cga_mode = CGAMode.G640;
                    }
                    else
                    {
                        _gf.width = 320;
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
                }
                _gf.height = font_descr.height * 25;
                _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
                _graphics_mode = value;
                Console.WriteLine($"CGA mode is now {value:X02} ({_cga_mode}), {_gf.width}x{_gf.height}");
            }
        }
        else
        {
            Console.WriteLine($"CGA output to this ({port:X04}) port not implemented");
        }

        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog("CGA::IO_Read", true);

        if ((port == 0x3d5 || port == 0x3d7) && _m6845_reg >= 0x0c)
            return (_m6845.Read(_m6845_reg), false);

        if (port == 0x3da)
        {
            int scanline = (_clock / 304) % 262;  // 262 scanlines, 304 cpu cycles per scanline
            Log.DoLog($"Scanline: {scanline}, clock: {_clock}");

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

    public void DrawOnConsole(uint use_offset)
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

            if (y < 200)
            {
                byte b = _ram[use_offset];

                for(int x_i = 0; x_i < 4; x_i++)
                {
                    int color_index = (b >> (x_i * 2)) & 3;
                    int offset = (y * 320 + x + 3 - x_i) * 3;

                    _gf.rgb_pixels[offset + 0] = _gf.rgb_pixels[offset + 1] = _gf.rgb_pixels[offset + 2] = 0;
                    if (color_index == 1)  // green
                        _gf.rgb_pixels[offset + 1] = 255;
                    else if (color_index == 2)  // red
                        _gf.rgb_pixels[offset + 0] = 255;
                    else if (color_index == 3)  // blue
                        _gf.rgb_pixels[offset + 2] = 255;
                }
            }
        }
        else if (_cga_mode == CGAMode.G640)
        {
            if (use_offset >= _display_address && use_offset < _display_address + 1600)
            {
                int x = (int)(use_offset % 80) * 8;
                int y = (int)use_offset / 80;

                byte b = _ram[use_offset];
                for(int x_i = 0; x_i < 8; x_i++)
                {
                    int offset = (y * 640 + x + x_i) * 3;
                    _gf.rgb_pixels[offset + 0] = _gf.rgb_pixels[offset + 1] = _gf.rgb_pixels[offset + 2] = (byte)((b & 128) != 0 ? 255 : 0);
                    b <<= 1;
                }
            }
        }
        else  // text
        {
            int width = _cga_mode == CGAMode.Text40 ? 40 : 80;

            if (use_offset >= _display_address && use_offset < _display_address + width * 25 * 2)
            {
                uint y = (uint)(use_offset / (width * 2));
                uint x = (uint)(use_offset % (width * 2)) / 2;

                uint mask = uint.MaxValue - 1;
                uint char_base_offset = use_offset & mask;

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
            Log.DoLog($"Unexpected mode {_cga_mode}");
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

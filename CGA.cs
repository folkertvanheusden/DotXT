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
        Log.DoLog($"M6845 set register {reg} to {value:X02}", LogLevel.DEBUG);
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
    private int _cursor_location = -1;
    private CGAMode _cga_mode = CGAMode.Text80;
    private byte _color_configuration = 0;
    private bool _color_configuration_changed = false;
    private int _color_update_line_count = 0;
    private int _render_version = 1;
    private byte [] _palette_index = new byte[200];
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
        font_descr = fonts.get_font(FontName.CGA);

        _m6845.Write(9, 8);

        _gf.width = 640;
        _gf.height = 512;
        _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
    }

    public override String GetName()
    {
        return "CGA";
    }

    public override List<string> GetState()
    {
        List<string> @out = new();
        @out.Add($"Mode: {_cga_mode} ({_graphics_mode})");
        @out.Add($"Color configuration: {_color_configuration}, changed: {_color_configuration_changed}");

        string pal = "Palette index per scan line: ";
        for(int i=0; i<_palette_index.Length; i++)
            pal += $"{_palette_index[i]}";
        @out.Add(pal);

        for(int i=0; i<18; i++)
            @out.Add($"M6845 register {i} ({i:X02}): {_m6845.Read(i):X02}");

        return @out;
    }

    public override int GetCurrentScanLine()
    {
        // 304 cpu cycles per scan line
        // 262 scan lines
        return (int)((_clock / 304) % 262);
    }

    public int GetVisibileScanline()
    {
        if (!IsInVSync())
            return GetCurrentScanLine() - 16;
        return -1;
    }

    public override bool IsInHSync()
    {
        int pixel = (int)(_clock % 304);
        Log.DoLog($"Pixel: {pixel}", LogLevel.TRACE);
        return pixel < 16 || pixel > 280;  // TODO
    }

    public override bool IsInVSync()
    {
        int scan_line = GetCurrentScanLine();
        //Log.DoLog($"Scan line: {scan_line}", LogLevel.TRACE);
        return scan_line < 16 || scan_line >= 216;
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

    public override int GetWaitStateCycles()
    {
        // 5.8125 really, see https://www.reenigne.org/blog/the-cga-wait-states/
        return 6;
    }

    public override bool HasAddress(uint addr)
    {
        return addr >= 0xb8000 && addr < 0xc0000;
    }

    public override bool IO_Write(ushort port, byte value)
    {
        Log.DoLog($"CGA::IO_Write {port:X4} {value:X4}", LogLevel.DEBUG);

        if (port == 0x3d4 || port == 0x3d6 || port == 0x3d0 || port == 0x3d2)
            _m6845_reg = value;
        else if (port == 0x3d5 || port == 0x3d7 || port == 0x3d1 || port == 0x3d3)
        {
            _m6845.Write(_m6845_reg, value);

            if (_m6845_reg == 12 || _m6845_reg == 13)
            {
                _display_address = (uint)(_m6845.Read(12) << 8) | _m6845.Read(13);
                Log.DoLog($"Set base address to {_display_address:X04}", LogLevel.DEBUG);
            }

            if (_m6845_reg == 14 || _m6845_reg == 15)
                _cursor_location = (_m6845.Read(14) << 8) | _m6845.Read(15);
        }
        else if (port == 0x3d8)
        {
            if (_graphics_mode != value)
            {
                if ((value & 2) == 2)  // graphics 320x200
                {
                    if ((value & 16) == 16)  // graphics 640x200
                        _cga_mode = CGAMode.G640;
                    else
                        _cga_mode = CGAMode.G320;
                }
                else
                {
                    if ((value & 1) == 1)
                        _cga_mode = CGAMode.Text80;
                    else
                        _cga_mode = CGAMode.Text40;
                }
                _graphics_mode = value;
                Log.DoLog($"CGA mode is now {value:X04} ({_cga_mode}), {_gf.width}x{_gf.height}", LogLevel.DEBUG);
                Console.WriteLine($"CGA mode is now {value:X04} ({_cga_mode}), {_gf.width}x{_gf.height}", LogLevel.DEBUG);

                Array.Fill<byte>(_gf.rgb_pixels, 0x00);

                _cursor_location = -1;
            }
        }
        else if (port == 0x3d9)
        {
            _color_configuration = (byte)((value >> 4) & 3);
            _color_configuration_changed = true;
            _color_update_line_count = 0;
            Log.DoLog($"CGA color configuration: {_color_configuration:X02} at scanline {GetCurrentScanLine()} V-sync: {IsInVSync()} H-sync: {IsInHSync()} ", LogLevel.DEBUG);
        }
        else
        {
            Log.DoLog($"CGA output to this ({port:X04}) port not implemented", LogLevel.DEBUG);
        }

        _gf_version++;

        return false;
    }

    public override (byte, bool) IO_Read(ushort port)
    {
        Log.DoLog($"CGA::IO_Read {port:X04}", LogLevel.TRACE);
        byte rc = 0;

        if ((port == 0x3d5 || port == 0x3d7) && _m6845_reg >= 0x0c)
            rc = _m6845.Read(_m6845_reg);
        else if (port == 0x3da)
        {
            if (IsInVSync())
                rc = 1 /* regen buffer */ | 8 /* in vertical retrace */;
            else if (IsInHSync())
                rc = 1;
            else
                rc = 0;
        }
        else if (port == 0x3d8)
        {
            rc = _graphics_mode;
        }

        Log.DoLog($"CGA::IO_Read {port:X04}: {rc:X02}", LogLevel.TRACE);

        return (rc, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        _ram[offset & 0x3fff] = value;
        _gf_version++;
    }

    private byte [] GetPixelColor(int line, int color_index)  // TODO
    {
        byte [] rgb = new byte[3];
        if (_palette_index[line] == 2 || _palette_index[line] == 3)
        {
            byte brightness = (byte)(_palette_index[line] == 3 ? 255 : 200);
            if (color_index == 1)
                rgb[1] = rgb[2] = brightness;  // cyan
            else if (color_index == 2)
                rgb[0] = rgb[2] = brightness;  // magenta
            else if (color_index == 3)
                rgb[0] = rgb[1] = rgb[2] = brightness;  // white
        }
        else
        {
            byte brightness = (byte)(_palette_index[line] == 1 ? 255 : 200);
            if (color_index == 1)  // green
                rgb[1] = brightness;
            else if (color_index == 2)  // red
                rgb[0] = brightness;
            else if (color_index == 3)  // blue
                rgb[2] = brightness;
        }
        return rgb;
    }

    private void RenderTextFrameGraphical()  // TODO: text render for telnet
    {
        if (_gf.rgb_pixels == null)
            return;

        int width = _cga_mode == CGAMode.Text40 ? 40 : 80;

        uint mem_pointer = _display_address;
        int y = 0;
        int reg_9 = _m6845.Read(9) + 1;
        int n_lines_from_char = reg_9;

        while(y < 256 && mem_pointer < 16384)
        {
            int x = (int)(mem_pointer % (width * 2)) / 2;

            uint char_base_offset = mem_pointer & 16382;
            byte character = _ram[char_base_offset + 0];
            byte attributes = _ram[char_base_offset + 1];
            bool cursor = _cursor_location == (mem_pointer >> 1);

            int char_offset = character * font_descr.height;
            int fg = attributes & 15;
            int bg = (attributes >> 4) & 7;
            int render_n = Math.Min(200 - y, n_lines_from_char);
            for(int yo=0; yo<render_n; yo++)
            {
                int y_pixel_offset = (y + yo) * _gf.width * 3 * 2;
                byte line = 0;
                if (yo < font_descr.height)
                    line = (byte)(font_descr.pixels[char_offset + yo] ^ (cursor ? 255 : 0));
                byte bit_mask = 128;
                for(int x_pixel_offset=x * n_lines_from_char * 3; x_pixel_offset<(x + 1) * n_lines_from_char * 3; x_pixel_offset += 3, bit_mask >>= 1)
                {
                    int pixel_offset = x_pixel_offset + y_pixel_offset;
                    bool is_fg = (line & bit_mask) != 0;
                    if (is_fg)
                    {
                        _gf.rgb_pixels[pixel_offset + 0] = palette[fg][0];
                        _gf.rgb_pixels[pixel_offset + 1] = palette[fg][1];
                        _gf.rgb_pixels[pixel_offset + 2] = palette[fg][2];
                        pixel_offset += _gf.width * 3;
                        _gf.rgb_pixels[pixel_offset + 0] = palette[fg][0];
                        _gf.rgb_pixels[pixel_offset + 1] = palette[fg][1];
                        _gf.rgb_pixels[pixel_offset + 2] = palette[fg][2];
                    }
                    else
                    {
                        _gf.rgb_pixels[pixel_offset + 0] = palette[bg][0];
                        _gf.rgb_pixels[pixel_offset + 1] = palette[bg][1];
                        _gf.rgb_pixels[pixel_offset + 2] = palette[bg][2];
                        pixel_offset += _gf.width * 3;
                        _gf.rgb_pixels[pixel_offset + 0] = palette[bg][0];
                        _gf.rgb_pixels[pixel_offset + 1] = palette[bg][1];
                        _gf.rgb_pixels[pixel_offset + 2] = palette[bg][2];
                    }
                }
            }

            if (x == width - 1)
                y += reg_9;

            mem_pointer += 2;
        }
    }

    private void RenderG320FrameGraphical()  // TODO: text render for telnet
    {
        for(uint addr=_display_address; addr<Math.Min(_display_address + 16000, 16384); addr++)
        {
            int x = 0;
            int y = 0;

            if (addr - _display_address >= 8192)
            {
                uint addr_without_base = addr - 8192 - _display_address;
                y = (int)addr_without_base / 80 * 2 + 1;
                x = (int)(addr_without_base % 80) * 4;
            }
            else
            {
                uint addr_without_base = addr - _display_address;
                y = (int)addr_without_base / 80 * 2;
                x = (int)(addr_without_base % 80) * 4;
            }

            if (y >= 200)
                break;

            byte b = _ram[addr];

            int y_offset = y * 320 * 3 * 4;
            for(int x_i = 0; x_i < 4; x_i++)
            {
                int color_index = (b >> (x_i * 2)) & 3;
                int x_offset = (x + 3 - x_i) * 3 * 2;
                int offset = y_offset + x_offset;

                byte [] color = GetPixelColor(y, color_index);
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

    private void RenderG640FrameGraphical()  // TODO: text render for telnet
    {
        for(uint addr=_display_address; addr<Math.Min(_display_address + 16000, 16384); addr++)
        {
            int x = 0;
            int y = 0;
            if (addr - _display_address >= 8192)
            {
                uint addr_without_base = addr - 8192 - _display_address;
                y = (int)addr_without_base / 80 * 2 + 1;
                x = (int)(addr_without_base % 80) * 8;
            }
            else
            {
                uint addr_without_base = addr - _display_address;
                y = (int)addr_without_base / 80 * 2;
                x = (int)(addr_without_base % 80) * 8;
            }

            if (y >= 200)
                break;

            byte b = _ram[addr];
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

    void RenderTextFrameText()
    {
        int width = _cga_mode == CGAMode.Text40 ? 40 : 80;

        for(uint y=0; y<25; y++)
        {
            for(uint x=0; x<width; x++)
            {
                uint offset = (uint)(_display_address + y * width * 2 + x * 2);

                byte character = _ram[offset + 0];
                byte attributes = _ram[offset + 1];
                EmulateTextDisplay(x, y, character, attributes);
            }
        }
    }

    private void Redraw()
    {
        if (_cga_mode == CGAMode.Text40 || _cga_mode == CGAMode.Text80)
        {
            RenderTextFrameGraphical();
            RenderTextFrameText();
        }
        else if (_cga_mode == CGAMode.G320)
            RenderG320FrameGraphical();
        else if (_cga_mode == CGAMode.G640)
            RenderG640FrameGraphical();
        else
        {
            Log.DoLog($"Unexpected mode {_cga_mode}", LogLevel.WARNING);
            return;
        }
    }

    public override GraphicalFrame GetFrame()
    {
        if (_render_version != _gf_version)
        {
            Redraw();
            _render_version = _gf_version;
        }

        return base.GetFrame();
    }

    public override byte ReadByte(uint offset)
    {
        return _ram[offset & 0x3fff];
    }

    public override bool Tick(int cycles, long clock)
    {
        _clock = clock;

        int line = GetVisibileScanline();

        if (_color_configuration_changed)
        {
            // 200: there's also a 160x100 mode for which this needs to be adjusted
            if (line >= 0 && line < 200)
            {
                _palette_index[line] = _color_configuration;

                _color_update_line_count++;
                if (_color_update_line_count >= 200)
                    _color_configuration_changed = false;
            }
        }

        return base.Tick(cycles, clock);
    }
}

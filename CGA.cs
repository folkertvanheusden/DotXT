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

    private M6845 _m6845 = new();
    private byte _m6845_reg;
    private uint _display_address = 0;
    private bool _fake_status_bits = false;
    private byte _graphics_mode = 255;
    private CGAMode _cga_mode = CGAMode.Text40;

    public CGA(List<EmulatorConsole> consoles): base(consoles)
    {
        _gf.width = 640;
        _gf.height = 200;
        _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
    }

    public override String GetName()
    {
        return "CGA";
    }

    public override void RegisterDevice(Dictionary <ushort, Device> mappings)
    {
        Log.DoLog("CGA::RegisterDevice", true);

        mappings[0x3d4] = this;
        mappings[0x3d5] = this;
        mappings[0x3d6] = this;
        mappings[0x3d7] = this;
        mappings[0x3d8] = this;
        mappings[0x3da] = this;
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

        if (port == 0x3d4 || port == 0x3d6)
            _m6845_reg = value;
        else if (port == 0x3d5 || port == 0x3d7)
        {
            _m6845.Write(_m6845_reg, value);
            _display_address = (uint)(_m6845.Read(12) << 8) | _m6845.Read(13);
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
                _gf.height = 200;
                _gf.rgb_pixels = new byte[_gf.width * _gf.height * 3];
                _graphics_mode = value;
                Console.WriteLine($"CGA mode is now {value:X02} ({_cga_mode}), {_gf.width}x{_gf.height}");
            }
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
            _fake_status_bits = !_fake_status_bits;

            int scanline = (_clock / 304) % 262;  // 262 scanlines, 304 cpu cycles per scanline

            if (scanline >= 200)  // 200 scanlines visible
                return (1 /* regen buffer */ | 8 /* in vertical retrace */, false);
            return (0, false);
        }

        if (port == 0x3d8)
            return (_graphics_mode, false);

        return (0xee, false);
    }

    public override void WriteByte(uint offset, byte value)
    {
        uint use_offset = offset - 0xb8000;
        _ram[use_offset] = value;
        DrawOnConsole(use_offset);
    }

    public void DrawOnConsole(uint use_offset)
    {
        if (_cga_mode == CGAMode.G320)
        {
            int x = 0;
            int y = 0;

            if (use_offset >= 8192)
            {
                y = (int)(use_offset - 8192) / 80 * 2 + 1;
                x = (int)(use_offset % 80) * 4;
            }
            else
            {
                y = (int)use_offset / 80 * 2;
                x = (int)(use_offset % 80) * 4;
            }

            if (y < 200)
            {
                byte b = _ram[use_offset];

                for(int x_i = 0; x_i < 4; x_i++)
                {
                    int color_index = (b >> (x_i * 2)) & 3;
                    int offset = (y * 320 + x + x_i) * 3;

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
            // TODO
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

                EmulateTextDisplay(x, y, _ram[char_base_offset + 0], _ram[char_base_offset + 1]);

                int char_offset = _ram[char_base_offset + 0] * 8;
                for(int yo=0; yo<8; yo++)
                {
                    int pixel_offset = ((int)y * 8 + yo) * _gf.width * 3;
                    byte line = font_cga.glyphs[char_offset + yo];
                    byte bit_mask = 128;
                    for(int xo=0; xo<8; xo++)
                    {
                        int x_pixel_offset = pixel_offset + ((int)x * 8 + xo) * 3;
                        byte color = (byte)((line & bit_mask) != 0 ? 255 : 0);
                        bit_mask >>= 1;
                        _gf.rgb_pixels[x_pixel_offset + 0] = _gf.rgb_pixels[x_pixel_offset + 1] = _gf.rgb_pixels[x_pixel_offset + 2] = color;
                    }
                }
            }
        }
    }

    public override void Redraw()
    {
        int width = _cga_mode == CGAMode.Text40 ? 40 : 80;
        for(uint i=_display_address; i<_display_address + width * 25 * 2; i += 2)
        {
            DrawOnConsole(i);
        }
    }

    public override byte ReadByte(uint offset)
    {
        return _ram[offset - 0xb8000];
    }

    public override bool Tick(int cycles)
    {
        return false;
    }
}
